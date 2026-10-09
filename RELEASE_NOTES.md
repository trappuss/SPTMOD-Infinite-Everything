## Infinite Everything 2.5.0

For SPT 4.1.x (not 4.0.x). Extract the zip into your SPT folder (the one with `EscapeFromTarkov.exe`), start the server, then in game press F12 > **trappuss-InfiniteEverything**.

**New: any magazine / round type from the hold-R menu**
- With Infinite ammo on, the hold-R reload menu lists every magazine that fits your gun and every round type it takes, not only what you carry (mods' items included). Shotguns, bolt-actions with internal magazines, revolvers, break-actions and underbarrel launchers list round types.
- Magazines you do not own are borrowed, with no free inventory space needed: your own magazine is kept aside while the borrowed one is in the gun, comes back when the borrowed one leaves the gun, and is put back in the gun at raid end. Your inventory ends exactly as it started.
- Your pick sticks: R reloads the magazine / round type you picked again instead of switching back, also after holstering or switching guns.
- Can be turned off in F12: "Infinite Magazine Options (Hold-R Scroll Menu)".

**Changed**
- "Launcher and flare ammo" is gone as a separate option. Grenade launchers and flare guns now follow **Infinite ammo**.
- RShG-2: with **Infinite magazine** it gets a new rocket after every shot. With **Infinite ammo** alone, press R after a shot to put a new rocket in.
- Double-tap R plays the fast reload also when the gun only has the magazine that is in it.
- A reload with a completely full inventory no longer throws the old magazine on the ground: it is kept aside and comes back when there is room.

**Fixes**
- RShG-2: it used to stay an empty tube after one shot.
- Revolvers: a reload with rounds you do not carry could loop forever without loading anything.

**In the zip**
- `BepInEx\plugins\InfiniteEverything\` - client plugin
- `SPT_Runtime\user\mods\InfiniteEverything\` - server part (needed for infinite money and hideout fuel/filters)
