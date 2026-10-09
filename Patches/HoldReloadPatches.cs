using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using FirearmController = EFT.Player.FirearmController;

namespace InfiniteEverything.Patches
{
    /// <summary>
    /// The hold-R ammo selector (AmmoSelector, opened by InventoryScreenQuickAccessPanel.TryShowAmmoSelector) lists what
    /// AmmoSelector.FindAmmoForWeapon returns: reachable loaded magazines, or loose rounds for internal-magazine, revolver,
    /// break-action and underbarrel reloads. The pick goes to GamePlayerOwner.SwitchMagazine.
    /// With Infinite ammo on, in a raid or the hideout, the list also gets one entry per compatible
    /// magazine / round type you do not carry ("virtual" items, never in the inventory while listed):
    ///  - a virtual magazine, when picked, is "borrowed" (see <see cref="Borrowed"/>): it is put in the rig/pockets
    ///    (backpack if they are full) just for the normal reload with its animation, and it never stays in the inventory;
    ///  - a virtual round type, when picked, is loaded from temporary rounds (see <see cref="TempRounds"/>).
    /// Whatever was picked (real or virtual) is remembered for that gun (<see cref="Prefs"/>), so a later R reloads the
    /// same magazine / round type instead of switching back.
    /// </summary>
    internal static class HoldReload
    {
        internal static bool InRaid
        {
            get
            {
#pragma warning disable CS0618 // InGameStatus.InRaid is marked obsolete but is still what the game itself checks
                return InGameStatus.InRaid;
#pragma warning restore CS0618
            }
        }

        /// <summary>
        /// Walking around the hideout with a weapon (the shooting range, or anywhere with Hideout Uncensored). The
        /// hideout player holds a copy of the gear that the game throws away on holster, so borrowed magazines and
        /// temporary rounds never reach the real inventory.
        /// </summary>
        internal static bool InHideout => MainPlayer.Get() is HideoutPlayer;

        /// <summary>The option that applies to <paramref name="weapon"/> is on, for the local player, in a raid or the hideout.</summary>
        internal static bool Active(InventoryController controller, Weapon weapon)
        {
            if (weapon == null || !(InRaid || InHideout) || !Plugin.Enabled.Value || !Plugin.HoldReloadShowAll.Value || MainPlayer.InventoryIfMine(controller) == null)
            {
                return false;
            }

            return Plugin.InfiniteAmmo.Value;
        }

        internal static bool InWeapon(Item item)
        {
            Item parent = item?.CurrentAddress?.Container?.ParentItem;
            for (int depth = 0; parent != null && depth < 32; depth++)
            {
                if (parent is Weapon)
                {
                    return true;
                }

                parent = parent.CurrentAddress?.Container?.ParentItem;
            }

            return false;
        }

        internal static bool HandsIdle(Player player)
        {
            return !(player.HandsController is FirearmController controller) || controller.CurrentOperation is FirearmController.Idling;
        }

        /// <summary>
        /// Choices made in the hideout are dropped when its world goes away: RaidEndPatch is on the raid's game class
        /// (HideoutGame is a BaseLocalGame of HideoutPlayerOwner), so nothing else clears them. Called every frame.
        /// </summary>
        internal static void Tick()
        {
            if (MainPlayer.Get() == null)
            {
                Prefs.Clear();
                VirtualItems.Clear();
            }
        }

        /// <summary>The virtual entries for the selector, after the real ones (same rules as FindAmmoForWeapon).</summary>
        internal static List<Item> BuildVirtual(InventoryController controller, Weapon weapon, List<Item> real)
        {
            var result = new List<Item>();
            Magazine current = weapon.GetCurrentMagazine();
            bool magazines = false;
            IContainer ammoContainer = null;

            if (weapon.IsUnderBarrelDeviceActive)
            {
                ammoContainer = weapon.GetUnderbarrelWeapon()?.Chamber;
            }
            else
            {
                switch (weapon.ReloadMode)
                {
                    case Weapon.EReloadMode.ExternalMagazine:
                        magazines = true;
                        break;
                    case Weapon.EReloadMode.OnlyBarrel:
                        ammoContainer = weapon.HasChambers ? weapon.Chambers[0] : null;
                        break;
                    case Weapon.EReloadMode.ExternalMagazineWithInternalReloadSupport:
                        magazines = true;
                        if (current != null && weapon.HasChambers)
                        {
                            ammoContainer = weapon.Chambers[0];
                        }

                        break;
                    case Weapon.EReloadMode.InternalMagazine:
                        if (current == null)
                        {
                            magazines = true;
                        }
                        else if (current.Count < current.MaxCount)
                        {
                            ammoContainer = weapon is Revolver ? (IContainer)current.Cartridges : (weapon.HasChambers ? weapon.Chambers[0] : null);
                        }

                        break;
                }
            }

            var realTemplates = new HashSet<string>(real.Where(x => x != null).Select(x => x.StringTemplateId));

            if (magazines && weapon.GetMagazineSlot() is Slot slot)
            {
                string ammoTpl = AmmoHelper.FallbackTemplate(weapon, current);
                foreach (string tpl in Catalog.MagazinesFor(slot))
                {
                    if (realTemplates.Contains(tpl))
                    {
                        continue;
                    }

                    Magazine magazine = CreateFullMagazine(controller, tpl, ammoTpl);
                    if (magazine != null && Fits(slot, magazine))
                    {
                        result.Add(magazine);
                    }
                }
            }

            if (ammoContainer != null)
            {
                foreach (string tpl in Catalog.AmmoFor(ammoContainer))
                {
                    if (realTemplates.Contains(tpl) || !(Singleton<ItemFactory>.Instance.CreateItem(controller.NextId, tpl, null) is Ammo ammo))
                    {
                        continue;
                    }

                    ammo.StackObjectsCount = Math.Max(1, ammo.Template.StackMaxSize);
                    result.Add(ammo);
                }
            }

            return result;
        }

        /// <summary>Weapon conflicts the magazine slot itself would refuse (filters, conflicting items, blocked slots).</summary>
        private static bool Fits(Slot slot, Magazine magazine)
        {
            if (!slot.CheckCompatibility(magazine) || slot.CheckConflictingItems(magazine).Failed)
            {
                return false;
            }

            foreach (Slot conflicting in slot.GetConflictingSlot(magazine))
            {
                if (conflicting?.ContainedItem != null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>A new magazine (not in any inventory) filled to capacity with the gun's round type, or the first one it takes.</summary>
        internal static Magazine CreateFullMagazine(ItemController owner, string magazineTpl, string preferredAmmoTpl)
        {
            ItemFactory factory = Singleton<ItemFactory>.Instance;
            if (!(factory.CreateItem(owner.NextId, magazineTpl, null) is Magazine magazine) || magazine is CylinderMagazine)
            {
                return null;
            }

            string ammoTpl = preferredAmmoTpl;
            if (ammoTpl == null || !(factory.CreateItem(owner.NextId, ammoTpl, null) is Ammo probe) || !magazine.CheckCompatibility(probe))
            {
                ammoTpl = Catalog.AmmoFor(magazine.Cartridges).FirstOrDefault();
            }

            if (ammoTpl == null)
            {
                return magazine; // shown empty; the reload fills it (EmptyReloadPatch / refill)
            }

            for (int guard = 0; guard < 64 && magazine.Count < magazine.MaxCount; guard++)
            {
                if (!(factory.CreateItem(owner.NextId, ammoTpl, null) is Ammo ammo))
                {
                    break;
                }

                ammo.StackObjectsCount = Math.Min(Math.Max(1, ammo.Template.StackMaxSize), magazine.MaxCount - magazine.Count);
                if (!magazine.CheckCompatibility(ammo) || magazine.Cartridges.Add(ammo, false).Failed)
                {
                    break;
                }
            }

            return magazine;
        }
    }

    /// <summary>Compatible magazine / round templates per container type, from the item database (mods included). Built once per container type.</summary>
    internal static class Catalog
    {
        private static readonly Dictionary<string, List<string>> Magazines = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, List<string>> Rounds = new Dictionary<string, List<string>>();

        internal static List<string> MagazinesFor(Slot slot)
        {
            string key = slot.ParentItem.StringTemplateId + "/" + slot.ID;
            if (Magazines.TryGetValue(key, out List<string> cached))
            {
                return cached;
            }

            ItemFactory factory = Singleton<ItemFactory>.Instance;
            var found = new List<KeyValuePair<int, string>>();
            foreach (KeyValuePair<MongoID, ItemTemplate> pair in factory.ItemTemplates)
            {
                ItemTemplate template = pair.Value;
                if (!(template is MagazineTemplate) || template is CylinderMagazineTemplate || template._type != NodeType.Item || template.QuestItem)
                {
                    continue;
                }

                try
                {
                    if (factory.CreateItem(MongoID.Generate(), pair.Key, null) is Magazine magazine && slot.CheckCompatibility(magazine))
                    {
                        found.Add(new KeyValuePair<int, string>(magazine.MaxCount, pair.Key));
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug($"Catalog: magazine {pair.Key} skipped ({ex.Message}).");
                }
            }

            var list = found.OrderBy(x => x.Key).Select(x => x.Value).ToList();
            Magazines[key] = list;
            Plugin.Log.LogInfo($"Hold-R menu: {list.Count} compatible magazine type(s) for {key}.");
            return list;
        }

        internal static List<string> AmmoFor(IContainer container)
        {
            string key = container.ParentItem.StringTemplateId + "/" + container.ID;
            if (Rounds.TryGetValue(key, out List<string> cached))
            {
                return cached;
            }

            ItemFactory factory = Singleton<ItemFactory>.Instance;
            var list = new List<string>();
            foreach (KeyValuePair<MongoID, ItemTemplate> pair in factory.ItemTemplates)
            {
                ItemTemplate template = pair.Value;
                if (!(template is AmmoTemplate) || template._type != NodeType.Item || template.QuestItem || template.StackMaxSize < 2)
                {
                    continue; // single-round stacks (rockets) cannot be lent without leaving a round behind
                }

                try
                {
                    if (factory.CreateItem(MongoID.Generate(), pair.Key, null) is Ammo ammo && container.CanAccept(ammo))
                    {
                        list.Add(pair.Key);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug($"Catalog: round {pair.Key} skipped ({ex.Message}).");
                }
            }

            Rounds[key] = list;
            Plugin.Log.LogInfo($"Hold-R menu: {list.Count} compatible round type(s) for {key}.");
            return list;
        }
    }

    /// <summary>The virtual entries of the selector that is open right now.</summary>
    internal static class VirtualItems
    {
        private static readonly Dictionary<string, Item> Items = new Dictionary<string, Item>();

        internal static void Set(IEnumerable<Item> items)
        {
            Items.Clear();
            foreach (Item item in items)
            {
                Items[item.Id] = item;
            }
        }

        internal static void Clear()
        {
            Items.Clear();
        }

        internal static bool Contains(Item item)
        {
            return item != null && Items.TryGetValue(item.Id, out Item stored) && ReferenceEquals(stored, item);
        }
    }

    /// <summary>
    /// What the player picked in the hold-R menu, per gun (by item id), so R keeps using it:
    /// a magazine (that same magazine is refilled and reloaded) or a loose round type (reloaded from your rounds of that
    /// type, or temporary ones). Cleared at raid end.
    /// </summary>
    internal static class Prefs
    {
        private static readonly Dictionary<string, string> Magazines = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> Rounds = new Dictionary<string, string>();

        internal static void SetMagazine(Weapon weapon, Magazine magazine)
        {
            Magazines[weapon.Id] = magazine.Id;
        }

        internal static void SetRound(Weapon weapon, string tpl)
        {
            Rounds[weapon.Id] = tpl;
        }

        /// <summary>True when <paramref name="current"/> is the magazine picked for this gun; a different magazine in the gun clears the choice.</summary>
        internal static bool IsPickedMagazine(Weapon weapon, Magazine current)
        {
            if (current == null || !Magazines.TryGetValue(weapon.Id, out string id))
            {
                return false;
            }

            if (id == current.Id)
            {
                return true;
            }

            Magazines.Remove(weapon.Id); // the gun holds something else now (swapped by hand): back to normal reloads
            return false;
        }

        internal static string PickedRound(Weapon weapon)
        {
            return Rounds.TryGetValue(weapon.Id, out string tpl) ? tpl : null;
        }

        internal static void Clear()
        {
            Magazines.Clear();
            Rounds.Clear();
        }
    }

    /// <summary>
    /// Borrowed magazines: virtual magazines picked in the hold-R menu, and the twin a reload makes when the gun only
    /// has a borrowed magazine or a double-tap needs a second one. A borrowed magazine only ever exists inside a gun,
    /// and borrowing needs no free inventory space:
    ///  - Before its reload it waits in a free rig/pocket/backpack spot, or with none free, out of sight (see Park).
    ///    The reload moves it into the gun in the same call.
    ///  - The gun's own magazine is not moved to the rig. The reload takes it out of the inventory (the game's own
    ///    "no place for the old magazine" path: ReloadExternalMagResult.Run without an address) and it is kept here
    ///    (Held) instead of being thrown on the ground (KeepRemovedMagazinePatch). It is put back when the borrowed
    ///    magazine leaves the gun, and at raid end.
    ///  - A borrowed magazine swapped out by a reload is removed the same way and simply ends.
    ///  - Out of its gun for any other reason (the unload key, dragged out by hand): removed as soon as the hands
    ///    are idle, and the kept magazine comes back.
    ///  - Picked but never loaded (the reload could not start): removed after a few seconds.
    ///  - Holstering or switching guns leaves it in its gun.
    ///  - Raid end (LocalGame.Stop, before the profile is sent): every borrowed magazine is removed and every kept
    ///    magazine goes back into its gun, so the inventory ends exactly as it started.
    /// </summary>
    internal static class Borrowed
    {
        private sealed class Entry
        {
            public Magazine Magazine;
            public Magazine Original;
            public InventoryController Inventory;
            public float Created;
            public float NotBefore;
            public bool Loaded;
        }

        /// <summary>One of the player's own magazines, kept out of the inventory while a borrowed one is in its gun.</summary>
        private sealed class Kept
        {
            public Magazine Magazine;
            public Weapon Gun;
            public InventoryController Inventory;
            public float NotBefore;
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        private static readonly Dictionary<string, Kept> Held = new Dictionary<string, Kept>();

        internal static bool Contains(Item item)
        {
            return item != null && Entries.ContainsKey(item.Id);
        }

        /// <summary>One of the player's own magazines that is kept here, out of the inventory.</summary>
        internal static bool IsHeld(Item item)
        {
            return item != null && Held.ContainsKey(item.Id);
        }

        internal static void Register(InventoryController inventory, Magazine magazine, Weapon weapon)
        {
            Magazine original = weapon.GetCurrentMagazine();
            if (original != null && Entries.TryGetValue(original.Id, out Entry previous))
            {
                original = previous.Original; // borrowed in place of a borrowed one: keep the real original
            }

            Entries[magazine.Id] = new Entry { Magazine = magazine, Original = original, Inventory = inventory, Created = Time.unscaledTime };
        }

        /// <summary>
        /// Where a borrowed magazine waits for its reload: a free rig/pocket spot, then the backpack. With no free spot
        /// at all it is parked out of sight in the inventory's quest-item container: same owner, so the game's reload
        /// can take it from there. That container only takes quest items, hence AddWithoutRestrictions. It never stays:
        /// the reload moves it into the gun in the same call, and Tick removes anything left behind.
        /// </summary>
        internal static bool Park(InventoryController inventory, Magazine magazine)
        {
            ItemAddress spot = (ItemAddress)ReloadKeepMagazinePatch.FindSpot(inventory, magazine, backpack: false)
                               ?? ReloadKeepMagazinePatch.FindSpot(inventory, magazine, backpack: true);
            if (spot != null)
            {
                inventory.AddAndRaiseEvents(magazine, spot);
                return magazine.CurrentAddress != null;
            }

            Stash hidden = inventory.Inventory.QuestRaidItems;
            if (hidden?.Grids == null)
            {
                return false;
            }

            foreach (var grid in hidden.Grids)
            {
                ItemAddress address = grid.FindLocationForItem(magazine);
                if (address == null)
                {
                    continue;
                }

                OperationResult<AddResult> result = ItemManipulator.AddWithoutRestrictions(magazine, address, inventory);
                if (result.Failed)
                {
                    Plugin.Log.LogWarning($"Could not park the borrowed magazine out of sight ({result.Error}).");
                    return false;
                }

                result.Value.RaiseEvents(inventory, CommandStatus.Begin);
                result.Value.RaiseEvents(inventory, CommandStatus.Succeed);
                Plugin.Log.LogInfo($"Hold-R: no free spot, {magazine.StringTemplateId} waits out of sight for its reload.");
                return magazine.CurrentAddress != null;
            }

            return false;
        }

        /// <summary>A pick called off before its reload: the magazine leaves the inventory again (Tick retries if it cannot).</summary>
        internal static void Cancel(Magazine magazine)
        {
            if (Entries.TryGetValue(magazine.Id, out Entry entry) && Remove(entry, force: true))
            {
                Entries.Remove(magazine.Id);
            }
        }

        /// <summary>
        /// From ReloadKeepMagazinePatch, before ReloadExternalMagResult.Run takes <paramref name="current"/> out of the
        /// gun for <paramref name="next"/>. True when a borrowed magazine is involved; the address is settled here:
        ///  - own magazine out, borrowed in: no address, so Run removes it; <paramref name="keep"/> tells the postfix
        ///    (AfterReload) to hold it;
        ///  - borrowed out, borrowed in: no address, Run removes the old one and it ends;
        ///  - borrowed out, own magazine in: the magazine kept for this gun is put in the gun first (in place of the
        ///    borrowed one) and the reload moves it to a free spot; without a free spot it stays kept until there is one.
        /// </summary>
        internal static bool BeforeReload(InventoryController inventory, Weapon weapon, Magazine current, Magazine next, ref ItemAddress vestTargetAddress, out Magazine keep)
        {
            keep = null;
            bool leaving = Contains(current);
            bool coming = Contains(next);
            if (!leaving && !coming)
            {
                return false;
            }

            if (!leaving)
            {
                vestTargetAddress = null;
                keep = current;
                return true;
            }

            if (!coming)
            {
                Kept kept = HeldFor(weapon);
                if (kept != null)
                {
                    ItemAddress spot = (ItemAddress)ReloadKeepMagazinePatch.FindSpot(inventory, kept.Magazine, backpack: false)
                                       ?? ReloadKeepMagazinePatch.FindSpot(inventory, kept.Magazine, backpack: true);
                    if (spot != null && SwapBack(weapon, current, kept))
                    {
                        vestTargetAddress = spot;
                        return true;
                    }
                }
            }

            vestTargetAddress = null;
            return true;
        }

        /// <summary>From ReloadKeepMagazinePatch, after Run: the player's own magazine it took out of the inventory is kept.</summary>
        internal static void AfterReload(InventoryController inventory, Weapon weapon, Magazine keep)
        {
            if (keep == null || inventory == null || keep.CurrentAddress != null)
            {
                return; // the reload did not happen (rolled back): it is still in the gun
            }

            Held[keep.Id] = new Kept { Magazine = keep, Gun = weapon, Inventory = inventory };
            Plugin.Log.LogInfo($"Hold-R: your {keep} is kept aside while a borrowed magazine is in the gun.");
        }

        private static Kept HeldFor(Weapon weapon)
        {
            foreach (Kept kept in Held.Values)
            {
                if (ReferenceEquals(kept.Gun, weapon))
                {
                    return kept;
                }
            }

            return null;
        }

        /// <summary>The borrowed magazine in the gun ends and the kept one takes its place. False: the gun's slot is left as it is now.</summary>
        private static bool SwapBack(Weapon weapon, Magazine borrowed, Kept kept)
        {
            Slot slot = weapon.GetMagazineSlot();
            if (slot == null || !Entries.TryGetValue(borrowed.Id, out Entry entry) || !Remove(entry, force: true))
            {
                return false;
            }

            Entries.Remove(borrowed.Id);
            if (!PutBack(kept, slot.CreateItemAddress(), force: true))
            {
                return false; // the slot is empty now; the kept magazine comes back through Tick
            }

            Held.Remove(kept.Magazine.Id);
            return true;
        }

        /// <summary>Puts a kept magazine back in the inventory at <paramref name="address"/>. <paramref name="force"/>: a gun slot that refuses it (a malfunction) is overridden.</summary>
        private static bool PutBack(Kept kept, ItemAddress address, bool force)
        {
            OperationResult<AddResult> result = ItemManipulator.Add(kept.Magazine, address, kept.Inventory);
            if (result.Failed && force)
            {
                Plugin.Log.LogWarning($"Putting {kept.Magazine} back was refused ({result.Error}); adding it without restrictions.");
                result = ItemManipulator.AddWithoutRestrictions(kept.Magazine, address, kept.Inventory);
            }

            if (result.Failed)
            {
                Plugin.Log.LogWarning($"Could not put {kept.Magazine} back ({result.Error}).");
                return false;
            }

            result.Value.RaiseEvents(kept.Inventory, CommandStatus.Begin);
            result.Value.RaiseEvents(kept.Inventory, CommandStatus.Succeed);
            if (Plugin.On(Plugin.InfiniteAmmo))
            {
                MagazineMemory.Refill(kept.Inventory, kept.Magazine, null);
            }

            Plugin.Log.LogInfo($"Hold-R: your {kept.Magazine} is back ({(address is SlotItemAddress ? "in the gun" : "in the inventory")}).");
            return true;
        }

        /// <summary>A free place for a kept magazine: rig and pockets, the backpack, then any other container that is worn (the secure container).</summary>
        private static ItemAddress FreeSpot(InventoryController inventory, Magazine magazine, bool anywhere)
        {
            ItemAddress spot = (ItemAddress)ReloadKeepMagazinePatch.FindSpot(inventory, magazine, backpack: false)
                               ?? ReloadKeepMagazinePatch.FindSpot(inventory, magazine, backpack: true);
            if (spot != null || !anywhere)
            {
                return spot;
            }

            foreach (Slot slot in inventory.Inventory.Equipment.GetAllSlots())
            {
                if (!(slot.ContainedItem is CompoundItem box) || box is Weapon || box.Grids == null)
                {
                    continue;
                }

                foreach (var grid in box.Grids)
                {
                    ItemAddress address = grid.FindLocationForItem(magazine);
                    if (address != null)
                    {
                        return address;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Called every frame. Returns every borrowed magazine that is out of a gun once the hands are idle (the unload
        /// key, dragged out by hand, or picked and never loaded: after 5 seconds), forgets the ones a reload ended, and
        /// puts kept magazines back when their gun no longer holds a borrowed one.
        /// </summary>
        internal static void Tick()
        {
            if (Entries.Count == 0 && Held.Count == 0)
            {
                return;
            }

            Player player = MainPlayer.Get();
            if (player == null)
            {
                Entries.Clear();
                Held.Clear(); // the world is gone, and the profile was saved with them back in place (RaidEnd)
                return;
            }

            bool idle = HoldReload.HandsIdle(player);
            foreach (Entry entry in Entries.Values.ToList())
            {
                try
                {
                    Magazine magazine = entry.Magazine;
                    if (magazine.CurrentAddress == null)
                    {
                        if (idle)
                        {
                            Entries.Remove(magazine.Id); // a reload took it out (BeforeReload): it has ended
                        }

                        continue;
                    }

                    bool mine = ReferenceEquals(magazine.Owner, entry.Inventory);
                    if (mine && HoldReload.InWeapon(magazine))
                    {
                        entry.Loaded = true;
                        continue;
                    }

                    if (!mine || !idle || Time.unscaledTime < entry.NotBefore)
                    {
                        continue; // dropped in the world with its gun, or a reload is still running
                    }

                    if (entry.Loaded || Time.unscaledTime > entry.Created + 5f)
                    {
                        if (Remove(entry))
                        {
                            Entries.Remove(magazine.Id);
                        }
                        else
                        {
                            entry.NotBefore = Time.unscaledTime + 5f; // it stays tracked; raid end forces the removal
                        }
                    }
                }
                catch (Exception ex)
                {
                    Entries.Remove(entry.Magazine.Id);
                    Plugin.Log.LogError($"Borrowed magazine cleanup failed: {ex}");
                }
            }

            if (!idle)
            {
                return;
            }

            foreach (Kept kept in Held.Values.ToList())
            {
                try
                {
                    if (Time.unscaledTime < kept.NotBefore)
                    {
                        continue;
                    }

                    bool gunMine = ReferenceEquals(kept.Gun.Owner, kept.Inventory);
                    Slot slot = kept.Gun.GetMagazineSlot();
                    if (gunMine && Contains(slot?.ContainedItem))
                    {
                        continue; // its stand-in is still in the gun
                    }

                    if (!gunMine && HoldReload.InHideout)
                    {
                        Held.Remove(kept.Magazine.Id); // the hideout threw that copy of the gear away
                        continue;
                    }

                    // The borrowed magazine is out of the gun (unloaded), or the gun left the inventory: back to a free
                    // spot, else into the gun's empty slot, else wait for room.
                    ItemAddress spot = FreeSpot(kept.Inventory, kept.Magazine, anywhere: false);
                    if (spot == null && gunMine && slot != null && slot.ContainedItem == null)
                    {
                        spot = slot.CreateItemAddress();
                    }

                    if (spot != null && PutBack(kept, spot, force: false))
                    {
                        Held.Remove(kept.Magazine.Id);
                    }
                    else
                    {
                        kept.NotBefore = Time.unscaledTime + 2f;
                    }
                }
                catch (Exception ex)
                {
                    kept.NotBefore = Time.unscaledTime + 5f;
                    Plugin.Log.LogError($"Putting a kept magazine back failed: {ex}");
                }
            }
        }

        /// <summary>Raid end: put the inventory back as it was.</summary>
        internal static void RaidEnd()
        {
            foreach (Entry entry in Entries.Values.ToList())
            {
                try
                {
                    Magazine magazine = entry.Magazine;
                    if (!ReferenceEquals(magazine.Owner, entry.Inventory) || magazine.CurrentAddress == null)
                    {
                        continue; // not in the inventory (dropped in the world): not part of the profile
                    }

                    ItemAddress place = magazine.CurrentAddress;
                    bool inGunSlot = place is SlotItemAddress && place.Container?.ParentItem != null && HoldReload.InWeapon(magazine);
                    if (!Remove(entry, force: true))
                    {
                        continue;
                    }

                    // The magazine it replaced, when that went to the rig instead of being kept (put in by hand).
                    Magazine original = entry.Original;
                    if (!inGunSlot || original == null || !ReferenceEquals(original.Owner, entry.Inventory) || original.CurrentAddress == null || HoldReload.InWeapon(original))
                    {
                        continue;
                    }

                    OperationResult<MoveResult> move = ItemManipulator.Move(original, place, entry.Inventory);
                    if (move.Failed)
                    {
                        Plugin.Log.LogWarning($"Raid end: could not put {original} back in the gun ({move.Error}); it stays where it is.");
                        continue;
                    }

                    move.Value.RaiseEvents(entry.Inventory, CommandStatus.Begin);
                    move.Value.RaiseEvents(entry.Inventory, CommandStatus.Succeed);
                    Plugin.Log.LogInfo($"Raid end: borrowed magazine removed, {original} put back in the gun.");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Raid end: borrowed magazine cleanup failed: {ex}");
                }
            }

            Entries.Clear();

            foreach (Kept kept in Held.Values.ToList())
            {
                try
                {
                    bool back = false;
                    Slot slot = kept.Gun.GetMagazineSlot();
                    if (ReferenceEquals(kept.Gun.Owner, kept.Inventory) && slot != null && slot.ContainedItem == null)
                    {
                        back = PutBack(kept, slot.CreateItemAddress(), force: true);
                    }

                    if (!back)
                    {
                        ItemAddress spot = FreeSpot(kept.Inventory, kept.Magazine, anywhere: true);
                        back = spot != null && PutBack(kept, spot, force: false);
                    }

                    if (back)
                    {
                        Plugin.Log.LogInfo($"Raid end: your {kept.Magazine} is back in the inventory.");
                    }
                    else
                    {
                        Plugin.Log.LogError($"Raid end: no place for your {kept.Magazine}: its gun is gone or holds another magazine, and the inventory is full.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Raid end: putting {kept.Magazine} back failed: {ex}");
                }
            }

            Held.Clear();
        }

        /// <summary>
        /// <paramref name="force"/> (raid end, or a pick called off): a refusal is overridden. Slot.RemoveItemInternal
        /// refuses to take a magazine out of a gun with a feed malfunction (IncompatibleByMalfunction), and a borrowed
        /// magazine left in the gun at raid end would be saved with the profile.
        /// </summary>
        private static bool Remove(Entry entry, bool force = false)
        {
            OperationResult<RemoveResult> result = ItemManipulator.Remove(entry.Magazine, entry.Inventory);
            if (result.Failed && force)
            {
                Plugin.Log.LogWarning($"Borrowed magazine {entry.Magazine} refused removal ({result.Error}); removing it without restrictions.");
                result = ItemManipulator.RemoveWithoutRestrictions(entry.Magazine, entry.Inventory);
            }

            if (result.Failed)
            {
                Plugin.Log.LogWarning($"Could not remove borrowed magazine {entry.Magazine} ({result.Error}).");
                return false;
            }

            result.Value.RaiseEvents(entry.Inventory, CommandStatus.Begin);
            result.Value.RaiseEvents(entry.Inventory, CommandStatus.Succeed);
            Plugin.Log.LogInfo($"Borrowed magazine {entry.Magazine.StringTemplateId} returned (removed).");
            return true;
        }
    }
    /// <summary>Adds the virtual entries to the hold-R selector list.</summary>
    public class AmmoSelectorListPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AmmoSelector), nameof(AmmoSelector.FindAmmoForWeapon));
        }

        [PatchPostfix]
        private static void Postfix(InventoryController controller, Weapon weapon, ref IEnumerable<Item> __result)
        {
            try
            {
                VirtualItems.Clear();
                if (!HoldReload.Active(controller, weapon))
                {
                    return;
                }

                List<Item> list = __result?.ToList() ?? new List<Item>();
                List<Item> extra = HoldReload.BuildVirtual(controller, weapon, list);
                if (extra.Count == 0)
                {
                    __result = list;
                    return;
                }

                VirtualItems.Set(extra);
                list.AddRange(extra);
                __result = list;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(AmmoSelectorListPatch)}: {ex}");
            }
        }
    }

    /// <summary>
    /// Virtual and borrowed items count as examined: the menu shows their names, and the magazine slot accepts a borrowed
    /// magazine (Slot.CheckConditions refuses unexamined items). Nothing is written to the profile's encyclopedia.
    /// </summary>
    public class VirtualExaminedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.Examined), new[] { typeof(Item) });
        }

        [PatchPostfix]
        private static void Postfix(Item item, ref bool __result)
        {
            if (!__result && item != null && (VirtualItems.Contains(item) || Borrowed.Contains(item) || TempRounds.Contains(item)))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// The pick: a virtual magazine is placed in the inventory and registered as borrowed; a virtual round type is
    /// replaced by temporary rounds of that type. The game's SwitchMagazine then runs its normal reload. Afterwards the
    /// pick is remembered for the gun (Prefs).
    /// </summary>
    public class SwitchMagazinePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GamePlayerOwner), nameof(GamePlayerOwner.SwitchMagazine));
        }

        [PatchPrefix]
        private static bool Prefix(GamePlayerOwner __instance, Weapon weapon, ref Item item)
        {
            try
            {
                if (weapon is EFT.InventoryLogic.RocketLauncher && RocketRearm.Wanted)
                {
                    return false; // the game's barrel reload never ends on it; R re-arms it (see NoAmmoBarrelsPatch)
                }

                if (item == null || !VirtualItems.Contains(item))
                {
                    return true;
                }

                InventoryController inventory = __instance.Player?.InventoryController;
                if (inventory == null || weapon == null)
                {
                    VirtualItems.Clear();
                    return false;
                }

                if (!ItemModels.Ready(item))
                {
                    // Its model (and that of the rounds in a magazine) is not loaded yet: the same pick is made again
                    // when it is. The entry stays in VirtualItems until then.
                    Item picked = item;
                    GamePlayerOwner owner = __instance;
                    ItemModels.Load(picked, () =>
                    {
                        if (owner != null && VirtualItems.Contains(picked) && ReferenceEquals(owner.Player?.HandsController?.Item, weapon))
                        {
                            owner.SwitchMagazine(weapon, picked);
                        }
                    });
                    Plugin.Log.LogInfo($"Hold-R: loading the model of {picked.StringTemplateId}; the reload starts when it is ready.");
                    return false;
                }

                VirtualItems.Clear();

                if (item is Magazine magazine)
                {
                    if (!Borrowed.Park(inventory, magazine))
                    {
                        Plugin.Notify("Infinite ammo: that magazine could not be taken");
                        return false;
                    }

                    // No room is needed for the magazine in the gun: the reload takes it out and Borrowed keeps it.
                    Borrowed.Register(inventory, magazine, weapon);
                    Plugin.Log.LogInfo($"Hold-R: borrowed {magazine.StringTemplateId} ({magazine.Count}/{magazine.MaxCount}).");
                    return true;
                }

                if (item is Ammo ammo)
                {
                    Ammo rounds = TempRounds.ProvideTemplate(inventory, ammo.StringTemplateId, "hold-R pick");
                    if (rounds == null)
                    {
                        Plugin.Notify("Infinite ammo: no free space for those rounds");
                        return false;
                    }

                    // A single-barrel gun puts its loaded round back in the rig or pockets (GamePlayerOwner.SwitchMagazine
                    // looks nowhere else); without a spot there ReloadSingleBarrelResult.Run throws it on the ground.
                    // The temporary rounds are removed again by TempRounds.Tick.
                    if (!weapon.IsUnderBarrelDeviceActive && weapon.ReloadMode == Weapon.EReloadMode.OnlyBarrel && !weapon.IsMultiBarrel
                        && weapon.HasChambers && weapon.Chambers[0].ContainedItem is Ammo loaded && !loaded.IsUsed
                        && ReloadKeepMagazinePatch.FindSpot(inventory, loaded, backpack: false) == null)
                    {
                        Plugin.Notify("Infinite ammo: no free rig/pocket space to keep the loaded round");
                        return false;
                    }

                    item = rounds;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(SwitchMagazinePatch)} prefix: {ex}");
                return false;
            }
        }

        [PatchPostfix]
        private static void Postfix(GamePlayerOwner __instance, Weapon weapon, Item item)
        {
            try
            {
                if (weapon == null || item == null || !ReferenceEquals(item.Owner, __instance.Player?.InventoryController))
                {
                    return;
                }

                if (!Plugin.On(Plugin.InfiniteAmmo) || weapon is EFT.InventoryLogic.RocketLauncher)
                {
                    return;
                }

                if (item is Magazine magazine)
                {
                    Prefs.SetMagazine(weapon, magazine);
                }
                else if (item is Ammo && !weapon.IsUnderBarrelDeviceActive)
                {
                    Prefs.SetRound(weapon, item.StringTemplateId);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(SwitchMagazinePatch)} postfix: {ex}");
            }
        }
    }

    /// <summary>Raid end (LocalGame.Stop calls this before the profile is sent): borrowed magazines go back, choices are forgotten.</summary>
    public class RaidEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), nameof(BaseLocalGame<EftGamePlayerOwner>.Stop));
        }

        [PatchPrefix]
        private static void Prefix(string profileId)
        {
            try
            {
                Player player = MainPlayer.Get();
                if (player == null || player.ProfileId != profileId)
                {
                    return;
                }

                Borrowed.RaidEnd();
                Prefs.Clear();
                VirtualItems.Clear();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(RaidEndPatch)}: {ex}");
            }
        }
    }

    /// <summary>
    /// The RShG-2 (and any RocketLauncher-class weapon) is not a "one-off" weapon in EFT: RocketLauncherFire.OnFireEvent
    /// removes the rocket from the chamber, and the game has no working reload for it (its barrel reload never ends on
    /// this launcher: no animation events).
    ///  - Infinite magazine: a new rocket of the same type goes into the chamber when the shot ends
    ///    (RocketLauncherFire.OnFireEndEvent), so it fires again without a reload.
    ///  - Infinite ammo: the tube stays empty after the shot and R puts a new rocket in (NoAmmoBarrelsPatch), without
    ///    an animation because the launcher has none.
    /// </summary>
    internal static class RocketRearm
    {
        /// <summary>The rocket type last fired from each launcher (by item id).</summary>
        internal static readonly Dictionary<string, string> LastFired = new Dictionary<string, string>();

        internal static bool Wanted => Plugin.On(Plugin.InfiniteAmmo) || Plugin.On(Plugin.InfiniteMagazine);

        /// <summary>The rocket type to load: the one last fired, else the launcher's default ammo.</summary>
        internal static string Template(Weapon weapon)
        {
            if (LastFired.TryGetValue(weapon.Id, out string tpl))
            {
                return tpl;
            }

            string defAmmo = weapon.Template.defAmmo;
            return string.IsNullOrEmpty(defAmmo) ? null : defAmmo;
        }

        /// <summary>Puts a new rocket in the empty chamber. False when it could not.</summary>
        internal static bool Arm(InventoryController inventory, Weapon weapon, FirearmsAnimator animator, string tpl)
        {
            if (tpl == null || inventory == null || !weapon.HasChambers || weapon.Chambers[0].ContainedItem != null)
            {
                return false;
            }

            Item rocket = Singleton<ItemFactory>.Instance.CreateItem(inventory.NextId, tpl, null);
            if (rocket == null)
            {
                return false;
            }

            inventory.AddAndRaiseEvents(rocket, weapon.Chambers[0].CreateItemAddress());
            if (rocket.CurrentAddress == null)
            {
                return false;
            }

            animator?.SetAmmoInChamber(weapon.ChamberAmmoCount);
            Plugin.Log.LogInfo($"Infinite ammo: {weapon.StringTemplateId} re-armed ({tpl}).");
            return true;
        }

        /// <summary>R on a rocket launcher: an empty tube gets a new rocket once the hands are idle, a loaded one nothing.</summary>
        internal static void Reload(FirearmHandsInputTranslator translator, Weapon weapon)
        {
            if (!weapon.HasChambers || weapon.Chambers[0].ContainedItem != null)
            {
                return;
            }

            string tpl = Template(weapon);
            if (tpl == null)
            {
                Plugin.Log.LogWarning($"Infinite ammo: no rocket type known for {weapon}.");
                return;
            }

            if (!ItemModels.Ready(tpl))
            {
                TempRounds.ReloadWhenLoaded(null, tpl, translator, weapon, "rocket");
                return;
            }

            Player player = translator._player;
            if (player.HandsController is FirearmController controller && HoldReload.HandsIdle(player))
            {
                Arm(translator._inventoryController, weapon, controller.FirearmsAnimator, tpl);
            }
        }
    }

    public class RocketFirePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FirearmController.RocketLauncherFire), nameof(FirearmController.RocketLauncherFire.OnFireEvent));
        }

        [PatchPrefix]
        private static void Prefix(FirearmController.RocketLauncherFire __instance)
        {
            try
            {
                if (RocketRearm.Wanted && __instance.Player != null && __instance.Player.IsYourPlayer && __instance.Weapon?.FirstLoadedChamberSlot?.ContainedItem is Ammo rocket)
                {
                    RocketRearm.LastFired[__instance.Weapon.Id] = rocket.StringTemplateId;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(RocketFirePatch)}: {ex}");
            }
        }
    }

    public class RocketFireEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FirearmController.RocketLauncherFire), nameof(FirearmController.RocketLauncherFire.OnFireEndEvent));
        }

        [PatchPostfix]
        private static void Postfix(FirearmController.RocketLauncherFire __instance)
        {
            try
            {
                Weapon weapon = __instance.Weapon;
                if (weapon == null || !Plugin.On(Plugin.InfiniteMagazine) || __instance.Player == null || !__instance.Player.IsYourPlayer)
                {
                    return; // Infinite ammo alone: the tube stays empty until R (RocketRearm.Reload)
                }

                RocketRearm.Arm(__instance.Player.InventoryController, weapon, __instance.FirearmsAnimator, RocketRearm.Template(weapon));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{nameof(RocketFireEndPatch)}: {ex}");
            }
        }
    }
}
