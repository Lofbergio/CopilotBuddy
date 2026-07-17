#nullable disable
using System.Collections.Generic;
using System.Linq;
using Styx.Helpers;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.Combat.CombatRoutine;

namespace Styx.Logic.Inventory
{
    /// <summary>
    /// Manages inventory operations and item categorization.
    /// </summary>
    public static class InventoryManager
    {
        /// <summary>
        /// Gets the inventory slots for a given equipment type.
        /// </summary>
        public static List<InventorySlot> GetInventorySlotsByEquipSlot(InventoryType type)
        {
            var list = new List<InventorySlot>();

            switch (type)
            {
                case InventoryType.Head:
                    list.Add(InventorySlot.HeadSlot);
                    break;
                case InventoryType.Neck:
                    list.Add(InventorySlot.NeckSlot);
                    break;
                case InventoryType.Shoulder:
                    list.Add(InventorySlot.ShoulderSlot);
                    break;
                case InventoryType.Body:
                    list.Add(InventorySlot.ShirtSlot);
                    break;
                case InventoryType.Chest:
                case InventoryType.Robe:
                    list.Add(InventorySlot.ChestSlot);
                    break;
                case InventoryType.Waist:
                    list.Add(InventorySlot.WaistSlot);
                    break;
                case InventoryType.Legs:
                    list.Add(InventorySlot.LegsSlot);
                    break;
                case InventoryType.Feet:
                    list.Add(InventorySlot.FeetSlot);
                    break;
                case InventoryType.Wrist:
                    list.Add(InventorySlot.WristSlot);
                    break;
                case InventoryType.Hand:
                    list.Add(InventorySlot.HandsSlot);
                    break;
                case InventoryType.Finger:
                    list.Add(InventorySlot.Finger0Slot);
                    list.Add(InventorySlot.Finger1Slot);
                    break;
                case InventoryType.Trinket:
                    list.Add(InventorySlot.Trinket0Slot);
                    list.Add(InventorySlot.Trinket1Slot);
                    break;
                case InventoryType.Weapon:
                    list.Add(InventorySlot.MainHandSlot);
                    list.Add(InventorySlot.SecondaryHandSlot);
                    break;
                case InventoryType.Shield:
                case InventoryType.WeaponOffHand:
                case InventoryType.Holdable:
                    list.Add(InventorySlot.SecondaryHandSlot);
                    break;
                case InventoryType.Ranged:
                case InventoryType.Thrown:
                case InventoryType.RangedRight:
                case InventoryType.Relic:
                    list.Add(InventorySlot.RangedSlot);
                    break;
                case InventoryType.Cloak:
                    list.Add(InventorySlot.BackSlot);
                    break;
                case InventoryType.TwoHandWeapon:
                {
                    list.Add(InventorySlot.MainHandSlot);
                    bool flag = StyxWoW.Me.HasTitansGrip;
                    WoWItem mainHand = StyxWoW.Me.Inventory.Equipped.MainHand;
                    if (flag && mainHand != null && mainHand.ItemInfo.InventoryType == InventoryType.TwoHandWeapon)
                    {
                        list.Add(InventorySlot.SecondaryHandSlot);
                    }
                    break;
                }
                case InventoryType.WeaponMainHand:
                    list.Add(InventorySlot.MainHandSlot);
                    break;
                case InventoryType.Bag:
                case InventoryType.Quiver:
                    list.Add(InventorySlot.Bag0Slot);
                    list.Add(InventorySlot.Bag1Slot);
                    list.Add(InventorySlot.Bag2Slot);
                    list.Add(InventorySlot.Bag3Slot);
                    break;
                case InventoryType.Tabard:
                    list.Add(InventorySlot.TabardSlot);
                    break;
                case InventoryType.Ammo:
                    // WotLK: Ammo slot exists (physical slot 0) but AutoEquip2 (Cata plugin)
                    // never adds AmmoSlot=0 to its EquippedItems dictionary → KeyNotFoundException.
                    // Return empty list so AutoEquip2 skips ammo items gracefully.
                    // Ammo is auto-equipped by AutoEquipper.CheckAndEquipAmmo() instead.
                    break;
            }

            return list;
        }

        /// <summary>
        /// Gets whether there are items to mail.
        /// </summary>
        public static bool HaveItemsToMail => GetItemsToMail().Length != 0;

        /// <summary>
        /// Gets items that should be mailed.
        /// </summary>
        public static WoWItem[] GetItemsToMail()
        {
            var items = new List<WoWItem>();
            var allItems = StyxWoW.Me?.CarriedItems;
            if (allItems == null) return items.ToArray();

            foreach (var item in allItems)
            {
                if (ShouldMailItem(item))
                {
                    Logging.Write($"Mailing {item.Name}");
                    items.Add(item);
                }
            }

            return items.ToArray();
        }

        /// <summary>
        /// Determines if an item should be mailed.
        /// </summary>
        private static bool ShouldMailItem(WoWItem item)
        {
            Profile currentProfile = ProfileManager.CurrentProfile;
            string itemName = item.Name.ToLower();
            uint entry = item.Entry;
            string entryStr = entry.ToString();

            string foodName = LevelbotSettings.Instance.FoodName.ToLower();
            string drinkName = LevelbotSettings.Instance.DrinkName.ToLower();

            // Force mail check - item in ForceMail list should always be mailed
            if (!item.IsSoulbound && currentProfile.ForceMail.Contains(item.Entry))
                return true;

            // Standard mail check
            if (item.IsSoulbound)
            {
                Logging.WriteDebug("Can't mail item:{0}. Item is soulbound", item.Name);
                return false;
            }
            if (item.IsConjured)
            {
                Logging.WriteDebug("Can't mail item:{0}. Item is conjured", item.Name);
                return false;
            }

            // Check protected items
            if (ProtectedItemsManager.Contains(item.Entry))
            {
                Logging.WriteDebug("Can't mail item:{0}. Item is a protected item", item.Name);
                return false;
            }
            if (ProtectedItemsManager.Contains(item.Name.ToLower()))
            {
                Logging.WriteDebug("Can't mail item:{0}. Item is a protected item", item.Name);
                return false;
            }

            // Check item quality
            if (!currentProfile.MailQualities.Contains(item.Quality))
            {
                Logging.WriteDebug("Can't mail item:{0}. Item doesn't meet the itemqualitys specified in the profile", item.Name);
                return false;
            }

            if (item.ItemInfo.Bond == WoWItemBondType.Quest)
            {
                Logging.WriteDebug("Can't mail item:{0}. Item is a questitem", item.Name);
                return false;
            }

            // Check if item is in bag with correct stack count
            string luaCheck = string.Format(
                "for bag=0,4 do if GetBagName(bag) then for slot=1,GetContainerNumSlots(bag) do " +
                "if GetContainerItemID(bag, slot) == {0} then local _, count = GetContainerItemInfo(bag, slot) " +
                "if count == {1} then return \"true\" end end end end end return \"false\"",
                item.Entry, item.StackCount);

            var result = Lua.GetReturnValues(luaCheck, "hax.lua");
            if (result[0] != "true")
            {
                Logging.WriteDebug("Can't mail item:{0}. Item is equipped", item.Name);
                return false;
            }

            // Don't mail food or drink
            if (itemName == foodName || itemName == drinkName ||
                entryStr == foodName || entryStr == drinkName)
                return false;

            return true;
        }
    }
}
