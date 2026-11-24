# Gotchas

Things that cost real time, roughly in descending order.

## Setup and playback are different worlds

`SetupAndUpdate` runs over the whole replay before anything is drawn. At that
moment `controller.Map` still shows turn 0, and `controller.Players[...]`
bindables hold turn-0 values. Reading live state there gives you turn-0 answers
for turn 25.

- State belongs in `ReplaySetupContext` (`context.Units`, `context.Buildings`,
  `context.FundsValuesForPlayers`, …).
- Static data is fine to read from the controller (`controller.COStorage`,
  `controller.Map.GetUnitDataForUnitName`) because it never changes.
- Anything the setup pass learns that playback needs must be **stored on the
  action instance**.

## Undo must be exactly symmetric

`GoToPreviousAction` calls `UndoAction` with no safety net. If `PerformAction`
changed something `UndoAction` does not restore, the error persists for the rest
of the session and compounds. Checklist for any action that writes:

- unit fields → store a `Clone()` in setup, re-apply in undo
- funds / unit value / property value → adjust the `PlayerInfo` bindable both ways
- building capture health → store the old value (every exit from a tile resets it
  to 20)
- discoveries → `RegisterDiscoveryAndSetUndo` in setup, `UndoDiscovery` in undo

Composite actions must undo in **reverse** order: own effect first, then the
wrapped `Move`.

## The setup pass can only be run once

Actions are stateful. `SetupAndUpdate` stores what its undo will need on the
action instance — `AttackUnitAction` fills an `originalUnits` dictionary — so
running the pass a second time over the same `replayData` throws on its own
leftovers (`An item with the same key has already been added`).

There is no reset. If something needs the pass redone — a CO variant changing
is the current case — read the replay again, which produces a fresh set of
actions, and `LoadReplay` it. `ReplayController.SetCOVariant` is the worked
example. Rebuilding the player list is a second reason to go that way: its items
are built around one CO, and `onCOChange` throws
`Player managed to change CO's during a match without tagging?` if the active CO
turns into something that is not the tag CO.

## A replay's index entry is replaced, not merged

`ReplayStorage.json` caches a `ReplayInfo` per replay, and `addReplay` overwrites
it wholesale with whatever the parse produced. A parse only knows what the file
says, so anything the *user* set — `UserDefinedName`, `COVariants` — is not in
it. `CarryOverUserChoicesFrom` exists to put those back; extend it when you add
another field the user controls.

Re-parsing is not rare. It happens for any replay not already indexed, and
`ReplayManager` also re-reads entries that predate a field it now needs, which is
how a change to `ReplayInfo` quietly rewrites the whole library.

## `Overwrite` merges; it does not assign

```csharp
HitPoints = other.HitPoints ?? HitPoints;
```

Action payloads are partial. `originalUnit = unit.Clone()` must clone the
*context's* unit, never the action's. Cloning the action's partial unit produces
an "undo" that nulls out half the unit's state.

## A failed turn is silent

The setup loop catches per turn, logs `Failed to setup turn N`, and moves on.
Every action from the failure onward keeps `SuccessfullySetup == false`, and
playback skips straight to the next turn when it meets one. Symptom: a turn that
mysteriously ends early with no dialog. Always read the log.

## Fractional HP

The state file has `d:9.4`; everything else has `10`. Consequences:

- A regex scanning for `hit_points";i:` finds nothing — HP is a PHP `d:`.
- Comparisons in `ReplayUnit` use `ceil` on both sides, so fractional drift never
  trips a desync and never shows in the UI.
- If you add a feature that exposes the fraction, note it is only available at
  **turn boundaries**; the action stream gives you integers only.

See [game-rules.md](game-rules.md#hit-points).

## Player-keyed JSON

`GetPlayerSpecificDataFromJObject` falls back `playerId -> teamName -> "global"`
and `Debug.Assert`s that one of them exists. In release that assert vanishes and
you get a `NullReferenceException` somewhere downstream instead.

`PowerAction` is the one builder that iterates **every** player, so a
`unitReplace` that only carries the active player's key throws when it reaches
the opponent. Emit the same list under every player id, or use `global`.

## `Unload` accepts a wrapped `Move` but never gets one

`UnloadUnitActionBuilder` reads `jObject["Move"]` like the other composites, so
the code path exists — but AWBW does not use it. A transport that moves and then
unloads is emitted as **two** actions, a `Move` followed by an `Unload`. Anything
counting actions, or looking for "the move belonging to this unload", has to
account for both shapes.

## `Load`'s destination is occupied on purpose

The loading unit's path ends on the transport's tile. Any "is the destination
free?" check needs to exempt the transport.

## `Resign` loses its `GameOver`

`ResignActionBuilder` forwards only `jObject["Resign"]` to the `Eliminated`
builder, discarding a sibling `GameOver` object. A replay that ends with a
resignation can therefore fall through to `AddGameOverAction` and display
`Match ended in Draw!`. If you are synthesising an ending, prefer an `End` action
with `updatedInfo.event = "GameOver"`.

## Some AWBW numbers are flags wearing a number's clothes

Two fields carry meaning in a way that reads as ordinary data:

- A **negative** `co_max_spower` means the CO has no super power, not that it
  costs a strange amount. Its magnitude still escalates normally, so only the
  sign says anything. Read as a real requirement it builds a power meter with
  negative bar widths, then throws on the next CO update.
- A **numeric** `weather_code` is the number of days a CO-induced weather has
  left, not a weather. `ParseWeatherCode` only knows letters; use
  `TryParseTimedWeatherCode` and let `ReplayPostProcessor` resolve it from the
  power that set it.

Both are documented in [replay-format.md](replay-format.md#co-power-meters).
Assume a new number is a flag until you have seen it vary.

## Luck can exceed the attack

Luck is added before anything scales it, and Sonja, Flak and Jugger roll it
negative — down to `-44` under Jugger's super. A weak enough attack therefore
computes as *negative* damage, which is why `DamageCalculator` floors it at zero.
Anything consuming `DamageResult.Outcomes` is entitled to assume damage is not
negative; `HitPointBelief.WithDamage` indexes an array by `hitPoints - damage`
and would walk off the end of it.

## The parser throws on anything new

Unknown keys, changed-per-turn header fields, unknown CO/power combinations,
unknown `unitReplace` keys (`units_fired` among them), out-of-order action
indices — all hard failures. This is deliberate: silent tolerance produces
desyncs that are far harder to diagnose. When AWBW adds a field, the fix is to
handle it explicitly, not to loosen the check.

## PHP string lengths are byte counts

`s:<n>:"..."` counts UTF-8 bytes. Replay titles and usernames contain non-ASCII
regularly. `readString` handles this; any external tool that edits a replay must
too.

## Resource JSON is not strict JSON

`Tiles.json`, `Buildings.json` and `COs.json` contain `//` and `/* */` comments.
Newtonsoft accepts them. Python's `json`, `jq` and `System.Text.Json` do not.

## Tile ids above 1000 are not AWBW ids

`CustomShoalGenerator` rewrites sea and shoal tiles into directional local
variants during map setup. They exist only inside this program; never persist one
into replay or map data.

## Turn index is not action-file index

Actions bind to turns by `(ActivePlayerID, Day)`, not position. In a tag match
two turns can share a day, and the first match wins.

## The last turn is never checked

`FinishTurnAndCheckForDesyncs` compares turn *i*'s simulated end state against
turn *i+1*'s snapshot, so the final turn's outcome is never validated against
anything. Useful when fabricating a final turn; also means a bug in the last turn
will not raise a desync warning.

## osu!framework threading

`Drawable` mutation must happen on the update thread.
`GameMap.SetToInitialGameState` asserts it. Work triggered from parsing or IO
goes through `Schedule` / `ScheduleAfterChildren`; `ScheduleLoadReplay` exists
for exactly this reason.

## A green test run does not mean the app has your change

`dotnet test` builds the test project and what it references, which includes
`AWBWApp.Resources` — but not `AWBWApp.Desktop`. The desktop output keeps its own
copies of those assemblies, so after changing anything under `AWBWApp.Resources`
(a texture, a JSON file) the tests can pass against the new data while the
running app still shows the old. Build `AWBWApp.Desktop.slnf` before believing
what the window tells you.

The app also holds those assemblies open while it runs, so a build will fail with
`MSB3027 … being used by another process` until you close it.

## Visual tests open a window

`AWBWApp.Game.Tests/Visual/**` are osu!framework test scenes, not headless unit
tests. `TestReplayParsing` is marked `[Category("NoCI")]` because it reads the
real replay store from `%AppData%`.
