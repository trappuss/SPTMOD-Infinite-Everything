using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.Communications;
using InfiniteEverything.Patches;
using SPT.Reflection.Patching;

namespace InfiniteEverything
{
    /// <summary>
    /// Infinite Everything for SPT 4.1.x (EFT 0.16.9.40743). Successor of InfiniteAmmo 1.0.0.
    /// Every option is in F12 (ConfigurationManager) and only affects the local player, never bots.
    /// One hotkey: master on/off, unbound by default. Turning an option (or the mod) off undoes it right away.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.SPT.core", "4.1.0")]
    [BepInIncompatibility("com.trappuss.infiniteammo")]
    [BepInProcess("EscapeFromTarkov.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.trappuss.infiniteeverything";
        public const string PluginName = "trappuss-InfiniteEverything"; // Forge rule: "Username-ModName"
        public const string PluginVersion = "2.5.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool> ShowToggleMessage;

        internal static ConfigEntry<bool> InfiniteAmmo;
        internal static ConfigEntry<bool> HoldReloadShowAll;
        internal static ConfigEntry<bool> InfiniteMagazine;
        internal static ConfigEntry<bool> InfiniteWeaponDurability;
        internal static ConfigEntry<bool> WeaponReliability;

        internal static ConfigEntry<bool> InfiniteGrenades;
        internal static ConfigEntry<bool> InfiniteItemUsage;
        internal static ConfigEntry<bool> InfiniteKeyUsage;
        internal static ConfigEntry<bool> InfiniteMoney;

        internal static ConfigEntry<bool> InfiniteHealth;
        internal static ConfigEntry<bool> InfiniteEnergyHydration;
        internal static ConfigEntry<bool> InfiniteStamina;
        internal static ConfigEntry<bool> InfiniteArmor;
        internal static ConfigEntry<bool> InfiniteCarryWeight;
        internal static ConfigEntry<bool> InfiniteRaidTime;

        internal static ConfigEntry<bool> InfiniteHideoutResources;

        internal static ConfigEntry<bool> TurretAmmo;
        internal static ConfigEntry<bool> TurretMagazine;

        internal static bool On(ConfigEntry<bool> option) => Enabled.Value && option.Value;

        private void Awake()
        {
            Log = Logger;
            BindConfig();

            var patches = new List<Func<ModulePatch>>
            {
                // weapons
                () => new ReloadKeepMagazinePatch(),
                () => new KeepRemovedMagazinePatch(),
                () => new ReloadFinishedRefillPatch(),
                () => new EmptyReloadPatch(),
                () => new LooseAmmoReloadPatch(),
                () => new AmmoPackCountPatch(),
                () => new AmmoPackPickPatch(),
                () => new NoAmmoInternalMagPatch(),
                () => new NoAmmoCylinderPatch(),
                () => new NoAmmoBarrelsPatch(),
                () => new ChamberLoadPatch(),
                () => new AmmoSelectorListPatch(),
                () => new VirtualExaminedPatch(),
                () => new SwitchMagazinePatch(),
                () => new RaidEndPatch(),
                () => new RocketFirePatch(),
                () => new RocketFireEndPatch(),
                () => new ShotPatch(),
                () => new WeaponDurabilityPatch(),
                () => new NoMalfunctionPatch(),
                () => new NoOverheatPatch(),
                () => new OneOffLauncherPatch(),
                // grenades and items
                () => new GrenadeThrowPatch(),
                () => new MedEffectAddedPatch(),
                () => new MedEffectResiduePatch(),
                () => new MedEffectForceRemovePatch(),
                () => new MedRemoveItemPatch(),
                () => new KeyUnlockPatch(),
                () => new KeycardDoorUnlockPatch(),
                () => new LabsKeycardPatch(),
                // player
                () => new KillPatch(),
                () => new StaminaProcessPatch(),
                () => new StaminaConsumePatch(),
                () => new ArmorDurabilityPatch(),
                () => new CarryWeightPatch(),
                () => new RaidTimerPatch(),
                // money
                () => new MoneySumsPatch(),
                () => new HealingKeepMoneyPatch(),
                () => new TraderMoneyRequisitePatch(),
                () => new FleaBuyMoneyPatch(),
                () => new NoAutoExchangePatch(),
                () => new HideoutMoneyRequirementPatch(),
                () => new HideoutMoneyReferencesPatch(),
                () => new PaidExfilPatch(),
                () => new TraderServicePatch(),
                // hideout
                () => new HideoutResourcePatch()
            };

            int ok = 0, failed = 0;
            foreach (Func<ModulePatch> create in patches)
            {
                Enable(create, ref ok, ref failed);
            }

            InflateMoneyPatch[] inflate = null;
            HideoutSpendPatch[] hideoutSpend = null;
            try
            {
                inflate = InflateMoneyPatch.All();
                hideoutSpend = HideoutSpendPatch.All();
            }
            catch (Exception ex)
            {
                failed++;
                Log.LogError($"Money screen patches could not be created: {ex}");
            }

            foreach (ModulePatch patch in (IEnumerable<ModulePatch>)inflate ?? Array.Empty<ModulePatch>())
            {
                Enable(() => patch, ref ok, ref failed);
            }

            foreach (ModulePatch patch in (IEnumerable<ModulePatch>)hideoutSpend ?? Array.Empty<ModulePatch>())
            {
                Enable(() => patch, ref ok, ref failed);
            }

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded: {ok} patches enabled, {failed} failed (mod {(Enabled.Value ? "ON" : "OFF")}).");
        }

        private static void Enable(Func<ModulePatch> create, ref int ok, ref int failed)
        {
            string name = "?";
            try
            {
                ModulePatch patch = create();
                name = patch.GetType().Name;
                patch.Enable();
                ok++;
            }
            catch (Exception ex)
            {
                failed++;
                Log.LogError($"Patch {name} could not be enabled, that option will not work: {ex}");
            }
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                Enabled.Value = !Enabled.Value; // saved to the .cfg by BepInEx
                string text = $"Infinite Everything {(Enabled.Value ? "ON" : "OFF")}";
                Log.LogInfo(text);
                if (ShowToggleMessage.Value)
                {
                    Notify(text);
                }
            }

            PlayerTicker.Tick();
            StateSync.Tick();
            MoneyRefunds.Tick();
            ChamberRefunds.Tick();
            TempRounds.Tick(); // after ChamberRefunds: temporary rounds wait for a pending chamber refund
            Borrowed.Tick();
            HoldReload.Tick();
            ItemModels.Tick();
        }

        internal static void Notify(string message)
        {
            try
            {
                NotificationManager.DisplayMessageNotification(message);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Notification failed (UI not ready?): {ex.Message}");
            }
        }

        private ConfigEntry<bool> Option(string section, string key, bool value, int order, string description)
        {
            return Config.Bind(section, key, value,
                new ConfigDescription(description, null, new ConfigurationManagerAttributes { Order = order }));
        }

        private void BindConfig()
        {
            const string general = "1. General";
            Enabled = Option(general, "Enabled", true, 100, "Master switch for every option below. The toggle hotkey flips this.");
            ToggleKey = Config.Bind(general, "Toggle hotkey", KeyboardShortcut.Empty,
                new ConfigDescription("Turns the whole mod on/off in game. Not bound by default.", null,
                    new ConfigurationManagerAttributes { Order = 90 }));
            ShowToggleMessage = Option(general, "Show on/off message", true, 80, "Shows a notification when the hotkey turns the mod on or off.");

            const string weapons = "2. Weapons";
            InfiniteAmmo = Option(weapons, "Infinite ammo (reloads never use anything up)", true, 70,
                "You still reload, but it never costs you anything. Magazines: the one taken out goes back into your rig, pockets " +
                "or backpack (also on a double-tap quick reload) and is refilled to full; with only empty magazines left, one is " +
                "filled for the reload; with no spare at all, the gun's own magazine is refilled and reloaded (full animation, " +
                "nothing added to or removed from your inventory). Loose rounds (shotgun tubes, " +
                "internal magazines, revolvers, break-action barrels, single rounds into the chamber) go into the gun without " +
                "leaving your inventory and always load to full; with no matching rounds on you, temporary ones are used for the " +
                "reload and removed right after (needs a free rig/pocket spot; tubes/internal magazines are filled directly without one). " +
                "Grenade launchers (underbarrel and standalone) and flare guns reload the same way. The RShG-2 has no reload in the " +
                "game: after a shot, R puts a new rocket in the tube.");
            HoldReloadShowAll = Option(weapons, "Infinite Magazine Options (Hold-R Scroll Menu)", true, 65,
                "With Infinite ammo on, in a raid, the hold-R reload menu also lists every magazine that fits the gun and every " +
                "round type it takes, even ones you do not carry (with mods' items too). A magazine you do not own is borrowed: " +
                "the one in the gun is kept aside (no free space needed), and the borrowed one is returned as soon as it leaves the gun (a reload, " +
                "unloading it) or at raid end (your own magazine is put back in the gun), so your inventory ends as it started. " +
                "Whatever you pick is kept: R then reloads that magazine / round type again instead of switching back, also after " +
                "holstering or switching guns.");
            InfiniteMagazine = Option(weapons, "Infinite magazine (rounds are never used up)", false, 60,
                "Firing never takes a round out of the magazine, so you never need to reload. A gun that is already empty is " +
                "refilled and a round chambered as soon as it is idle. Magazine- and tube-fed weapons, and the RShG-2 (a new rocket " +
                "after every shot); not revolver cylinders, grenade launchers, flare guns or break-action barrels. Turrets have " +
                "their own option.");
            InfiniteWeaponDurability = Option(weapons, "Infinite weapon durability", false, 50,
                "Firing never lowers your weapon's durability or maximum durability.");
            WeaponReliability = Option(weapons, "Weapon reliability (no malfunctions, no overheating)", false, 45,
                "No jams, misfires, feed or slide malfunctions, and the barrel never heats up (no fire-rate change, slide lock or auto-fire from heat).");

            const string items = "3. Grenades, items and money";
            InfiniteGrenades = Option(items, "Infinite grenades", false, 70,
                "A thrown grenade is replaced by a new one of the same type in the same spot (and the same quick slot).");
            InfiniteItemUsage = Option(items, "Infinite item usage (meds, food, drinks, stims)", false, 60,
                "Medical items, food, drinks, stims and injectors are never used up (resources come back after each use, " +
                "single-use items stay). Keys, grenades and ammo have their own options.");
            InfiniteKeyUsage = Option(items, "Infinite key usage", false, 55,
                "Keys and keycards never lose a use (including the Labs access keycard at raid start).");
            InfiniteMoney = Option(items, "Infinite money (incl. GP coins)", false, 50,
                "Buy without paying, in any currency incl. GP coins: traders, flea market purchases, flea listing/renew fees, " +
                "repairs, insurance, healing and hideout upgrades / scav case. In raid, paid exfils and BTR/trader services give " +
                "your money back (you still need it on you). Your money is never touched, so turning this off leaves exactly what " +
                "you had. Out of raid it needs the Infinite Everything server part (SPT_Runtime/user/mods/InfiniteEverything). " +
                "Not: clothing, hideout crafts and improvements.");

            const string player = "4. Player";
            InfiniteHealth = Option(player, "Infinite health (god mode)", false, 70,
                "No damage, bleeding, fractures or death. Turning it on also heals you fully and removes negative effects.");
            InfiniteEnergyHydration = Option(player, "Infinite energy and hydration", false, 60,
                "Energy and hydration stay full.");
            InfiniteStamina = Option(player, "Infinite stamina", false, 50,
                "Body stamina, arm stamina and breath (holding breath while aiming) stay full.");
            InfiniteArmor = Option(player, "Infinite armor durability", false, 40,
                "Hits never lower the durability of the armor, plates and helmets you wear. Armor still protects normally.");
            InfiniteCarryWeight = Option(player, "Infinite carry weight", false, 30,
                "Never overweight: no walk/sprint limits, extra stamina drain or inertia from what you carry.");
            InfiniteRaidTime = Option(player, "Infinite raid time", false, 20,
                "The raid timer never ends the raid. Turning this off after the time is up ends the raid right away.");

            const string turrets = "5. Turrets (stationary weapons)";
            TurretAmmo = Option(turrets, "Turret ammo (belt refills when empty)", false, 70,
                "When a mounted turret's belt runs out it is refilled right away (EFT has no turret reload), " +
                "so it keeps firing; the round counter still goes down.");
            TurretMagazine = Option(turrets, "Turret magazine (rounds are never used up)", false, 60,
                "Firing a mounted turret never takes a round out of its belt; an empty belt is refilled as soon as you mount it.");

            const string hideout = "6. Hideout";
            InfiniteHideoutResources = Option(hideout, "Infinite fuel and filters", false, 70,
                "Generator fuel, water filters and air filters never run down (one still has to be installed). " +
                "Needs the Infinite Everything server part. No Hideout Management XP from using them up while it is on.");
        }
    }
}
