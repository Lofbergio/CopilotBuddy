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
        private static WoWPartyMember GetPartyMember(WoWPlayer p) => StyxWoW.Me.GroupInfo.RaidMembers.FirstOrDefault(m => m.Guid == p.Guid);

        /// <summary>
        /// True si le joueur a le rôle TANK assigné dans le LFG/groupe.
        /// </summary>
        public static bool IsTank(this WoWPlayer me)
        {
            if (me.IsMe) {
                string role = Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0);
                if (!string.IsNullOrEmpty(role) && role != "NONE") return role == "TANK";
                return me.Class switch { WoWClass.Warrior => SpellManager.HasSpell("Shield Slam"), WoWClass.Paladin => SpellManager.HasSpell("Avenger's Shield"), WoWClass.DeathKnight => SpellManager.HasSpell("Heart Strike"), WoWClass.Druid => SpellManager.HasSpell("Mangle") && !SpellManager.HasSpell("Moonkin Form"), _ => false };
            }
            var pm = GetPartyMember(me); return pm != null && (pm.Role & WoWPartyMember.GroupRole.Tank) != 0;
        }

        /// <summary>
        /// True si le joueur a le rôle HEALER assigné dans le LFG/groupe.
        /// </summary>
        public static bool IsHealer(this WoWPlayer me)
        {
            if (me.IsMe) {
                string role = Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0);
                if (!string.IsNullOrEmpty(role) && role != "NONE") return role == "HEALER";
                return me.Class switch { WoWClass.Priest => !SpellManager.HasSpell("Shadowform"), WoWClass.Paladin => SpellManager.HasSpell("Holy Shock"), WoWClass.Shaman => SpellManager.HasSpell("Riptide"), WoWClass.Druid => SpellManager.HasSpell("Swiftmend"), _ => false };
            }
            var pm = GetPartyMember(me); return pm != null && (pm.Role & WoWPartyMember.GroupRole.Healer) != 0;
        }

        /// <summary>
        /// True si le joueur a le rôle DPS (DAMAGER) assigné.
        /// </summary>
        public static bool IsDps(this WoWPlayer me)
        {
            if (me.IsMe) {
                string role = Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0);
                if (!string.IsNullOrEmpty(role) && role != "NONE") return role == "DAMAGER";
                return !me.IsTank() && !me.IsHealer();
            }
            var pm = GetPartyMember(me); return pm != null && (pm.Role & WoWPartyMember.GroupRole.Damage) != 0;
        }

        public static bool IsFollower(this WoWPlayer me) => !me.IsTank();
        public static bool IsRange(this WoWPlayer me) => !me.IsMelee();

        public static bool IsMelee(this WoWPlayer me)
        {
            if (!me.IsMe) return true; // fallback
            return me.Class switch { WoWClass.Warrior => true, WoWClass.Rogue => true, WoWClass.DeathKnight => true, WoWClass.Paladin => !SpellManager.HasSpell("Holy Shock"), WoWClass.Shaman => SpellManager.HasSpell("Lava Lash"), WoWClass.Druid => SpellManager.HasSpell("Mangle"), _ => false };
        }

        public static bool IsLeader(this WoWPlayer me) => me.IsTank();
    }
}