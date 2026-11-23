# AWBW Replay Player — developer notes

Orientation for anyone (human or agent) making changes to this codebase. These
documents describe what the program actually does, not what Advance Wars does.
Where the two differ, the difference is called out — those gaps are usually the
interesting part.

## Read in this order

| Document | Covers |
|---|---|
| [architecture.md](architecture.md) | Project layout, the load pipeline, the two-pass design everything else depends on |
| [replay-format.md](replay-format.md) | The AWBW `.zip` replay: PHP-serialised state, JSON action stream, what data is really in there |
| [action-pipeline.md](action-pipeline.md) | `IReplayAction`'s four-phase contract; how to add or change an action |
| [game-rules.md](game-rules.md) | The Advance Wars rules this program models (HP, funds, fog, powers, repair) and the ones it doesn't |
| [data-files.md](data-files.md) | `Units.json`, `Tiles.json`, `Buildings.json`, `COs.json`, `Countries.json`, terrain caching |
| [damage.md](damage.md) | The damage formula and its charts — the one rule this program computes rather than reads |
| [gotchas.md](gotchas.md) | Non-obvious traps, ranked by how much time they cost |
| [releases.md](releases.md) | Cutting a release: what the tag does, and what a release has to contain for the updater to work |

## The three things most likely to bite you

1. **Every action is executed twice, in two different ways.** A *setup* pass
   (`SetupAndUpdate`) walks the whole replay up front with no graphics, building
   a plain-data model of every turn; a *playback* pass (`PerformAction`) animates
   one action at a time against the live scene graph. Code that works in one and
   not the other is the most common class of bug here. See
   [action-pipeline.md](action-pipeline.md).

2. **Unit HP is fractional in the data and integral everywhere else.** AWBW
   tracks health internally on a 0–100 scale; the replay's state snapshots expose
   it as a one-decimal value (`d:9.4`), while the action stream reports `ceil()`
   of it. The player parses the fraction and then discards it at every display
   and comparison site. See [game-rules.md](game-rules.md#hit-points).

3. **The parser is strict; the setup loop is not.** A malformed replay throws
   loudly during parsing, but a failure inside one turn's setup is caught, logged
   and swallowed — that turn is silently truncated and playback continues. If a
   replay "stops early" with no error dialog, search the log for
   `Failed to setup turn`. See
   [ReplayController.cs:459](../AWBWApp.Game/Game/Logic/ReplayController.cs#L459).

## Building and running

Requires the **.NET SDK**; runtimes alone are not enough.

```powershell
dotnet build AWBWApp.Desktop.slnf
```

```powershell
dotnet run --project AWBWApp.Desktop
```

Visual tests live in `AWBWApp.Game.Tests/Visual` and are osu!framework test
scenes — they open a window and step through scripted actions. The parser test
(`AWBWApp.Game.Tests/Replays/TestReplayParsing.cs`) is marked `NoCI` because it
reads the real replay store under `%AppData%\AWBWReplayPlayer`.

## Where user data lives

```
%AppData%\AWBWReplayPlayer\
  <gameId>.zip                       replays, named by game ID
  ReplayStorage.json                 index of known replays
  UsernameStorage.json               player ID -> username cache
  ReplayData\Terrain\<mapId>.json    cached ReplayMap (tile IDs)
```

Storing by game ID means **two replays with the same ID overwrite each other**.
When producing a modified replay, bump the ID inside both zip members.
