using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.Logic.Combat;
using Styx.Combat.CombatRoutine; // contains WoWClass enum used by extension methods

namespace Styx.Helpers
{
    /// <summary>
    /// Extension methods pour WoWPlayer/LocalPlayer.
    /// Permet d'appeler IsTank(), IsDps(), IsHealer() comme méthodes
    /// (les scripts DungeonBuddy les appellent avec parenthèses).
    /// Compatible HB 4.3.4 qui avait ces méthodes.
    /// 
    /// IMPORTANT: Ces méthodes utilisent UnitGroupRolesAssigned() pour détecter
    /// le rôle LFG ASSIGNÉ, pas la classe du joueur.
    /// Un Paladin Holy ne doit PAS retourner IsTank()=true.
    /// Les properties WoWPlayer.IsTank/IsHealer (class-based) restent disponibles
    /// pour savoir si la classe PEUT jouer ce rôle.
    /// 
    /// Référence HB 4.3.4: ScriptHelpers.cs L1308-L1501
    /// </summary>
    public static class WoWPlayerExtensions
    {
        /// <summary>
        /// True si le joueur a le rôle TANK assigné dans le LFG/groupe.
        /// Utilise UnitGroupRolesAssigned() Lua API.
        /// </summary>
        public static bool IsTank(this LocalPlayer me)
        {
            string role = Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0);
            if (!string.IsNullOrEmpty(role) && role != "NONE")
                return role == "TANK";
            // Fallback: détection par spells de spec (pattern HB 4.3.4)
            return me.Class switch
            {
                WoWClass.Warrior => SpellManager.HasSpell("Shield Slam"),
                WoWClass.Paladin => SpellManager.HasSpell("Avenger's Shield"),
                WoWClass.DeathKnight => SpellManager.HasSpell("Heart Strike"),
                WoWClass.Druid => SpellManager.HasSpell("Mangle") && !SpellManager.HasSpell("Moonkin Form"),
                _ => false
            };
        }

        /// <summary>
        /// True si le joueur a le rôle HEALER assigné dans le LFG/groupe.
        /// </summary>
        public static bool IsHealer(this LocalPlayer me)
        {
            string role = Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0);
            if (!string.IsNullOrEmpty(role) && role != "NONE")
                return role == "HEALER";
            // Fallback par spec
            return me.Class switch
            {
                WoWClass.Priest => !SpellManager.HasSpell("Shadowform"),
                WoWClass.Paladin => SpellManager.HasSpell("Holy Shock"),
                WoWClass.Shaman => SpellManager.HasSpell("Riptide"),
                WoWClass.Druid => SpellManager.HasSpell("Swiftmend"),
                _ => false
            };
        }

        /// <summary>
        /// True si le joueur a le rôle DPS (DAMAGER) assigné.
        /// </summary>
        public static bool IsDps(this LocalPlayer me)
        {
            string role = Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0);
            if (!string.IsNullOrEmpty(role) && role != "NONE")
                return role == "DAMAGER";
            // Fallback: ni tank ni healer
            return !me.IsTank() && !me.IsHealer();
        }

        /// <summary>
        /// True si le joueur est un "follower" (suit le tank).
        /// En contexte donjon DungeonBuddy: !IsTank() = follower.
        /// Référence HB 4.3.4 ScriptHelpers.cs L1395
        /// </summary>
        public static bool IsFollower(this LocalPlayer me)
        {
            return !me.IsTank();
        }

        /// <summary>
        /// True si le joueur est un DPS/healer ranged.
        /// Référence HB 4.3.4 ScriptHelpers.cs L1501
        /// </summary>
        public static bool IsRange(this LocalPlayer me)
        {
            return !me.IsMelee();
        }

        /// <summary>
        /// True si le joueur est melee (warrior, rogue, DK, feral druid, ret pally, enh shaman).
        /// Référence HB 4.3.4 ScriptHelpers.cs L1487
        /// </summary>
        public static bool IsMelee(this LocalPlayer me)
        {
            return me.Class switch
            {
                WoWClass.Warrior => true,
                WoWClass.Rogue => true,
                WoWClass.DeathKnight => true,
                WoWClass.Paladin => !SpellManager.HasSpell("Holy Shock"),
                WoWClass.Shaman => SpellManager.HasSpell("Lava Lash"),
                WoWClass.Druid => SpellManager.HasSpell("Mangle"),
                _ => false
            };
        }
    }
}