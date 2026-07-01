using System;
using System.Collections.Generic;
using System.Linq;
using Bots.VibeGrinder.Data;
using Styx;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
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
        public GrindSpot SelectBest(uint mapId, WoWPoint playerLoc, int playerLevel, IEnumerable<(WoWPoint center, float radius)> blacklist, double caution = 1.0)
        {
            var blocked = blacklist != null ? blacklist.ToList() : new List<(WoWPoint center, float radius)>();

            // Path-gauntlet leniency ladder: prefer a route that avoids over-level hostiles, but if nothing
            // qualifies, relax rather than idle forever — the user would rather it grind the least-bad route
            // than stall. Balanced → ×2 → off (off = pre-gauntlet behaviour: accept the best-scored reachable
            // spot). Each rung keeps the existing blacklist-honoured-then-cleared fallback.
            int balanced = S.PathGauntletTolerance;
            int[] ladder = { balanced, balanced * 2, int.MaxValue };

            foreach (int tol in ladder)
            {
                GrindSpot best = Search(mapId, playerLoc, playerLevel, blocked, caution, tol);
                if (best == null && blocked.Count > 0)
                {
                    Styx.Helpers.Logging.Write("[VibeGrinder] No spot outside blacklist; clearing it and retrying once.");
                    best = Search(mapId, playerLoc, playerLevel, new List<(WoWPoint center, float radius)>(), caution, tol);
                }
                if (best != null)
                {
                    if (tol != balanced)
                        Styx.Helpers.Logging.Write(System.Drawing.Color.Orange,
                            "[VibeGrinder] Relaxed path-danger strictness ({0}) — no safer-route spot available; " +
                            "expect to fight through over-level mobs en route.",
                            tol == int.MaxValue ? "off" : "tolerate " + tol);
                    return best;
                }
            }
            return null;
        }

        private GrindSpot Search(uint mapId, WoWPoint playerLoc, int playerLevel, List<(WoWPoint center, float radius)> blocked, double caution, int pathGauntletTolerance)
        {
            bool scarce = playerLevel <= S.ScarcityLevelCeiling;
            // Scarce: halve the density requirement (floor of 2 — a lone spawn isn't a "spot").
            int minMobs = scarce ? Math.Max(2, S.MinMobsPerSpot / 2) : S.MinMobsPerSpot;

            // Combat-safe level window: ±2. +3 mobs are brutal for an unattended low-level character
            // (high miss/crit-against and heavy incoming damage); mobs >2 below grey out fast.
            // Longevity comes from committing to a spot (the supervisor relocates only when we've
            // genuinely out-leveled it), not from widening this window.
            int lvlMin = playerLevel - 2;
            int lvlMax = playerLevel + 2;

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

            // Learned-capability weight on hostile crowding: innate squishiness (level taper) ×
            // how badly we've been dying to packs (caution). 0 above the level ceiling or when the
            // toon's proven it can cleave. Confidence-ready: once caution dips below 1, this relaxes.
            float crowdWeight = S.CrowdLevelScale(playerLevel) * (float)caution;

            // Phase 1 — cheap rank, no navigation (reachability is gated in phase 2 via GeneratePath,
            // which works at Start; CanNavigateFully needs a provider that isn't wired until ticks).
            var candidates = new List<Scored>();
            int dropThin = 0, dropBlacklist = 0, dropFar = 0, dropWater = 0;
            foreach (Cluster c in clusters)
            {
                if (c.Members.Count < minMobs) { dropThin++; continue; }
                if (IsBlacklisted(c.Centroid, blocked)) { dropBlacklist++; continue; }

                float dist = playerLoc.Distance2D(c.Centroid);
                if (dist > S.MaxTravelDistance) { dropFar++; continue; }

                int totalNear = GrindMobsRepository.CountSpawnsNear(mapId, c.Centroid, S.GrindRadius);
                float purity = totalNear <= 0 ? 1f : (float)c.Members.Count / totalNear;
                if (purity > 1f) purity = 1f;

                float threat = DangerEvaluator.DestinationThreat(c.Centroid, mapId, playerLevel, out bool guardPack);
                // Dense AND near: strong proximity decay (half-score at ProximityHalfDistance) so it
                // never crosses the zone for density, but density still decides among nearby spots.
                float ratio = dist / Math.Max(1f, S.ProximityHalfDistance);
                float proximityFactor = 1f / (1f + ratio * ratio);

                // Hostile packing the elite-only danger model can't see: worst simultaneous-add knot.
                // Neutral-heavy camps (the bread-and-butter) score 0 here and keep full density value.
                int hostilePack = crowdWeight > 0f && S.SpotCrowdPenalty > 0f
                    ? WorstHostilePack(c.Members, S.PullCrowdRadius) : 0;
                int packExcess = Math.Max(0, hostilePack - 1);
                float survFactor = packExcess > 0
                    ? 1f / (1f + S.SpotCrowdPenalty * packExcess * crowdWeight) : 1f;

                // Level quality: penalise spots dragged down by low/gray mobs. Uses the population-weighted
                // average level of nearby ATTACKABLE mobs (not just the band-eligible cluster), so a couple
                // of band-edge anchors surrounded by lowbies can't win on density alone. Quadratic falloff
                // the further the local average sits below the player.
                float avgLevel = GrindMobsRepository.AverageAttackableLevelNear(mapId, c.Centroid, S.GrindRadius, _factions, ImmuneUnitFlagMask);
                float below = avgLevel > 0f ? Math.Max(0f, playerLevel - avgLevel) : 0f;
                float levelFactor = 1f / (1f + S.LowLevelSpotPenalty * below * below);

                // Underwater camps are a poor grind (can't rest/totem/eat, drowning risk, and the swimming
                // drop-pin logic won't fight submerged mobs). Devalue — not veto — so land wins but a lake
                // camp is still a last resort. Cheap: one liquid traceline per gated candidate centroid.
                bool submerged = S.SpotWaterPenalty > 0f && IsUnderwater(c.Centroid);
                float waterFactor = submerged ? 1f / (1f + S.SpotWaterPenalty) : 1f;
                if (submerged) dropWater++;

                float baseScore = c.Members.Count * purity * proximityFactor / (1f + threat) * survFactor * levelFactor * waterFactor;

                candidates.Add(new Scored
                {
                    Cluster = c,
                    Dist = dist,
                    Score = baseScore,
                    Threat = threat,
                    GuardPack = guardPack,
                    HostilePack = hostilePack,
                    AvgLevel = avgLevel,
                });
            }

            Logging.Write("[VibeGrinder] {0} candidates after gates (dropped: {1} thin, {2} blacklisted, {3} too far; {4} underwater devalued).",
                candidates.Count, dropThin, dropBlacklist, dropFar, dropWater);

            if (candidates.Count == 0)
                return null;

            // Scarcity also relaxes contention tolerance (accept Risky-by-contention).
            bool scarcityLenient = scarce || candidates.Count <= S.ScarcityCandidateFloor;

            // Phase 2 — rank by the dense-and-near score (proximity decay keeps it local), then take
            // the best reachable, non-Dangerous spot. Bounded budget caps the pathfinds.
            var ranked = candidates.OrderByDescending(c => c.Score).ToList();
            int budget = Math.Min(ranked.Count, S.TopCandidatesForPathCheck);

            GrindSpot bestSafe = null;
            GrindSpot bestRisky = null;
            int dangerousDropped = 0, contestedSkipped = 0, dropUnreachable = 0;

            for (int i = 0; i < budget; i++)
            {
                Scored sc = ranked[i];

                // GeneratePath works at Start (lazy Tripper tiles, no provider needed). A null/empty
                // or partial path = unreachable. One pathfind feeds both this gate and path-danger.
                WoWPoint[] path = Navigator.GeneratePath(playerLoc, sc.Cluster.Centroid);
                if (path == null || path.Length == 0)
                {
                    dropUnreachable++;
                    Logging.WriteDebug("[VibeGrinder]  cand#{0} unreachable (no path) @ {1}", i, sc.Cluster.Centroid);
                    continue;
                }
                // GeneratePath returns PARTIAL paths as "success" (Detour DT_PARTIAL_RESULT), so a path
                // that stops short of the centroid = unreachable (island/cliff/across water). Don't install
                // a spot we can only walk toward forever — the endpoint must land near the destination.
                if (path[path.Length - 1].Distance2D(sc.Cluster.Centroid) > 20f)
                {
                    dropUnreachable++;
                    Logging.WriteDebug("[VibeGrinder]  cand#{0} unreachable (partial path, ends {1:F0}yd short) @ {2}",
                        i, path[path.Length - 1].Distance2D(sc.Cluster.Centroid), sc.Cluster.Centroid);
                    continue;
                }

                float pathDanger = DangerEvaluator.PathDanger(path, mapId, playerLevel);
                bool contested = HostilePlayersNear(sc.Cluster.Centroid, S.GrindRadius) > 0;
                SpotClass cls = DangerEvaluator.Classify(sc.Threat, sc.GuardPack, pathDanger, contested);

                // Hard body-pull gate: an over-level hostile sitting in aggro range of a kill position
                // will drag in (from beyond our pull range) every loot walk — and we can't clear it.
                bool aggroBubble = cls != SpotClass.Dangerous &&
                    DangerEvaluator.OverlevelHostileInAggro(sc.Cluster.Members, sc.Cluster.Centroid,
                        mapId, playerLevel, _factions, S.AggroAvoidBuffer);
                if (aggroBubble)
                    cls = SpotClass.Dangerous;

                // Over-level hostile gauntlet on the *route* (elite-only PathDanger can't see normal
                // over-level packs). Reject routes that body-pull us past too many; the ladder in
                // SelectBest relaxes pathGauntletTolerance (→ off) if nothing safer qualifies.
                int gauntlet = 0;
                if (cls != SpotClass.Dangerous && pathGauntletTolerance != int.MaxValue)
                {
                    gauntlet = DangerEvaluator.OverlevelHostilesOnPath(
                        path, mapId, playerLevel, _factions, S.AggroAvoidBuffer, S.PathHostileLevelMargin);
                    if (gauntlet >= pathGauntletTolerance)
                        cls = SpotClass.Dangerous;
                }

                Logging.WriteDebug(
                    "[VibeGrinder]  cand#{0} score={1:F1} avgLvl={2:F1} threat={3:F1} guardPack={4} hostilePack={5} aggroBubble={6} gauntlet={7} pathDanger={8:F1} contested={9} mobs={10} dist={11:F0} -> {12} @ {13}",
                    i, sc.Score, sc.AvgLevel, sc.Threat, sc.GuardPack, sc.HostilePack, aggroBubble, gauntlet, pathDanger, contested, sc.Cluster.Members.Count,
                    playerLoc.Distance2D(sc.Cluster.Centroid), cls, sc.Cluster.Centroid);

                if (cls == SpotClass.Dangerous) { dangerousDropped++; continue; }
                if (cls == SpotClass.Risky && contested && !scarcityLenient) { contestedSkipped++; continue; }

                GrindSpot spot = BuildSpot(sc.Cluster, mapId, cls, sc.Score);
                if (cls == SpotClass.Safe && bestSafe == null)
                    bestSafe = spot;
                else if (cls == SpotClass.Risky && bestRisky == null)
                    bestRisky = spot;

                if (bestSafe != null)
                    break; // ranked by dense-and-near score — first Safe is the best Safe
            }

            GrindSpot chosen = bestSafe ?? bestRisky;
            if (chosen == null)
                Logging.Write("[VibeGrinder] No acceptable spot in top {0} ({1} unreachable, {2} Dangerous, {3} contested-skipped). " +
                    "Lower danger strictness, raise MaxTravelDistance, or move closer.", budget, dropUnreachable, dangerousDropped, contestedSkipped);
            else
                Logging.Write("[VibeGrinder] Chosen {0} spot, score {1:F1}, {2:F0} yd away.",
                    chosen.Classification, chosen.Score, playerLoc.Distance2D(chosen.Centroid));

            return chosen;
        }

        private static GrindSpot BuildSpot(Cluster c, uint mapId, SpotClass cls, float score)
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
                Score = score,
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
            public float Dist;
            public float Score;
            public float Threat;
            public bool GuardPack;
            public int HostilePack;
            public float AvgLevel;
        }

        /// <summary>
        /// Worst simultaneous-add count in a cluster: centred on each hostile (proximity-aggro)
        /// member, how many hostile members sit within <paramref name="radius"/>. Neutral members
        /// don't count — they won't pile on when you single-pull. 0 = no hostiles at all.
        /// </summary>
        private int WorstHostilePack(List<MobSpawn> members, float radius)
        {
            float r2 = radius * radius;
            int worst = 0;
            for (int i = 0; i < members.Count; i++)
            {
                if (!_factions.HostileFactions.Contains(members[i].Faction)) continue;
                int n = 1;
                for (int j = 0; j < members.Count; j++)
                {
                    if (j == i) continue;
                    if (!_factions.HostileFactions.Contains(members[j].Faction)) continue;
                    if (members[i].Point.DistanceSqr(members[j].Point) <= r2) n++;
                }
                if (n > worst) worst = n;
            }
            return worst;
        }

        /// <summary>
        /// True if the point sits under a liquid surface — a sky-down traceline that hits liquid.
        /// MUST use CGWorldFrameHitFlags (native WoW Intersect); the navmesh TraceLine overload ignores
        /// the Liquid flag (same gotcha GatherbuddyBot documents). Cheap enough per gated candidate.
        /// </summary>
        private static bool IsUnderwater(WoWPoint p)
        {
            try
            {
                return GameWorld.TraceLine(p.Add(0f, 0f, 200f), p, GameWorld.CGWorldFrameHitFlags.HitTestLiquid);
            }
            catch
            {
                return false; // never let a probe failure reject/keep a spot — treat as dry
            }
        }

        /// <summary>
        /// Greedy clustering, seed-based (a spawn joins if within radius of the seed), keeping member
        /// lists. Spatial-hashed into radius-sized cells so it's O(n) instead of O(n²): a continent-wide
        /// level band can return thousands of spawns, and all-pairs there froze the worker thread for
        /// seconds on every selection/relocation. Cells are radius-sized, so every spawn within radius
        /// of a seed lies in the seed cell or one of its 8 neighbours — same result as the old scan.
        /// </summary>
        private static List<Cluster> ClusterSpawns(List<MobSpawn> spawns, float radius)
        {
            var clusters = new List<Cluster>();
            int n = spawns.Count;
            var used = new bool[n];
            float r2 = radius * radius;
            float cell = radius <= 1f ? 1f : radius;

            var grid = new Dictionary<(int, int), List<int>>(n);
            for (int i = 0; i < n; i++)
            {
                var key = ((int)Math.Floor(spawns[i].Point.X / cell), (int)Math.Floor(spawns[i].Point.Y / cell));
                if (!grid.TryGetValue(key, out List<int> list)) { list = new List<int>(); grid[key] = list; }
                list.Add(i);
            }

            for (int i = 0; i < n; i++)
            {
                if (used[i]) continue;
                var cluster = new Cluster();
                cluster.Members.Add(spawns[i]);
                used[i] = true;

                int cx = (int)Math.Floor(spawns[i].Point.X / cell);
                int cy = (int)Math.Floor(spawns[i].Point.Y / cell);
                for (int gx = cx - 1; gx <= cx + 1; gx++)
                    for (int gy = cy - 1; gy <= cy + 1; gy++)
                    {
                        if (!grid.TryGetValue((gx, gy), out List<int> bucket)) continue;
                        foreach (int j in bucket)
                        {
                            if (used[j]) continue;
                            if (spawns[i].Point.DistanceSqr(spawns[j].Point) <= r2)
                            {
                                cluster.Members.Add(spawns[j]);
                                used[j] = true;
                            }
                        }
                    }

                float sx = 0, sy = 0, sz = 0;
                foreach (MobSpawn m in cluster.Members) { sx += m.Point.X; sy += m.Point.Y; sz += m.Point.Z; }
                int c = cluster.Members.Count;
                cluster.Centroid = new WoWPoint(sx / c, sy / c, sz / c);
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

        private static bool IsBlacklisted(WoWPoint centroid, List<(WoWPoint center, float radius)> blocked)
        {
            // Per-entry radius: a normal abandoned-spot entry is GrindRadius; a death-cluster entry can be much
            // wider (escalates with swarm size + regional deaths) so an entire death-trap area is excluded.
            foreach (var b in blocked)
                if (centroid.DistanceSqr(b.center) <= b.radius * b.radius)
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
