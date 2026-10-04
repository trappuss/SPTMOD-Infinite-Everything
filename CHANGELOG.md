# Infinite Everything changelog

## 2.3.0 (2026-10-04)
- First public release (GitHub). Release zip extracts straight into the SPT folder.
- One version for both parts (Directory.Build.props); server part reports 2.3.0.
- BUILD_AND_INSTALL.bat asks for the SPT folder once (build.local.cfg, not committed) and packages dist\SPTMOD-Infinite-Everything-<version>.zip; COLLECT_LOG.bat uses the same folder.
- F12: section 5 (Turrets) now listed before 6 (Hideout); Infinite ammo description covers the no-spare self-reload.
- Code cleanup: healing refund reuses the shared money snapshot; StateSync file renamed; Labs keycard note verified against the SPT 4.1.6 server.

## 2.2.2 (2026-10-04)
- Fix: with Infinite grenades, throwing the selected grenade switched the grenade slot (key G) to another grenade type. The game's grenade slot view moves its selection when the thrown grenade is removed and appended the replacement last; the replacement is now put first and selected again (FastAccessGrenadeItemView.SetNewTopPriorityGrenade).

## 2.2.1 (2026-10-04)
- Infinite ammo with no spare magazine: R now plays a normal reload with the gun's own magazine (filled first; moved out and straight back in within the same step), so nothing is added to or removed from the inventory. Falls back to an instant refill only when there is no free spot at all. A full, chambered gun is left alone.
- With only empty spare magazines, one of the same type as the gun's magazine is preferred for the refill.
- A self-reload is not refilled again afterwards (the mag ends like any full mag: one round in the chamber).

## 2.2.0 (2026-10-04)
- New: Infinite fuel and filters (section 6. Hideout). Server part skips HideoutHelper.UpdateFuel / UpdateWaterFilters / UpdateAirFilters for the session; the hideout screen's own simulation (ResourceConsumer) reads zero consumption.
- Infinite money now also covers in-raid payments: paid exfils and BTR/trader services give the money back (you still need it on you). GP coins confirmed covered (a currency on both client and server).
- Server part: state is now /infiniteeverything/state {money, hideout} and is saved in state.json next to the server dll, so it survives a server restart.

## 2.1.0 (2026-10-04)
- Fix: Infinite ammo with nothing loaded left. R now fills an empty spare magazine for the reload; with no spare at all it refills the magazine in the gun and chambers a round. Refills are always to full.
- Infinite ammo now also covers loose-round reloads (shotgun tubes, internal magazines, revolvers, break-action barrels).
- Fix: Infinite magazine (and turret magazine) on a gun that is already empty: it is refilled and a round chambered as soon as the gun is idle.
- New options: weapon reliability (no malfunctions/overheating), launcher and flare ammo (incl. single-use launchers), infinite key usage (split from item usage), infinite carry weight, infinite raid time, infinite money.
- Infinite money: new server part (SPT_Runtime/user/mods/InfiniteEverything) skips PaymentService.AddPaymentToOutput for the session while on; client screens (traders, flea buy/fee/renew, repair, insurance, healing, hideout upgrade/scav case) accept it. Money is never added or removed.
- Some option names changed (ammo, item usage, section 3): check those in F12.

## 2.0.0 (2026-10-04)
- Renamed from InfiniteAmmo 1.0.0 (GUID com.trappuss.infiniteeverything; marked incompatible with the old plugin, the build .bat moves it to BepInEx\_disabled after asking).
- Kept: infinite ammo (magazines kept and refilled on reload, incl. quick reload), infinite magazine.
- New: infinite weapon durability, infinite grenades (replacement grenade + quick-slot rebind), infinite item usage (meds, food, drinks, stims, keys/keycards), infinite health (SetDamageCoeff(0) + death block, heals on enable), infinite energy/hydration, infinite stamina (body, arms, breath), infinite armor durability, turret ammo (belt refills when empty), turret magazine (rounds never used).
- Every patch is enabled separately: one failing patch only disables its option (logged).
