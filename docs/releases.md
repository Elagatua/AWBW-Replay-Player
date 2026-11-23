# Cutting a release

Push a tag. [`release.yml`](../.github/workflows/release.yml) does the rest.

```
git tag v0.14.1
git push fork v0.14.1
```

The workflow runs the test suite, then builds each platform on its own runner.
The last job collects what they produced and creates the GitHub release. It can
also be run by hand from the Actions tab, giving the version as an input, in
which case it tags the commit it built.

A platform that fails takes the release with it. That is deliberate: a release
quietly missing a platform is worse than one that did not happen.

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

## What a release contains

| File | Platform | Why |
|---|---|---|
| `AWBWReplayPlayerInstaller.exe` | Windows | What a person downloads. Squirrel calls it `Setup.exe`; the workflow renames it |
| `AWBWReplayPlayer-<version>-full.nupkg` | Windows | The whole application, for a fresh install or a repair |
| `AWBWReplayPlayer-<version>-delta.nupkg` | Windows | What an existing install actually downloads |
| `RELEASES` | Windows | The index. The updater reads this first to find out what exists |
| `AWBWReplayPlayer.AppImage` | Linux | The whole application in one executable file |
| `AWBWReplayPlayer.AppImage.zsync` | Linux | Lets `AppImageUpdate` fetch only the parts that changed |
| `AWBW.Replay.Player.app.zip` | macOS | The app bundle |

**`RELEASES` and the packages are not optional.** A release holding only the
installer installs perfectly well and then never updates again, because the
updater has nothing to read.

The delta is generated against whatever is already in the release directory, so
the workflow pulls the previous release down (`Squirrel.exe github-down`) before
packing. That step is allowed to fail, because the first release has nothing to
pull.

Once it has served that purpose, the previous release's **full** package is
deleted rather than uploaded again: an install too old to follow the deltas
downloads the newest full package, never an older one. The deltas stay, because
they are the update path, and `RELEASES` is pruned to name only what is actually
there. A release carries one full package, a handful of deltas and the index.

This happens after packing, not before. Delete the old package first and there
is nothing to write a delta against, which turns every update back into a sixty
megabyte download.

## The package id

The pack id is `AWBWReplayPlayer`, the same as upstream's. This is deliberate:
Squirrel installs to `%LocalAppData%\<packId>`, but the app keeps its replays and
settings in `%AppData%\AWBWReplayPlayer` whatever the install is called. Two
installs under different ids would share one settings file and fight over it, so
a build from here replaces an upstream install rather than sitting beside it.

## Linux

An AppImage is the whole application plus three things at the root of an
`AppDir`: something to run, a desktop entry describing it, and an icon that
entry names. The icon is already in the repository at
`AWBWApp.Resources/Textures/Icon/ICO256.png`, so nothing is converted.

`AppRun` is a script rather than a symlink into `usr/bin` because the executable
is called `AWBW Replay Player`, spaces and all.

Two details about the runner rather than the format: `appimagetool` is pinned to
a tagged release rather than its `continuous` tag, so that a release built today
and one built next year are built by the same tool; and it runs with
`--appimage-extract-and-run`, because GitHub runners have no FUSE for it to
mount itself with.

The tool now lives in `AppImage/appimagetool`. The AppImageKit release it used
to ship in still exists with every asset renamed to `obsolete-` something, so
the old URL answers with a 404 *page* rather than an error, and a download
without `--fail` saves that page and calls it a tool.

## macOS

A plain bundle: the publish output in `Contents/MacOS`, the `.icns` already in
the repository in `Contents/Resources`, and an `Info.plist` naming the
executable.

**It is built on a Linux runner.** Nothing about it needs a Mac - the publish is
a cross-compile, a bundle is a directory with a plist in it, and the one step
that would genuinely require macOS is signing, which is not happening anyway.
What a macOS runner does bring is a queue: GitHub's arm64 pool is capacity
constrained, and the first release that asked for one never got it. The job sat
queued for fifteen minutes and was cancelled with *"The job was not acquired by
Runner of type hosted"*.

Two macOS tools had to be replaced, and both matter more than they look.
`plutil -lint` becomes a line of Python, so a malformed plist is still caught
here rather than on someone's Mac. `ditto -c -k` becomes `zip -y`, where the
`-y` is the whole point: without it the symlinks inside the bundle are followed
and copied, leaving an archive that is both larger and no longer something macOS
will open.

**It is not signed.** Nobody here has an Apple Developer account, so Gatekeeper
will refuse to open it until the person who downloaded it right-clicks and
chooses Open, or clears the quarantine attribute by hand. Upstream's bundle is
in the same position.

It is built for Intel, which Apple silicon runs under Rosetta. A universal
bundle would mean publishing twice and splicing the two binaries together.

## Only Windows updates itself

`AWBWAppGameDesktop.CreateUpdateManager` returns the Squirrel updater on Windows
and a do-nothing one everywhere else. So the Windows assets are the ones that
have to be right: a mistake there installs itself on everyone who already has
the app, while a mistake in the other two is only ever seen by someone
downloading afresh.

The `.zsync` file is there for `AppImageUpdate` and similar external tools. The
app itself does not use it.

## Checking the Windows half by hand

The two steps that produce the Windows assets can be run locally, which is how
they were arrived at:

```powershell
dotnet publish AWBWApp.Desktop/AWBWApp.Desktop.csproj -c Release -r win-x64 --self-contained true -p:Version=0.13.2 -o publish
```

```powershell
& "$env:USERPROFILE\.nuget\packages\clowd.squirrel\2.9.42\tools\Squirrel.exe" pack -u AWBWReplayPlayer -v 0.13.2 -p publish -r Releases --packTitle "AWBW Replay Player" -i AWBWApp.Desktop/game.ico
```

A correct pack reports `Detected SquirrelAwareApp 'AWBW Replay Player.exe'` and
leaves a `Releases` folder of around 120 MB.
