# Game rules as this program models them

**The player is not a game engine.** It does not resolve combat, roll luck,
decide what a unit may do, or validate anything. Outcomes come from the replay;
the program's job is to *apply and present* them, and to derive the handful of
values AWBW does not record directly (unit value, property income, repair cost,
visibility).

Keep that split in mind: if you are tempted to compute a game rule, check first
whether the replay already states the answer.

## Hit points

### Representation

| Where | Form | Example |
|---|---|---|
| State snapshot | PHP double, one decimal | `d:9.4` |
| Action payload | integer | `9` |
| `ReplayUnit.HitPoints` | `float?` | `9.4f` |
| `DrawableUnit.HealthPoints` | `BindableInt` | `10` |

AWBW stores health on an internal 0–100 scale; the replay writes it as *x*.*x*
out of 10. **The integer the game shows is `ceil()` of the exact value.**
Verified against a full game: of 1352 comparisons between a snapshot's fraction
and the integer the action stream reported for the same unit, `ceil` matched
everywhere except where the unit had been repaired in between (`round` missed 84,
`floor` missed 178).

### Everywhere it is rounded

```csharp
// Game/Units/DrawableUnit.cs:145
HealthPoints.Value = (int)MathF.Ceiling(unit.HitPoints.Value);

// API/Replay/ReplayUnit.cs:91  (drawable comparison)
if (HitPoints.HasValue && (int)(Math.Ceiling(HitPoints.Value)) != unit.HealthPoints.Value)

// API/Replay/ReplayUnit.cs:135 (desync comparison)
if (HitPoints.HasValue && (MathF.Ceiling(HitPoints.Value) != MathF.Ceiling(unit.HitPoints.Value)))

// API/Replay/ReplayActionHelper.cs:198 (unit value)
return (int)(unit.Cost.Value * priceMultiplier * Math.Ceiling(unit.HitPoints.Value) * 0.1);
```

So the fraction is parsed, carried through the setup context, and then discarded
at every point where it would become visible. Nothing in the UI exposes it today
— which is exactly the hook for surfacing it.

### Other HP rules

- `HitPoints <= 0` means the unit is removed; `CalculateUnitCost` returns 0 for
  it rather than a negative value.
- The health digit is **hidden at full health**:
  `healthSpriteText.Text = HealthPoints.Value >= 10 ? "" : ...`
  ([DrawableUnit.cs:301](../AWBWApp.Game/Game/Units/DrawableUnit.cs#L301)).
- `DrawableUnit.HideHP` implements Sonja's hidden-HP day-to-day ability
  (`COPower.HiddenHP` in `COs.json`). The user setting `SonjaHPVisibility`
  (`AlwaysVisible` / `VisibleWithVision` / `AlwaysHidden`) overrides it;
  `UpdateFogOfWar` applies it ([ReplayController.cs:833](../AWBWApp.Game/Game/Logic/ReplayController.cs#L833)).
- Player-wide power HP changes clamp to `[1, 10]` — "player wide changes cannot
  kill" ([PowerAction.cs:605](../AWBWApp.Game/API/Replay/Actions/PowerAction.cs#L605)).
- `unitReplace` HP changes do not clamp, but any result `<= 0.1` is snapped to 1.

## Unit value

Two overloads, deliberately different:

```csharp
// from replay data: uses the exact float, ceiled
cost * priceMultiplier * Math.Ceiling(unit.HitPoints.Value) * 0.1

// from a drawable: HealthPoints is already an int
unit.UnitData.Cost * priceMultiplier * (unit.HealthPoints.Value * 0.1)
```

`priceMultiplier` comes from the active power if it sets one, else the CO's
day-to-day (`UnitPriceMultiplier`; Hachi is 0.9, his powers 0.5). Unit value
feeds the player list and the per-turn stats readouts.

## Funds and income

- `ReplayInfo.FundsPerBuilding` is fixed for the game (changing mid-game throws).
- Per-player income = `FundsPerBuilding + CO.DayToDayPower.PropertyFundIncrease`
  multiplied by the count of owned buildings where
  `BuildingTile.GivesMoneyWhenCaptured` is true and the building's `CountryID`
  matches the player's country
  ([ReplaySetupContext.cs:230](../AWBWApp.Game/Game/Logic/ReplaySetupContext.cs#L230)).
  HQs and labs are excluded via `GivesMoneyWhenCaptured: false`.
- Replays record every player's funds as 0 until their first turn;
  `InitialSetup` back-fills them from the first turn each player actually plays
  ([ReplaySetupContext.cs:56](../AWBWApp.Game/Game/Logic/ReplaySetupContext.cs#L56)).
- **Repair cost is derived, not recorded.** `EndTurnAction` computes it as

  ```csharp
  repairCost = fundsBefore - (fundsAfterTurnStart - propertyIncome);
  ```

  i.e. whatever is unaccounted for after income is attributed to repairs
  ([EndTurnAction.cs:121](../AWBWApp.Game/API/Replay/Actions/EndTurnAction.cs#L121)).

## Capture

- `ReplayBuilding.Capture` is capture health, `20` = untouched.
- A `Capt` action carries the post-action `buildings_capture`. `20 -> 10` is an
  infantry at full health starting a capture; reaching 0 flips ownership, which
  shows up as a changed `terrain_id`.
- The readable name distinguishes the three cases
  ([CaptureBuildingAction.cs:140](../AWBWApp.Game/API/Replay/Actions/CaptureBuildingAction.cs#L140)):
  `Captures` when `TerrainID` changed, `Begins Capturing` when
  `LastCapture == 20`, else `Capturing`.
- **Leaving a tile resets its capture to 20.** Both `MoveUnitAction` and
  `AttackUnitAction` do this when a unit departs or dies on a partially captured
  property, storing the old value for undo.
- A capture that eliminates a player embeds an `eliminated` object, which the
  builder normalises (filling in `eliminatedByPId`, `playerId`, `message` when
  AWBW omitted them) and hands to the `Eliminated` builder.
- `ReplayInfo.CaptureWinBuildingNumber` records a capture-limit win condition but
  the player does not enforce it; the replay's own `GameOver` does.

## Fog of war

`FogOfWarGenerator.generateFog`
([FogOfWarGenerator.cs:43](../AWBWApp.Game/Game/Logic/FogOfWarGenerator.cs#L43))
rebuilds a `FogOfWarState[,]` from scratch:

```
FogOfWarState = Hidden | AirUnitsVisible | AllVisible
```

Rules, in order:

1. Every building the player owns reveals its own tile.
2. Each non-carried unit has `visionRange = max(1, UnitData.Vision + modifier)`.
3. **Ground and sea units add the terrain's `SightDistanceIncrease`** (Mountain
   +3); air units do not get this bonus.
4. Visibility is **Manhattan distance** (`|dx| + |dy| <= visionRange`), not a
   square.
5. A tile whose terrain/building has `LimitFogOfWarSightDistance > 0` (Wood and
   Reef, both `1`) is only fully visible from within that distance; beyond it the
   tile becomes `AirUnitsVisible` — air units standing there are seen, ground
   units hiding there are not. A CO power with `SeeIntoHiddenTiles` bypasses
   this.

The sight modifier is assembled in `calculateFogForPlayer`
([ReplayController.cs:917](../AWBWApp.Game/Game/Logic/ReplayController.cs#L917)):

```csharp
sightRangeModifier = dayToDay.SightIncrease + (activePower?.SightRangeIncrease ?? 0);
if (weather == Rain) sightRangeModifier -= 1;
```

`CurrentFogView` selects the perspective: a `long` player id, a team name string,
or `""` meaning "follow the active player". Team views union the fog of every
player on that team.

### Discovery is about buildings only

The `discovered` payload updates **per-team knowledge of which country owns a
property** — nothing else. `DiscoveryCollection` ignores any `units` key. The
pipeline is:

```
action.Discovered (parse)
  -> context.RegisterDiscoveryAndSetUndo  (setup; updates BuildingKnowledge)
  -> BuildingDiscoveryController.RegisterNewTurn  (per-turn snapshot for jumps)
  -> Map.RegisterDiscovery / UndoDiscovery (playback; DrawableBuilding.TeamToTile)
```

A discovery whose `terrain_id` is not a known building is only tolerated for pipe
rubble (115/116); anything else throws
`A building was turned into a terrain tile, and it was not a pipe?`.

## Turn start

All handled by the *preceding* turn's `EndTurnAction`, which carries the next
turn's data:

1. **Repair** — `repaired` lists `{units_id, units_hit_points}`. The unit is set
   to that HP and refilled to `MaxAmmo`/`MaxFuel`. Cost is derived as above.
2. **Supply** — `supplied` lists ids refilled to max ammo and fuel (APC/Black
   Boat adjacency, resolved by AWBW).
3. **Fuel drain** — for every unit belonging to the next player,
   `fuel -= UnitData.FuelUsagePerTurn`, reduced by `CO.AirFuelUsageDecrease` for
   air units. Skipped on day 1, and skipped for units that were supplied this
   turn.
4. **Crash** — a unit that hits 0 fuel is removed if its movement type is `Air`,
   `Sea` or `Lander`. Ground units just stop.
5. **Wait reset** — the outgoing player's units get `TimesMoved = 0`.

## CO powers

### Meter arithmetic

The meter is stored in raw points, **90 000 per star**:

| CO | Stars | `co_max_power` / `co_max_spower` |
|---|---|---|
| Andy super | 6 | 540 000 |
| Sami super | 8 | 720 000 |
| Eagle super | 9 | 810 000 |
| any normal (3 stars) | 3 | 270 000 |

Each use permanently raises that CO's requirement by **18 000 per star** (20%):

```csharp
coValue.PowerRequiredForNormal += 18000 * co.NormalPower.PowerStars;   // PowerAction.cs:525
```

so 270 000 becomes 324 000, 540 000 becomes 648 000, and so on. The replay
records the *current* requirement per turn, so the player reads it rather than
recomputing — but the increment above is applied at the moment of the power so
the bar is right for the rest of the turn.

Charge does not accumulate while a super power is active; the recorded values are
simply frozen for that span.

### What a `Power` action can carry

| Key | Effect |
|---|---|
| `global.units_movement_points` | `MovementRangeIncrease` |
| `global.units_vision` | `SightRangeIncrease` |
| `hpChange.hpGain` / `hpLoss` | Player-wide HP and fuel-percentage change, clamped to 1..10 |
| `unitReplace` | Per-unit ammo/fuel/HP/movement/range/`units_moved` overrides |
| `playerReplace` | `players_funds`, `players_co_power`, `tags_co_power` |
| `unitAdd` | Units created by the power (Black Bomb etc.); HP hardcoded to 9 |
| `missileCoords` | Sturm/Von Bolt style strike positions |

`units_moved` inside `unitReplace` is the mechanism behind "refresh" powers:

```csharp
unit.CanMove.Value  = change.Value.UnitsMoved.Value == 0;   // 0  -> can act again
unit.Stunned.Value  = change.Value.UnitsMoved.Value == -1;  // -1 -> stunned (Von Bolt)
```

`PowerActionBuilder` **throws on any unrecognised key** — including
`units_fired`, which is not handled. It also throws for a CO/power combination
not in its `compatibleCOs` whitelist
([PowerAction.cs:24](../AWBWApp.Game/API/Replay/Actions/PowerAction.cs#L24)), and
if the power's `playerID` is not the active player.

The builder iterates **every player** when reading `unitReplace`, so that object
must resolve for each of them (per-player key, team key, or `global`).

Powers are registered with the controller (`RegisterPower`) with a turn span
running until that player's next turn, which is how `GetActivePowerForPlayer`
knows a power is still up on later turns.

Known limitation, stated in the source: *"We don't get information about unit
damage increases"* — attack/defence boosts are described in `COs.json` for
display but are never applied, because the replay already contains the damaged
HP.

## Movement

Movement is **not simulated** during playback — the replay supplies the path and
the resulting fuel. The cost model exists only for the UI's movement-range
overlay (`GameMap.getMovementTiles`, [GameMap.cs:1032](../AWBWApp.Game/Game/Logic/GameMap.cs#L1032)),
a Dijkstra over 4-neighbours using `MovementCostsPerType`.

That model does include weather and CO effects
([GameMap.cs:1110](../AWBWApp.Game/Game/Logic/GameMap.cs#L1110)):

- **Rain**: Tread/Tire cost +1 on Plain and Forest.
- **Snow**: Air ×2 everywhere; LightInf ×2 on Plain/Forest/Mountain; HeavyInf ×2
  on Mountain; Sea/Lander ×2 on Sea and Building; Tread/Tire +1 on Plain/Forest.
- `COPower.MoveCostPerTile` (Sturm) flattens every tile to a fixed cost unless it
  is snowing.
- `WeatherWithNoMovementAffect` / `WeatherWithAdditionalMovementAffect` cover
  Olaf and Drake.

In the replay's own data, a `Move` action's `dist` is the sum of the entry costs
along `paths` and equals the fuel consumed. That relationship is exact: checked
against 542 historical moves in a real replay with zero mismatches (the only
fuel discrepancies were moves preceded by a supply).

Movement-type letters in replay payloads (`units_movement_type`) map
`F -> LightInf, B -> HeavyInf, T -> Tread, W -> Tire, A -> Air`. The player
stores the string on `ReplayUnit.MovementType` but never uses it — it looks the
type up from `Units.json` by unit name instead.

## Transports

- `units_cargo1_units_id` / `units_cargo2_units_id`, with `0` meaning empty, feed
  `ReplayUnit.CargoUnits`.
- Carried units keep a position, and it is kept in sync by
  `ReplayActionHelper.UpdateUnitCargoPositions`, recursively (a Lander carrying a
  T-Copter carrying infantry works).
- Carried units are skipped by the fog generator and are removed along with their
  transport by `RemoveUnitFromSetupContext`, which also totals their value.
- A `Load` action's destination is deliberately the transport's own tile, so any
  occupancy check must special-case it.
