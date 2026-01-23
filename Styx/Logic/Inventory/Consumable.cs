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

            foreach (var item in food)
            {
                if (item.StackCount > bestStack && item.ItemInfo.RequiredLevel > bestLevel)
                {
                    if (!includeSpecialtyItems && !item.ItemSpells.All(IsBasicFoodOrDrink))
                        continue;

                    bestItem = item;
                    bestStack = item.StackCount;
                    bestLevel = item.ItemInfo.RequiredLevel;
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

            foreach (var item in drinks)
            {
                if (item.StackCount > bestStack && item.ItemInfo.RequiredLevel > bestLevel)
                {
                    if (!includeSpecialtyItems && !item.ItemSpells.All(IsBasicFoodOrDrink))
                        continue;

                    bestItem = item;
                    bestStack = item.StackCount;
                    bestLevel = item.ItemInfo.RequiredLevel;
                }
            }

            return bestItem;
        }

        /// <summary>
        /// Checks if an item is food.
        /// </summary>
        private static bool IsFood(WoWItem item)
        {
            if ((int)item.ItemInfo.ItemClass != (int)WoWItemClass.Consumable)
                return false;

            return item.ItemSpells.Any(s => s.ActualSpell.Name == "Food");
        }

        /// <summary>
        /// Checks if an item is a drink.
        /// </summary>
        private static bool IsDrink(WoWItem item)
        {
            if ((int)item.ItemInfo.ItemClass != (int)WoWItemClass.Consumable)
                return false;

            return item.ItemSpells.Any(s => s.ActualSpell.Name == "Drink");
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
