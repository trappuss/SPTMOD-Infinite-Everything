using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace InfiniteEverything.Patches
{
    /// <summary>
    /// Infinite money in raid. Paid exfils and BTR/trader services take the money out of your inventory in the raid
    /// itself (no server involved), so it is put back afterwards: a stack that was split gets its old count back, a stack
    /// that was handed over whole is replaced by a new one of the same currency and size in the same spot (or the first
    /// free spot in rig, pockets, backpack). You still need the money on you to pay. The pending list is processed by
    /// PlayerTicker every frame; entries that never pay out expire.
    /// </summary>
    internal static class MoneyRefunds
    {
        internal sealed class Stack
        {
            public Item Item;
            public ItemAddress Address;
            public int Count;
            public string Tpl;
        }

        private sealed class Pending
        {
            public string What;
            public InventoryController Inventory;
            public List<Stack> Stacks;
            public Task<bool> Service;
            public float Deadline;
        }

        private static readonly List<Pending> PendingList = new List<Pending>();

        internal static List<Stack> Snapshot(IEnumerable<Item> items)
        {
            var stacks = new List<Stack>();
            foreach (Item item in items)
            {
                if (MoneyRules.IsMoney(item) && item.CurrentAddress != null && item.StackObjectsCount > 0)
                {
                    stacks.Add(new Stack { Item = item, Address = item.CurrentAddress, Count = item.StackObjectsCount, Tpl = item.StringTemplateId });
                }
            }

            return stacks;
        }

        internal static void Add(string what, InventoryController inventory, List<Stack> stacks, Task<bool> service)
        {
            if (stacks == null || stacks.Count == 0)
            {
                return;
            }

            PendingList.Add(new Pending { What = what, Inventory = inventory, Stacks = stacks, Service = service, Deadline = Time.unscaledTime + 60f });
        }

        internal static void Tick()
        {
            for (int i = PendingList.Count - 1; i >= 0; i--)
            {
                Pending pending = PendingList[i];
                try
                {
                    if (pending.Service != null)
                    {
                        if (!pending.Service.IsCompleted)
                        {
                            if (Time.unscaledTime > pending.Deadline)
                            {
                                PendingList.RemoveAt(i);
                            }

                            continue;
                        }

                        PendingList.RemoveAt(i);
                        if (pending.Service.Status == TaskStatus.RanToCompletion && pending.Service.Result)
                        {
                            Restore(pending);
                        }

                        continue;
                    }

                    if (AnyTaken(pending))
                    {
                        PendingList.RemoveAt(i);
                        Restore(pending);
                    }
                    else if (Time.unscaledTime > pending.Deadline)
                    {
                        PendingList.RemoveAt(i);
                    }
                }
                catch (Exception ex)
                {
                    PendingList.RemoveAt(i);
                    Plugin.Log.LogError($"Money refund ({pending.What}) failed: {ex}");
                }
            }
        }

        private static bool AnyTaken(Pending pending)
        {
            foreach (Stack stack in pending.Stacks)
            {
                if (!StillThere(stack) || stack.Item.StackObjectsCount < stack.Count)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool StillThere(Stack stack)
        {
            return stack.Item.CurrentAddress != null && stack.Item.CurrentAddress.Equals(stack.Address);
        }

        private static void Restore(Pending pending)
        {
            int restored = 0;
            foreach (Stack stack in pending.Stacks)
            {
                if (StillThere(stack))
                {
                    int missing = stack.Count - stack.Item.StackObjectsCount;
                    if (missing > 0)
                    {
                        stack.Item.StackObjectsCount = stack.Count;
                        stack.Item.RaiseRefreshEvent();
                        restored += missing;
                    }

                    continue;
                }

                if (Give(pending.Inventory, stack))
                {
                    restored += stack.Count;
                }
            }

            if (restored > 0)
            {
                Plugin.Log.LogInfo($"Infinite money: {pending.What} paid, {restored} money given back.");
            }
        }

        private static bool Give(InventoryController inventory, Stack stack)
        {
            Item money = Singleton<ItemFactory>.Instance.CreateItem(inventory.NextId, stack.Tpl, null);
            money.StackObjectsCount = stack.Count; // the same size the stack had a moment ago

            ItemAddress target = stack.Address;
            if (!ItemManipulator.Add(money, target, inventory, simulate: true).Succeeded)
            {
                target = (ItemAddress)ReloadKeepMagazinePatch.FindSpot(inventory, money, backpack: false)
                         ?? ReloadKeepMagazinePatch.FindSpot(inventory, money, backpack: true);
            }

            if (target == null)
            {
                Plugin.Log.LogWarning($"Infinite money: no space to give back {stack.Count} x {stack.Tpl}.");
                Plugin.Notify("Infinite money: no space to give your money back");
                return false;
            }

            inventory.AddAndRaiseEvents(money, target);
            return money.CurrentAddress != null;
        }
    }

    /// <summary>
    /// Paid exfils (car / V-Ex style): TransferItemRequirement.TransferExitItem moves or splits the money stack into the
    /// exfil's own stash through a network transaction (which may finish a frame later). The stack is noted here and
    /// given back by MoneyRefunds as soon as the money has left it. Secret exits are not touched.
    /// </summary>
    public class PaidExfilPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TransferItemRequirement), nameof(TransferItemRequirement.TransferExitItem));
        }

        [PatchPrefix]
        private static void Prefix(Player player, Item item, out List<MoneyRefunds.Stack> __state)
        {
            __state = null;
            try
            {
                if (Plugin.On(Plugin.InfiniteMoney) && player != null && player.IsYourPlayer && MoneyRules.IsMoney(item))
                {
                    __state = MoneyRefunds.Snapshot(new[] { item });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(PaidExfilPatch)}: {ex}");
            }
        }

        [PatchPostfix]
        private static void Postfix(Player player, List<MoneyRefunds.Stack> __state)
        {
            if (__state != null)
            {
                MoneyRefunds.Add("paid exfil", player.InventoryController, __state, null);
            }
        }
    }

    /// <summary>
    /// BTR / trader services in raid (taxi, item delivery, cover fire...): InventoryController.TryPurchaseTraderService
    /// (no overrides; called by the BTR dialog and the in-raid transfer screen) pays from the items in
    /// Equipment.PaymentSlots. The money there is noted before and given back once the purchase task reports success.
    /// </summary>
    public class TraderServicePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.TryPurchaseTraderService));
        }

        [PatchPrefix]
        private static void Prefix(InventoryController __instance, out List<MoneyRefunds.Stack> __state)
        {
            __state = null;
            try
            {
                if (!Plugin.On(Plugin.InfiniteMoney) || MainPlayer.InventoryIfMine(__instance) == null)
                {
                    return;
                }

                var items = new List<Item>();
                foreach (Slot slot in __instance.Inventory.Equipment.PaymentSlots)
                {
                    if (slot.ContainedItem != null)
                    {
                        items.AddRange(slot.ContainedItem.GetAllItems());
                    }
                }

                __state = MoneyRefunds.Snapshot(items);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(TraderServicePatch)}: {ex}");
            }
        }

        [PatchPostfix]
        private static void Postfix(InventoryController __instance, Task<bool> __result, List<MoneyRefunds.Stack> __state)
        {
            if (__state != null && __result != null)
            {
                MoneyRefunds.Add("trader service", __instance, __state, __result);
            }
        }
    }

    /// <summary>
    /// Infinite hideout resources, client side. The hideout screen simulates fuel and filter use itself with
    /// ResourceConsumer.Work, which treats ResultConsumption 0 as "running for free" (IsWorking, nothing drained). While the
    /// option is on and the server part has confirmed (the server stops its own drain), ResultConsumption reads 0.
    /// </summary>
    public class HideoutResourcePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(ResourceConsumer), nameof(ResourceConsumer.ResultConsumption));
        }

        [PatchPostfix]
        private static void Postfix(ref float __result)
        {
            if (__result > 0f && StateSync.HideoutActive)
            {
                __result = 0f;
            }
        }
    }
}
