using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using MedEffect = EFT.HealthSystem.ActiveHealthController.MedEffect;

namespace InfiniteEverything.Patches
{
    /// <summary>
    /// Infinite item usage, medical/food/drink/stims. In raid every use is an ActiveHealthController.MedEffect:
    /// Added() reads the item's MedKitComponent (HpResource) / FoodDrinkComponent (HpPercent); RegularUpdate() and
    /// Residue() drain them; at the end of Residue() the item is removed with MedEffectHelper.RemoveItem when it is
    /// empty, or always for items with neither component (stims, injectors).
    /// Added: snapshot the resource. Residue: block MedEffectHelper.RemoveItem for this item, then put the resource
    /// back. ForceRemove (interrupted use): put the resource back too.
    /// </summary>
    internal static class MedUsage
    {
        internal sealed class Snapshot
        {
            public float? HpResource;
            public float? HpPercent;
        }

        internal static readonly ConditionalWeakTable<MedEffect, Snapshot> Snapshots = new ConditionalWeakTable<MedEffect, Snapshot>();

        [ThreadStatic]
        internal static Item KeepItem;

        internal static bool IsMine(MedEffect effect)
        {
            Player player = effect.HealthController?.Player;
            return player != null && player.IsYourPlayer;
        }

        internal static void Restore(MedEffect effect)
        {
            if (!Plugin.On(Plugin.InfiniteItemUsage) || !Snapshots.TryGetValue(effect, out Snapshot snapshot))
            {
                return;
            }

            bool changed = false;
            if (snapshot.HpResource.HasValue && effect._medKit != null && effect._medKit.HpResource != snapshot.HpResource.Value)
            {
                effect._medKit.HpResource = snapshot.HpResource.Value;
                changed = true;
            }

            if (snapshot.HpPercent.HasValue && effect._foodDrink != null && effect._foodDrink.HpPercent != snapshot.HpPercent.Value)
            {
                effect._foodDrink.HpPercent = snapshot.HpPercent.Value;
                changed = true;
            }

            if (changed)
            {
                effect.MedItem?.RaiseRefreshEvent();
            }
        }
    }

    public class MedEffectAddedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MedEffect), nameof(MedEffect.Added));
        }

        [PatchPostfix]
        private static void Postfix(MedEffect __instance)
        {
            try
            {
                if (!Plugin.On(Plugin.InfiniteItemUsage) || !MedUsage.IsMine(__instance))
                {
                    return;
                }

                var snapshot = new MedUsage.Snapshot
                {
                    HpResource = __instance._medKit?.HpResource,
                    HpPercent = __instance._foodDrink?.HpPercent
                };
                MedUsage.Snapshots.Remove(__instance);
                MedUsage.Snapshots.Add(__instance, snapshot);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(MedEffectAddedPatch)}: {ex}");
            }
        }
    }

    public class MedEffectResiduePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MedEffect), nameof(MedEffect.Residue));
        }

        [PatchPrefix]
        private static void Prefix(MedEffect __instance, out Item __state)
        {
            __state = null;
            try
            {
                if (Plugin.On(Plugin.InfiniteItemUsage) && MedUsage.Snapshots.TryGetValue(__instance, out _))
                {
                    __state = __instance.MedItem;
                    MedUsage.KeepItem = __state;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(MedEffectResiduePatch)} prefix: {ex}");
            }
        }

        [PatchFinalizer]
        private static void Finalizer(MedEffect __instance, Item __state)
        {
            if (__state == null)
            {
                return;
            }

            MedUsage.KeepItem = null;
            try
            {
                MedUsage.Restore(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(MedEffectResiduePatch)} finalizer: {ex}");
            }
        }
    }

    public class MedEffectForceRemovePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MedEffect), nameof(MedEffect.ForceRemove));
        }

        [PatchPostfix]
        private static void Postfix(MedEffect __instance)
        {
            try
            {
                MedUsage.Restore(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(MedEffectForceRemovePatch)}: {ex}");
            }
        }
    }

    /// <summary>Skips MedEffectHelper.RemoveItem only for the item whose MedEffect.Residue is running (see above).</summary>
    public class MedRemoveItemPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MedEffectHelper), nameof(MedEffectHelper.RemoveItem));
        }

        [PatchPrefix]
        private static bool Prefix(Item item, ref bool __result)
        {
            if (item != null && ReferenceEquals(item, MedUsage.KeepItem))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Keys and keycards: WorldInteractiveObject.UnlockOperation and its KeycardDoor override (which does not call
    /// base) do key.NumberOfUsages++ and discard the key at MaximumNumberOfUsage. The prefix lowers the count by one
    /// first so the ++ lands on the old value (no discard); the finalizer puts the exact old value back either way.
    /// </summary>
    internal static class KeyUsage
    {
        internal struct State
        {
            public KeyComponent Key;
            public int Uses;
        }

        internal static State Before(KeyComponent key, Player player)
        {
            if (!Plugin.On(Plugin.InfiniteKeyUsage) || key == null || player == null || !player.IsYourPlayer)
            {
                return default;
            }

            var state = new State { Key = key, Uses = key.NumberOfUsages };
            key.NumberOfUsages -= 1;
            return state;
        }

        internal static void After(State state)
        {
            if (state.Key != null)
            {
                state.Key.NumberOfUsages = state.Uses;
            }
        }
    }

    public class KeyUnlockPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WorldInteractiveObject), nameof(WorldInteractiveObject.UnlockOperation));
        }

        [PatchPrefix]
        private static void Prefix(KeyComponent key, Player player, out KeyUsage.State __state)
        {
            __state = KeyUsage.Before(key, player);
        }

        [PatchFinalizer]
        private static void Finalizer(KeyUsage.State __state)
        {
            KeyUsage.After(__state);
        }
    }

    public class KeycardDoorUnlockPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(KeycardDoor), nameof(KeycardDoor.UnlockOperation));
        }

        [PatchPrefix]
        private static void Prefix(KeyComponent key, Player player, out KeyUsage.State __state)
        {
            __state = KeyUsage.Before(key, player);
        }

        [PatchFinalizer]
        private static void Finalizer(KeyUsage.State __state)
        {
            KeyUsage.After(__state);
        }
    }

    /// <summary>
    /// The Labs access keycard: at raid start BaseLocalGame.RemoveUsedLocationKeycard adds a use and removes the card
    /// at its limit. Skipped with Infinite key usage on. Verified: the SPT 4.1.6 server never changes NumberOfUsages, so the client is the only place a use is charged.
    /// </summary>
    public class LabsKeycardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), nameof(BaseLocalGame<EftGamePlayerOwner>.RemoveUsedLocationKeycard));
        }

        [PatchPrefix]
        private static bool Prefix()
        {
            if (Plugin.On(Plugin.InfiniteKeyUsage))
            {
                Plugin.Log.LogInfo("Infinite key usage: location keycard not charged.");
                return false;
            }

            return true;
        }
    }
}
