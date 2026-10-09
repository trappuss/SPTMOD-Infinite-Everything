# Infinite Everything changelog

## 2.5.1 (2026-10-09)
Fixes found by testing 2.5.0 in raids and by a full review of what the game does in a raid that it does not do in the hideout.
- Fix: in a raid, R with no spare magazine (or with a magazine picked in the hold-R menu) refilled the magazine but played no reload whenever the magazine had a free cell to go to, which is every pistol. The game only moves items it "knows" in a raid, and the out-and-back move of the gun's own magazine was refused. Double-tap was not affected. If the game still refuses, a borrowed twin is used and the log says why.
- Fix: looting experience was paid for every item the mod creates: 5 per borrowed or twin magazine (every double-tap) and, since earlier versions, 5 per replacement grenade. Created items are now registered like the gear you bring in. Borrowed magazines also no longer give a Mag Drills skill tick or leave an entry in the profile.
- Fix: Infinite energy and hydration paid raid experience and Metabolism skill, about 6 experience a minute, because the game rewards every refill. The drain is now stopped instead of being refilled.
- Fix: the game keeps input on for about a second after a raid ends and saves the profile right after. A reload in that second could save a borrowed magazine in the gun and lose your own, or leave temporary rounds in the profile. Clean-up now runs at both ends of that second and nothing is borrowed in between.
- Fix: unloading a gun that holds a borrowed magazine with no free space threw the borrowed magazine on the ground and could leave the hands stuck. That unload is now refused with a notice.
- Fix: loose-round guns (tubes, revolvers, break-actions) loaded with a round type you have not examined got the wrong rounds or none in a raid.
- Fix: the hold-R menu no longer lists magazines the game refuses to change in a raid, and a refused pick is cancelled at once with a notice.
- Fix: R with a picked round type is no longer swallowed when the game refuses the reload.
- Fix: paid exfil refund could duplicate the money stack when the payment did not start and the stack was moved by hand.
- Fix: replacement grenades and refunded money keep their found-in-raid status; the quick-slot key survives a G-key quick throw; turning god mode off after a BTR ride no longer leaves you invincible.
- At raid end a magazine that was kept aside tries every free place before giving up.

## 2.5.0 (2026-10-08)
- New: hold-R reload menu lists every magazine that fits the gun and every round type it takes (mods' items too), not only what you carry (Infinite ammo on, in a raid; option "Infinite Magazine Options (Hold-R Scroll Menu)", on by default). Internal-magazine, revolver, break-action and underbarrel guns list round types.
- A magazine you do not own is borrowed, and borrowing needs no free inventory space: your own magazine is kept aside (out of the inventory) while the borrowed one is in the gun. The borrowed one is removed as soon as it leaves the gun (a reload swaps it out, the unload key, dragging it out by hand) and your own magazine comes back; at raid end it is put back in the gun, so the inventory ends exactly as it started. A picked round type uses temporary rounds that are removed after the reload.
- Nothing of your own is dropped any more: a reload with rig, pockets and backpack all full used to throw the old magazine on the ground (as the base game does); it is now kept aside and comes back as soon as there is room, usually in the spot the new magazine came from. A round-type pick on a single-barrel gun is refused when the loaded round has nowhere to go, and at raid end a borrowed magazine is removed even from a jammed gun.
- Your pick is kept: after choosing a magazine or round type in the hold-R menu, R reloads that same magazine / round type again (refilled) instead of switching back, also after holstering or switching guns. With a malfunction, R leaves a picked magazine in the gun and examines the malfunction instead of swapping it out.
- The hold-R menu's full list also works in the hideout while a weapon is in your hands (shooting range, or anywhere with Hideout Uncensored). The hideout player holds a copy of your gear that the game discards on holster, so nothing borrowed there reaches your real inventory.
- Changed: the "Launcher and flare ammo" option is gone. Grenade launchers (underbarrel and standalone) and flare guns now follow Infinite ammo like every other gun. The RShG-2 follows both: with Infinite magazine it gets a new rocket after every shot; with Infinite ammo alone the tube stays empty after a shot and R puts a new rocket in (no animation, the launcher has no reload in the game).
- Changed: double-tap R (quick reload) with only the magazine in the gun (no spare, or a magazine picked in the hold-R menu) now plays the fast reload, also on a full gun: a twin of that magazine is borrowed for the swap and returned like every borrowed magazine. Before, it played the normal reload, and nothing at all until the gun had been fired once. A normal reload with no free spot uses the same twin instead of refilling without an animation. R (single tap) now also reloads a gun that is already full, as the base game does when you have a spare magazine; before, R did nothing until a shot had been fired.
- Fix: a revolver reload with temporary rounds could loop forever without loading anything. The game only loads the 3D models of items that are in the raid (in the hideout: in your profile); a round type the mod created had no model loaded, and the cylinder reload throws on it before counting the round. Seen with the weapon's default ammo in the hideout; 2.4.0 had the same flaw. Models of temporary rounds, borrowed magazines and refill rounds are now loaded first (the reload starts a moment later the first time a type is used).
- Fix: R on the RShG-2 with a rocket in your rig started the game's barrel reload, which never ends on this launcher: stuck hands and the loaded rocket moved into the inventory. R is now handled by the mod (see above).
- Fix: RShG-2 stayed an empty tube after one shot. It is a RocketLauncher-class weapon in EFT (not a one-off weapon), so the old one-off fix never applied.

## 2.4.0 (2026-10-05)
- Fix: Infinite ammo's "no spare magazine / only empty spares" handling threw "Collection was modified" whenever a spare magazine was in the rig or pockets (seen in game on 2.2.2), so empty spares were never refilled. Magazines are now collected first and checked afterwards, like the game does.
- Loose-round reloads (pump/tube shotguns, bolt-action internal magazines, revolvers, break-action and single-shot guns) now always load to full. Before, a reload only loaded as many rounds as you carried, and a single last round loaded one at a time.
- Infinite ammo with no matching rounds on you: those reloads still work. Temporary rounds are placed in a free rig/pocket spot for the reload (normal animation) and removed as soon as it ends. Without a free spot, tubes/internal magazines are filled directly.
- Loading a single round straight into the chamber (R on a gun without a usable magazine) now gives the round back too.
- Forge metadata: the client plugin is named `trappuss-InfiniteEverything`, the server part uses the same GUID as the client (`com.trappuss.infiniteeverything`) and the name `InfiniteEverything`, and the release zip now carries the MIT license in each mod folder.

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
