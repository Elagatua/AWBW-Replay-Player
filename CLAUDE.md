# CLAUDE.md

An Advance Wars By Web replay player, built on osu!framework (C#, .NET).

## Read the docs first

[`docs/`](docs/) describes how this codebase actually works. Start with
[docs/README.md](docs/README.md); it indexes the rest.

- [docs/architecture.md](docs/architecture.md) — project layout, the load
  pipeline, and the two-pass (setup/playback) design everything depends on
- [docs/replay-format.md](docs/replay-format.md) — the AWBW `.zip` replay format
- [docs/action-pipeline.md](docs/action-pipeline.md) — the `IReplayAction`
  contract; read before adding or changing an action
- [docs/game-rules.md](docs/game-rules.md) — which Advance Wars rules this
  program models, and which it deliberately does not
- [docs/data-files.md](docs/data-files.md) — `Units.json`, `Tiles.json`,
  `Buildings.json`, `COs.json`, `Countries.json`
- [docs/damage.md](docs/damage.md) — the damage formula and its charts; read
  before touching anything under `Game/Damage/`
- [docs/gotchas.md](docs/gotchas.md) — the traps, in order of cost
- [docs/releases.md](docs/releases.md) — cutting a release; read before pushing a
  tag, because there are two remotes and two version series and only one of each
  is ours

## Rules of thumb

- **The player is not a game engine.** It never resolves combat, rolls luck, or
  validates a move; it applies outcomes the replay already states. Before
  computing a game rule, check whether the replay supplies the answer. The sole
  exception is `Game/Damage/`, which answers what *would* happen rather than what
  did — see [docs/damage.md](docs/damage.md).
- **Every action runs twice**: `SetupAndUpdate` (plain data, whole replay, up
  front) and `PerformAction` (drawables, one action at a time). Know which world
  your code is in. See [docs/architecture.md](docs/architecture.md).
- **Undo must mirror perform exactly.** `GoToPreviousAction` has no safety net.
- **Fail loudly on unknown replay data.** The parser throws on unrecognised keys
  by design; do not loosen it to make a file load.
- `AWBWApp.Game` holds all the logic. `AWBWApp.Resources` holds data and assets
  with no logic.

## Build

```powershell
dotnet build AWBWApp.Desktop.slnf
```

```powershell
dotnet run --project AWBWApp.Desktop
```

Requires the .NET SDK, not just the runtime. Tests under
`AWBWApp.Game.Tests/Visual` are osu!framework visual test scenes and open a
window.

## Conventions

- Follow the existing `.editorconfig`. Allman braces
  (`csharp_new_line_before_open_brace = all`), `var` throughout, public members
  PascalCase (enforced as an **error**), private members camelCase with no
  underscore prefix, and private `const` / `static readonly` fields
  `all_lower_with_underscores` — which is why you see `turn_start_text` rather
  than `TurnStartText`.
- New replay actions are discovered by reflection — adding the builder class is
  the whole registration step.
- The `replays/` directory at the repo root is working data, not source.
