## Infinite Everything 2.3.0

This is the first public release, for SPT 4.1.x. Extract the zip into your SPT folder (the one with `EscapeFromTarkov.exe`), start the server, then press F12 in game and open **Infinite Everything**.

It has 18 options in total; [see the README](https://github.com/trappuss/SPTMOD-Infinite-Everything#options) for the full list. Infinite ammo is on by default and everything else starts off. The master hotkey is unbound.

**What's in the zip:**

- `BepInEx\plugins\InfiniteEverything\InfiniteEverything.dll`: the client plugin.
- `SPT_Runtime\user\mods\InfiniteEverything\InfiniteEverythingServer.dll`: the server part. It is needed for infinite money and hideout fuel/filters.

**Changes since 2.2.2:**

- Cleanup and packaging. There are no changes to the options themselves.
- One version number for both parts (`Directory.Build.props`).
- The build script asks for your SPT folder once and builds the release zip.
- The F12 sections are now in order (5. Turrets before 6. Hideout).

Remove the old InfiniteAmmo plugin if you still have it installed.
