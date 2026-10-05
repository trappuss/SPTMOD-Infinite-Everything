using System.Collections.Generic;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using InfiniteEverything.Patches;
using UnityEngine;
using FirearmController = EFT.Player.FirearmController;

namespace InfiniteEverything
{
    /// <summary>
    /// Runs from Plugin.Update. Applies the "state" options to the local player:
    ///  - Infinite health: ActiveHealthController.SetDamageCoeff(0) (the game's own switch: ApplyDamage, bleeding,
    ///    fractures, pain, intoxication and radiation all check DamageCoeff > 0, and it sets every body part's
    ///    HealthValue.DownMult to 0 so no health can go down). The old value is put back when the option goes off.
    ///  - Infinite energy/hydration: tops both up.
    ///  - Infinite stamina: publishes the player's three Stamina objects for StaminaProcessPatch/StaminaConsumePatch.
    ///  - Infinite carry weight: publishes the player's PhysicalBase for CarryWeightPatch and recalculates the weight
    ///    penalties when the option changes.
    ///  - Infinite magazine / turret magazine: while the gun is idle, an empty magazine is filled and an empty chamber is
    ///    loaded, so a gun that was already dry fires again right away.
    /// </summary>
    internal static class PlayerTicker
    {
        internal static Stamina BodyStamina;
        internal static Stamina ArmStamina;
        internal static Stamina Breath;

        private static Player _player;
        private static bool _godApplied;
        private static float _savedDamageCoeff = 1f;
        private static float _nextHealthTick;
        private static float _nextFeedTick;

        internal static PhysicalBase NoWeightPhysical;
        private static PhysicalBase _weightPhysical;
        private static bool _weightApplied;

        internal static bool IsTracked(Stamina stamina)
        {
            return stamina != null && (ReferenceEquals(stamina, BodyStamina) || ReferenceEquals(stamina, ArmStamina) || ReferenceEquals(stamina, Breath));
        }

        internal static void Fill(Stamina stamina)
        {
            float capacity = stamina.TotalCapacity;
            if (stamina.Current < capacity)
            {
                stamina.Current = capacity;
            }

            stamina.Overuse = 0f;
        }

        internal static void Tick()
        {
            Player player = MainPlayer.Get();
            if (!ReferenceEquals(player, _player))
            {
                _player = player;
                _godApplied = false; // a new raid: a new health controller
            }

            if (player == null)
            {
                BodyStamina = ArmStamina = Breath = null;
                NoWeightPhysical = null;
                _weightPhysical = null;
                _weightApplied = false;
                return;
            }

            PhysicalBase physical = player.Physical;
            if (physical != null && Plugin.On(Plugin.InfiniteStamina))
            {
                BodyStamina = physical.Stamina;
                ArmStamina = physical.HandsStamina;
                Breath = physical.Oxygen;
            }
            else
            {
                BodyStamina = ArmStamina = Breath = null;
            }

            UpdateCarryWeight(physical);

            if (Time.unscaledTime >= _nextFeedTick)
            {
                _nextFeedTick = Time.unscaledTime + 0.1f;
                KeepGunFed(player);
            }

            if (Time.unscaledTime < _nextHealthTick)
            {
                return;
            }

            _nextHealthTick = Time.unscaledTime + 0.25f;

            try
            {
                ActiveHealthController health = player.ActiveHealthController;
                if (health == null || !health.IsAlive)
                {
                    return;
                }

                bool god = Plugin.On(Plugin.InfiniteHealth);
                if (god && !_godApplied)
                {
                    _savedDamageCoeff = health.DamageCoeff > 0f ? health.DamageCoeff : 1f;
                    HealEverything(health); // first: FullRestoreBodyPart makes a new HealthValue (DownMult = 1)
                    health.SetDamageCoeff(0f);
                    _godApplied = true;
                    Plugin.Log.LogInfo($"Infinite health on (damage coefficient was {_savedDamageCoeff}).");
                }
                else if (god)
                {
                    // Keep it applied: something may reset DamageCoeff, and surgery (RestoreBodyPart) or a full
                    // restore creates a new HealthValue whose DownMult is back to 1.
                    bool reset = health.DamageCoeff != 0f;
                    foreach (var state in health.BodyState.Values)
                    {
                        if (state.Health.DownMult != 0f)
                        {
                            reset = true;
                            break;
                        }
                    }

                    if (reset)
                    {
                        health.SetDamageCoeff(0f);
                    }
                }
                else if (!god && _godApplied)
                {
                    health.SetDamageCoeff(_savedDamageCoeff);
                    _godApplied = false;
                    Plugin.Log.LogInfo($"Infinite health off (damage coefficient back to {_savedDamageCoeff}).");
                }

                if (Plugin.On(Plugin.InfiniteEnergyHydration))
                {
                    ValueStruct energy = health.Energy;
                    if (energy.Current < energy.Maximum - 0.01f)
                    {
                        health.ChangeEnergy(energy.Maximum - energy.Current);
                    }

                    ValueStruct hydration = health.Hydration;
                    if (hydration.Current < hydration.Maximum - 0.01f)
                    {
                        health.ChangeHydration(hydration.Maximum - hydration.Current);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"{nameof(PlayerTicker)}: {ex}");
                _nextHealthTick = Time.unscaledTime + 5f; // don't spam
            }
        }

        /// <summary>
        /// PhysicalBase.UpdateWeightLimits sets the weight thresholds; OnWeightUpdated turns the carried weight into the
        /// overweight, walk/sprint limits, inertia and the encumbered events. With the option on, CarryWeightPatch raises
        /// the thresholds to 9000-10000 kg (the values the game itself uses for bots), and both are re-run on every change.
        /// </summary>
        private static void UpdateCarryWeight(PhysicalBase physical)
        {
            bool want = physical != null && Plugin.On(Plugin.InfiniteCarryWeight);
            if (physical == null || (want == _weightApplied && ReferenceEquals(physical, _weightPhysical)))
            {
                return;
            }

            try
            {
                NoWeightPhysical = want ? physical : null;
                physical.UpdateWeightLimits();
                physical.OnWeightUpdated();
                _weightPhysical = physical;
                _weightApplied = want;
                Plugin.Log.LogInfo($"Infinite carry weight {(want ? "on" : "off")}.");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Carry weight update failed: {ex}");
                _weightPhysical = physical;
                _weightApplied = want;
            }
        }

        private static void KeepGunFed(Player player)
        {
            try
            {
                if (!(player.HandsController is FirearmController controller))
                {
                    return;
                }

                bool turret = controller.IsStationaryWeapon;
                if (!(turret ? Plugin.On(Plugin.TurretMagazine) : Plugin.On(Plugin.InfiniteMagazine)))
                {
                    return;
                }

                if (!(controller.CurrentOperation is FirearmController.Idling))
                {
                    return; // never interfere with a reload, shot or other action in progress
                }

                Weapon weapon = controller.Item;
                if (weapon == null || weapon.MalfState.State != Weapon.EMalfunctionState.None)
                {
                    return;
                }

                Magazine magazine = weapon.GetCurrentMagazine();
                if (magazine == null || magazine is CylinderMagazine || !(magazine.Owner is ItemController owner))
                {
                    return;
                }

                if (magazine.Count == 0)
                {
                    int added = MagazineMemory.Refill(owner, magazine, null, AmmoHelper.FallbackTemplate(weapon, magazine));
                    if (added > 0)
                    {
                        controller.FirearmsAnimator?.SetAmmoOnMag(magazine.Count);
                        Plugin.Log.LogInfo($"Infinite magazine: refilled empty {magazine} (+{added}).");
                    }
                }

                if (weapon.HasChambers && weapon.Chambers[0].ContainedItem == null && magazine.Count > 0)
                {
                    if (AmmoHelper.TryChamber(controller, owner, weapon, magazine))
                    {
                        Plugin.Log.LogInfo("Infinite magazine: chambered a round.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Infinite magazine feed failed: {ex}");
                _nextFeedTick = Time.unscaledTime + 5f;
            }
        }

        private static void HealEverything(ActiveHealthController health)
        {
            var parts = new List<EBodyPart>(health.BodyState.Keys);
            foreach (EBodyPart part in parts)
            {
                if (part == EBodyPart.Common)
                {
                    continue;
                }

                var state = health.BodyState[part];
                if (state.IsDestroyed || state.Health.Current < state.Health.Maximum)
                {
                    health.FullRestoreBodyPart(part);
                }
            }

            foreach (EBodyPart part in parts)
            {
                health.RemoveNegativeEffects(part);
            }
        }
    }
}
