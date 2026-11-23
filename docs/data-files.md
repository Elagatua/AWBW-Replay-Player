# Data files

All static game data lives in [`AWBWApp.Resources/Json`](../AWBWApp.Resources/Json)
and is embedded in the assembly. Each file is a **dictionary keyed by a code
string**, loaded with `JsonConvert.PopulateObject` into a
`Dictionary<string, T>`, then re-indexed by `AWBWID`.

> These files contain `//` and `/* */` comments and are **not strict JSON**.
> `Newtonsoft.Json` tolerates them; `System.Text.Json` and Python's `json` do
> not. Strip comments before parsing them with other tools.

| File | Loaded into | Keyed by |
|---|---|---|
| `Units.json` | `UnitStorage` → `UnitData` | unit name (`"Infantry"`, `"B-Copter"`) |
| `Tiles.json` | `TerrainTileStorage` → `TerrainTile` | tile code (`"Plain"`, `"HRoad"`) |
| `Buildings.json` | `BuildingStorage` → `BuildingTile` | `"<Country> <Type>"` (`"Blue Moon City"`) |
| `COs.json` | `COStorage` → `COData` | CO name |
| `Countries.json` | `CountryStorage` → `CountryData` | country name |
| `ExamplePowerAction.json` | — | reference payload for power actions |

The **key string is what replays use** for units: `GetUnitDataForUnitName` is
`UnitStorage.GetUnitByCode`, and `units_name` in a replay is that key.

## Units.json

```json
"Infantry": {
    "Name": "Infantry", "AWBWId": 1,
    "MaxFuel": 99, "MaxAmmo": 0, "FuelUsagePerTurn": 0,
    "Vision": 2, "MovementRange": 3,
    "AttackRange": {"x": 1, "y": 1},
    "Cost": 1000, "MovementType": "LightInf",
    "SecondWeapon": false, "Symbol": "A",
    "IdleAnimation": { "Texture": "Infantry", "Frames": [160,160,320,160] },
    "MoveSideAnimation": { ... }, "MoveUpAnimation": { ... }, "MoveDownAnimation": { ... }
}
```

25 units. `AttackRange` is min–max (`{1,1}` direct, `{2,3}` artillery,
`{0,0}` unarmed). `MovementType` is the `MovementType` flags enum
(`LightInf`, `HeavyInf`, `Tire`, `Tread`, `Sea`, `Lander`, `Air`, `Pipe`).
`Symbol` is used by the map editor, not by replay playback.

| Unit | Id | Cost | Mv | Type | Vis | Fuel | Use | Ammo | Range |
|---|--:|--:|--:|---|--:|--:|--:|--:|---|
| Infantry | 1 | 1000 | 3 | LightInf | 2 | 99 | 0 | 0 | 1–1 |
| Mech | 2 | 3000 | 2 | HeavyInf | 2 | 70 | 0 | 3 | 1–1 |
| Md.Tank | 3 | 16000 | 5 | Tread | 1 | 50 | 0 | 8 | 1–1 |
| Tank | 4 | 7000 | 6 | Tread | 3 | 70 | 0 | 9 | 1–1 |
| Recon | 5 | 4000 | 8 | Tire | 5 | 80 | 0 | 0 | 1–1 |
| APC | 6 | 5000 | 6 | Tread | 1 | 70 | 0 | 0 | 0–0 |
| Artillery | 7 | 6000 | 5 | Tread | 1 | 50 | 0 | 9 | 2–3 |
| Rocket | 8 | 15000 | 5 | Tire | 1 | 50 | 0 | 6 | 3–5 |
| Anti-Air | 9 | 8000 | 6 | Tread | 2 | 60 | 0 | 9 | 1–1 |
| Missile | 10 | 12000 | 4 | Tire | 5 | 50 | 0 | 6 | 3–5 |
| Fighter | 11 | 20000 | 9 | Air | 2 | 99 | 5 | 9 | 1–1 |
| Bomber | 12 | 22000 | 7 | Air | 2 | 99 | 5 | 9 | 1–1 |
| B-Copter | 13 | 9000 | 6 | Air | 3 | 99 | 2 | 6 | 1–1 |
| T-Copter | 14 | 5000 | 6 | Air | 2 | 99 | 2 | 0 | 0–0 |
| Battleship | 15 | 28000 | 5 | Sea | 2 | 99 | 1 | 9 | 2–6 |
| Cruiser | 16 | 18000 | 6 | Sea | 3 | 99 | 1 | 9 | 1–1 |
| Lander | 17 | 12000 | 6 | Lander | 1 | 99 | 1 | 0 | 0–0 |
| Sub | 18 | 20000 | 5 | Sea | 5 | 60 | 1 | 6 | 1–1 |
| Black Boat | 28 | 7500 | 7 | Lander | 1 | 60 | 1 | 0 | 0–0 |
| Carrier | 29 | 30000 | 5 | Sea | 4 | 99 | 1 | 9 | 3–8 |
| Stealth | 30 | 24000 | 6 | Air | 4 | 60 | 5 | 6 | 1–1 |
| Neotank | 46 | 22000 | 6 | Tread | 1 | 99 | 0 | 9 | 1–1 |
| Piperunner | 960900 | 20000 | 9 | Pipe | 4 | 99 | 0 | 9 | 2–5 |
| Black Bomb | 968731 | 25000 | 9 | Air | 1 | 45 | 5 | 0 | 0–0 |
| Mega Tank | 1141438 | 28000 | 4 | Tread | 1 | 50 | 0 | 3 | 1–1 |

## Tiles.json

```json
"Mountain": {
    "AWBWID": 2, "BaseDefence": 4, "SightDistanceIncrease": 3,
    "MovementCostsPerType": { "LightInf": 2, "HeavyInf": 1, "Air": 1 },
    "Colour": "#d08848", "TerrainType": "Mountain",
    "Textures": { "Clear": "Mountain", "Rain": "Mountain_Rain", "Snow": "Mountain_Snow" }
}
```

- A movement type **absent** from `MovementCostsPerType` is impassable for that
  type (Mountain has no `Tread`/`Tire` entry).
- `LimitFogOfWarSightDistance` defaults to `-1` (no limit). Wood and Reef set it
  to `1` — the hiding rule.
- `SightDistanceIncrease` is the Mountain +3 bonus (ground/sea only).
- `TerrainType` is a flags enum; `Land = Plain | Road | Building | Mountain |
  Forest` and `CustomShoals = Shoal | River` are composites used by the shoal
  generator.
- `AWBWID 0` is aliased to `"Teleport Tile"` at load.

### Real vs synthesised tile IDs

AWBW's own terrain IDs run to roughly 195. **Every tile with `AWBWID >= 1000` is
local to this program** — `CustomShoalGenerator`
([Game/Logic/CustomShoalGenerator.cs](../AWBWApp.Game/Game/Logic/CustomShoalGenerator.cs))
rewrites each sea and shoal tile into a directional variant
(`Sea-N-E-SW`, `Shoal-AN-AE-AS-AW`, …) based on its eight neighbours, purely so
coastlines render nicely. This happens inside `GameMap.SetToInitialGameState`,
*after* parsing, so a `ReplayMap` loaded from the terrain cache still holds the
AWBW ids. Never write a 1000+ id back into replay or map data.

## Buildings.json

```json
"Neutral City": {
    "AWBWID": 34, "BaseDefence": 3, "Colour": "#f0f0f0", "Name": "Neutral City",
    "MovementCostsPerType": { "LightInf": 1, "HeavyInf": 1, "Tread": 1, "Tire": 1, "Air": 1 },
    "BuildingType": "City",
    "Textures": { "Clear": "Neutral/City", ... }
}
```

- `CountryID` defaults to `-1` (neutral). A building with a country **must** also
  have a `BuildingType`, or loading throws.
- `GivesMoneyWhenCaptured` defaults to `true`; HQ/lab-type buildings set it false.
- `BuildingStorage` indexes three ways: by AWBW id, by code, and by
  `(BuildingType, CountryID)` — the last is how a captured building is mapped to
  the capturing country's sprite.
- `SafeGetBuildingByAWBWId` handles ids this build does not know (a newer AWBW
  country) by wrapping into the last 7 known ids:

  ```csharp
  var buildId = (id - (HighestBuildingId + 1)) % 7;
  return GetBuildingByAWBWId(buildId + (HighestBuildingId - 6));
  ```

  It renders *something* rather than crashing — so a wrong-looking building for
  an exotic country is this, not a parse bug.

## COs.json

```json
"Eagle": {
    "Name": "Eagle", "AWBWID": ..., "ToolTip": "...",
    "DayToDayPower": { ... },
    "NormalPower": { "PowerStars": 3, ... },
    "SuperPower": { "PowerStars": 9, ... }
}
```

`COPower` fields, all optional:

| Field | Meaning |
|---|---|
| `PowerStars` | Star cost; ×90 000 is the meter requirement, ×18 000 is the per-use increase |
| `PowerIncreases[]` | `AffectedUnits` (or `"all"`), `PowerIncrease`, `DefenseIncrease`, `RangeIncrease`, `ConditionalTerrain` — **display only; never applied to damage** |
| `UnitPriceMultiplier` | Hachi; affects unit value and build cost |
| `PropertyFundIncrease` | Sasha-style income bonus; feeds property value |
| `HiddenHP` | Sonja day-to-day |
| `SightIncrease`, `SeeIntoHiddenTiles` | Fog modifiers, applied |
| `AirFuelUsageDecrease` | Eagle day-to-day, applied at turn start |
| `MoveCostPerTile`, `WeatherWithNoMovementAffect`, `WeatherWithAdditionalMovementAffect` | Sturm / Olaf / Drake; used by the movement-range overlay |
| `LuckRange`, `CounterAttackPower`, `AttackFirst` | Recorded, not used — combat is never simulated |

`PowerActionBuilder.compatibleCOs` is a separate whitelist of `<CO>-Y` / `<CO>-S`
strings; a power from a CO not on it throws rather than mis-rendering.

## Countries.json

```json
"Blue Moon": {
    "AWBWID": 2, "code": "bm", "name": "Blue Moon",
    "unitPath": "Units/BlueMoon", "faceDirection": "Left",
    "colours": { "playerList": "#0098f8", "replayList": "#0098f8" }
}
```

`faceDirection` is overridable per player in the UI (one of the README's
headline features). The `AWBWXmlReplayParser` carries its **own** hardcoded
country-name→id table so it does not have to depend on storages — keep the two in
sync when adding a country.

## Map data

`ReplayMap` is a flat tile-id array:

```csharp
public string TerrainName;
public Vector2I Size;
public short[] Ids;        // Ids[y * Size.X + x]
```

Cached as `%AppData%\AWBWReplayPlayer\ReplayData\Terrain\<mapId>.json`, fetched
by `MapFileStorage` when absent. `ReplayMap.GenerateTexture` renders the minimap
at 5×5 px per tile, colouring from `TerrainTile.Colour` or the owning country's
`playerList` colour.

Test fixtures live in `AWBWApp.Resources/Json/Maps` (a map plus a reference PNG
of its expected render) and `AWBWApp.Resources/Json/Replays` /
`TestActions` (a sample replay zip and two captured power payloads).
