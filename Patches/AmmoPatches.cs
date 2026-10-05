using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using FirearmController = EFT.Player.FirearmController;
using OneOffFire = EFT.Player.FirearmController.OneOffGunFire;

namespace InfiniteEverything.Patches
{
    internal static class AmmoHelper
    {
        /// <summary>Round type for a refill: the magazine's own (or last known) type, the chambered round, the weapon's default ammo.</summary>
        internal static string FallbackTemplate(Weapon weapon, Magazine magazine)
        {
            string tpl = MagazineMemory.TopTemplate(magazine);
            if (tpl != null)
            {
                return tpl;
            }

            if (weapon.HasChambers && weapon.Chambers[0].ContainedItem != null)
            {
                return weapon.Chambers[0].ContainedItem.StringTemplateId;
            }

            string defAmmo = weapon.Template.defAmmo;
            return string.IsNullOrEmpty(defAmmo) ? null : defAmmo;
        }

        /// <summary>Launchers and flare guns follow "Launcher and flare ammo", every other gun "Infinite ammo".</summary>
        internal static bool IsLauncher(Weapon weapon, bool launcherMode)
        {
            return launcherMode || weapon is GrenadeLauncher || weapon is EFT.InventoryLogic.RocketLauncher || weapon.IsFlareGun;
        }

        /// <summary>
        /// The local player is reloading <paramref name="ammo"/> (their own) into the weapon in their hands and the option
        /// for that weapon is on. Used for loose-round reloads.
        /// </summary>
        internal static bool InfiniteLooseFor(Ammo ammo)
        {
            if (ammo == null || !Plugin.Enabled.Value)
            {
                return false;
            }

            Player player = MainPlayer.Get();
            if (player == null || !ReferenceEquals(ammo.Owner, player.InventoryController) || !(player.HandsController is FirearmController controller) || controller.Item == null)
            {
                return false;
            }

            return IsLauncher(controller.Item, controller.IsInLauncherMode()) ? Plugin.LauncherAmmo.Value : Plugin.InfiniteAmmo.Value;
        }

        /// <summary>True when <paramref name="item"/> is <paramref name="root"/> or sits anywhere inside it.</summary>
        internal static bool IsInside(Item item, Item root)
        {
            for (int depth = 0; item != null && depth < 32; depth++)
            {
                if (ReferenceEquals(item, root))
                {
                    return true;
                }

                item = item.Parent?.Container?.ParentItem;
            }

            return false;
        }

        /// <summary>
        /// Moves the magazine's top round into an empty chamber, the same inventory move the game makes at the end of a
        /// reload (Cartridges.PopTo into Chambers[0]), then updates the round model and the animator like
        /// ReloadExternalMagOperation.OnAddAmmoInChamber does.
        /// </summary>
        internal static bool TryChamber(FirearmController controller, ItemController owner, Weapon weapon, Magazine magazine)
        {
            if (!weapon.HasChambers || weapon.Chambers[0].ContainedItem != null || magazine.Count <= 0 || !magazine.IsAmmoCompatible(weapon.Chambers))
            {
                return false;
            }

            OperationResult<IItemOperationResult> result = magazine.Cartridges.PopTo(owner, weapon.Chambers[0].CreateItemAddress());
            if (result.Failed)
            {
                Plugin.Log.LogWarning($"Could not chamber a round in {weapon}: {result.Error}");
                return false;
            }

            result.Value.RaiseEvents(owner, CommandStatus.Begin);
            result.Value.RaiseEvents(owner, CommandStatus.Succeed);

            if (result.Value.ResultItem is Ammo round)
            {
                controller.Firearms?.SetRoundIntoWeapon(round);
            }

            FirearmsAnimator animator = controller.FirearmsAnimator;
            if (animator != null)
            {
                animator.SetAmmoInChamber(weapon.ChamberAmmoCount);
                animator.SetAmmoOnMag(magazine.Count);
                animator.SetBoltCatch(false);
            }

            return true;
        }
    }

    /// <summary>
    /// Infinite ammo with nothing left to load. R / double-tap R go through FirearmHandsInputTranslator.ReloadExternalMagazine,
    /// which only picks magazines with Count > 0 from the rig/pockets (GetReachableItemsOfTypeNonAlloc) and otherwise shows
    /// "no magazine". Before that search, with Infinite ammo on:
    ///  - a loaded spare magazine exists: nothing to do (normal reload);
    ///  - only empty spare magazines: one of the same type as the gun's magazine (else the biggest) is filled to
    ///    capacity, then the normal reload picks it;
    ///  - no spare magazine at all: the magazine in the gun is filled and a normal reload is started with that SAME
    ///    magazine. ReloadExternalMagResult.Run then moves it out (to a free rig/pocket/backpack spot) and straight back
    ///    into the gun in the same step, so the full reload animation plays and the inventory ends exactly as before:
    ///    same magazine in the gun, nothing added or removed except the rounds. Only if there is no free spot at all is
    ///    the magazine refilled and a round chambered without the animation.
    /// </summary>
    public class EmptyReloadPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FirearmHandsInputTranslator), nameof(FirearmHandsInputTranslator.ReloadExternalMagazine));
        }

        [PatchPrefix]
        private static bool Prefix(FirearmHandsInputTranslator __instance, Weapon weapon, ref bool __result)
        {
            try
            {
                if (!Plugin.On(Plugin.InfiniteAmmo) || weapon == null)
                {
                    return true;
                }

                Player player = __instance._player;
                InventoryController inventory = __instance._inventoryController;
                if (player == null || !player.IsYourPlayer || inventory == null)
                {
                    return true;
                }

                Slot magazineSlot = weapon.GetMagazineSlot();
                if (magazineSlot == null || weapon.MalfState.State != Weapon.EMalfunctionState.None)
                {
                    return true; // malfunctions keep the game's own handling
                }

                Magazine current = weapon.GetCurrentMagazine();
                // Collect first, check the move afterwards: CheckMoveIgnoringTargetItem simulates a move, which changes the
                // container being enumerated ("Collection was modified", seen in game in 2.2.2). The game's own
                // ReloadExternalMagazine does it in this order too.
                var found = new List<Magazine>();
                inventory.GetReachableItemsOfTypeNonAlloc(found, (Magazine mag) => !ReferenceEquals(mag, current));
                var spares = new List<Magazine>();
                foreach (Magazine mag in found)
                {
                    if (ItemManipulator.CheckMoveIgnoringTargetItem(mag, magazineSlot, inventory).Succeeded)
                    {
                        spares.Add(mag);
                    }
                }

                Magazine pick = null;
                foreach (Magazine spare in spares)
                {
                    if (spare.Count > 0)
                    {
                        return true; // a loaded spare: the normal reload works
                    }

                    bool sameType = current != null && spare.TemplateId == current.TemplateId;
                    bool pickSameType = current != null && pick != null && pick.TemplateId == current.TemplateId;
                    if (pick == null || (sameType && !pickSameType) || (sameType == pickSameType && spare.MaxCount > pick.MaxCount))
                    {
                        pick = spare;
                    }
                }

                string tpl = AmmoHelper.FallbackTemplate(weapon, current);
                if (pick != null)
                {
                    int added = MagazineMemory.Refill(inventory, pick, current, tpl);
                    Plugin.Log.LogInfo($"Infinite ammo: filled empty spare magazine {pick} (+{added}) for the reload.");
                    return true; // the normal reload now finds it
                }

                if (current == null)
                {
                    return true; // no magazine anywhere: nothing to fill
                }

                var controller = player.HandsController as FirearmController;
                int addedInGun = MagazineMemory.Refill(inventory, current, null, tpl);
                if (addedInGun == 0 && (!weapon.HasChambers || weapon.Chambers[0].ContainedItem != null))
                {
                    return true; // already full and chambered: nothing to reload, the game's own handling applies
                }

                // Reload the gun's own magazine with the normal animation (out to a free spot and back in, in one step).
                ItemAddress spot = (ItemAddress)ReloadKeepMagazinePatch.FindSpot(inventory, current, backpack: false)
                                   ?? ReloadKeepMagazinePatch.FindSpot(inventory, current, backpack: true);
                if (controller != null && spot != null && controller.CanStartReload())
                {
                    controller.ReloadMag(current, spot, null);
                    Plugin.Log.LogInfo($"Infinite ammo: no spare magazine, reloading the gun's own {current} (+{addedInGun} rounds).");
                    __result = true;
                    return false;
                }

                bool chambered = controller != null && AmmoHelper.TryChamber(controller, inventory, weapon, current);
                controller?.FirearmsAnimator?.SetAmmoOnMag(current.Count);
                Plugin.Log.LogInfo($"Infinite ammo: no spare magazine, refilled the one in the gun (+{addedInGun}, chambered {chambered}).");
                __result = addedInGun > 0 || chambered;
                return !__result; // handled: skip the game's "no magazine" path
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(EmptyReloadPatch)}: {ex}");
                return true;
            }
        }
    }

    /// <summary>
    /// Loose-round reloads (shotgun tubes, bolt-action internal magazines, revolver cylinders, break-action barrels,
    /// flare guns, underbarrel and standalone grenade launchers). Every round goes from the inventory into the gun through
    /// ItemManipulator.ApplySingleItemToAddress (AmmoPack.LoadAmmo, ReloadSingleBarrelResult and the cylinder reloads),
    /// which splits one round off the stack, moves the last round, or merges it. For the local player's ammo going into the
    /// weapon in their hands, the round is put back: the stack count is restored, or a new round of the same type is
    /// placed where the old one was. Launchers and flare guns follow "Launcher and flare ammo", every other gun "Infinite ammo".
    /// A stack of exactly one round is raised to two for the call, so the game splits a round off instead of moving the
    /// stack itself: the stack stays in the AmmoPack (AmmoPack.LoadAmmo dequeues a stack only on a move or merge) and
    /// the reload keeps going. Its count is put back afterwards in every case (finalizer).
    /// </summary>
    public class LooseAmmoReloadPatch : ModulePatch
    {
        internal sealed class State
        {
            public Item Round;
            public ItemAddress From;
            public int Count;
            public string Tpl;
            public InventoryController Inventory;
            public bool Bumped;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemManipulator), nameof(ItemManipulator.ApplySingleItemToAddress));
        }

        [PatchPrefix]
        private static void Prefix(Item item, ItemAddress to, out State __state)
        {
            __state = null;
            if (!Plugin.Enabled.Value || (!Plugin.InfiniteAmmo.Value && !Plugin.LauncherAmmo.Value) || !(item is Ammo))
            {
                return;
            }

            try
            {
                Player player = MainPlayer.Get();
                if (player == null)
                {
                    return;
                }

                InventoryController inventory = player.InventoryController;
                if (!ReferenceEquals(item.Owner, inventory) || !(player.HandsController?.Item is Weapon weapon))
                {
                    return;
                }

                Item target = to?.Container?.ParentItem;
                if (!AmmoHelper.IsInside(target, weapon) || AmmoHelper.IsInside(item.Parent?.Container?.ParentItem, weapon))
                {
                    return; // not a reload into the gun in hand (or a move inside the gun)
                }

                bool launcher = target is Launcher || weapon is GrenadeLauncher || weapon is EFT.InventoryLogic.RocketLauncher || weapon.IsFlareGun;
                if (launcher ? !Plugin.LauncherAmmo.Value : !Plugin.InfiniteAmmo.Value)
                {
                    return;
                }

                __state = new State
                {
                    Round = item,
                    From = item.CurrentAddress,
                    Count = item.StackObjectsCount,
                    Tpl = item.StringTemplateId,
                    Inventory = inventory
                };

                if (item.StackObjectsCount == 1 && item.Template.StackMaxSize >= 2 && __state.From != null)
                {
                    item.StackObjectsCount = 2; // split one off instead of moving the stack (see the summary)
                    __state.Bumped = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(LooseAmmoReloadPatch)} prefix: {ex}");
            }
        }

        [PatchFinalizer]
        private static void Finalizer(OperationResult<IItemOperationResult> __result, Exception __exception, State __state)
        {
            if (__state == null || __state.From == null)
            {
                return;
            }

            try
            {
                Item round = __state.Round;
                if (round.CurrentAddress != null && round.CurrentAddress.Equals(__state.From))
                {
                    if (round.StackObjectsCount != __state.Count)
                    {
                        round.StackObjectsCount = __state.Count; // give the round back (and undo the bump)
                        round.RaiseRefreshEvent();
                    }

                    return;
                }

                if (__exception != null || __result.Failed)
                {
                    return;
                }

                // The whole (last) round moved or merged into the gun: put a new one where it was.
                Item replacement = Singleton<ItemFactory>.Instance.CreateItem(__state.Inventory.NextId, __state.Tpl, null);
                replacement.StackObjectsCount = 1;
                if (ItemManipulator.Add(replacement, __state.From, __state.Inventory, simulate: true).Succeeded)
                {
                    __state.Inventory.AddAndRaiseEvents(replacement, __state.From);
                }
                else
                {
                    Plugin.Log.LogWarning($"Infinite ammo: could not put a round back ({__state.Tpl}): its place is taken.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(LooseAmmoReloadPatch)} finalizer: {ex}");
            }
        }
    }

    /// <summary>
    /// Single-use launchers (RShG-2, M72 and the like): OneOffGunFire.OnFireEvent sets Repairable.Durability to 0, which
    /// marks them spent and makes the game throw them away (ProcessRemoveOneOffWeapon). The durability is put back so the
    /// launcher stays and can fire again (firing a one-off only checks Weapon.IsOneOff).
    /// </summary>
    public class OneOffLauncherPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(OneOffFire), nameof(OneOffFire.OnFireEvent));
        }

        [PatchPrefix]
        private static void Prefix(OneOffFire __instance, out float __state)
        {
            __state = -1f;
            try
            {
                if (Plugin.On(Plugin.LauncherAmmo) && __instance.Player != null && __instance.Player.IsYourPlayer && __instance.Weapon?.Repairable != null)
                {
                    __state = __instance.Weapon.Repairable.Durability;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(OneOffLauncherPatch)} prefix: {ex}");
            }
        }

        [PatchFinalizer]
        private static void Finalizer(OneOffFire __instance, float __state)
        {
            if (__state > 0f && __instance.Weapon?.Repairable != null)
            {
                __instance.Weapon.Repairable.Durability = __state;
            }
        }
    }

    /// <summary>
    /// Weapon reliability, malfunctions. FireOperationBase.ProcessInventoryOperationsForSingleshotWithMalf rolls a
    /// malfunction and then keeps it only if Weapon.ValidateMalfunction(state) is true (its only caller). For the weapon in
    /// the local player's hands (turrets included) any malfunction is refused.
    /// </summary>
    public class NoMalfunctionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Weapon), nameof(Weapon.ValidateMalfunction));
        }

        [PatchPrefix]
        private static bool Prefix(Weapon __instance, Weapon.EMalfunctionState malfState, ref bool __result)
        {
            if (malfState != Weapon.EMalfunctionState.None && Plugin.On(Plugin.WeaponReliability) && MainPlayer.InHands(__instance))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Weapon reliability, overheating. Weapon.OnShot adds GetShotOverheat(ammoHeatFactor, ...) to the barrel heat; every
    /// overheat effect (fire-rate change, slide lock, auto-fire, extra wear) is derived from MalfState.LastShotOverheat.
    /// For the weapon in the local player's hands the shot adds no heat and the stored heat is cleared.
    /// </summary>
    public class NoOverheatPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Weapon), nameof(Weapon.OnShot));
        }

        [PatchPrefix]
        private static void Prefix(Weapon __instance, ref float ammoHeatFactor)
        {
            if (Plugin.On(Plugin.WeaponReliability) && MainPlayer.InHands(__instance))
            {
                ammoHeatFactor = 0f;
                __instance.MalfState.LastShotOverheat = 0f;
            }
        }
    }
}
