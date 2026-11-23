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

## Visual tests open a window

`AWBWApp.Game.Tests/Visual/**` are osu!framework test scenes, not headless unit
tests. `TestReplayParsing` is marked `[Category("NoCI")]` because it reads the
real replay store from `%AppData%`.
