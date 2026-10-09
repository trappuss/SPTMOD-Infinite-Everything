# SPT Forge listing: Infinite Everything

Use this text and these values when you create the mod and add a version on [The Forge](https://forge.sp-tarkov.com). `FORGE_PREP.bat` opens the pages and copies the download link for you.

The description and the changelog use only headings, paragraphs and one-level lists, with a blank line around each block. Nested lists rendered badly on the Forge, so keep it that way.

## Mod (entered once)

| Field | Value |
|---|---|
| Name | Infinite Everything |
| GUID | `com.trappuss.infiniteeverything` (the same in the client plugin and the server part) |
| License | MIT |
| Source code | https://github.com/trappuss/SPTMOD-Infinite-Everything |
| Teaser | 18 switchable "infinite" options for SPT, all in F12: ammo, magazines, grenades, meds, keys, money, health, stamina, turrets, hideout fuel and more. Money is never added or removed. |

## Version (entered for every release)

| Field | Value |
|---|---|
| Version | 2.5.0 |
| SPT version | 4.1.x only (`~4.1.0`). Do not tick 4.0.x: 4.0 runs EFT 0.16.9-40087 and a .NET 9 server, this mod is built for EFT 40743 and a .NET 10 server |
| Download link | https://github.com/trappuss/SPTMOD-Infinite-Everything/releases/download/v2.5.0/SPTMOD-Infinite-Everything-2.5.0.zip |
| VirusTotal link | Upload this exact zip to VirusTotal and paste the result link. Every version needs a new scan. |
| Dependencies | None |
| Changelog | Copy from RELEASE_NOTES.md: everything from the "New" heading down to, but not including, "In the zip" |

## Description (paste everything below this line)

**Infinite Everything** gives you 18 "infinite" options that you switch on and off separately in the F12 menu. They all work together. One master hotkey turns the whole mod on or off; it is **not bound by default**.

It only affects you, never bots. It only works in single-player SPT: it needs the SPT client and server, and it can't do anything in live Tarkov.

## Options

### Weapons

**Infinite ammo** (on by default)

- You still reload, but reloading never costs anything. Your magazine goes back into your rig, pockets or backpack, refilled, even on a double-tap quick reload.
- R and double-tap R always reload, also on a full gun and with only the magazine that is in the gun.
- Nothing is dropped when your inventory is full: the old magazine is kept aside and comes back when there is room.
- Shotguns, bolt-actions, revolvers, break-actions, grenade launchers and flare guns load to full, even with no rounds on you.
- R puts a new rocket in an empty RShG-2.

**Infinite Magazine Options (Hold-R Scroll Menu)** (on by default)

- The hold-R reload menu lists every magazine that fits your gun and every round type it takes, even ones you don't carry.
- Magazines you don't own are borrowed, and that needs no free inventory space. Your own magazine is kept aside, comes back when the borrowed one leaves the gun, and is back in the gun at raid end.
- Whatever you pick sticks for later reloads.

**Other weapon options**

- **Infinite magazine**: firing never uses rounds. The RShG-2 gets a new rocket after every shot.
- **Infinite weapon durability**.
- **Weapon reliability**: no malfunctions and no overheating.

### Grenades, items and money

- **Infinite grenades**: you get the same type back in the same slot, and it stays selected.
- **Infinite item usage**: meds, food, drinks and stims.
- **Infinite key usage**: keys, keycards and the Labs card.
- **Infinite money, including GP coins**: traders, the flea market, repairs, insurance, healing, hideout upgrades and the scav case. In raid, paid exfils and BTR services give your money back.

Infinite money is non-destructive: no money is ever added to or removed from your stash. Turn it off and you have exactly what you had before.

### Player

- God mode.
- Infinite energy and hydration.
- Infinite stamina.
- Infinite armor durability.
- Infinite carry weight.
- Infinite raid time.

### Turrets

- **Turret ammo**: the belt refills when empty.
- **Turret magazine**: rounds are never used.

### Hideout

- Infinite generator fuel, water filters and air filters.

## Install

1. Download the zip.
2. Extract it into your SPT folder, the one that contains `EscapeFromTarkov.exe`.
3. Start the SPT server. The log shows `[Infinite Everything] server part 2.5.0 loaded`.
4. Start the game, press **F12** and open **trappuss-InfiniteEverything**.

The zip adds two folders: `BepInEx\plugins\InfiniteEverything\` and `SPT_Runtime\user\mods\InfiniteEverything\`.

If you used the old *InfiniteAmmo* plugin, delete its folder first. The two are marked incompatible.

## Usage

- Every option is off by default except Infinite ammo and Infinite Magazine Options (Hold-R Scroll Menu).
- Turning an option, or the whole mod, off undoes it right away.
- Infinite money and the hideout option need the server part. Without it, those two options do nothing.

## Source and building

The full source code is on [GitHub](https://github.com/trappuss/SPTMOD-Infinite-Everything), along with the build script and the options spreadsheet.
