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
        public HashSet<int> AttackableFactions { get; private set; } = new HashSet<int>();

        public void Build(uint mapId)
        {
            var attackable = new HashSet<int>();
            var me = StyxWoW.Me;
            if (me == null)
            {
                AttackableFactions = attackable;
                return;
            }

            WoWFaction myFaction = me.FactionTemplate.Faction;
            foreach (int faction in GrindMobsRepository.DistinctFactionsOnMap(mapId))
            {
                if (faction <= 0)
                    continue;
                try
                {
                    // Attackable == the client reaction is Hostile or worse (Hated/Hostile).
                    if (myFaction.RelationTo(new WoWFaction((uint)faction)) <= WoWUnitReaction.Hostile)
                        attackable.Add(faction);
                }
                catch
                {
                    // Unknown/invalid faction template — leave to the live Targeting filter.
                }
            }

            AttackableFactions = attackable;
        }

        public bool CanAttack(int faction) => AttackableFactions.Contains(faction);
    }
}
