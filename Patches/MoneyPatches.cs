using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using EFT.UI.Insurance;
using EFT.UI.Ragfair;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace InfiniteEverything.Patches
{
    /// <summary>
    /// Infinite money, client side. The money is never taken by the SERVER part (PaymentService.AddPaymentToOutput is
    /// skipped), so these patches only (1) let the screens accept a purchase you could not afford and (2) stop the client
    /// from taking money out of its own copy of the stash where it does that itself (healing, hideout).
    /// Nothing here adds or removes money: turning the option off leaves exactly the money you had.
    /// </summary>
    internal static class MoneyRules
    {
        /// <summary>What the "money in stash" checks see while infinite money is on.</summary>
        internal const int Shown = 999_999_999;

        [ThreadStatic]
        internal static int InflateDepth;

        [ThreadStatic]
        internal static int HideoutDepth;

        internal static bool IsMoney(Item item)
        {
            return item != null && CurrencyUtil.TryGetCurrencyType(item.TemplateId, out _);
        }

        internal static bool IsMoneyTpl(string tpl)
        {
            return !string.IsNullOrEmpty(tpl) && CurrencyUtil.TryGetCurrencyType(tpl, out _);
        }
    }

    /// <summary>
    /// InventoryExtension.GetMoneySums is what the insurance, trader repair, healing, flea listing fee and flea renew
    /// screens compare the price against. Only while one of those screens is checking (InflateDepth > 0) and infinite money
    /// is active, each currency reads as at least 999,999,999. Everything else (quests, transfers, displays) sees the real sums.
    /// </summary>
    public class MoneySumsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InventoryExtension), nameof(InventoryExtension.GetMoneySums));
        }

        [PatchPostfix]
        private static void Postfix(Dictionary<ECurrencyType, int> __result)
        {
            if (MoneyRules.InflateDepth <= 0 || __result == null || !StateSync.MoneyActive)
            {
                return;
            }

            foreach (ECurrencyType currency in new List<ECurrencyType>(__result.Keys))
            {
                if (__result[currency] < MoneyRules.Shown)
                {
                    __result[currency] = MoneyRules.Shown;
                }
            }
        }
    }

    /// <summary>
    /// Marks the screen checks whose GetMoneySums calls are inflated (see MoneySumsPatch). One instance per screen method:
    /// insurance (InsurerParametersPanel.UpdateLabels), trader repair (RepairerParametersPanel.UpdateTraderLabels),
    /// healing (HealthTreatmentServiceView.UpdateMoneyInStash), flea listing fee (AddOfferWindow.SetPrices) and flea renew
    /// (RenewOfferWindow.UpdateInfo). (ModulePatch only finds patch methods declared on the class itself, hence one class
    /// taking its target in the constructor.)
    /// </summary>
    public class InflateMoneyPatch : ModulePatch
    {
        private readonly MethodBase _target;

        public InflateMoneyPatch(MethodBase target)
            : base("com.trappuss.infiniteeverything.inflate." + (target?.DeclaringType?.Name ?? "null") + "." + (target?.Name ?? "null"))
        {
            _target = target;
        }

        internal static InflateMoneyPatch[] All()
        {
            return new[]
            {
                new InflateMoneyPatch(AccessTools.Method(typeof(InsurerParametersPanel), nameof(InsurerParametersPanel.UpdateLabels))),
                new InflateMoneyPatch(AccessTools.Method(typeof(RepairerParametersPanel), nameof(RepairerParametersPanel.UpdateTraderLabels))),
                new InflateMoneyPatch(AccessTools.Method(typeof(HealthTreatmentServiceView), nameof(HealthTreatmentServiceView.UpdateMoneyInStash))),
                new InflateMoneyPatch(AccessTools.Method(typeof(AddOfferWindow), nameof(AddOfferWindow.SetPrices))),
                new InflateMoneyPatch(AccessTools.Method(typeof(RenewOfferWindow), nameof(RenewOfferWindow.UpdateInfo)))
            };
        }

        protected override MethodBase GetTargetMethod()
        {
            return _target;
        }

        [PatchPrefix]
        private static void Prefix()
        {
            MoneyRules.InflateDepth++;
        }

        [PatchFinalizer]
        private static void Finalizer()
        {
            MoneyRules.InflateDepth--;
        }
    }

    /// <summary>
    /// Healing: HealthTreatmentServiceView.ApplyTreatments takes the roubles out of the client's stash itself
    /// (ItemManipulator.Remove / StackObjectsCount -=) before sending the request. The server part doesn't take them, so
    /// the client's copy is put back exactly as it was (same items, same places, same counts).
    /// </summary>
    public class HealingKeepMoneyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HealthTreatmentServiceView), nameof(HealthTreatmentServiceView.ApplyTreatments));
        }

        [PatchPrefix]
        private static void Prefix(HealthTreatmentServiceView __instance, out List<MoneyRefunds.Stack> __state)
        {
            __state = null;
            if (!StateSync.MoneyActive)
            {
                return;
            }

            try
            {
                __state = MoneyRefunds.Snapshot(__instance.StashItems);
            }
            catch (Exception ex)
            {
                __state = null;
                Plugin.Log.LogError($"{nameof(HealingKeepMoneyPatch)} prefix: {ex}");
            }
        }

        [PatchFinalizer]
        private static void Finalizer(HealthTreatmentServiceView __instance, List<MoneyRefunds.Stack> __state, InventoryController ____inventoryController)
        {
            if (__state == null)
            {
                return;
            }

            try
            {
                foreach (MoneyRefunds.Stack stack in __state)
                {
                    if (stack.Item.CurrentAddress == null)
                    {
                        ItemManipulator.Add(stack.Item, stack.Address, ____inventoryController);
                    }

                    stack.Item.StackObjectsCount = stack.Count;
                }

                __instance.UpdateMoneyInStash();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(HealingKeepMoneyPatch)} finalizer: {ex}");
            }
        }
    }

    /// <summary>
    /// Traders: the Deal button needs every Requisite to be Enough (PreparedItemsCount >= RequiredItemsCount). Money
    /// requisites count as enough; the trade then sends whatever money stacks were prepared (possibly none) and the server
    /// part takes nothing. Barter items are untouched.
    /// </summary>
    public class TraderMoneyRequisitePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(Requisite), nameof(Requisite.Enough));
        }

        [PatchPostfix]
        private static void Postfix(Requisite __instance, ref bool __result)
        {
            if (!__result && __instance.RequiredItemsCount > 0 && StateSync.MoneyActive && MoneyRules.IsMoney(__instance.RequiredItem))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// Flea market purchases: HandoverRagfairMoneyWindow refuses ("not enough money") when _existingAmount is below
    /// _neededAmount for a currency. Existing is raised to needed; the money stacks it picked are sent and the server part
    /// takes nothing.
    /// </summary>
    public class FleaBuyMoneyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HandoverRagfairMoneyWindow), nameof(HandoverRagfairMoneyWindow.UpdateItemsData));
        }

        [PatchPostfix]
        private static void Postfix(Dictionary<ECurrencyType, int> ____neededAmount, Dictionary<ECurrencyType, int> ____existingAmount)
        {
            if (!StateSync.MoneyActive || ____neededAmount == null || ____existingAmount == null)
            {
                return;
            }

            foreach (KeyValuePair<ECurrencyType, int> need in ____neededAmount)
            {
                if (need.Value > 0 && (!____existingAmount.TryGetValue(need.Key, out int have) || have < need.Value))
                {
                    ____existingAmount[need.Key] = need.Value;
                }
            }
        }
    }

    /// <summary>
    /// The flea's automatic currency exchange (buying dollars/euros with roubles through a trader) must not run while
    /// infinite money is on: it would be a trade that hands you currency without taking any.
    /// </summary>
    public class NoAutoExchangePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AutoExchange), nameof(AutoExchange.CanPurchaseWithAutoExchange));
        }

        [PatchPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (StateSync.MoneyActive)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Hideout upgrades and the scav case: a money ItemRequirement counts as fulfilled. Works without the server part:
    /// the server takes only the items the client sends, and the client sends no money (see HideoutMoneyReferencesPatch).
    /// </summary>
    public class HideoutMoneyRequirementPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemRequirement), nameof(ItemRequirement.Retest));
        }

        [PatchPostfix]
        private static void Postfix(ItemRequirement __instance)
        {
            if (!__instance.Fulfilled && Plugin.On(Plugin.InfiniteMoney) && MoneyRules.IsMoneyTpl(__instance.TemplateId))
            {
                __instance.Error = null;
                __instance.SetFulfillment(true);
            }
        }
    }

    /// <summary>
    /// HideoutRepresentation.UpgradeZone and StartScavCaseProduction call GetItemReferences, remove those items from the
    /// client's stash (ResolveReferenceAction) and send them to the server, which removes exactly those. Both run that part
    /// synchronously before their first await, so while they run (HideoutDepth > 0) money references are dropped from the
    /// list: no money is removed on either side.
    /// </summary>
    public class HideoutSpendPatch : ModulePatch
    {
        private readonly MethodBase _target;

        public HideoutSpendPatch(MethodBase target)
            : base("com.trappuss.infiniteeverything.hideoutspend." + (target?.Name ?? "null"))
        {
            _target = target;
        }

        internal static HideoutSpendPatch[] All()
        {
            return new[]
            {
                new HideoutSpendPatch(AccessTools.Method(typeof(HideoutRepresentation), nameof(HideoutRepresentation.UpgradeZone))),
                new HideoutSpendPatch(AccessTools.Method(typeof(HideoutRepresentation), nameof(HideoutRepresentation.StartScavCaseProduction)))
            };
        }

        protected override MethodBase GetTargetMethod()
        {
            return _target;
        }

        [PatchPrefix]
        private static void Prefix()
        {
            MoneyRules.HideoutDepth++;
        }

        [PatchFinalizer]
        private static void Finalizer()
        {
            MoneyRules.HideoutDepth--;
        }
    }

    public class HideoutMoneyReferencesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutRepresentation), nameof(HideoutRepresentation.GetItemReferences));
        }

        [PatchPostfix]
        private static void Postfix(List<HideoutItemReference> __result)
        {
            if (MoneyRules.HideoutDepth > 0 && __result != null && Plugin.On(Plugin.InfiniteMoney))
            {
                int removed = __result.RemoveAll(reference => MoneyRules.IsMoney(reference.Item));
                if (removed > 0)
                {
                    Plugin.Log.LogInfo($"Infinite money: {removed} money stack(s) kept out of a hideout payment.");
                }
            }
        }
    }
}
