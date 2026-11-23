# Architecture

## Projects

| Project | Role |
|---|---|
| `AWBWApp.Game` | Everything: parsing, game logic, UI, editor. Changes almost always go here. |
| `AWBWApp.Resources` | Embedded JSON data + textures + shaders. No logic. |
| `AWBWApp.Desktop` | Windows/macOS/Linux entry point, auto-updater (Clowd.Squirrel). |
| `AWBWApp.iOS` | iOS entry point. |
| `AWBWApp.Game.Tests` | NUnit tests and osu!framework visual test scenes. |

The app is built on **osu!framework**. Practical consequences:

- UI and game objects are `Drawable`s in a scene graph; `Drawable`s must be
  mutated on the update thread. Anything crossing a thread boundary goes through
  `Schedule(...)` / `ScheduleAfterChildren(...)`.
- State that the UI reacts to is held in `Bindable<T>` (`BindableInt`,
  `BindableBool`, …). Setting `unit.HealthPoints.Value` is what repaints the
  health digit — there is no explicit invalidate.
- Animations are *transforms* on drawables. Actions yield `ReplayWait` values to
  pause playback until a transform finishes.

## The load pipeline

```
ReplayManager.GetReplayData(id)              IO/ReplayManager.cs
  └─ AWBWJsonReplayParser.ParseReplayZip     API/Replay/AWBWJsonReplayParser.cs
       ├─ readBaseReplayData   -> ReplayInfo + 1 TurnData per turn (state snapshots)
       ├─ readReplayActions    -> List<IReplayAction> attached to each TurnData
       └─ ReplayPostProcessor.ProcessReplay  (tag-match end-of-turn fixup)

MapFileStorage.GetOrAwaitDownloadMap(mapId)  IO/MapFileStorage.cs
  └─ ReplayMap { TerrainName, Size, short[] Ids }

ReplayController.LoadReplay(replayData, map) Game/Logic/ReplayController.cs
  ├─ Players = { id -> PlayerInfo }
  ├─ Map.SetToInitialGameState(data, map)    Game/Logic/GameMap.cs
  │    └─ CustomShoalGenerator rewrites shoal/sea tiles into local variants
  └─ setupActions()                          <-- the setup pass, see below
```

`ReplayData` is the parsed, still-plain-data form:

```csharp
ReplayData
  ReplayInfo            // id, players, map id, fog, funds per building, ...
  List<TurnData>        // one entry per turn, in file order
    TurnData
      Day, ActivePlayerID, ActiveTeam, StartWeather
      Dictionary<long, ReplayUserTurn>  Players      // funds, CO, power meters
      Dictionary<Vector2I, ReplayBuilding> Buildings // start-of-turn snapshot
      Dictionary<long, ReplayUnit>        ReplayUnit // start-of-turn snapshot
      List<IReplayAction> Actions                    // what happened during it
```

Each `TurnData` holds the board **as it was at the start of that turn**, plus the
actions taken during it. The two come from different files in the zip and are
matched up by `(ActivePlayerID, Day)` — see
[replay-format.md](replay-format.md#how-the-two-files-are-matched).

## The two-pass design

This is the central idea in the codebase, and the source of most of its
subtleties.

### Pass 1 — setup (`ReplayController.setupActions`, [ReplayController.cs:417](../AWBWApp.Game/Game/Logic/ReplayController.cs#L417))

Runs once, immediately after load, over *every* turn and *every* action. There is
no scene graph involvement. It carries a `ReplaySetupContext` — a mutable,
plain-data mirror of the board:

```csharp
ReplaySetupContext
  Dictionary<long, ReplayUnit>            Units
  Dictionary<Vector2I, ReplayBuilding>    Buildings
  Dictionary<Vector2I, Dictionary<string, BuildingTile>> BuildingKnowledge  // per team
  Dictionary<long, int> FundsValuesForPlayers / PropertyValuesForPlayers / PowerValuesForPlayers
  Dictionary<long, PlayerStatsReadout>    StatsReadouts
```

For each turn it calls `SetupForTurn` (reset the context from that turn's
snapshot), then `action.SetupAndUpdate(controller, context)` on each action in
order. Actions use this pass to:

- record the "before" state they will need in order to undo themselves later,
- apply their effect to the context so later actions in the same turn see it,
- accumulate stats, funds, power and building-knowledge changes,
- register anything the controller needs globally (e.g. `RegisterPower`).

At the end of each turn, `FinishTurnAndCheckForDesyncs` compares the simulated
context against the *next* turn's snapshot and records an `EndTurnDesync`. That
record serves double duty: it is logged as a diagnostic, and it is replayed in
reverse when the user steps backwards across a turn boundary
([ReplayController.cs:659](../AWBWApp.Game/Game/Logic/ReplayController.cs#L659)).

### Pass 2 — playback (`PerformAction`)

Driven by the user stepping forward. Operates on `DrawableUnit` /
`DrawableBuilding` in the live `GameMap`, plays animations, and yields
`ReplayWait`s between them. `GoToPreviousAction` calls `UndoAction` instead,
which must restore exactly what `PerformAction` changed.

### Why it matters

- `SetupAndUpdate` must never touch `controller.Map` for *state*; the map is
  still showing turn 0. It may read storages (`controller.COStorage`,
  `controller.Map.GetUnitDataForUnitName`) because those are static data.
- `PerformAction` must not recompute anything it could have computed in setup;
  the user may jump to an arbitrary turn, so playback has to be positionally
  independent.
- Anything an action changes in the context must also be changed on the drawable
  side, and undone symmetrically. Asymmetry shows up as a desync warning, or
  worse, silently wrong state after a rewind.

## Turn and action navigation

| Call | Effect |
|---|---|
| `GoToNextAction()` | Performs the next action, or rolls to the next turn at the end |
| `GoToPreviousAction()` | Undoes the current action; crossing a turn boundary applies the stored `EndTurnDesync` in reverse |
| `GoToTurn(idx)` | Jumps: resets the map from `TurnData[idx]`'s snapshot, restores building discoveries for that turn, sets `currentActionIndex = -1` |

Jumping is cheap because every turn has a full snapshot. Stepping is the
expensive path, because it depends on undo being correct.

`BuildingDiscoveryController` ([Game/Logic/BuildingDiscoveryController.cs](../AWBWApp.Game/Game/Logic/BuildingDiscoveryController.cs))
stores a copy of per-team building knowledge at the start of each turn so that a
jump can restore fog-of-war discovery without replaying the actions.

## Key types at a glance

| Type | File | Role |
|---|---|---|
| `ReplayController` | `Game/Logic/ReplayController.cs` | Owns the replay, players, navigation, fog view |
| `GameMap` | `Game/Logic/GameMap.cs` | Scene graph for tiles/buildings/units; drawable lookups |
| `ReplaySetupContext` | `Game/Logic/ReplaySetupContext.cs` | Plain-data board used during the setup pass |
| `ReplayActionDatabase` | `API/Replay/ReplayActionDatabase.cs` | `action` string -> builder, discovered by reflection |
| `FogOfWarGenerator` | `Game/Logic/FogOfWarGenerator.cs` | Per-player visibility array |
| `PlayerInfo` | `Game/Logic/PlayerInfo.cs` | Per-player bindables: funds, unit value, CO, power |
| `ReplayManager` | `IO/ReplayManager.cs` | Replay store on disk, import, rename, delete |
| `MapFileStorage` | `IO/MapFileStorage.cs` | Terrain cache + download |
