#nullable disable
using System;
using System.Collections.Generic;
using Styx.Helpers;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Combat
{
    /// <summary>
    /// Helper class for healing and potion usage.
    /// WoW 3.3.5a build 12340.
    /// </summary>
    public static class Healing
    {
        // Health potion item IDs (sorted by level/quality)
        private static readonly HashSet<uint> HealthPotionIds = new HashSet<uint>
        {
            118,    // Minor Healing Potion
            858,    // Lesser Healing Potion
            929,    // Healing Potion
            1710,   // Greater Healing Potion
            3928,   // Superior Healing Potion
            13446,  // Major Healing Potion
            22829,  // Super Healing Potion
            33447,  // Runic Healing Potion
        };

        // Mana potion item IDs (sorted by level/quality)
        private static readonly HashSet<uint> ManaPotionIds = new HashSet<uint>
        {
            2455,   // Minor Mana Potion
            3385,   // Lesser Mana Potion
            3827,   // Mana Potion
            6149,   // Greater Mana Potion
            13443,  // Superior Mana Potion
            13444,  // Major Mana Potion
            22832,  // Super Mana Potion
            33448,  // Runic Mana Potion
        };

        /// <summary>
        /// Uses the best available health potion.
        /// </summary>
        public static void UseHealthPotion()
        {
            uint bestEntry = 0;
            string bestName = string.Empty;

            foreach (WoWItem item in ObjectManager.GetObjectsOfType<WoWItem>(false))
            {
                if (HealthPotionIds.Contains(item.Entry) && item.Entry > bestEntry)
                {
                    bestEntry = item.Entry;
                    bestName = item.Name;
                }

                if (StyxWoW.Me.Dead)
                    break;
            }

            if (bestEntry > 0)
            {
                Logging.Write("Using health potion: {0}", bestName);
                Lua.DoString(string.Format("RunMacroText('/use {0}')", bestName));
            }
            else if (SpellManager.KnownSpells.ContainsKey("Gift of the Naaru") && 
                     SpellManager.CanCastSpell("Gift of the Naaru"))
            {
                SpellManager.CastSpell("Gift of the Naaru");
            }
            else if (SpellManager.KnownSpells.ContainsKey("Lifeblood") && 
                     SpellManager.CanCastSpell("Lifeblood"))
            {
                SpellManager.CastSpell("Lifeblood");
            }
        }

        /// <summary>
        /// Uses the best available mana potion.
        /// </summary>
        public static void UseManaPotion()
        {
            uint bestEntry = 0;
            string bestName = string.Empty;

            foreach (WoWItem item in ObjectManager.GetObjectsOfType<WoWItem>(false))
            {
                if (ManaPotionIds.Contains(item.Entry) && item.Entry > bestEntry)
                {
                    bestEntry = item.Entry;
                    bestName = item.Name;
                }
            }

            if (bestEntry > 0)
            {
                Logging.Write("Using mana potion: {0}", bestName);
                Lua.DoString(string.Format("RunMacroText('/use {0}')", bestName));
            }
        }

        /// <summary>
        /// Heal another player (placeholder).
        /// </summary>
        public static void HealOther()
        {
            // Placeholder - implement based on class
        }

        /// <summary>
        /// Fast heal another player (placeholder).
        /// </summary>
        public static void FastHealOther()
        {
            // Placeholder - implement based on class
        }
    }
}
