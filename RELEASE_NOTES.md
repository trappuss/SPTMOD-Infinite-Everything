## Infinite Everything 2.4.0

Extract the zip into your SPT folder (the one with `EscapeFromTarkov.exe`), start the server, then in game press F12 > **trappuss-InfiniteEverything**.

**Changes since 2.3.0**
- **Fix:** with Infinite ammo, empty spare magazines in your rig/pockets were never refilled (an error was thrown and the normal reload ran instead).
- **Loose-round guns load to full:** pump/tube shotguns, bolt-actions with internal magazines, revolvers, break-action and single-shot guns. Previously a reload stopped at the number of rounds you carried.
- **No matching rounds on you?** Those guns still reload, with the normal animation: temporary rounds are used for the reload and removed right after (needs one free rig/pocket spot; tubes and internal magazines are filled directly without one).
- **Single round into the chamber** (R on a gun without a usable magazine) no longer uses up the round.
- In F12 the mod is now listed as **trappuss-InfiniteEverything** (naming rule of the SPT Forge). Your settings are kept.

**In the zip**
- `BepInEx\plugins\InfiniteEverything\InfiniteEverything.dll` - client plugin
- `SPT_Runtime\user\mods\InfiniteEverything\InfiniteEverythingServer.dll` - server part (needed for infinite money and hideout fuel/filters)
