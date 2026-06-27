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
            if (me != null)
            {
                WoWFaction myFaction = me.FactionTemplate.Faction;
                foreach (int faction in GrindMobsRepository.DistinctFactionsOnMap(mapId))
                {
                    if (faction <= 0)
                        continue;
                    try
                    {
                        // The MOB's reaction toward the player (does it aggro us) — NOT the player's toward
                        // the mob. CompareFactions only checks A's enemy mask, and aggressive-but-asymmetric
                        // factions like "Monster" (14, e.g. Flatland Cougar) encode their hostility in THEIR
                        // enemy mask. Computing it the wrong way round read every such mob as Neutral, so the
                        // whole hostile-based add/crowd/danger model went blind and picked dense hostile camps
                        // as "safe". (Live combat was fine — it uses the native reaction.)
                        WoWUnitReaction r = new WoWFaction((uint)faction).RelationTo(myFaction);
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
