using System;
using System.Collections.Generic;
using System.Linq;
using Bots.VibeGrinder.Data;
using Styx;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.VibeGrinder.Selection
{
    /// <summary>
    /// The spot-picking brain. Eligible spawns → clustered candidate spots → two-phase scoring
    /// (cheap rank, then danger gate on the top-N) → best spot under the leniency/scarcity rules.
    /// Principle: relax for inconvenience (contested/thin), never for lethality (Dangerous).
    /// </summary>
    public class SpotSelector
    {
        // 3.3.5a UNIT_FLAGS to exclude: NON_ATTACKABLE(0x2) | IMMUNE_TO_PC(0x100) | NOT_SELECTABLE(0x2000000).
        // TODO: confirm against Styx/Patchables/Offsets335.txt if any target type is wrongly excluded.
        private const long ImmuneUnitFlagMask = 0x2L | 0x100L | 0x2000000L;

        private static VibeGrinderSettings S => VibeGrinderSettings.Instance;

        private readonly FactionResolver _factions;

        public SpotSelector(FactionResolver factions)
        {
            _factions = factions;
        }

        /// <summary>
        /// Returns the best spot from the character's current position, or null if none qualifies.
        /// Blacklisted centroids (recently abandoned) are excluded; if that empties the field, the
        /// blacklist is cleared and the search retried once.
        /// </summary>
        public GrindSpot SelectBest(uint mapId, WoWPoint playerLoc, int playerLevel, IEnumerable<WoWPoint> blacklist)
        {
            var blocked = blacklist != null ? blacklist.ToList() : new List<WoWPoint>();

            GrindSpot best = Search(mapId, playerLoc, playerLevel, blocked);
            if (best == null && blocked.Count > 0)
            {
                Styx.Helpers.Logging.Write("[VibeGrinder] No spot outside blacklist; clearing it and retrying once.");
                best = Search(mapId, playerLoc, playerLevel, new List<WoWPoint>());
            }
            return best;
        }

        private GrindSpot Search(uint mapId, WoWPoint playerLoc, int playerLevel, List<WoWPoint> blocked)
        {
            bool scarce = playerLevel <= S.ScarcityLevelCeiling;
            int minMobs = scarce ? Math.Max(3, S.MinMobsPerSpot / 2) : S.MinMobsPerSpot;

            int lvlMin = playerLevel - S.LevelBandBelow;
            int lvlMax = playerLevel + S.LevelBandAbove;

            List<MobSpawn> eligible = GrindMobsRepository.QueryEligibleSpawns(
                mapId, lvlMin, lvlMax, _factions, ImmuneUnitFlagMask);

            Logging.Write("[VibeGrinder] Selecting: L{0} band[{1},{2}] map {3}, {4} hostile + {5} neutral factions, {6} eligible spawns.",
                playerLevel, lvlMin, lvlMax, mapId,
                _factions.HostileFactions.Count, _factions.NeutralFactions.Count, eligible.Count);

            if (eligible.Count == 0)
            {
                Logging.Write("[VibeGrinder] No eligible mobs (check level band / attackable factions / map coverage).");
                return null;
            }

            List<Cluster> clusters = ClusterSpawns(eligible, S.GrindRadius);
            Logging.WriteDebug("[VibeGrinder] {0} clusters from {1} spawns (minMobs={2}, scarce={3}).",
                clusters.Count, eligible.Count, minMobs, scarce);

            // Phase 1 — cheap rank with hard gates. Count drop reasons so "no spot" is explainable.
            var candidates = new List<Scored>();
            int dropThin = 0, dropBlacklist = 0, dropFar = 0, dropUnreachable = 0;
            foreach (Cluster c in clusters)
            {
                if (c.Members.Count < minMobs) { dropThin++; continue; }
                if (IsBlacklisted(c.Centroid, blocked)) { dropBlacklist++; continue; }

                float dist = playerLoc.Distance2D(c.Centroid);
                if (dist > S.MaxTravelDistance) { dropFar++; continue; }
                if (!Navigator.CanNavigateFully(playerLoc, c.Centroid)) { dropUnreachable++; continue; }

                int totalNear = GrindMobsRepository.CountSpawnsNear(mapId, c.Centroid, S.GrindRadius);
                float purity = totalNear <= 0 ? 1f : (float)c.Members.Count / totalNear;
                if (purity > 1f) purity = 1f;

                float threat = DangerEvaluator.DestinationThreat(c.Centroid, mapId, playerLevel, out bool guardPack);
                float proximityBonus = 1f - dist / S.MaxTravelDistance;
                float baseScore = c.Members.Count * purity * purity * (1f + 0.25f * proximityBonus) / (1f + threat);

                candidates.Add(new Scored
                {
                    Cluster = c,
                    Score = baseScore,
                    Threat = threat,
                    GuardPack = guardPack,
                });
            }

            Logging.Write("[VibeGrinder] {0} candidates after gates (dropped: {1} thin, {2} blacklisted, {3} too far, {4} unreachable).",
                candidates.Count, dropThin, dropBlacklist, dropFar, dropUnreachable);

            if (candidates.Count == 0)
                return null;

            // Scarcity also relaxes contention tolerance (accept Risky-by-contention).
            bool scarcityLenient = scarce || candidates.Count <= S.ScarcityCandidateFloor;

            // Phase 2 — danger gate on the top-N by base score.
            var ranked = candidates.OrderByDescending(c => c.Score).ToList();
            int topN = Math.Min(S.TopCandidatesForPathCheck, ranked.Count);

            GrindSpot bestSafe = null;
            GrindSpot bestRisky = null;
            int dangerousDropped = 0, contestedSkipped = 0;

            for (int i = 0; i < topN; i++)
            {
                Scored sc = ranked[i];
                float pathDanger = DangerEvaluator.PathDanger(playerLoc, sc.Cluster.Centroid, mapId, playerLevel);
                bool contested = HostilePlayersNear(sc.Cluster.Centroid, S.GrindRadius) > 0;
                SpotClass cls = DangerEvaluator.Classify(sc.Threat, sc.GuardPack, pathDanger, contested);

                Logging.WriteDebug(
                    "[VibeGrinder]  cand#{0} score={1:F1} threat={2:F1} guardPack={3} pathDanger={4:F1} contested={5} mobs={6} dist={7:F0} -> {8} @ {9}",
                    i, sc.Score, sc.Threat, sc.GuardPack, pathDanger, contested, sc.Cluster.Members.Count,
                    playerLoc.Distance2D(sc.Cluster.Centroid), cls, sc.Cluster.Centroid);

                if (cls == SpotClass.Dangerous) { dangerousDropped++; continue; }
                if (cls == SpotClass.Risky && contested && !scarcityLenient) { contestedSkipped++; continue; }

                GrindSpot spot = BuildSpot(sc.Cluster, mapId, cls);
                if (cls == SpotClass.Safe && bestSafe == null)
                    bestSafe = spot;
                else if (cls == SpotClass.Risky && bestRisky == null)
                    bestRisky = spot;

                if (bestSafe != null)
                    break; // ranked desc — first Safe is the best Safe
            }

            GrindSpot chosen = bestSafe ?? bestRisky;
            if (chosen == null)
                Logging.Write("[VibeGrinder] No acceptable spot among top {0} ({1} Dangerous, {2} contested-skipped). " +
                    "Lower danger strictness, raise MaxTravelDistance, or move closer.", topN, dangerousDropped, contestedSkipped);
            else
                Logging.Write("[VibeGrinder] Chosen {0} spot, score {1:F1}, {2} yd away.",
                    chosen.Classification, chosen.Score, playerLoc.Distance2D(chosen.Centroid));

            return chosen;
        }

        private static GrindSpot BuildSpot(Cluster c, uint mapId, SpotClass cls)
        {
            return new GrindSpot
            {
                Centroid = c.Centroid,
                Map = mapId,
                Hotspots = OrderRoamCircuit(c.Centroid, c.Members, 12),
                MobIds = c.Members.Select(m => (int)m.Entry).Distinct().ToList(),
                Factions = c.Members.Select(m => m.Faction).Distinct().ToList(),
                DominantMaxLevel = c.Members.Count > 0 ? (int)c.Members.Average(m => m.MaxLevel) : 0,
                Classification = cls,
            };
        }

        // ---- clustering ----

        private sealed class Cluster
        {
            public WoWPoint Centroid;
            public List<MobSpawn> Members = new List<MobSpawn>();
        }

        private sealed class Scored
        {
            public Cluster Cluster;
            public float Score;
            public float Threat;
            public bool GuardPack;
        }

        /// <summary>Greedy clustering (same approach as CreatureSpawnQueries), keeping member lists.</summary>
        private static List<Cluster> ClusterSpawns(List<MobSpawn> spawns, float radius)
        {
            var clusters = new List<Cluster>();
            var used = new bool[spawns.Count];
            float r2 = radius * radius;

            for (int i = 0; i < spawns.Count; i++)
            {
                if (used[i]) continue;
                var cluster = new Cluster();
                cluster.Members.Add(spawns[i]);
                used[i] = true;

                for (int j = i + 1; j < spawns.Count; j++)
                {
                    if (used[j]) continue;
                    if (spawns[i].Point.DistanceSqr(spawns[j].Point) <= r2)
                    {
                        cluster.Members.Add(spawns[j]);
                        used[j] = true;
                    }
                }

                float sx = 0, sy = 0, sz = 0;
                foreach (MobSpawn m in cluster.Members) { sx += m.Point.X; sy += m.Point.Y; sz += m.Point.Z; }
                int n = cluster.Members.Count;
                cluster.Centroid = new WoWPoint(sx / n, sy / n, sz / n);
                clusters.Add(cluster);
            }
            return clusters;
        }

        /// <summary>Nearest-neighbour ordering from the centroid so the roam loop is a sane circuit.</summary>
        private static List<WoWPoint> OrderRoamCircuit(WoWPoint centroid, List<MobSpawn> members, int cap)
        {
            var pts = members.Select(m => m.Point).ToList();
            var ordered = new List<WoWPoint>();
            WoWPoint current = centroid;
            while (pts.Count > 0 && ordered.Count < cap)
            {
                int bestIdx = 0;
                float bestD = float.MaxValue;
                for (int i = 0; i < pts.Count; i++)
                {
                    float d = current.DistanceSqr(pts[i]);
                    if (d < bestD) { bestD = d; bestIdx = i; }
                }
                current = pts[bestIdx];
                ordered.Add(current);
                pts.RemoveAt(bestIdx);
            }
            if (ordered.Count == 0)
                ordered.Add(centroid);
            return ordered;
        }

        private static bool IsBlacklisted(WoWPoint centroid, List<WoWPoint> blocked)
        {
            float r2 = S.GrindRadius * S.GrindRadius;
            foreach (WoWPoint b in blocked)
                if (centroid.DistanceSqr(b) <= r2)
                    return true;
            return false;
        }

        private static int HostilePlayersNear(WoWPoint center, float radius)
        {
            try
            {
                int n = 0;
                foreach (WoWPlayer p in ObjectManager.GetObjectsOfType<WoWPlayer>(false, false))
                {
                    if (p == null || p.Guid == StyxWoW.Me.Guid)
                        continue;
                    // No group/friend nuance yet — any non-self player in-spot counts as contention.
                    if (p.Location.Distance(center) <= radius)
                        n++;
                }
                return n;
            }
            catch
            {
                return 0;
            }
        }
    }
}
