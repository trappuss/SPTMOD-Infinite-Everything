using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace InfiniteEverything
{
    /// <summary>
    /// Remembers what each magazine held when it was loaded into the gun (bottom -> top, as in StackSlot._items),
    /// and refills a magazine back to that. EFT pops rounds from the top (StackSlot.Last), so the rounds still in a
    /// magazine are the bottom part of its snapshot and the missing ones are the top part.
    /// </summary>
    internal static class MagazineMemory
    {
        private struct AmmoStack
        {
            public string Tpl;
            public int Count;
        }

        private static readonly Dictionary<string, List<AmmoStack>> Snapshots = new Dictionary<string, List<AmmoStack>>();

        /// <summary>Called when a magazine is about to go into the gun.</summary>
        internal static void Remember(Magazine magazine)
        {
            if (magazine == null)
            {
                return;
            }

            var stacks = new List<AmmoStack>();
            foreach (Item item in magazine.Cartridges.Items)
            {
                if (item.StackObjectsCount > 0)
                {
                    stacks.Add(new AmmoStack { Tpl = item.StringTemplateId, Count = item.StackObjectsCount });
                }
            }

            if (stacks.Count == 0)
            {
                return; // an empty magazine: keep whatever we knew about it
            }

            if (Snapshots.Count > 512)
            {
                Snapshots.Clear();
            }

            Snapshots[magazine.Id] = stacks;
        }

        /// <summary>The round type a magazine holds now, or held when it was last loaded into a gun.</summary>
        internal static string TopTemplate(Magazine magazine)
        {
            if (magazine == null)
            {
                return null;
            }

            Item last = magazine.Cartridges.Last;
            if (last != null)
            {
                return last.StringTemplateId;
            }

            return Snapshots.TryGetValue(magazine.Id, out List<AmmoStack> stacks) && stacks.Count > 0
                ? stacks[stacks.Count - 1].Tpl
                : null;
        }

        /// <summary>
        /// Fills <paramref name="magazine"/> to capacity. The missing rounds come from its snapshot first (so a mixed
        /// load, e.g. tracers on top, comes back in the same order), then the rest of the space is filled with the
        /// top round type. Without a snapshot the round type is the one it holds, else the type of
        /// <paramref name="fallbackSource"/>, else <paramref name="fallbackTpl"/>.
        /// <paramref name="owner"/> is the controller that owns the magazine (the player's inventory, or a turret's).
        /// </summary>
        internal static int Refill(ItemController owner, Magazine magazine, Magazine fallbackSource, string fallbackTpl = null)
        {
            StackSlot cartridges = magazine.Cartridges;
            int capacity = cartridges.MaxCount;
            int have = cartridges.Count;
            if (have >= capacity)
            {
                return 0;
            }

            var plan = new List<AmmoStack>();
            if (Snapshots.TryGetValue(magazine.Id, out List<AmmoStack> snapshot))
            {
                int skip = have;
                foreach (AmmoStack stack in snapshot)
                {
                    if (skip >= stack.Count)
                    {
                        skip -= stack.Count;
                        continue;
                    }

                    plan.Add(new AmmoStack { Tpl = stack.Tpl, Count = stack.Count - skip });
                    skip = 0;
                }

                // The snapshot may be a partly loaded magazine: fill the rest with its top round type.
                int planned = have;
                foreach (AmmoStack stack in plan)
                {
                    planned += stack.Count;
                }

                if (planned < capacity && snapshot.Count > 0)
                {
                    plan.Add(new AmmoStack { Tpl = snapshot[snapshot.Count - 1].Tpl, Count = capacity - planned });
                }
            }
            else
            {
                string tpl = TopTemplate(magazine) ?? TopTemplate(fallbackSource) ?? fallbackTpl;
                if (tpl == null)
                {
                    Plugin.Log.LogWarning($"Refill: no known round type for {magazine}; left as is.");
                    return 0;
                }

                plan.Add(new AmmoStack { Tpl = tpl, Count = capacity - have });
            }

            ItemFactory factory = Singleton<ItemFactory>.Instance;
            int added = 0;
            foreach (AmmoStack stack in plan)
            {
                int remaining = stack.Count;
                while (remaining > 0 && cartridges.Count < capacity)
                {
                    Item ammo = factory.CreateItem(owner.NextId, stack.Tpl, null);
                    int maxStack = Math.Max(1, ammo.Template.StackMaxSize);
                    int count = Math.Min(Math.Min(remaining, maxStack), capacity - cartridges.Count);
                    ammo.StackObjectsCount = count;

                    if (!cartridges.CheckCompatibility(ammo))
                    {
                        Plugin.Log.LogWarning($"Refill: {stack.Tpl} does not fit {magazine}; stopped.");
                        return added;
                    }

                    int before = cartridges.Count;
                    owner.AddAndRaiseEvents(ammo, cartridges.CreateItemAddress());
                    if (cartridges.Count <= before)
                    {
                        Plugin.Log.LogWarning($"Refill: adding {count} x {stack.Tpl} to {magazine} failed (see the error above); stopped.");
                        return added;
                    }

                    remaining -= count;
                    added += count;
                }
            }

            if (added > 0)
            {
                Plugin.Log.LogDebug($"Refilled {magazine}: +{added} ({cartridges.Count}/{capacity}).");
            }

            return added;
        }
    }
}
