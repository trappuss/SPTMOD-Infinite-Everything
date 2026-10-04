using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace InfiniteEverything
{
    internal static class MainPlayer
    {
        /// <summary>The local player in a raid or the hideout, else null.</summary>
        internal static Player Get()
        {
            if (!Singleton<GameWorld>.Instantiated)
            {
                return null;
            }

            Player player = Singleton<GameWorld>.Instance.MainPlayer;
            return player != null ? player : null;
        }

        /// <summary>The local player's InventoryController if <paramref name="controller"/> is it, else null.</summary>
        internal static InventoryController InventoryIfMine(ItemController controller)
        {
            if (controller == null)
            {
                return null;
            }

            Player player = Get();
            if (player == null)
            {
                return null;
            }

            InventoryController inventory = player.InventoryController;
            return ReferenceEquals(inventory, controller) ? inventory : null;
        }

        /// <summary>True when <paramref name="item"/> is what the local player holds (a hand-held weapon or a mounted turret).</summary>
        internal static bool InHands(Item item)
        {
            if (item == null)
            {
                return false;
            }

            Player player = Get();
            return player != null && ReferenceEquals(player.HandsController?.Item, item);
        }

        /// <summary>True when <paramref name="item"/> sits somewhere in the local player's inventory.</summary>
        internal static bool Owns(Item item)
        {
            if (item == null)
            {
                return false;
            }

            Player player = Get();
            return player != null && ReferenceEquals(item.Owner, player.InventoryController);
        }
    }
}
