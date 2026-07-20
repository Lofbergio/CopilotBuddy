using System;
using System.Collections.Generic;
using Bots.Vibes.Shared.GrindData;
using Styx;
using Styx.WoWInternals;

namespace Bots.Vibes.Shared.GrindData
{
    /// <summary>
    /// Resolves which creature factions the current character may attack, using the live client
    /// faction system (same path NpcQueries uses: WoWFaction.RelationTo). Built once per Start
    /// for the current map's distinct factions — no precomputed hostility table needed.
    /// </summary>
    public class FactionResolver
    {
        // CREATURE_TYPE_HUMANOID (3.3.5a). The neutral-humanoid guard protects REP-BEARING factions
        // (cartel towns — killing their guards overnight bricks the bot's own vendor/flight hubs).
        public const int HumanoidType = 7;

        /// <summary>Reaction below Neutral (Hated/Hostile/Unfriendly) — attackable regardless of type.</summary>
        public HashSet<int> HostileFactions { get; private set; } = new HashSet<int>();

        /// <summary>Reaction exactly Neutral (yellow) — attackable unless humanoid AND rep-bearing.</summary>
        public HashSet<int> NeutralFactions { get; private set; } = new HashSet<int>();

        /// <summary>Neutral factions whose Faction.dbc row carries a reputation index — the ones the
        /// humanoid guard actually exists for. Since Wrath made ALL starter-zone mobs neutral, a blanket
        /// neutral-humanoid exclusion blinded spot selection to kobold/Defias-type populations (the
        /// densest low-level grind real estate) while TARGETING still killed them opportunistically —
        /// rep-less neutral humanoids are now eligible (2026-07-10, Northshire L1 validation).</summary>
        public HashSet<int> RepBearingNeutrals { get; private set; } = new HashSet<int>();

        public void Build(uint mapId)
        {
            var hostile = new HashSet<int>();
            var neutral = new HashSet<int>();
            var repBearing = new HashSet<int>();
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
                        {
                            neutral.Add(faction);          // yellow — passive
                            // Rep-bearing = Faction.dbc reputationIndex >= 0 (RepGainId; -1 = no rep row).
                            // DBC lookup failure keeps the faction PROTECTED (old blanket behavior).
                            WoWFaction f = mobTemplate.Faction;
                            if (f == null || f.Record.RepGainId >= 0)
                                repBearing.Add(faction);
                        }
                    }
                    catch
                    {
                        // Unknown/invalid faction template — leave to the live Targeting filter.
                    }
                }
            }
            HostileFactions = hostile;
            NeutralFactions = neutral;
            RepBearingNeutrals = repBearing;
        }

        /// <summary>
        /// Two-tier safety: hostile/unfriendly factions are attackable for any creature type;
        /// neutral factions unless the mob is humanoid AND its faction carries reputation —
        /// protects the cartel vendor towns the bot itself depends on, without hiding rep-less
        /// neutral humanoids (kobolds/Defias — post-Wrath starter zones are ALL neutral).
        /// </summary>
        public bool IsAttackable(int faction, int type)
        {
            if (HostileFactions.Contains(faction))
                return true;
            if (NeutralFactions.Contains(faction) && (type != HumanoidType || !RepBearingNeutrals.Contains(faction)))
                return true;
            return false;
        }
    }
}
