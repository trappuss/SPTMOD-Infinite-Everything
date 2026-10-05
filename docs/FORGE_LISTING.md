# SPT Forge listing: Infinite Everything

Use this text and these values when you create the mod and add a version on [The Forge](https://forge.sp-tarkov.com). `FORGE_PREP.bat` opens the pages and copies the download link for you.

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
| Version | 2.4.0 |
| SPT version | 4.1.x (`~4.1.0`) |
| Download link | https://github.com/trappuss/SPTMOD-Infinite-Everything/releases/download/v2.4.0/SPTMOD-Infinite-Everything-2.4.0.zip |
| VirusTotal link | Upload this exact zip to VirusTotal and paste the result link. Every version needs a new scan. |
| Dependencies | None |
| Changelog | Copy from RELEASE_NOTES.md |

## Description (paste as-is)

**Infinite Everything** gives you 18 "infinite" options that you switch on and off separately in the F12 menu. They all work together. One master hotkey turns the whole mod on or off; it is **not bound by default**.

It only affects you, never bots. It also only works in single-player SPT: it needs the SPT client and server, and it can't do anything in live Tarkov.

### Options

- **Weapons**
  - **Infinite ammo** (on by default):
    - You still reload, but reloading never costs anything. Your magazine goes back into your rig, pockets or backpack, refilled, even on a double-tap quick reload.
    - Shotguns, bolt-actions, revolvers and break-actions load to full, even with no rounds on you.
  - **Infinite magazine**: firing never uses rounds.
  - **Infinite weapon durability**.
  - **Weapon reliability**: no malfunctions and no overheating.
  - **Launcher and flare ammo**, including single-use launchers.
- **Grenades, items and money**
  - **Infinite grenades**: you get the same type back in the same slot, and it stays selected.
  - **Infinite item usage**: meds, food, drinks and stims.
  - **Infinite key usage**: keys, keycards and the Labs card.
  - **Infinite money, including GP coins.** It is non-destructive: no money is ever added to or removed from your stash. Turn it off and you have exactly what you had before. It covers:
    - traders and the flea market;
    - repairs, insurance and healing;
    - hideout upgrades and the scav case;
    - in raid, paid exfils and BTR services.
- **Player**
  - God mode.
  - Infinite energy and hydration.
  - Infinite stamina.
  - Infinite armor durability.
  - Infinite carry weight.
  - Infinite raid time.
- **Turrets**: the belt refills when empty, or rounds are never used.
- **Hideout**: infinite generator fuel, water filters and air filters.

### Install

1. Download the zip.
2. Extract it into your SPT folder, the one that contains `EscapeFromTarkov.exe`. It adds:
   - `BepInEx\plugins\InfiniteEverything\`
   - `SPT_Runtime\user\mods\InfiniteEverything\`
3. Start the SPT server. The log shows `[Infinite Everything] server part 2.4.0 loaded`.
4. Start the game, press **F12** and open **trappuss-InfiniteEverything**.

If you used the old *InfiniteAmmo* plugin, delete its folder first. The two are marked incompatible.

### Usage

- Every option is off by default except Infinite ammo.
- Turning an option, or the whole mod, off undoes it right away.
- Infinite money and the hideout option need the server part. Without it, those two options do nothing.

### Source and building

The full source code is on [GitHub](https://github.com/trappuss/SPTMOD-Infinite-Everything), along with the build script and the options spreadsheet.
