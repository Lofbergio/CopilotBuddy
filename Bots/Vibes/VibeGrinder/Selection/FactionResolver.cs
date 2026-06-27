using System;
using System.Collections.Generic;
using Bots.VibeGrinder.Data;
using Styx;
using Styx.WoWInternals;

namespace Bots.VibeGrinder.Selection
{
    /// <summary>
    /// Resolves which creature factions the current character may attack, using the live client
    /// faction system (same path NpcQueries uses: WoWFaction.RelationTo). Built once per Start
    /// for the current map's distinct factions — no precomputed hostility table needed.
    /// </summary>
    public class FactionResolver
    {
        // CREATURE_TYPE_HUMANOID (3.3.5a). Neutral humanoid factions are the rep-bearing ones
        // (goblin towns, ogre clans, etc.) we must not grind unattended.
        public const int HumanoidType = 7;

        /// <summary>Reaction below Neutral (Hated/Hostile/Unfriendly) — attackable regardless of type.</summary>
        public HashSet<int> HostileFactions { get; private set; } = new HashSet<int>();

        /// <summary>Reaction exactly Neutral (yellow) — attackable only for non-humanoid mobs.</summary>
        public HashSet<int> NeutralFactions { get; private set; } = new HashSet<int>();

        public void Build(uint mapId)
        {
            var hostile = new HashSet<int>();
            var neutral = new HashSet<int>();
            var me = StyxWoW.Me;
            // Compare via FactionTemplate.dbc, the MOB's reaction toward the player ("does it aggro us").
            // NOT WoWFaction.RelationTo: that path took the player's faction from me.FactionTemplate.Faction,
            // which is WoWFaction.FromId(..., isTemplate:false) and so carries NO _template — and
            // CompareFactions returns Neutral whenever either side's template FactionId is 0. Net effect:
            // EVERY mob read as Neutral (the log's "0 hostile factions"), blinding the whole add/crowd/danger
            // model so it picked dense aggressive camps as "safe". WoWFactionTemplate.GetReactionTowards is the
            // complete impl and both templates here are valid. (Live combat was always fine — native reaction.)
            WoWFactionTemplate myTemplate = me?.FactionTemplate;
            if (me != null && myTemplate != null)
            {
                foreach (int faction in GrindMobsRepository.DistinctFactionsOnMap(mapId))
                {
                    if (faction <= 0)
                        continue;
                    try
                    {
                        WoWFactionTemplate mobTemplate = WoWFactionTemplate.FromId((uint)faction);
                        if (mobTemplate == null)
                            continue;
                        WoWUnitReaction r = mobTemplate.GetReactionTowards(myTemplate);
                        if (r < WoWUnitReaction.Neutral)
                            hostile.Add(faction);          // red/orange — aggros on sight; counts as add-risk
                        else if (r == WoWUnitReaction.Neutral)
                            neutral.Add(faction);          // yellow — passive; grindable only if non-humanoid
                    }
                    catch
                    {
                        // Unknown/invalid faction template — leave to the live Targeting filter.
                    }
                }
            }
            HostileFactions = hostile;
            NeutralFactions = neutral;
        }

        /// <summary>
        /// Two-tier safety: hostile/unfriendly factions are attackable for any creature type;
        /// neutral factions only for non-humanoids — avoids tanking reputation with neutral
        /// humanoid factions (towns/clans) over a long unattended session.
        /// </summary>
        public bool IsAttackable(int faction, int type)
        {
            if (HostileFactions.Contains(faction))
                return true;
            if (NeutralFactions.Contains(faction) && type != HumanoidType)
                return true;
            return false;
        }
    }
}
