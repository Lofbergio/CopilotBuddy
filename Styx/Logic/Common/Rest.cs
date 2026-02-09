using System.Linq;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.Inventory;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Common;

/// <summary>
/// Handles resting (eating/drinking) functionality.
/// </summary>
public static class Rest
{
    /// <summary>
    /// Gets or sets the health percentage threshold for resting.
    /// </summary>
    public static double RestPercentageHealth { get; set; }

    /// <summary>
    /// Gets or sets the mana percentage threshold for resting.
    /// </summary>
    public static double RestPercentageMana { get; set; }

    /// <summary>
    /// Gets whether the player has no food available.
    /// </summary>
    public static bool NoFood { get; private set; }

    /// <summary>
    /// Gets whether the player has no drink available.
    /// </summary>
    public static bool NoDrink { get; private set; }

    /// <summary>
    /// Makes the player eat and drink to restore health and mana.
    /// </summary>
    public static void Feed()
    {
        var me = ObjectManager.Me;
        if (me == null)
            return;

        if (me.CurrentTarget != null)
        {
            Logging.Write("Resting.");
            ObjectManager.Me.ClearTarget();
        }

        if (me.IsMoving)
        {
            WoWMovement.MoveStop();
        }

        // Eat food
        if (!string.IsNullOrEmpty(LevelbotSettings.Instance.FoodName) &&
            me.HealthPercent <= 55.0 &&
            !me.Buffs.ContainsKey("Food"))
        {
            var foodCount = Lua.GetReturnVal<int>($"return GetItemCount(\"{LevelbotSettings.Instance.FoodName}\")", 0);
            if (foodCount > 0)
            {
                Logging.Write("Eating {0}", LevelbotSettings.Instance.FoodName);
                Lua.DoString($"UseItemByName(\"{LevelbotSettings.Instance.FoodName}\")");
                NoFood = false;
            }
            else
            {
                NoFood = true;
                Logging.Write("No {0} in bags.", LevelbotSettings.Instance.FoodName);
            }
        }

        // Drink
        if (!string.IsNullOrEmpty(LevelbotSettings.Instance.DrinkName) &&
            me.ManaPercent <= 55.0 &&
            !me.Auras.ContainsKey("Drink"))
        {
            var drinkCount = Lua.GetReturnVal<int>($"return GetItemCount(\"{LevelbotSettings.Instance.DrinkName}\")", 0);
            if (drinkCount > 0)
            {
                NoDrink = false;
                Logging.Write("Drinking {0}", LevelbotSettings.Instance.DrinkName);
                Lua.DoString($"UseItemByName(\"{LevelbotSettings.Instance.DrinkName}\")");
            }
            else
            {
                NoDrink = true;
                Logging.Write("No {0} in bags.", LevelbotSettings.Instance.DrinkName);
            }
        }

        // Wait while eating/drinking
        if (!string.IsNullOrEmpty(LevelbotSettings.Instance.DrinkName) ||
            !string.IsNullOrEmpty(LevelbotSettings.Instance.FoodName))
        {
            Thread.Sleep(1000);
            while (me.Auras.ContainsKey("Food") || me.Auras.ContainsKey("Drink"))
            {
                Thread.Sleep(100);

                if (me.HealthPercent == 100.0 && me.ManaPercent == 100.0)
                    break;

                if (me.Combat)
                    break;

                if (me.HealthPercent == 100.0 && !me.Auras.ContainsKey("Drink"))
                    break;

                if (me.ManaPercent == 100.0 && !me.Auras.ContainsKey("Food"))
                    break;
            }
        }
    }

    /// <summary>
    /// Immediately uses food to restore health without waiting.
    /// Used by Singular for quick eating.
    /// </summary>
    public static void FeedImmediate()
    {
        var me = ObjectManager.Me;
        if (me == null)
            return;

        // Auto-detect best food
        WoWItem food = Consumable.GetBestFood(false);
        if (food != null)
        {
            Logging.Write("Eating {0}", food.Name);
            food.UseContainerItem();
        }
        else
        {
            NoFood = true;
            Logging.Write("Could not find any food to eat.");
        }
    }

    /// <summary>
    /// Immediately uses drink to restore mana without waiting.
    /// Used by Singular for quick drinking.
    /// </summary>
    public static void DrinkImmediate()
    {
        var me = ObjectManager.Me;
        if (me == null)
            return;

        // Auto-detect best drink
        WoWItem drink = Consumable.GetBestDrink(false);
        if (drink != null)
        {
            Logging.Write("Drinking {0}", drink.Name);
            drink.UseContainerItem();
        }
        else
        {
            NoDrink = true;
            Logging.Write("Could not find any water to drink.");
        }
    }
}
