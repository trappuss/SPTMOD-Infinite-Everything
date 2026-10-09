using System;
using EFT;
using EFT.InventoryLogic;

namespace InfiniteEverything
{
    /// <summary>
    /// Items the mod creates are not loot. In a raid the game pays looting experience (and a Search skill action) for an
    /// item in two places, and skips both only for the items the player brought in (BaseStatisticsManager fills
    /// _foundItemsIds and _lootedItemsIds from the inventory when the raid starts):
    ///  - "found": the first time an item becomes known in a rig, pocket or backpack grid
    ///    (AddResult.RaiseEvents, ActiveSearchController.SetItemAsKnown, OnItemFound, BaseStatisticsManager.OnLootItem
    ///    and Player.InventoryControllerOnItemFound);
    ///  - "grabbed": when the player moves an item, with everything inside it, into their inventory by hand
    ///    (PlayerInventoryController.InProcess, OnGrabLoot).
    /// A created item has a new id, so both paid out: every borrowed twin of a double-tap gave looting experience in
    /// the 2.5.0 raid test. Before it enters the inventory a created item is entered as already discovered, found and
    /// looted, like the items the player came in with. The hideout has neither controller and nothing happens there.
    /// </summary>
    internal static class NotLoot
    {
        /// <summary>Marks <paramref name="item"/>, and everything inside it, for the local player's raid statistics.</summary>
        internal static void Mark(ItemController owner, Item item)
        {
            try
            {
                Player player = MainPlayer.Get();
                if (item == null || player == null || MainPlayer.InventoryIfMine(owner) == null)
                {
                    return;
                }

                if (owner.SearchController is ActiveSearchController search)
                {
                    search._discoveredItems.Add(item); // SetItemAsKnown then raises no "item found"
                }

                if (player.StatisticsManager is BaseStatisticsManager statistics)
                {
                    foreach (Item part in item.GetAllItems())
                    {
                        statistics._foundItemsIds.Add(part.Id);
                        statistics._lootedItemsIds.Add(part.Id);
                    }
                }

                // A full or empty magazine the profile has not "checked" is checked by the game the first time it is
                // moved (PlayerInventoryController.CheckMagazineAmmoDepend), which pays a MagDrills skill action and
                // stores its id in the profile. A created magazine is entered as checked, and taken out again when it
                // ends (Forget), so neither happens.
                if (item is Magazine magazine && player.Profile?.CheckedMagazines != null && !player.Profile.CheckedMagazines.ContainsKey(magazine.Id))
                {
                    player.Profile.CheckedMagazines[magazine.Id] = 2;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(NotLoot)}: {ex}");
            }
        }

        /// <summary>A created magazine has left the inventory for good: its id leaves the profile's checked-magazine list.</summary>
        internal static void Forget(Item item)
        {
            try
            {
                Player player = MainPlayer.Get();
                if (item is Magazine magazine && player?.Profile?.CheckedMagazines != null)
                {
                    player.Profile.CheckedMagazines.Remove(magazine.Id);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(NotLoot)}: {ex}");
            }
        }

        /// <summary>ItemController.AddAndRaiseEvents for an item the mod created.</summary>
        internal static void Add(ItemController owner, Item item, ItemAddress address)
        {
            Mark(owner, item);
            owner.AddAndRaiseEvents(item, address);
        }
    }
}
