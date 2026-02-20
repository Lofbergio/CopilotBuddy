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
            var slots = new List<InventorySlot>();

            switch (type)
            {
                case InventoryType.Head:
                    slots.Add(InventorySlot.HeadSlot);
                    break;
                case InventoryType.Neck:
                    slots.Add(InventorySlot.NeckSlot);
                    break;
                case InventoryType.Shoulder:
                    slots.Add(InventorySlot.ShoulderSlot);
                    break;
                case InventoryType.Body:
                    slots.Add(InventorySlot.ShirtSlot);
                    break;
                case InventoryType.Chest:
                case InventoryType.Robe:
                    slots.Add(InventorySlot.ChestSlot);
                    break;
                case InventoryType.Waist:
                    slots.Add(InventorySlot.WaistSlot);
                    break;
                case InventoryType.Legs:
                    slots.Add(InventorySlot.LegsSlot);
                    break;
                case InventoryType.Feet:
                    slots.Add(InventorySlot.FeetSlot);
                    break;
                case InventoryType.Wrist:
                    slots.Add(InventorySlot.WristSlot);
                    break;
                case InventoryType.Hand:
                    slots.Add(InventorySlot.HandsSlot);
                    break;
                case InventoryType.Finger:
                    slots.Add(InventorySlot.Finger0Slot);
                    slots.Add(InventorySlot.Finger1Slot);
                    break;
                case InventoryType.Trinket:
                    slots.Add(InventorySlot.Trinket0Slot);
                    slots.Add(InventorySlot.Trinket1Slot);
                    break;
                case InventoryType.Weapon:
                    slots.Add(InventorySlot.MainHandSlot);
                    slots.Add(InventorySlot.SecondaryHandSlot);
                    break;
                case InventoryType.Shield:
                case InventoryType.WeaponOffHand:
                case InventoryType.Holdable:
                    slots.Add(InventorySlot.SecondaryHandSlot);
                    break;
                case InventoryType.Ranged:
                case InventoryType.Thrown:
                case InventoryType.RangedRight:
                case InventoryType.Relic:
                    slots.Add(InventorySlot.RangedSlot);
                    break;
                case InventoryType.Cloak:
                    slots.Add(InventorySlot.BackSlot);
                    break;
                case InventoryType.TwoHandWeapon:
                {
                    slots.Add(InventorySlot.MainHandSlot);
                    bool flag = StyxWoW.Me.Class == WoWClass.Warrior && Lua.GetReturnVal<int>("return GetTalentInfo(2,20)", 4U) > 0;
                    WoWItem mainHand = StyxWoW.Me.Inventory.Equipped.MainHand;
                    if (flag && mainHand != null && mainHand.ItemInfo.InventoryType == InventoryType.TwoHandWeapon)
                    {
                        slots.Add(InventorySlot.SecondaryHandSlot);
                    }
                    break;
                }
                case InventoryType.WeaponMainHand:
                    slots.Add(InventorySlot.MainHandSlot);
                    break;
                case InventoryType.Bag:
                case InventoryType.Quiver:
                    slots.Add(InventorySlot.Bag0Slot);
                    slots.Add(InventorySlot.Bag1Slot);
                    slots.Add(InventorySlot.Bag2Slot);
                    slots.Add(InventorySlot.Bag3Slot);
                    break;
                case InventoryType.Tabard:
                    slots.Add(InventorySlot.TabardSlot);
                    break;
                case InventoryType.Ammo:
                    slots.Add(InventorySlot.AmmoSlot);
                    break;
            }

            return slots;
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
            var allItems = ObjectManager.GetObjectsOfType<WoWItem>().ToArray();

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
            if (item.IsSoulbound || item.IsConjured)
                return false;

            // Check protected items
            if (ProtectedItemsManager.Contains(item.Entry))
                return false;
            if (ProtectedItemsManager.Contains(item.Name.ToLower()))
                return false;

            // Check item quality
            if (!currentProfile.MailQualities.Contains(item.Quality))
                return false;

            if (item.ItemInfo.Bond == WoWItemBondType.Quest)
                return false;

            // Check if item is in bag with correct stack count
            string luaCheck = string.Format(
                "for bag=0,4 do if GetBagName(bag) then for slot=1,GetContainerNumSlots(bag) do " +
                "if GetContainerItemID(bag, slot) == {0} then local _, count = GetContainerItemInfo(bag, slot) " +
                "if count == {1} then return \"true\" end end end end end return \"false\"",
                item.Entry, item.StackCount);

            var result = Lua.GetReturnValues(luaCheck, "hax.lua");
            if (result[0] != "true")
                return false;

            // Don't mail food or drink
            if (itemName == foodName || itemName == drinkName ||
                entryStr == foodName || entryStr == drinkName)
                return false;

            return true;
        }
    }
}
