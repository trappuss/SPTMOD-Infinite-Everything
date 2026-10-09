using System;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using Grid = EFT.InventoryLogic.Grid;
using ReloadOp = EFT.Player.FirearmController.ReloadExternalMagOperation;
using ReloadResult = EFT.Player.FirearmController.ReloadExternalMagResult;

namespace InfiniteEverything.Patches
{
    /// <summary>
    /// Infinite ammo, part 1. ReloadExternalMagResult.Run (static) builds every external-mag swap. With
    /// vestTargetAddress == null it REMOVES the old magazine and OnMagPuttedToRig throws it on the ground (quick reload,
    /// or a normal reload without space); with an address it MOVES it there. For the local player this fills in a free
    /// spot (rig + pockets, then backpack, smallest grid first like the game's own normal reload). The quick-reload
    /// flag is untouched, so the double-tap animation stays fast. Also records what the incoming magazine holds.
    /// With no free spot at all the magazine is not thrown on the ground: Run removes it, Borrowed keeps it
    /// (KeepRemovedMagazinePatch stops the throw) and puts it back as soon as there is room, usually the spot the new
    /// magazine came from. Swaps with a borrowed magazine never use a spot at all (Borrowed.BeforeReload).
    /// </summary>
    public class ReloadKeepMagazinePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ReloadResult), nameof(ReloadResult.Run));
        }

        [PatchPrefix]
        private static void Prefix(ItemController itemController, Weapon weapon, Magazine nextMagazine, ref ItemAddress vestTargetAddress, out Magazine __state)
        {
            __state = null; // the player's own magazine Run is about to take out of the inventory
            try
            {
                InventoryController inventory = MainPlayer.InventoryIfMine(itemController);
                if (inventory == null)
                {
                    return; // bots
                }

                MagazineMemory.Remember(nextMagazine);

                Magazine current = weapon.GetCurrentMagazine();
                if (current == null)
                {
                    return;
                }

                if (!ReferenceEquals(current, nextMagazine) && Borrowed.BeforeReload(inventory, weapon, current, nextMagazine, ref vestTargetAddress, out __state))
                {
                    return;
                }

                if (!Plugin.On(Plugin.InfiniteAmmo) || vestTargetAddress != null)
                {
                    return;
                }

                ItemAddress spot = FindSpot(inventory, current, backpack: false) ?? FindSpot(inventory, current, backpack: true);
                if (spot != null)
                {
                    vestTargetAddress = spot;
                }
                else
                {
                    __state = current;
                    Plugin.Log.LogInfo($"No free space in rig, pockets or backpack: {current} is kept aside until there is room.");
                    Plugin.Notify("Infinite ammo: no space for the old magazine - kept until there is room");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(ReloadKeepMagazinePatch)}: {ex}");
            }
        }

        /// <summary>A finalizer, not a postfix: the magazine must be kept even if Run throws after removing it.</summary>
        [PatchFinalizer]
        private static void Finalizer(ItemController itemController, Weapon weapon, Magazine __state)
        {
            if (__state == null)
            {
                return;
            }

            try
            {
                Borrowed.AfterReload(MainPlayer.InventoryIfMine(itemController), weapon, __state);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(ReloadKeepMagazinePatch)} finalizer: {ex}");
            }
        }
        internal static GridItemAddress FindSpot(InventoryController inventory, Item item, bool backpack)
        {
            GridItemAddress best = null;
            int bestArea = int.MaxValue;
            foreach (Grid grid in inventory.Inventory.Equipment.GetPrioritizedGridsForUnloadedObject(backpack))
            {
                GridItemAddress address = grid.FindLocationForItem(item);
                if (address == null)
                {
                    continue;
                }

                // In a raid the game refuses a move into a container it has not searched (UnknownAddressError), and a
                // reload given such a place for the old magazine is refused without a word.
                if (!ItemManipulator.CanTransferTo(address, inventory, out _))
                {
                    continue;
                }

                int area = grid.GridWidth * grid.GridHeight;
                if (area < bestArea)
                {
                    best = address;
                    bestArea = area;
                }
            }

            return best;
        }
    }

    /// <summary>
    /// Infinite ammo, part 2. ReloadExternalMagOperation.SwitchToIdlingState is where an external-mag reload completes
    /// (normal end, IdleStart event and FastForward all go through it; its body only runs while State != Finished).
    /// The old magazine is then back in the inventory and gets refilled here.
    /// </summary>
    public class ReloadFinishedRefillPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ReloadOp), nameof(ReloadOp.SwitchToIdlingState));
        }

        [PatchPrefix]
        private static void Prefix(ReloadOp __instance, out ReloadResult __state)
        {
            __state = null;
            try
            {
                if (__instance.State != Player.EOperationState.Finished && __instance.Player != null && __instance.Player.IsYourPlayer)
                {
                    __state = __instance.ReloadExternalMagResult;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(ReloadFinishedRefillPatch)} prefix: {ex}");
            }
        }

        [PatchPostfix]
        private static void Postfix(ReloadOp __instance, ReloadResult __state)
        {
            if (__state == null || !Plugin.On(Plugin.InfiniteAmmo))
            {
                return;
            }

            try
            {
                Magazine oldMagazine = __state.OldMagazine;
                if (oldMagazine == null)
                {
                    return; // the gun had no magazine
                }

                if (ReferenceEquals(oldMagazine, __state.NextMagazine))
                {
                    // A self-reload (EmptyReloadPatch): the magazine was filled before the reload and one round went into
                    // the chamber, like any full magazine. Leave it as the game left it.
                    return;
                }

                if (Borrowed.Contains(oldMagazine) || Borrowed.IsHeld(oldMagazine))
                {
                    return; // a borrowed magazine has ended; one of your own that is kept aside is refilled when it comes back
                }

                InventoryController inventory = __instance.Player.InventoryController;
                if (!ReferenceEquals(oldMagazine.Owner, inventory))
                {
                    Plugin.Log.LogInfo($"Old magazine {oldMagazine} is not in your inventory (dropped?); not refilled.");
                    return;
                }

                MagazineMemory.Refill(inventory, oldMagazine, __state.NextMagazine);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(ReloadFinishedRefillPatch)} postfix: {ex}");
            }
        }
    }

    /// <summary>
    /// ReloadExternalMagOperation.OnMagPuttedToRig throws the magazine a reload REMOVED (no place for it) on the ground
    /// through DropMod. Not a borrowed magazine, which has simply ended, and not one of the player's own magazines that
    /// Borrowed keeps aside to put back later. Every other magazine is thrown as the game does.
    /// </summary>
    public class KeepRemovedMagazinePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ReloadOp), nameof(ReloadOp.DropMod));
        }

        [PatchPrefix]
        private static bool Prefix(Item droppedMod)
        {
            return !(Borrowed.Contains(droppedMod) || Borrowed.IsHeld(droppedMod));
        }
    }
}