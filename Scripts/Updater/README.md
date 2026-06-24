# RimWorld Together MP Updater

A small Windows script that pulls our playtester build of the RimWorld Together
mod down from GitHub and replaces whatever Steam currently has installed.
Designed to be run **before every play session** so that Steam Workshop syncs
cannot leave you on a different version than everyone else.

## One-time install

1. **Download these two files** from this folder:
   - `UpdateRTMP.bat`
   - `Update-RTMP.ps1`

2. Put them anywhere convenient on your machine — Desktop is fine, or a
   `C:\Tools\RTMPUpdater\` folder. **Both files must live in the same folder.**

3. (Optional) Right-click `UpdateRTMP.bat` and create a shortcut. Pin the
   shortcut to your taskbar. Run it from the taskbar each play session.

That's it. No subscription changes, no Workshop unsubscribe. You stay
subscribed to the Workshop mod exactly as you are now.

## Each play session

1. **Close RimWorld and the launcher fully** if they are open.
2. **Double-click `UpdateRTMP.bat`.**
3. Wait. One of three things will print:
   - `Up to date. Nothing to do.` — good, launch RimWorld normally.
   - `Files drifted from manifest (Steam may have re-synced). Re-applying our build.` — good, the script just fixed it.
   - `Applying new release mp-...` — good, you just got the newest build.
4. When the window says it is closing, launch RimWorld normally.

If the updater **refuses to run** because RimWorld is still open, fully quit
RimWorld (including the launcher) and try again.

## What it does, plainly

- Finds your Steam install via the registry.
- Finds the `steamapps\workshop\content\294100\3005289691\` folder where Steam
  keeps RimWorld Together.
- Asks GitHub: "what's the newest `mp-*` release for this mod?"
- Compares to what you already have. If the same, exits in about 1 second.
- If different (or if Steam clobbered your files since last run), downloads the
  release zip, verifies every file's SHA-256 against the manifest published
  alongside the release on GitHub, and swaps it into place.
- Keeps the last two installs in `3005289691.bak-YYYYMMDD-HHMMSS` folders next
  to the live install, in case you ever need to roll back.

## Optional: auto-launch RimWorld

If you'd like the updater to launch RimWorld for you when it's done, either:

- Run `UpdateRTMP.bat` with the `-Launch` flag, or
- Drop a `config.json` next to the script with `{ "AutoLaunch": true }`.

## Optional: tell the updater where Steam is

The updater finds your Steam library automatically by reading the registry and
`libraryfolders.vdf`. If for some reason that fails on your machine, create a
`config.json` next to the script with:

```json
{
  "ModFolderOverride": "D:\\SteamLibrary\\steamapps\\workshop\\content\\294100\\3005289691"
}
```

Use the actual path on your machine. Double backslashes are required in JSON.

## When something is wrong

- The updater writes a log to `%LOCALAPPDATA%\RTMPUpdater\Update-RTMP.log`
  (paste that path into Explorer's address bar).
- Downloaded zips are cached at `%LOCALAPPDATA%\RTMPUpdater\cache\` — safe to
  delete if you need to force a fresh download.
- The last two known-good installs are in `<workshop>\3005289691.bak-...` —
  you can manually rename one back to `3005289691` to roll back.

If you connect to the server and it tells you "Server needs updating," that
means Steam just clobbered your files between when you ran the updater and
when you launched the game. Quit RimWorld, run the updater again, relaunch.
