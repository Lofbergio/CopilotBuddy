#nullable disable
using System.Collections.Generic;
using System.Linq;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Inventory
{
    /// <summary>
    /// Helper class for finding consumable items (food and drink).
    /// </summary>
    public static class Consumable
    {
        /// <summary>
        /// Gets all food items in the player's bags.
        /// </summary>
        public static List<WoWItem> GetFood()
        {
            return ObjectManager.Me.BagItems
                .Where(IsFood)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Gets all drink items in the player's bags.
        /// </summary>
        public static List<WoWItem> GetDrinks()
        {
            return ObjectManager.Me.BagItems
                .Where(IsDrink)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Gets the best food item.
        /// </summary>
        /// <param name="includeSpecialtyItems">Include items with special effects.</param>
        public static WoWItem GetBestFood(bool includeSpecialtyItems)
        {
            var food = GetFood();
            if (food.Count == 0)
                return null;

            uint bestStack = 0;
            int bestLevel = -1;
            WoWItem bestItem = null;
            int playerLevel = StyxWoW.Me.Level;

            foreach (var item in food)
            {
                int level = item.ItemInfo.RequiredLevel;
                if (level > playerLevel)
                    continue;
                if (!includeSpecialtyItems && !item.ItemSpells.All(IsBasicFoodOrDrink))
                    continue;

                // Best = highest usable tier; tie-break by larger stack.
                if (level > bestLevel || (level == bestLevel && item.StackCount > bestStack))
                {
                    bestItem = item;
                    bestLevel = level;
                    bestStack = item.StackCount;
                }
            }

            return bestItem;
        }

        /// <summary>
        /// Gets the best drink item.
        /// </summary>
        /// <param name="includeSpecialtyItems">Include items with special effects.</param>
        public static WoWItem GetBestDrink(bool includeSpecialtyItems)
        {
            var drinks = GetDrinks();
            if (drinks.Count == 0)
                return null;

            uint bestStack = 0;
            int bestLevel = -1;
            WoWItem bestItem = null;
            int playerLevel = StyxWoW.Me.Level;

            foreach (var item in drinks)
            {
                int level = item.ItemInfo.RequiredLevel;
                if (level > playerLevel)
                    continue;
                if (!includeSpecialtyItems && !item.ItemSpells.All(IsBasicFoodOrDrink))
                    continue;

                // Best = highest usable tier; tie-break by larger stack.
                if (level > bestLevel || (level == bestLevel && item.StackCount > bestStack))
                {
                    bestItem = item;
                    bestLevel = level;
                    bestStack = item.StackCount;
                }
            }

            return bestItem;
        }

        /// <summary>
        /// Checks if an item is food.
        /// HB 4.3.4 Consumable.smethod_1: spell name "Food" OR "Refreshment".
        /// "Refreshment" covers WotLK Mage conjured food (Conjured Mana Strudel, etc.).
        /// </summary>
        private static bool IsFood(WoWItem item)
        {
            if ((int)item.ItemInfo.ItemClass != (int)WoWItemClass.Consumable)
                return false;

            return item.ItemSpells.Any(s =>
                s.ActualSpell != null &&
                (s.ActualSpell.Name == "Food" || s.ActualSpell.Name == "Refreshment"));
        }

        /// <summary>
        /// Checks if an item is a drink.
        /// HB 4.3.4 Consumable.smethod_3: spell name "Drink", "Starfire Espresso" or "Refreshment".
        /// "Refreshment" covers WotLK Mage conjured water; "Starfire Espresso" is a buff food.
        /// </summary>
        private static bool IsDrink(WoWItem item)
        {
            if ((int)item.ItemInfo.ItemClass != (int)WoWItemClass.Consumable)
                return false;

            return item.ItemSpells.Any(s =>
                s.ActualSpell != null &&
                (s.ActualSpell.Name == "Drink" ||
                 s.ActualSpell.Name == "Starfire Espresso" ||
                 s.ActualSpell.Name == "Refreshment"));
        }

        /// <summary>
        /// Checks if a spell is basic food or drink (no special effects).
        /// </summary>
        private static bool IsBasicFoodOrDrink(WoWItem.WoWItemSpell spell)
        {
            return spell.ActualSpell.Name == "Food" || spell.ActualSpell.Name == "Drink";
        }
    }
}
