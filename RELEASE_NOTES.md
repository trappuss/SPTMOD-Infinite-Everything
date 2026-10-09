## Infinite Everything 2.5.0

For SPT 4.1.x (not 4.0.x). Extract the zip into your SPT folder (the one with `EscapeFromTarkov.exe`), start the server, then in game press F12 > **trappuss-InfiniteEverything**.

### New

- The hold-R reload menu lists every magazine that fits your gun and every round type it takes, not only what you carry (mods' items included). It needs Infinite ammo.
- Magazines you do not own are borrowed, with no free inventory space needed. Your own magazine is kept aside while the borrowed one is in the gun, comes back when the borrowed one leaves the gun, and is put back in the gun at raid end.
- Your pick sticks: R reloads the magazine or round type you picked again, also after holstering or switching guns.
- F12 option: "Infinite Magazine Options (Hold-R Scroll Menu)", on by default.

### Changed

- "Launcher and flare ammo" is gone as a separate option. Grenade launchers and flare guns now follow Infinite ammo.
- RShG-2: with Infinite magazine it gets a new rocket after every shot. With Infinite ammo alone, press R after a shot to put a new rocket in.
- Double-tap R plays the fast reload also when the gun only has the magazine that is in it.
- R now reloads a gun that is already full.
- A reload with a completely full inventory no longer throws the old magazine on the ground. It is kept aside and comes back when there is room.

### Fixed

- RShG-2: it used to stay an empty tube after one shot.
- Revolvers: a reload with rounds you do not carry could loop forever without loading anything.

### In the zip

- `BepInEx\plugins\InfiniteEverything\` - client plugin
- `SPT_Runtime\user\mods\InfiniteEverything\` - server part (needed for infinite money and hideout fuel/filters)
