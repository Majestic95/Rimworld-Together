# RimWorld Together — Local Server Updater

A Windows script that rebuilds the RT server from source in this repo and
deploys it to the local install location used by the in-game "Host" flow.

Intended for the **host's machine only** — friends connecting to a server
don't run one themselves.

## Prerequisites

- The repo cloned somewhere on disk (you're reading this file from inside it).
- **.NET 8 SDK** installed (`dotnet --version` returns 8.x).

## One-time setup

Nothing — the script works in place. You can run it from the repo location:

```cmd
Scripts\Server\UpdateServer.bat
```

Or copy `UpdateServer.bat` + `Update-Server.ps1` somewhere convenient (Desktop,
taskbar shortcut). **Both files must live in the same folder.**

## Each time you want to ship a server change

1. **Stop your running server cleanly.** Switch to the server console window
   and type `quit`. (Don't kill the process — in-flight saves can corrupt.)
2. **Double-click `UpdateServer.bat`.**
3. Wait for `dotnet publish` to complete (~30 seconds for a clean build,
   ~5 seconds for an incremental build).
4. The script backs up your current `GameServer.exe` and copies the new build
   into place.
5. Relaunch the server when ready.

## What it does, plainly

- Reads the server install location (default: the path RimWorld's in-game
  "Host" flow uses):
  ```
  %USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\RimWorld Together\Local Server\GameServer.exe
  ```
- Refuses to run if a server is detected at that exact path (so we never
  hot-swap a process that has open file handles).
- Runs `dotnet publish Source/Server/GameServer.csproj -c Release -r win-x64
  --self-contained true -p:PublishSingleFile=true`. Output lands at
  `Source/Server/bin/Release/net8.0/win-x64/publish/GameServer.exe`.
- Backs up the current install as `GameServer.exe.bak-YYYYMMDD-HHMMSS`.
- Copies the freshly built EXE into the install location.
- Keeps the last 3 timestamped backups. Older `.bak-*` files are auto-pruned.
- Backups with other suffixes (e.g. `.workshop-backup`, `.preWipe-*`) are
  **never touched** — only timestamped `.bak-*` files are auto-managed.

## Flags

| Flag | Effect |
|---|---|
| `-Launch` | Start the new server immediately after the swap. |
| `-NoBuild` | Skip `dotnet publish`. Use when iterating after a recent build. Requires `Source/Server/bin/.../publish/GameServer.exe` to already exist. |
| `-InstallDir <path>` | Override the install location. |
| `-ConfigPath <path>` | Use an explicit config.json instead of the one next to the script. |

## Optional: config.json

Drop a `config.json` next to the script with overrides:

```json
{
  "InstallDir": "D:\\rimworld-server\\Local Server",
  "AutoLaunch": true
}
```

`AutoLaunch: true` is equivalent to always passing `-Launch`.

## What you DON'T need to do

- You do NOT need to coordinate this with friends. They don't run a server.
  Whenever you redeploy, they connect to the new server as usual.
- You do NOT need to bump a version number anywhere. The version is baked
  into `Source/Assemblies/RTShared.dll` and ships with the new build
  automatically.

## Mismatched-version footgun

If your server is running version X and friends' clients are on a different
version Y, RT's version handshake fails closed with a "Server needs updating"
message. The script does NOT enforce this — match versions manually:

- After running this script, your server is on whatever
  `Source/Assemblies/RTShared.dll` reports in the current repo state
  (currently `26.6.23.1`).
- Friends' clients must match. Push a new `mp-*` release; friends run
  `UpdateRTMP.bat` to pick it up.

## When something is wrong

- Script writes errors to console; no separate log file (use
  `> Update-Server.log 2>&1` if you want one).
- Backups live in the same folder as `GameServer.exe`. Roll back manually
  by renaming `GameServer.exe.bak-<timestamp>` to `GameServer.exe`.
- `dotnet publish` failures are usually a missing .NET 8 SDK or a partial
  upstream merge that doesn't compile — fix the source, re-run.
