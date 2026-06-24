# MP Release Process

How to ship a new playtester build of this fork to LizardMan + akaitzgoat (and
anyone else using the [updater](../Scripts/Updater/README.md)).

## TL;DR

```powershell
# from repo root
powershell -ExecutionPolicy Bypass -File .\Scripts\Release\Build-Release.ps1

# then run the gh release create command the build script prints at the end
```

Friends pick up the new release on their next `UpdateRTMP.bat` run.

## Tagging convention

All personal builds use the prefix `mp-` so they don't collide with upstream
sync tags (`26.5.24.1`, `26.6.9.1`, etc.).

Format: `mp-YYYY-MM-DD-N` where `N` starts at 1 and increments for additional
releases on the same day.

Examples: `mp-2026-06-23-1`, `mp-2026-06-23-2`, `mp-2026-06-24-1`.

The build script auto-generates a tag if you don't pass one.

## Build artifacts

Each release publishes exactly two assets:

| Asset | What it is |
|---|---|
| `rt-mp-<tag>.zip` | Full mod-folder snapshot. The updater extracts this directly into the Workshop folder. |
| `manifest.json` | `{ tag, built_at, built_from_commit, files{ "<rel>": "<sha256>" } }`. The updater hash-verifies every file in the zip against this before swapping. |

The zip contains only the mod-folder layout (no `Source/`, no `Scripts/`, no
git). What's inside:

```
About/
1.5/
1.6/
  Assemblies/
    RTClient.dll              <- our build (renamed from GameClient.dll)
    RTNetwork.dll             <- copied from Source/Assemblies/Shared/
    RTShared.dll              <- copied from Source/Assemblies/Shared/
    (plus the third-party DLLs already in 1.6/Assemblies)
  Defs/ Languages/ Sounds/ Textures/
LoadFolders.xml
LICENSE
```

The Steam Workshop `packageId` (`nova.rimworldtogether`) is **unchanged** so
save compatibility holds.

## Step-by-step

### 1. Build

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Release\Build-Release.ps1
```

What it does:

1. Auto-generates a `mp-YYYY-MM-DD-N` tag if none provided.
2. Checks the working tree is clean; warns + prompts if not.
3. Runs `dotnet build Source\Client\GameClient.csproj -c Release`.
4. Stages a clean mod-folder snapshot into `dist\snapshot\`.
5. Renames `GameClient.dll` -> `RTClient.dll`.
6. Strips `.pdb` / `.xml` cruft.
7. Generates `dist\manifest.json` with SHA-256 of every file.
8. Zips the snapshot to `dist\rt-mp-<tag>.zip`.
9. Prints the exact `gh release create` command to run next.

Flags:

| Flag | Effect |
|---|---|
| `-Tag mp-...` | Use this tag instead of auto-generating one. |
| `-SkipBuild` | Skip `dotnet build`. Use when iterating on packaging logic. |
| `-Force` | Overwrite an existing `rt-mp-<tag>.zip` in `dist\`. |

### 2. Publish

Run the `gh release create` command the build script printed. It looks like:

```powershell
gh release create mp-2026-06-23-1 `
    --repo Majestic95/Rimworld-Together `
    --title "MP Build mp-2026-06-23-1" `
    --notes "<release notes here>" `
    "F:\rimworld-together\dist\rt-mp-2026-06-23-1.zip" `
    "F:\rimworld-together\dist\manifest.json"
```

Edit `--notes` to describe what's in this build before running. Keep notes
short and human — friends will see them in the GitHub Releases page if they
look.

### 3. Verify (recommended, especially for the first few releases)

Run the updater against your own install before telling friends to update:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Updater\Update-RTMP.ps1
```

You should see `Applying new release mp-...` (first time) or `Up to date.`
(if you re-run). Launch RimWorld, host a quick session, confirm version
banner reads correctly.

### 4. Notify friends

Send a Discord/whatever message: "New MP build is up, run `UpdateRTMP.bat`
before launching." They run it; the updater handles the rest.

## When upstream publishes a release

Upstream (`RimWorld-Together/Rimworld-Together`) ships its own Workshop
updates on its own cadence. **Whether to incorporate an upstream release into
our fork is a per-release judgment call.** Reasons we might want to:

- A security or stability fix that affects the visit feature.
- A bug fix in `Source/Server/` we'd otherwise have to port manually.
- New RimWorld game-version compat that we want to support.

Reasons we might not:

- Upstream changes conflict with our WIP and merging would block playtest.
- Upstream change is cosmetic only.
- We're mid-experiment and want a stable foundation.

### The merge itself

The fork has **diverged from upstream in ABI-breaking ways** (fork's
`RTNetwork.dll` adds the `ServerClient` type that upstream's doesn't have).
A naive `git merge upstream/development` will conflict in:

- `Source/Assemblies/Shared/RTNetwork.dll`
- `Source/Assemblies/Shared/RTShared.dll`
- Any `Source/Server/` file touching `ServerClient`

Expect a careful manual session, not a clean merge. Plan for:

1. `git fetch upstream`
2. `git log <fork-base>..upstream/development --oneline` — review what's new
3. Decide: full merge, cherry-pick specific commits, or skip
4. Resolve `Assemblies/Shared/*.dll` conflicts by **keeping our fork-built
   versions** unless we explicitly want to adopt upstream's. Their version
   stamp is baked into `RTShared.dll` and changing it will break the version
   handshake for all currently-connected friends until everyone updates.
5. Test a build + run a 3-player session before publishing the next `mp-*`
   release.

If the upstream changes are small and isolated, cherry-pick them onto our
branch rather than merging the whole thing. Less to untangle.

## Rollback

If a release breaks something for friends:

- Friends can roll back manually by renaming the previous backup folder.
  `<workshop>\3005289691.bak-<timestamp>` -> `<workshop>\3005289691`.
  They keep the last two backups automatically.
- For a clean rollback, publish a new `mp-*` release built from a previous
  commit (use `git checkout <sha>` first, then `Build-Release.ps1`, then
  publish, then `git checkout` back). The updater will pull the newer-by-date
  release on next run.
- You can also `gh release delete mp-<bad-tag>` and the updater will fall back
  to the previous `mp-*` release.

## What's NOT shipped via this pipeline

- **The server.** `GameServer.exe` lives on the user's local machine. Build
  it manually with `dotnet publish Source/Server/GameServer.csproj -c Release
  -r win-x64 --self-contained true -p:PublishSingleFile=true` and copy to
  `%LOCALAPPDATA%Low\Ludeon Studios\RimWorld by Ludeon Studios\RimWorld
  Together\Local Server\GameServer.exe`. Friends connect to that server; they
  don't need their own copy.
- **The `Source/` tree.** Source stays in git. The release ships only the
  mod-folder layout.
- **Dev artifacts** (`Dockerfile`, `Makefile`, `Scripts/`, `docs/`, `.claude/`,
  `CLAUDE.md`, etc.). Not part of the mod folder, not in the zip.
