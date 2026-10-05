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
        /// Gives the player a temporary reachable stack for this reload when none of their reachable rounds pass
        /// <paramref name="accept"/>. Returns false when nothing was needed or nothing could be done.
        /// </summary>
        internal static bool Provide(FirearmHandsInputTranslator translator, Weapon weapon, Predicate<Ammo> accept, string what)
        {
            Player player = translator._player;
            InventoryController inventory = translator._inventoryController;
            if (player == null || !player.IsYourPlayer || inventory == null || weapon == null)
            {
                return false;
            }

            bool launcherMode = player.HandsController is FirearmController fc && fc.IsInLauncherMode();
            if (!(AmmoHelper.IsLauncher(weapon, launcherMode) ? Plugin.On(Plugin.LauncherAmmo) : Plugin.On(Plugin.InfiniteAmmo)))
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

            ItemAddress spot = ReloadKeepMagazinePatch.FindSpot(inventory, round, backpack: false);
            if (spot == null)
            {
                return false;
            }

            inventory.AddAndRaiseEvents(round, spot);
            if (round.CurrentAddress == null)
            {
                return false;
            }

            Entries.Add(new Entry
            {
                Item = round,
                Inventory = inventory,
                NotBefore = Time.unscaledTime + 0.25f,
                Deadline = Time.unscaledTime + 60f
            });
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

                ammo.StackObjectsCount = Math.Min(2, Math.Max(1, ammo.Template.StackMaxSize));
                if (ammo.StackObjectsCount >= 2 && accept(ammo))
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

                Entries.RemoveAt(i);
                try
                {
                    Item item = entry.Item;
                    if (item.CurrentAddress == null || !ReferenceEquals(item.Owner, entry.Inventory) || item.Parent?.Container?.ParentItem is Weapon)
                    {
                        continue; // already gone
                    }

                    OperationResult<RemoveResult> result = ItemManipulator.Remove(item, entry.Inventory);
                    if (result.Failed)
                    {
                        Plugin.Log.LogWarning($"Infinite ammo: could not remove the temporary rounds ({result.Error}).");
                        continue;
                    }

                    result.Value.RaiseEvents(entry.Inventory, CommandStatus.Begin);
                    result.Value.RaiseEvents(entry.Inventory, CommandStatus.Succeed);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Infinite ammo: removing temporary rounds failed: {ex}");
                }
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
        private static void Prefix(FirearmHandsInputTranslator __instance, Weapon weapon, bool quickReload)
        {
            try
            {
                if (!(weapon?.GetCurrentMagazine() is CylinderMagazine cylinder) || (!quickReload && cylinder.Count >= cylinder.MaxCount))
                {
                    return;
                }

                InventoryController inventory = __instance._inventoryController;
                Predicate<Ammo> accept = ammo => ammo.StackObjectsCount > 0 && inventory.Examined(ammo) && ammo.CheckAction(null).Succeeded && cylinder.CheckCompatibility(ammo);
                if (!TempRounds.Provide(__instance, weapon, accept, "cylinder reload") && NoAmmoInternalMagPatch.NoReachableAmmo(__instance, accept) && WantsInfinite(__instance, weapon))
                {
                    Plugin.Notify("Infinite ammo: no free rig/pocket space for the reload rounds");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(NoAmmoCylinderPatch)}: {ex}");
            }
        }

        internal static bool WantsInfinite(FirearmHandsInputTranslator translator, Weapon weapon)
        {
            bool launcherMode = translator._player?.HandsController is FirearmController fc && fc.IsInLauncherMode();
            return AmmoHelper.IsLauncher(weapon, launcherMode) ? Plugin.On(Plugin.LauncherAmmo) : Plugin.On(Plugin.InfiniteAmmo);
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
        private static void Prefix(FirearmHandsInputTranslator __instance, Weapon weapon)
        {
            try
            {
                if (weapon == null || !weapon.HasChambers || (weapon.IsMultiBarrel && weapon.FreeChamberSlotsCount == 0))
                {
                    return;
                }

                Slot chamber = weapon.IsMultiBarrel ? weapon.FirstFreeChamberSlot : weapon.Chambers[0];
                if (chamber == null || (chamber.ContainedItem is Ammo loaded && !loaded.IsUsed))
                {
                    return; // already loaded
                }

                InventoryController inventory = __instance._inventoryController;
                Predicate<Ammo> accept = ammo => ammo.StackObjectsCount > 0 && chamber.CanAccept(ammo) && inventory.Examined(ammo) && ammo.CheckAction(null).Succeeded;
                if (!TempRounds.Provide(__instance, weapon, accept, "barrel reload") && NoAmmoInternalMagPatch.NoReachableAmmo(__instance, accept) && NoAmmoCylinderPatch.WantsInfinite(__instance, weapon))
                {
                    Plugin.Notify("Infinite ammo: no free rig/pocket space for the reload rounds");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(NoAmmoBarrelsPatch)}: {ex}");
            }
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

            inventory.AddAndRaiseEvents(round, target);
            Plugin.Log.LogInfo("Infinite ammo: chambered round given back.");
        }
    }
}
