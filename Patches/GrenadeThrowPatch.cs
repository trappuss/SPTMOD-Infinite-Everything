using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using EFT.UI.DragAndDrop;
using GrenadeHands = EFT.Player.BaseGrenadeHandsController;

namespace InfiniteEverything.Patches
{
    /// <summary>
    /// Every grenade throw (normal and quick-throw; the Client* overrides call base) goes through
    /// BaseGrenadeHandsController.ThrowGrenade -> Player.ThrowGrenade -> ItemManipulator.Discard(grenade), which
    /// removes the grenade from its container (CurrentAddress becomes null) and unbinds its quick slot.
    /// The live grenade keeps a reference to the thrown item, so instead of keeping that item we put a NEW grenade of the
    /// same template where the old one was (or any free spot) and re-bind the quick slot it had.
    /// </summary>
    public class GrenadeThrowPatch : ModulePatch
    {
        internal sealed class ThrowState
        {
            public ThrowWeap Grenade;
            public ItemAddress Address;
            public InventoryController Inventory;
            public EBoundItem? Binding;
            public bool WasSelected;
        }

        private static AccessTools.FieldRef<FastAccessGrenadeItemView, List<ThrowWeap>> _sortedList;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GrenadeHands), nameof(GrenadeHands.ThrowGrenade));
        }

        [PatchPrefix]
        private static void Prefix(GrenadeHands __instance, out ThrowState __state)
        {
            __state = null;
            if (!Plugin.On(Plugin.InfiniteGrenades))
            {
                return;
            }

            try
            {
                Player player = __instance._player;
                if (player == null || !player.IsYourPlayer)
                {
                    return;
                }

                ThrowWeap grenade = __instance.Item;
                if (grenade == null || grenade.CurrentAddress == null)
                {
                    return;
                }

                InventoryController inventory = player.InventoryController;
                EBoundItem? binding = null;
                foreach (KeyValuePair<EBoundItem, Item> pair in inventory.Inventory.FastAccess.BoundItems)
                {
                    if (ReferenceEquals(pair.Value, grenade))
                    {
                        binding = pair.Key;
                        break;
                    }
                }

                __state = new ThrowState
                {
                    Grenade = grenade,
                    Address = grenade.CurrentAddress,
                    Inventory = inventory,
                    Binding = binding,
                    WasSelected = ReferenceEquals(inventory.Inventory.Equipment.TopPriorityGrenade, grenade)
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(GrenadeThrowPatch)} prefix: {ex}");
            }
        }

        /// <summary>
        /// The grenade quick-throw selection (Equipment.TopPriorityGrenade, key G) is kept by FastAccessGrenadeItemView:
        /// when the selected grenade is removed (the throw) it moves the selection to the first grenade left in its
        /// _sortedGrenadeList, which can be another type, and the replacement added after that is only appended to the
        /// end. So the replacement is moved to the front of that list and SetNewTopPriorityGrenade() is called (the view's
        /// own method: selects it and refreshes the icon). Without a view the selection is set directly.
        /// </summary>
        private static void KeepSelected(InventoryController inventory, ThrowWeap grenade)
        {
            bool viaView = false;
            try
            {
                _sortedList ??= AccessTools.FieldRefAccess<FastAccessGrenadeItemView, List<ThrowWeap>>("_sortedGrenadeList");
                foreach (FastAccessGrenadeItemView view in UnityEngine.Object.FindObjectsOfType<FastAccessGrenadeItemView>())
                {
                    List<ThrowWeap> list = _sortedList(view);
                    if (list == null || !list.Contains(grenade))
                    {
                        continue; // not in a throwing slot (rig/pockets) of this view
                    }

                    list.Remove(grenade);
                    list.Insert(0, grenade);
                    view.SetNewTopPriorityGrenade();
                    viaView = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Infinite grenades: grenade slot view not updated ({ex.Message}).");
            }

            if (!viaView)
            {
                inventory.Inventory.Equipment.TopPriorityGrenade = grenade;
            }
        }

        [PatchPostfix]
        private static void Postfix(ThrowState __state)
        {
            if (__state == null || __state.Grenade.CurrentAddress != null)
            {
                return; // not thrown (the throw failed)
            }

            try
            {
                InventoryController inventory = __state.Inventory;
                Item replacement = Singleton<ItemFactory>.Instance.CreateItem(inventory.NextId, __state.Grenade.StringTemplateId, null);

                ItemAddress target = __state.Address;
                if (!ItemManipulator.Add(replacement, target, inventory, simulate: true).Succeeded)
                {
                    target = (ItemAddress)ReloadKeepMagazinePatch.FindSpot(inventory, replacement, backpack: false)
                             ?? ReloadKeepMagazinePatch.FindSpot(inventory, replacement, backpack: true);
                }

                if (target == null)
                {
                    Plugin.Log.LogWarning("Infinite grenades: no free space for the replacement grenade.");
                    Plugin.Notify("Infinite grenades: no space for a new grenade");
                    return;
                }

                inventory.AddAndRaiseEvents(replacement, target);
                if (replacement.CurrentAddress == null)
                {
                    Plugin.Log.LogWarning("Infinite grenades: adding the replacement grenade failed (see the error above).");
                    return;
                }

                if (__state.Binding.HasValue)
                {
                    inventory.TryRunNetworkTransaction(BindResult.Run(inventory, replacement, __state.Binding.Value, simulate: true));
                }

                if (__state.WasSelected && replacement is ThrowWeap newGrenade)
                {
                    KeepSelected(inventory, newGrenade);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(GrenadeThrowPatch)} postfix: {ex}");
            }
        }
    }
}
