using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace InfiniteEverything.Patches
{
    /// <summary>
    /// Infinite health backstop. SetDamageCoeff(0) (PlayerTicker) stops all damage, but ActiveHealthController.Kill
    /// itself is not gated by DamageCoeff, so it is blocked here for the local player while god mode is on.
    /// </summary>
    public class KillPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill));
        }

        [PatchPrefix]
        private static bool Prefix(ActiveHealthController __instance, EDamageType damageType)
        {
            if (Plugin.On(Plugin.InfiniteHealth) && __instance.Player != null && __instance.Player.IsYourPlayer)
            {
                Plugin.Log.LogInfo($"Infinite health: blocked death ({damageType}).");
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Infinite stamina. Stamina.Process (every frame, from Physical.Update) and Stamina.Consume (jumps, vaults,
    /// throws, hits...) are the only places Current goes down. After either, the local player's body stamina, arm
    /// stamina and breath (PlayerTicker publishes those three objects) are set back to full.
    /// </summary>
    public class StaminaProcessPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Stamina), nameof(Stamina.Process));
        }

        [PatchPostfix]
        private static void Postfix(Stamina __instance)
        {
            if (PlayerTicker.IsTracked(__instance))
            {
                PlayerTicker.Fill(__instance);
            }
        }
    }

    public class StaminaConsumePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Stamina), nameof(Stamina.Consume));
        }

        [PatchPostfix]
        private static void Postfix(Stamina __instance)
        {
            if (PlayerTicker.IsTracked(__instance))
            {
                PlayerTicker.Fill(__instance);
            }
        }
    }

    /// <summary>
    /// Infinite armor durability. Every armor hit (bullets and explosions) ends in ArmorComponent.ApplyDurabilityDamage;
    /// for armor in the local player's inventory the damage is set to 0. Protection is untouched.
    /// </summary>
    public class ArmorDurabilityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ArmorComponent), nameof(ArmorComponent.ApplyDurabilityDamage));
        }

        [PatchPrefix]
        private static void Prefix(ArmorComponent __instance, ref float armorDamage)
        {
            try
            {
                if (Plugin.On(Plugin.InfiniteArmor) && MainPlayer.Owns(__instance.Item))
                {
                    armorDamage = 0f;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(ArmorDurabilityPatch)}: {ex}");
            }
        }
    }

    /// <summary>
    /// Infinite carry weight. PhysicalBase.UpdateWeightLimits sets the overweight thresholds from skills and health; for
    /// bots (and when EncumberDisabled) the game sets them to 9000-10000 kg. For the local player with the option on
    /// (PlayerTicker.NoWeightPhysical) the same values are used, and the inertia limits too, so OnWeightUpdated computes no
    /// overweight, no walk/sprint limit, no extra stamina drain and no inertia from carried weight. The real weight is
    /// still shown in the inventory.
    /// </summary>
    public class CarryWeightPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(PhysicalBase), nameof(PhysicalBase.UpdateWeightLimits));
        }

        [PatchPostfix]
        private static void Postfix(PhysicalBase __instance)
        {
            if (!ReferenceEquals(__instance, PlayerTicker.NoWeightPhysical) || !Plugin.On(Plugin.InfiniteCarryWeight))
            {
                return;
            }

            __instance.WalkOverweightLimits.Set(9000f, 10000f);
            __instance.BaseOverweightLimits.Set(9000f, 10000f);
            __instance.SprintOverweightLimits.Set(9000f, 10000f);
            __instance.WalkSpeedOverweightLimits.Set(9000f, 10000f);
            Vector3 inertia = __instance.BaseInertiaLimits;
            __instance.BaseInertiaLimits = new Vector3(9000f, 10000f, inertia.z);
        }
    }

    /// <summary>
    /// Infinite raid time. EndByTimerScenario.Update ends the raid (StopGame) once GameTimer.PastTime reaches SessionTime.
    /// With the option on that check is skipped; the clock keeps counting. Turning the option off after the time is up
    /// ends the raid on the next frame, as it would have.
    /// </summary>
    public class RaidTimerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EndByTimerScenario), nameof(EndByTimerScenario.Update));
        }

        [PatchPrefix]
        private static bool Prefix()
        {
            return !Plugin.On(Plugin.InfiniteRaidTime);
        }
    }
}
