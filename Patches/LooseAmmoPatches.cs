using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using FirearmController = EFT.Player.FirearmController;

namespace InfiniteEverything.Patches
{
    /// <summary>
    /// Loose-round reloads load to full. The internal-magazine, cylinder and multi-barrel reloads decide how many rounds
    /// to load from AmmoPack.AmmoCount (ReloadInternalMagBase.CanReload, ReloadCylinderMagOperation, the mastering
    /// full reload) and which round to show/load from AmmoPack.GetAmmoToReload(index). Because LooseAmmoReloadPatch puts
    /// every round back, the pack never really runs dry, but those checks would still stop at the rounds you carry.
    /// For a pack of the local player's own ammo with the option on, AmmoCount reads at least 999 (the reloads stop
    /// at the gun's capacity) and every index gives the pack's first stack, which is what AmmoPack.LoadAmmo (Peek) loads.
    /// Only for stacks that can hold 2+ rounds (the bump in LooseAmmoReloadPatch keeps them in the pack).
    /// </summary>
    internal static class InfinitePack
    {
        internal const int Count = 999;

        internal static bool Applies(AmmoPack pack)
        {
            LoopedQueue<Ammo> queue = pack?._ammoPacks;
            if (queue == null || queue.Size == 0)
            {
                return false;
            }

            Ammo first = queue[0];
            return first != null && first.Template.StackMaxSize >= 2 && AmmoHelper.InfiniteLooseFor(first);
        }
    }

    public class AmmoPackCountPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(AmmoPack), nameof(AmmoPack.AmmoCount));
        }

        [PatchPostfix]
        private static void Postfix(AmmoPack __instance, ref int __result)
        {
            try
            {
                if (__result > 0 && __result < InfinitePack.Count && InfinitePack.Applies(__instance))
                {
                    __result = InfinitePack.Count;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(AmmoPackCountPatch)}: {ex}");
            }
        }
    }

    public class AmmoPackPickPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AmmoPack), nameof(AmmoPack.GetAmmoToReload));
        }

        [PatchPostfix]
        private static void Postfix(AmmoPack __instance, ref Ammo __result)
        {
            try
            {
                if (InfinitePack.Applies(__instance))
                {
                    __result = __instance._ammoPacks[0];
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(AmmoPackPickPatch)}: {ex}");
            }
        }
    }

    /// <summary>
    /// Infinite ammo with NO matching loose rounds on you. The loose-round reloads (FirearmHandsInputTranslator
    /// ReloadWithAmmo / ReloadRevolverDrum / ReloadBarrels / LoadAmmoToChamber) only start with a reachable round
    /// (rig or pockets) and otherwise say "no ammo". A temporary stack of 2 rounds of the gun's type is put in a free
    /// rig/pocket spot just for that reload, so the normal reload with its animation runs (every round is put back, so
    /// the stack stays at 2), and the stack is removed again as soon as the weapon is idle. Nothing is left behind.
    /// No free rig/pocket spot: internal magazines and tubes are filled directly (no animation); cylinders, barrels and
    /// the chamber stay empty and a notice explains why.
    /// </summary>
    internal static class TempRounds
    {
        private sealed class Entry
        {
            public Item Item;
            public InventoryController Inventory;
            public float NotBefore;
            public float Deadline;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>
        /// Set by <see cref="Provide"/>: the model of the rounds is still loading (see <see cref="ItemModels"/>). The
        /// caller skips the game's reload; R is pressed again for the player when the model is there.
        /// </summary>
        internal static bool Deferred;

        /// <summary>Loads the model, then starts the reload again if the same gun is still in the hands.</summary>
        internal static void ReloadWhenLoaded(Item item, string tpl, FirearmHandsInputTranslator translator, Weapon weapon, string what)
        {
            Action retry = () =>
            {
                Player player = MainPlayer.Get();
                if (player != null && ReferenceEquals(player.HandsController, translator._controller) && ReferenceEquals(translator._controller?.Item, weapon))
                {
                    translator.Reload();
                }
            };

            if (item != null)
            {
                ItemModels.Load(item, retry);
            }
            else
            {
                ItemModels.Load(tpl, retry);
            }

            Plugin.Log.LogInfo($"Infinite ammo: loading the model of {item?.StringTemplateId ?? tpl} for the {what}; the reload starts when it is ready.");
        }

        /// <summary>A round being tried in <see cref="CreateRound"/>: it counts as examined like the tracked ones (VirtualExaminedPatch).</summary>
        private static Item _probe;

        internal static bool Contains(Item item)
        {
            if (item != null && ReferenceEquals(_probe, item))
            {
                return true;
            }

            foreach (Entry entry in Entries)
            {
                if (ReferenceEquals(entry.Item, item))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A temporary stack (2 rounds) of <paramref name="tpl"/> in a free rig/pocket spot (backpack if those are full),
        /// removed once the weapon is idle again. Used for a round type picked in the hold-R menu or remembered for the gun.
        /// Null when the type cannot be lent (single-round stacks) or there is no space.
        /// </summary>
        internal static Ammo ProvideTemplate(InventoryController inventory, string tpl, string what)
        {
            if (HoldReload.RaidEnding || !(Singleton<ItemFactory>.Instance.CreateItem(inventory.NextId, tpl, null) is Ammo round) || round.Template.StackMaxSize < 2)
            {
                return null;
            }

            round.StackObjectsCount = 2;
            ItemAddress spot = (ItemAddress)ReloadKeepMagazinePatch.FindSpot(inventory, round, backpack: false)
                               ?? ReloadKeepMagazinePatch.FindSpot(inventory, round, backpack: true);
            if (spot == null)
            {
                return null;
            }

            NotLoot.Add(inventory, round, spot);
            if (round.CurrentAddress == null)
            {
                return null;
            }

            Track(round, inventory);
            Plugin.Log.LogInfo($"Infinite ammo: temporary {tpl} rounds for the {what} (removed when it ends).");
            return round;
        }

        private static void Track(Item round, InventoryController inventory)
        {
            Entries.Add(new Entry
            {
                Item = round,
                Inventory = inventory,
                NotBefore = Time.unscaledTime + 0.25f,
                Deadline = Time.unscaledTime + 60f
            });
        }

        /// <summary>
        /// Gives the player a temporary reachable stack for this reload when none of their reachable rounds pass
        /// <paramref name="accept"/>. Returns false when nothing was needed or nothing could be done.
        /// </summary>
        internal static bool Provide(FirearmHandsInputTranslator translator, Weapon weapon, Predicate<Ammo> accept, string what)
        {
            Deferred = false;
            Player player = translator._player;
            InventoryController inventory = translator._inventoryController;
            if (player == null || !player.IsYourPlayer || inventory == null || weapon == null)
            {
                return false;
            }

            if (!Plugin.On(Plugin.InfiniteAmmo) || HoldReload.RaidEnding)
            {
                return false;
            }

            if (weapon.MalfState.State != Weapon.EMalfunctionState.None)
            {
                return false; // the game's own malfunction handling
            }

            var reachable = new List<Ammo>();
            inventory.GetReachableItemsOfTypeNonAlloc(reachable, accept);
            if (reachable.Count > 0)
            {
                return false; // real rounds: the normal reload (LooseAmmoReloadPatch puts them back)
            }

            Ammo round = CreateRound(inventory, weapon, accept);
            if (round == null)
            {
                Plugin.Log.LogWarning($"Infinite ammo: no round type found for {weapon} ({what}).");
                return false;
            }

            if (!ItemModels.Ready(round))
            {
                Deferred = true;
                ReloadWhenLoaded(round, null, translator, weapon, what);
                return false;
            }

            ItemAddress spot = ReloadKeepMagazinePatch.FindSpot(inventory, round, backpack: false);
            if (spot == null)
            {
                return false;
            }

            NotLoot.Add(inventory, round, spot);
            if (round.CurrentAddress == null)
            {
                return false;
            }

            Track(round, inventory);
            Plugin.Log.LogInfo($"Infinite ammo: no {round.StringTemplateId} rounds on you, temporary rounds for the {what} (removed when it ends).");
            return true;
        }

        /// <summary>
        /// A round of the gun's own type: what is in the magazine / cylinder / chambers now, what the magazine last held,
        /// else the weapon's default ammo. It must pass <paramref name="accept"/> (fits, examined).
        /// </summary>
        private static Ammo CreateRound(InventoryController inventory, Weapon weapon, Predicate<Ammo> accept)
        {
            var candidates = new List<string>();
            Magazine magazine = weapon.GetCurrentMagazine();
            if (magazine is CylinderMagazine cylinder)
            {
                foreach (Slot camora in cylinder.Camoras)
                {
                    candidates.Add(camora.ContainedItem?.StringTemplateId);
                }
            }
            else
            {
                candidates.Add(MagazineMemory.TopTemplate(magazine));
            }

            foreach (Slot chamber in weapon.Chambers)
            {
                candidates.Add(chamber.ContainedItem?.StringTemplateId);
            }

            candidates.Add(weapon.Template.defAmmo);

            var tried = new HashSet<string>();
            foreach (string tpl in candidates)
            {
                if (string.IsNullOrEmpty(tpl) || !tried.Add(tpl))
                {
                    continue;
                }

                if (!(Singleton<ItemFactory>.Instance.CreateItem(inventory.NextId, tpl, null) is Ammo ammo))
                {
                    continue;
                }

                // "accept" asks the inventory whether the round is examined. In a raid that is the profile's
                // encyclopedia (33 of the 208 round types are not examined by default), and a gun loaded with such
                // rounds got the default ammo or nothing. The hideout counts everything as examined and hid it.
                ammo.StackObjectsCount = Math.Min(2, Math.Max(1, ammo.Template.StackMaxSize));
                bool accepted;
                _probe = ammo;
                try
                {
                    accepted = ammo.StackObjectsCount >= 2 && accept(ammo);
                }
                finally
                {
                    _probe = null;
                }

                if (accepted)
                {
                    return ammo;
                }
            }

            return null;
        }

        /// <summary>Removes each temporary stack once its weapon is idle again (or hands changed). Called every frame.</summary>
        internal static void Tick()
        {
            if (Entries.Count == 0)
            {
                return;
            }

            Player player = MainPlayer.Get();
            if (player == null)
            {
                Entries.Clear(); // raid over: the inventory is gone with it
                return;
            }

            if (ChamberRefunds.Any)
            {
                return; // a chamber round may still be on its way; remove after it is given back
            }

            bool busy = player.HandsController is FirearmController controller && !(controller.CurrentOperation is FirearmController.Idling);
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry entry = Entries[i];
                if (Time.unscaledTime < entry.NotBefore || (busy && Time.unscaledTime < entry.Deadline))
                {
                    continue;
                }

                if (TryRemove(entry, force: false))
                {
                    Entries.RemoveAt(i);
                }
                else
                {
                    entry.NotBefore = Time.unscaledTime + 2f; // it stays tracked: tried again, and forced at raid end
                }
            }
        }

        /// <summary>Raid end, before the profile is saved: every temporary stack still in the inventory goes, whatever the hands are doing.</summary>
        internal static void RaidEnd()
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                TryRemove(Entries[i], force: true);
            }

            Entries.Clear();
        }

        /// <summary>True: the stack is out of the inventory (removed now, or it was already gone).</summary>
        private static bool TryRemove(Entry entry, bool force)
        {
            try
            {
                Item item = entry.Item;
                if (item.CurrentAddress == null || !ReferenceEquals(item.Owner, entry.Inventory) || item.Parent?.Container?.ParentItem is Weapon)
                {
                    return true; // already gone
                }

                OperationResult<RemoveResult> result = ItemManipulator.Remove(item, entry.Inventory);
                if (result.Failed && force)
                {
                    result = ItemManipulator.RemoveWithoutRestrictions(item, entry.Inventory);
                }

                if (result.Failed)
                {
                    Plugin.Log.LogWarning($"Infinite ammo: could not remove the temporary rounds ({result.Error}).");
                    return false;
                }

                result.Value.RaiseEvents(entry.Inventory, CommandStatus.Begin);
                result.Value.RaiseEvents(entry.Inventory, CommandStatus.Succeed);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Infinite ammo: removing temporary rounds failed: {ex}");
                return false;
            }
        }
    }

    /// <summary>Tube / internal magazine (pump shotguns, Mosin...): FirearmHandsInputTranslator.ReloadWithAmmo.</summary>
    public class NoAmmoInternalMagPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FirearmHandsInputTranslator), nameof(FirearmHandsInputTranslator.ReloadWithAmmo));
        }

        [PatchPrefix]
        private static bool Prefix(FirearmHandsInputTranslator __instance, Weapon weapon, ref bool __result)
        {
            try
            {
                if (PickedRounds.TryReload(__instance, weapon, PickedRounds.Kind.InternalMagazine, false))
                {
                    __result = true;
                    return false; // reloaded with the round type picked in the hold-R menu
                }

                Magazine magazine = weapon?.GetCurrentMagazine();
                if (magazine == null || magazine is CylinderMagazine || !weapon.HasChambers)
                {
                    return true;
                }

                bool needsRounds = magazine.Count < magazine.MaxCount || weapon.ChamberAmmoCount == 0;
                if (!needsRounds)
                {
                    return true;
                }

                InventoryController inventory = __instance._inventoryController;
                Slot chamber = weapon.Chambers[0];
                Predicate<Ammo> accept = ammo => ammo.StackObjectsCount > 0 && chamber.CanAccept(ammo) && inventory.Examined(ammo) && ammo.CheckAction(null).Succeeded;
                if (TempRounds.Provide(__instance, weapon, accept, "tube/internal magazine reload"))
                {
                    return true; // the normal reload now finds the temporary rounds
                }

                if (TempRounds.Deferred)
                {
                    __result = true;
                    return false; // their model is loading: the reload starts when it is ready
                }

                // No free rig/pocket spot (or a round type that cannot be placed): fill it directly, no animation.
                if (!NoReachableAmmo(__instance, accept) || !Plugin.On(Plugin.InfiniteAmmo) || AmmoHelper.IsLauncher(weapon, false)
                    || weapon.MalfState.State != Weapon.EMalfunctionState.None)
                {
                    return true;
                }

                int added = MagazineMemory.Refill(inventory, magazine, null, AmmoHelper.FallbackTemplate(weapon, magazine));
                var controller = __instance._player?.HandsController as FirearmController;
                bool chambered = controller != null && AmmoHelper.TryChamber(controller, inventory, weapon, magazine);
                controller?.FirearmsAnimator?.SetAmmoOnMag(magazine.Count);
                if (added == 0 && !chambered)
                {
                    return true;
                }

                Plugin.Log.LogInfo($"Infinite ammo: no rounds and no free rig/pocket spot, filled {magazine} directly (+{added}, chambered {chambered}).");
                __result = true;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(NoAmmoInternalMagPatch)}: {ex}");
                return true;
            }
        }

        internal static bool NoReachableAmmo(FirearmHandsInputTranslator translator, Predicate<Ammo> accept)
        {
            if (translator._inventoryController == null || translator._player == null || !translator._player.IsYourPlayer)
            {
                return false;
            }

            var reachable = new List<Ammo>();
            translator._inventoryController.GetReachableItemsOfTypeNonAlloc(reachable, accept);
            return reachable.Count == 0;
        }
    }

    /// <summary>Revolvers and revolver grenade launchers: FirearmHandsInputTranslator.ReloadRevolverDrum.</summary>
    public class NoAmmoCylinderPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FirearmHandsInputTranslator), nameof(FirearmHandsInputTranslator.ReloadRevolverDrum));
        }

        [PatchPrefix]
        private static bool Prefix(FirearmHandsInputTranslator __instance, Weapon weapon, bool quickReload)
        {
            try
            {
                if (PickedRounds.TryReload(__instance, weapon, PickedRounds.Kind.Cylinder, quickReload))
                {
                    return false;
                }

                if (!(weapon?.GetCurrentMagazine() is CylinderMagazine cylinder) || (!quickReload && cylinder.Count >= cylinder.MaxCount))
                {
                    return true;
                }

                InventoryController inventory = __instance._inventoryController;
                Predicate<Ammo> accept = ammo => ammo.StackObjectsCount > 0 && inventory.Examined(ammo) && ammo.CheckAction(null).Succeeded && cylinder.CheckCompatibility(ammo);
                bool provided = TempRounds.Provide(__instance, weapon, accept, "cylinder reload");
                if (TempRounds.Deferred)
                {
                    return false; // their model is loading: the reload starts when it is ready
                }

                if (!provided && NoAmmoInternalMagPatch.NoReachableAmmo(__instance, accept) && WantsInfinite(__instance, weapon))
                {
                    Plugin.Notify("Infinite ammo: no free rig/pocket space for the reload rounds");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(NoAmmoCylinderPatch)}: {ex}");
            }

            return true;
        }

        internal static bool WantsInfinite(FirearmHandsInputTranslator translator, Weapon weapon)
        {
            return Plugin.On(Plugin.InfiniteAmmo);
        }
    }

    /// <summary>Break-action and single-shot guns (incl. flare guns, M79-style launchers): FirearmHandsInputTranslator.ReloadBarrels.</summary>
    public class NoAmmoBarrelsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FirearmHandsInputTranslator), nameof(FirearmHandsInputTranslator.ReloadBarrels));
        }

        [PatchPrefix]
        private static bool Prefix(FirearmHandsInputTranslator __instance, Weapon weapon)
        {
            try
            {
                if (weapon is EFT.InventoryLogic.RocketLauncher && RocketRearm.Wanted && __instance._player != null && __instance._player.IsYourPlayer)
                {
                    // The game's barrel reload never ends on the RShG-2 (seen in game with a rocket in the rig: stuck
                    // hands, and the loaded rocket moved to the inventory). R is handled here instead: an empty tube
                    // gets a new rocket, a loaded one nothing.
                    RocketRearm.Reload(__instance, weapon);
                    return false;
                }

                if (PickedRounds.TryReload(__instance, weapon, PickedRounds.Kind.Barrels, false))
                {
                    return false;
                }

                if (weapon == null || !weapon.HasChambers || (weapon.IsMultiBarrel && weapon.FreeChamberSlotsCount == 0))
                {
                    return true;
                }

                Slot chamber = weapon.IsMultiBarrel ? weapon.FirstFreeChamberSlot : weapon.Chambers[0];
                if (chamber == null || (chamber.ContainedItem is Ammo loaded && !loaded.IsUsed))
                {
                    return true; // already loaded
                }

                InventoryController inventory = __instance._inventoryController;
                Predicate<Ammo> accept = ammo => ammo.StackObjectsCount > 0 && chamber.CanAccept(ammo) && inventory.Examined(ammo) && ammo.CheckAction(null).Succeeded;
                bool provided = TempRounds.Provide(__instance, weapon, accept, "barrel reload");
                if (TempRounds.Deferred)
                {
                    return false; // their model is loading: the reload starts when it is ready
                }

                if (!provided && NoAmmoInternalMagPatch.NoReachableAmmo(__instance, accept) && NoAmmoCylinderPatch.WantsInfinite(__instance, weapon))
                {
                    Plugin.Notify("Infinite ammo: no free rig/pocket space for the reload rounds");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(NoAmmoBarrelsPatch)}: {ex}");
            }

            return true;
        }
    }

    /// <summary>
    /// R after a round type was picked in the hold-R menu for this gun (Prefs): reload with that type again, from your own
    /// rounds of that type if you carry some, else from temporary ones, instead of whatever the game finds first.
    /// Same reload calls the game's SwitchMagazine makes. Returns false (game's normal R) when nothing applies.
    /// </summary>
    internal static class PickedRounds
    {
        internal enum Kind
        {
            InternalMagazine,
            Cylinder,
            Barrels
        }

        internal static bool TryReload(FirearmHandsInputTranslator translator, Weapon weapon, Kind kind, bool quickReload)
        {
            string tpl = weapon == null ? null : Prefs.PickedRound(weapon);
            Player player = translator._player;
            InventoryController inventory = translator._inventoryController;
            IFirearmHandsController controller = translator._controller;
            if (tpl == null || player == null || !player.IsYourPlayer || inventory == null || controller == null)
            {
                return false;
            }

            if (!Plugin.On(Plugin.InfiniteAmmo) || weapon.MalfState.State != Weapon.EMalfunctionState.None || !weapon.HasChambers)
            {
                return false;
            }

            if (!(Singleton<ItemFactory>.Instance.CreateItem(inventory.NextId, tpl, null) is Ammo probe))
            {
                return false;
            }

            Magazine magazine = weapon.GetCurrentMagazine();
            Slot barrel = null;
            switch (kind)
            {
                case Kind.InternalMagazine:
                    if (magazine == null || magazine is CylinderMagazine || !weapon.Chambers[0].CanAccept(probe)
                        || (magazine.Count >= magazine.MaxCount && weapon.ChamberAmmoCount > 0))
                    {
                        return false;
                    }

                    break;
                case Kind.Cylinder:
                    if (!(magazine is CylinderMagazine cylinder) || !cylinder.CheckCompatibility(probe) || (!quickReload && cylinder.Count >= cylinder.MaxCount))
                    {
                        return false;
                    }

                    break;
                default:
                    if (weapon.IsMultiBarrel && weapon.FreeChamberSlotsCount == 0)
                    {
                        return false;
                    }

                    barrel = weapon.IsMultiBarrel ? weapon.FirstFreeChamberSlot : weapon.Chambers[0];
                    if (barrel == null || !barrel.CanAccept(probe))
                    {
                        return false;
                    }

                    if (barrel.ContainedItem is Ammo inBarrel && !inBarrel.IsUsed && inBarrel.StringTemplateId == tpl)
                    {
                        // Already loaded with that type: nothing to do. The game's ReloadBarrels has no "loaded" check
                        // for a single barrel and would swap the round for whatever type it finds first.
                        return true;
                    }

                    break;
            }

            if (!controller.CanStartReload())
            {
                return false;
            }

            var own = new List<Ammo>();
            inventory.GetReachableItemsOfTypeNonAlloc(own, ammo => ammo.StackObjectsCount > 0 && ammo.StringTemplateId == tpl && ammo.CheckAction(null).Succeeded);
            if (own.Count == 0 && !ItemModels.Ready(probe))
            {
                TempRounds.ReloadWhenLoaded(probe, null, translator, weapon, "picked round type");
                return true; // handled: the reload starts when the model is ready
            }

            Ammo rounds = own.Count > 0 ? own[0] : TempRounds.ProvideTemplate(inventory, tpl, "picked round type");
            if (rounds == null)
            {
                return false;
            }

            var pack = new AmmoPack(new List<Ammo> { rounds });
            bool idle = (controller as FirearmController)?.CurrentOperation is FirearmController.Idling;
            Callback refused = result =>
            {
                if (result.Failed)
                {
                    Plugin.Log.LogWarning($"Infinite ammo: the reload with the picked round type {tpl} was refused ({result.Error}).");
                }
            };
            switch (kind)
            {
                case Kind.InternalMagazine:
                    controller.ReloadWithAmmo(pack, refused);
                    break;
                case Kind.Cylinder:
                    controller.ReloadCylinderMagazine(pack, refused, quickReload);
                    break;
                default:
                    ItemAddress place = null;
                    if (barrel.ContainedItem is Ammo old && !old.IsUsed)
                    {
                        place = (ItemAddress)ReloadKeepMagazinePatch.FindSpot(inventory, old, backpack: false)
                                ?? ReloadKeepMagazinePatch.FindSpot(inventory, old, backpack: true);
                        if (place == null)
                        {
                            // Without a place ReloadSingleBarrelResult.Run removes the loaded round and throws it on the ground.
                            Plugin.Notify("Infinite ammo: no free space to keep the loaded round");
                            return true;
                        }
                    }

                    controller.ReloadBarrels(pack, place, refused);
                    break;
            }

            if (idle && (controller as FirearmController)?.CurrentOperation is FirearmController.Idling)
            {
                // From idle the game starts the reload inside the call. It did not: the game's own reload gets this R.
                Plugin.Log.LogWarning($"Infinite ammo: the reload with the picked round type {tpl} did not start.");
                return false;
            }

            Plugin.Log.LogInfo($"Infinite ammo: R reloads the picked round type {tpl}.");
            return true;
        }
    }

    /// <summary>
    /// Loading a single round straight into the chamber (R on a gun with no usable magazine, e.g. a bolt-action without
    /// its magazine): FirearmHandsInputTranslator.LoadAmmoToChamber -> Weapon.Apply moves or splits one round into
    /// Chambers[0] through a network transaction, which does NOT go through ApplySingleItemToAddress. The reachable
    /// stacks that fit are noted before; when the call reports it started, ChamberRefunds gives exactly one round back
    /// to the stack it came from as soon as the chamber holds it. No round on you: temporary rounds as above.
    /// </summary>
    public class ChamberLoadPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FirearmHandsInputTranslator), nameof(FirearmHandsInputTranslator.LoadAmmoToChamber));
        }

        [PatchPrefix]
        private static void Prefix(FirearmHandsInputTranslator __instance, out ChamberRefunds.Pending __state)
        {
            __state = null;
            try
            {
                Weapon weapon = __instance._controller?.Item;
                if (weapon == null || !weapon.HasChambers || weapon.Chambers[0].ContainedItem != null || !NoAmmoCylinderPatch.WantsInfinite(__instance, weapon))
                {
                    return;
                }

                InventoryController inventory = __instance._inventoryController;
                Player player = __instance._player;
                if (inventory == null || player == null || !player.IsYourPlayer)
                {
                    return;
                }

                Slot chamber = weapon.Chambers[0];
                Predicate<Ammo> accept = ammo => ammo.StackObjectsCount > 0 && chamber.CanAccept(ammo) && inventory.Examined(ammo) && ammo.CheckAction(null).Succeeded;
                TempRounds.Provide(__instance, weapon, accept, "chamber load");

                var stacks = new List<ChamberRefunds.Stack>();
                var reachable = new List<Ammo>();
                inventory.GetReachableItemsOfTypeNonAlloc(reachable, accept);
                foreach (Ammo ammo in reachable)
                {
                    stacks.Add(new ChamberRefunds.Stack { Item = ammo, Address = ammo.CurrentAddress, Count = ammo.StackObjectsCount, Tpl = ammo.StringTemplateId });
                }

                if (stacks.Count > 0)
                {
                    __state = new ChamberRefunds.Pending { Weapon = weapon, Inventory = inventory, Stacks = stacks };
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(ChamberLoadPatch)} prefix: {ex}");
            }
        }

        [PatchPostfix]
        private static void Postfix(bool __result, ChamberRefunds.Pending __state)
        {
            if (__result && __state != null)
            {
                ChamberRefunds.Add(__state);
            }
        }
    }

    internal static class ChamberRefunds
    {
        internal sealed class Stack
        {
            public Item Item;
            public ItemAddress Address;
            public int Count;
            public string Tpl;
        }

        internal sealed class Pending
        {
            public Weapon Weapon;
            public InventoryController Inventory;
            public List<Stack> Stacks;
            public float Deadline;
        }

        private static readonly List<Pending> Queue = new List<Pending>();

        internal static bool Any => Queue.Count > 0;

        internal static void Add(Pending pending)
        {
            pending.Deadline = Time.unscaledTime + 10f;
            Queue.Add(pending);
        }

        /// <summary>Gives back the one round that went into the chamber. Called every frame.</summary>
        internal static void Tick()
        {
            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                Pending pending = Queue[i];
                try
                {
                    Item chambered = pending.Weapon.Chambers[0].ContainedItem;
                    Stack taken = chambered == null ? null : FindTaken(pending, chambered.StringTemplateId);
                    if (taken == null)
                    {
                        if (Time.unscaledTime > pending.Deadline || MainPlayer.Get() == null)
                        {
                            Queue.RemoveAt(i);
                        }

                        continue;
                    }

                    Queue.RemoveAt(i);
                    GiveOne(pending.Inventory, taken);
                }
                catch (Exception ex)
                {
                    Queue.RemoveAt(i);
                    Plugin.Log.LogError($"Infinite ammo: chamber refund failed: {ex}");
                }
            }
        }

        private static Stack FindTaken(Pending pending, string tpl)
        {
            foreach (Stack stack in pending.Stacks)
            {
                if (stack.Tpl != tpl)
                {
                    continue;
                }

                bool there = stack.Item.CurrentAddress != null && stack.Item.CurrentAddress.Equals(stack.Address);
                if (!there || stack.Item.StackObjectsCount < stack.Count)
                {
                    return stack;
                }
            }

            return null;
        }

        private static void GiveOne(InventoryController inventory, Stack stack)
        {
            bool there = stack.Item.CurrentAddress != null && stack.Item.CurrentAddress.Equals(stack.Address);
            if (there)
            {
                stack.Item.StackObjectsCount = Math.Min(stack.Count, stack.Item.StackObjectsCount + 1);
                stack.Item.RaiseRefreshEvent();
                Plugin.Log.LogInfo("Infinite ammo: chambered round given back.");
                return;
            }

            // The whole stack (one round) went into the chamber: a new round where it was.
            Item round = Singleton<ItemFactory>.Instance.CreateItem(inventory.NextId, stack.Tpl, null);
            round.StackObjectsCount = 1;
            ItemAddress target = stack.Address;
            if (!ItemManipulator.Add(round, target, inventory, simulate: true).Succeeded)
            {
                target = ReloadKeepMagazinePatch.FindSpot(inventory, round, backpack: false)
                         ?? ReloadKeepMagazinePatch.FindSpot(inventory, round, backpack: true);
            }

            if (target == null)
            {
                Plugin.Log.LogWarning($"Infinite ammo: no space to give back the chambered round ({stack.Tpl}).");
                return;
            }

            NotLoot.Add(inventory, round, target);
            Plugin.Log.LogInfo("Infinite ammo: chambered round given back.");
        }
    }
}
