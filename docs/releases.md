# Cutting a release

Push a tag. [`release.yml`](../.github/workflows/release.yml) does the rest.

```
git tag v0.13.2
git push fork v0.13.2
```

The workflow runs the test suite, publishes a self-contained Windows build,
packs it with Squirrel and creates the GitHub release with the results. It can
also be run by hand from the Actions tab, giving the version as an input, in
which case it tags the commit it built.

## The version comes from the tag

The tag with its leading `v` removed is passed to `dotnet publish` as
`-p:Version=`. Nothing in the repository carries a version: `AWBWApp.Desktop.csproj`
says `0.0.0` and stays that way, which is also true of the repository this was
forked from at each of its own release tags.

That zero is load-bearing. The app decides whether it is a deployed build from
its own assembly version:

```csharp
public bool IsDeployedBuild => AssemblyVersion.Major > 0 || AssemblyVersion.Minor > 0;
```

A development build is `0.0.0`, so it never checks for updates. The moment a
build is stamped with a real version it starts checking on startup — which is
why the next section matters.

## Updates come from this fork

[`SquirrelUpdateManager`](../AWBWApp.Desktop/SquirrelUpdateManager.cs) names the
repository it looks in:

```csharp
updateManager ??= new GithubUpdateManager(@"https://github.com/Elagatua/AWBW-Replay-Player");
```

Left pointing at the repository this was forked from, a build installed from
here would check upstream's releases on first run, find a higher version, and
quietly replace itself with that build. Everything in this fork would disappear
behind an update nobody asked for.

## What a release has to contain

| File | Why |
|---|---|
| `AWBWReplayPlayerInstaller.exe` | What a person downloads. Squirrel calls it `Setup.exe`; the workflow renames it |
| `AWBWReplayPlayer-<version>-full.nupkg` | The whole application, for a fresh install or a repair |
| `AWBWReplayPlayer-<version>-delta.nupkg` | What an existing install actually downloads |
| `RELEASES` | The index. The updater reads this first to find out what exists |

**`RELEASES` and the packages are not optional.** A release holding only the
installer installs perfectly well and then never updates again, because the
updater has nothing to read.

The delta is generated against whatever is already in the release directory, so
the workflow pulls the previous release down (`Squirrel.exe github-down`) before
packing. That step is allowed to fail, because the first release has nothing to
pull.

## The package id

The pack id is `AWBWReplayPlayer`, the same as upstream's. This is deliberate:
Squirrel installs to `%LocalAppData%\<packId>`, but the app keeps its replays and
settings in `%AppData%\AWBWReplayPlayer` whatever the install is called. Two
installs under different ids would share one settings file and fight over it, so
a build from here replaces an upstream install rather than sitting beside it.

## What is not built here

Upstream's releases also carry a Linux `AppImage` and a macOS `.app.zip`. Those
need their own toolchains and are not produced by this workflow. The in-app
updater only exists on Windows — see `AWBWAppGameDesktop.CreateUpdateManager` —
so the Windows assets are the ones that have to be right.

## Checking it by hand

The workflow's two real steps can be run locally, which is how they were
arrived at:

```powershell
dotnet publish AWBWApp.Desktop/AWBWApp.Desktop.csproj -c Release -r win-x64 --self-contained true -p:Version=0.13.2 -o publish
```

```powershell
& "$env:USERPROFILE\.nuget\packages\clowd.squirrel\2.9.42\tools\Squirrel.exe" pack -u AWBWReplayPlayer -v 0.13.2 -p publish -r Releases --packTitle "AWBW Replay Player" -i AWBWApp.Desktop/game.ico
```

A correct pack reports `Detected SquirrelAwareApp 'AWBW Replay Player.exe'` and
leaves a `Releases` folder of around 120 MB.
