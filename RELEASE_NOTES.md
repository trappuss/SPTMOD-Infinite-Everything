## Infinite Everything 2.5.1

For SPT 4.1.x (not 4.0.x). Extract the zip into your SPT folder (the one with `EscapeFromTarkov.exe`), start the server, then in game press F12 > **trappuss-InfiniteEverything**.

A fix release for 2.5.0. Everyone on 2.5.0 should update.

### Fixed

- In a raid, R on a pistol with no spare magazine refilled the magazine but played no reload. Double-tap was not affected.
- Looting experience was paid for magazines and grenades the mod creates (every double-tap reload, every replacement grenade).
- Infinite energy and hydration paid experience and Metabolism skill for doing nothing.
- A reload in the last second of a raid could leave a borrowed magazine in the gun instead of your own.
- Unloading a borrowed magazine with a full inventory threw it on the ground and could leave the hands stuck. It is now refused with a notice.
- Shotguns, revolvers and break-action guns loaded with rounds you have not examined got the wrong rounds or none in a raid.
- The hold-R menu no longer offers magazines the game refuses to change in a raid.
- A paid exfil refund could double your money stack in a rare case.
- Replacement grenades keep their found-in-raid status and their quick-slot key after a G-key throw.
- Turning god mode off after a BTR ride no longer leaves you invincible.

### In the zip

- `BepInEx\plugins\InfiniteEverything\` - client plugin
- `SPT_Runtime\user\mods\InfiniteEverything\` - server part (needed for infinite money and hideout fuel/filters)
