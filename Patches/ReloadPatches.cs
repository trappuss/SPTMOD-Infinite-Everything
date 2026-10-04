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
    /// </summary>
    public class ReloadKeepMagazinePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ReloadResult), nameof(ReloadResult.Run));
        }

        [PatchPrefix]
        private static void Prefix(ItemController itemController, Weapon weapon, Magazine nextMagazine, ref ItemAddress vestTargetAddress)
        {
            try
            {
                InventoryController inventory = MainPlayer.InventoryIfMine(itemController);
                if (inventory == null)
                {
                    return; // bots
                }

                MagazineMemory.Remember(nextMagazine);

                if (!Plugin.On(Plugin.InfiniteAmmo) || vestTargetAddress != null)
                {
                    return;
                }

                Magazine current = weapon.GetCurrentMagazine();
                if (current == null)
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
                    Plugin.Log.LogWarning("No free space in rig, pockets or backpack: the old magazine will be dropped.");
                    Plugin.Notify("Infinite ammo: no space for the old magazine - dropped");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(ReloadKeepMagazinePatch)}: {ex}");
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
}
