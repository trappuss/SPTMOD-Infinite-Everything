using System;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using FireOp = EFT.Player.FirearmController.FireOperationBase;

namespace InfiniteEverything.Patches
{
    /// <summary>
    /// FireOperationBase.ProcessInventoryOperationsForSingleShot runs once per shot for semi-auto and full-auto fire
    /// (FireOperation and AutomaticFireOperation), for hand-held weapons and mounted turrets alike (a turret is a
    /// normal FirearmController; FirearmController.IsStationaryWeapon tells them apart). It fires the chambered round
    /// and pops the magazine's top round (StackSlot.Last) into the chamber: a split when the top stack has 2+ rounds,
    /// a move of the whole item when it has 1.
    ///  - Infinite magazine / Turret magazine: the prefix adds one round to the top stack first, so the pop is always
    ///    a split and the count ends where it started. The finalizer takes it back if nothing was popped.
    ///  - Turret ammo: the postfix refills an empty turret belt right after the shot (the last round is already in
    ///    the chamber, so the turret never runs dry). EFT has no turret reload: FirearmHandsInputTranslator refuses
    ///    to unload/reload stationary weapons and CheckAmmo/CheckChamber skip them.
    /// Revolver cylinders, launchers and break-action barrels use other fire operations and are not affected.
    /// </summary>
    public class ShotPatch : ModulePatch
    {
        internal sealed class ShotState
        {
            public Magazine Magazine;
            public Item Top;
            public int CountBefore;
            public bool Turret;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FireOp), nameof(FireOp.ProcessInventoryOperationsForSingleShot));
        }

        [PatchPrefix]
        private static void Prefix(FireOp __instance, out ShotState __state)
        {
            __state = null;
            if (!Plugin.Enabled.Value)
            {
                return;
            }

            try
            {
                Player player = __instance.Player;
                if (player == null || !player.IsYourPlayer)
                {
                    return;
                }

                bool turret = __instance.Controller != null && __instance.Controller.IsStationaryWeapon;
                bool keepRounds = turret ? Plugin.TurretMagazine.Value : Plugin.InfiniteMagazine.Value;
                bool refillBelt = turret && Plugin.TurretAmmo.Value;
                if (!keepRounds && !refillBelt)
                {
                    return;
                }

                Magazine magazine = __instance.Weapon?.GetCurrentMagazine();
                if (magazine == null || magazine is CylinderMagazine)
                {
                    return;
                }

                var state = new ShotState { Magazine = magazine, Turret = turret, CountBefore = magazine.Cartridges.Count };
                if (keepRounds)
                {
                    Item top = magazine.Cartridges.Last;
                    if (top != null && top.StackObjectsCount > 0)
                    {
                        top.StackObjectsCount += 1;
                        state.Top = top;
                    }
                }

                __state = state;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(ShotPatch)} prefix: {ex}");
            }
        }

        [PatchFinalizer]
        private static void Finalizer(FireOp __instance, ShotState __state)
        {
            if (__state == null)
            {
                return;
            }

            try
            {
                StackSlot cartridges = __state.Magazine.Cartridges;

                // Popped: the count is back to CountBefore. Not popped: one higher -> undo our extra round.
                if (__state.Top != null && cartridges.Count > __state.CountBefore && __state.Top.StackObjectsCount > 1)
                {
                    __state.Top.StackObjectsCount -= 1;
                }

                if (__state.Turret && Plugin.On(Plugin.TurretAmmo) && cartridges.Count == 0)
                {
                    var owner = __state.Magazine.Owner as ItemController;
                    if (owner == null)
                    {
                        Plugin.Log.LogWarning($"Turret belt {__state.Magazine} has no item controller; not refilled.");
                        return;
                    }

                    Weapon weapon = __instance.Weapon;
                    string chambered = weapon != null && weapon.HasChambers ? weapon.Chambers[0].ContainedItem?.StringTemplateId : null;
                    int added = MagazineMemory.Refill(owner, __state.Magazine, null, chambered);
                    Plugin.Log.LogInfo($"Turret belt refilled (+{added}).");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(ShotPatch)} finalizer: {ex}");
            }
        }
    }

    /// <summary>
    /// Weapon.OnShot lowers Repairable.Durability (and MaxDurability when overheated) once per shot. For weapons in the
    /// local player's inventory both values are put back afterwards.
    /// </summary>
    public class WeaponDurabilityPatch : ModulePatch
    {
        internal struct Durability
        {
            public float Current;
            public float Max;
            public bool Active;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Weapon), nameof(Weapon.OnShot));
        }

        [PatchPrefix]
        private static void Prefix(Weapon __instance, out Durability __state)
        {
            __state = default;
            try
            {
                if (Plugin.On(Plugin.InfiniteWeaponDurability) && __instance.Repairable != null && MainPlayer.Owns(__instance))
                {
                    __state = new Durability { Current = __instance.Repairable.Durability, Max = __instance.Repairable.MaxDurability, Active = true };
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(WeaponDurabilityPatch)} prefix: {ex}");
            }
        }

        [PatchFinalizer]
        private static void Finalizer(Weapon __instance, Durability __state)
        {
            if (!__state.Active)
            {
                return;
            }

            __instance.Repairable.MaxDurability = __state.Max;
            __instance.Repairable.Durability = __state.Current;
        }
    }
}
