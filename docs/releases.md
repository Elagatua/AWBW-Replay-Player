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

## Which repository

There are two remotes, and only one of them is ours:

| Remote | Repository | What it is |
|---|---|---|
| `fork` | `Elagatua/AWBW-Replay-Player` | This project. Branches, pull requests, tags and releases all go here |
| `origin` | `DeamonHunter/AWBW-Replay-Player` | The repository this was forked from. Read-only as far as this work goes |

Nothing here is ever pushed to `origin`. `git push origin` would be aimed at
somebody else's project.

`gh` has **no default repository configured**, so a bare `gh pr list` or
`gh release list` answers about whichever repo it guesses, which is usually not
this one and usually silently. Pass `--repo Elagatua/AWBW-Replay-Player` to
every `gh` command.

## The path a release takes

Work happens on `improvements` and reaches `master` through a pull request
**within the fork** — `Elagatua:improvements` into `Elagatua:master`. The tag
then goes on the merge commit that lands on `master`, not on the branch:
`v0.14.5` points at `dca7d271`, which is *"Merge pull request #13 from
Elagatua/improvements"*.

```powershell
gh pr create --repo Elagatua/AWBW-Replay-Player --base master --head improvements --title "..." --body-file body.md
```

```powershell
gh pr merge 15 --repo Elagatua/AWBW-Replay-Player --merge
```

Merge commits rather than squashes, because that is what the tags point at.

**Auto-merge rarely helps here.** The repository allows it, but GitHub refuses
it with *"Pull request is in clean status"* whenever nothing is left to wait
for — and nothing is, because the PR check (`dotnet.yml`) is not a *required*
check on `master`, so a PR is mergeable the moment it opens. Waiting for the
check and then merging is the whole of it. Making `dotnet.yml` a required check
through branch protection would change that, and nobody has asked for it.

## Choosing the version

Both series of tags are visible locally, and only one of them is this fork's:

| Series | Lives on | Whose |
|---|---|---|
| `v0.14.x` | `fork` | This project |
| `v1.0.x` | `origin` | Upstream's |

`git tag --sort=-v:refname | head` lists **upstream's first**, because the local
repository has both remotes' tags. Taking the top of that list and adding one
would publish a release claiming to be a version of a different project, and the
updater would hand it to everyone. Ask the fork instead:

```powershell
gh release list --repo Elagatua/AWBW-Replay-Player --limit 5
```

Releases here have gone up a patch at a time, whatever the size of the change.
Use the next patch unless the person asks for something else — the version is
theirs to choose, not a detail to infer.

## Checking a release landed

Pushing the tag starts `release.yml`; it takes about four minutes. It is worth
watching rather than assuming, because a failure part-way leaves a tag with no
release behind it.

```powershell
gh run list --repo Elagatua/AWBW-Replay-Player --workflow release.yml --limit 2
```

```powershell
gh run watch <run-id> --repo Elagatua/AWBW-Replay-Player --exit-status
```

Two things to confirm afterwards. All six jobs — `test`, `version`, `windows`,
`macos`, `linux`, `release` — and that the index is whole:

```powershell
gh release download v0.14.6 --repo Elagatua/AWBW-Replay-Player --pattern RELEASES --dir .
```

`RELEASES` must name the **full** package as well as the deltas. That last line
is the one the newline bug under [What a release
contains](#what-a-release-contains) eats, and the one an install too old to
follow the deltas depends on. v0.14.3 shipped without it.

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

**Squirrel writes `RELEASES` with no newline on the end.** A `while read` loop
over it drops the last line, which is the full package - the one entry an
install too old for the deltas needs to find. v0.14.3 shipped that way and had
its index repaired by hand. Anything that reads this file line by line needs
`|| [ -n "$entry" ]` on the loop.

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
a cross-compile, a bundle is a directory with a plist in it, and even the
signing is done by a reimplementation of `codesign` that runs anywhere. What a
macOS runner does bring is a queue: GitHub's arm64 pool is capacity
constrained, and the first release that asked for one never got it. The job sat
queued for fifteen minutes and was cancelled with *"The job was not acquired by
Runner of type hosted"*.

Three macOS tools had to be replaced, and each matters more than it looks.
`plutil -lint` becomes a line of Python, so a malformed plist is still caught
here rather than on someone's Mac. `codesign -dv` becomes a few more lines of
it, reading the Mach-O directly to confirm a signature landed. `ditto -c -k`
becomes `zip -y`, where the `-y` is the whole point: without it the symlinks
inside the bundle are followed and copied, leaving an archive that is both
larger and no longer something macOS will open.

It is built for Intel, which Apple silicon runs under Rosetta. A universal
bundle would mean publishing twice and splicing the two binaries together.

### Signing

**The bundle is ad-hoc signed, and that does not get it past Gatekeeper.** This
was tried, shipped in v0.14.9, and tested on a Mac: a downloaded copy is still
refused with *"is damaged and can't be opened"*. An ad-hoc signature carries no
certificate, and for a quarantined app Gatekeeper treats that as no valid
signature at all. Expecting it to earn the softer *"developer cannot be
verified"* prompt was simply wrong.

| What the bundle carries | What the person who downloaded it sees |
|---|---|
| A signed executable in an unsealed bundle, up to v0.14.8 | *"is damaged and can't be opened"* |
| An ad-hoc signed bundle, from v0.14.9 | the same thing |
| Developer ID, notarized and stapled | it opens |

So the only thing that helps a person today is clearing the quarantine
attribute by hand, and on macOS 15 and later that is the *only* route, because
the right-click-and-choose-Open bypass no longer exists:

```
xattr -dr com.apple.quarantine "/Applications/AWBW Replay Player.app"
```

**The signing step stays anyway**, for two reasons that have nothing to do with
that dialog.

The first is that what shipped before it was wrong on its own terms. It was
never the case that nothing was signed: `dotnet publish` ad-hoc signs the
apphost itself, so the executable inside v0.14.8 already carried a 1112-byte
signature. What was missing was everything around it - no
`Contents/_CodeSignature/CodeResources` sealing the bundle, and an identifier
of `apphost` rather than the `CFBundleIdentifier` in `Info.plist`. The same
executable in v0.14.9 carries 6144 bytes signed as
`com.github.elagatua.awbwreplayplayer` with `CodeResources` beside it, and its
code directory's Info.plist and CodeResources hashes match the files they seal.

The second is that it is the groundwork for the only real fix. Going from here
to a **Developer ID** certificate is a matter of naming one, not of building the
step: `--p12-file` and `--code-signature-flags runtime` in place of no
arguments. What that additionally needs is an Apple Developer Program
membership at $99 a year, which nobody here has, and a macOS runner, because
`notarytool` and `stapler` have no equivalent that runs anywhere else. Note
that the hardened runtime `runtime` flag is **required** for notarization and
will stop .NET's JIT dead unless `com.apple.security.cs.allow-jit` and
`com.apple.security.cs.allow-unsigned-executable-memory` go in an entitlements
file alongside it. That is why it is not asked for now.

Signing is done by [rcodesign](https://github.com/indygreg/apple-platform-rs),
which reimplements `codesign` in Rust and so runs on the Linux runner with
everything else. Naming no certificate is what makes the signature ad-hoc, and
recursion into the dylibs sitting beside the executable is on by default.

The job then does two things, both because signing rewrites the executable in
place. It re-applies the executable bit rather than assume it survived, and it
reads the Mach-O load commands looking for `LC_CODE_SIGNATURE`, failing the
build when it is absent. **`rcodesign sign` exits zero whether or not it found
anything to sign**, so without that check the signing could quietly stop
happening and nothing here would say so.

None of this can be checked by running the app, since the runner is Linux. It
can all be checked by unzipping a release asset and reading the Mach-O load
commands and the code directory out of the executable, which needs no Mac.
Upstream's bundle is not signed at all.

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
