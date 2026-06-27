using System;
using System.Collections.Generic;
using System.Linq;
using Bots.VibeGrinder.Data;
using Styx.Logic.Pathing;

namespace Bots.VibeGrinder.Selection
{
    /// <summary>
    /// All spot-danger logic in one place: destination threat, guard-pack detection, path-corridor
    /// danger, and Safe/Risky/Dangerous classification. Classification is pure — leniency/scarcity
    /// live in SpotSelector and may relax Risky, never Dangerous.
    /// </summary>
    public static class DangerEvaluator
    {
        private static VibeGrinderSettings S => VibeGrinderSettings.Instance;

        // Hazard weighting by creature rank (0 normal-but-overlevel, 1 elite, 2 rareelite, 3 boss, 4 rare).
        private static float HazardWeight(int rank)
        {
            switch (rank)
            {
                case 3: return 8f;   // world boss
                case 1: return 4f;   // elite
                case 2: return 4f;   // rare-elite
                case 4: return 2f;   // rare
                default: return 1f;  // normal but over-level
            }
        }

        /// <summary>
        /// Threat at a centroid: Σ weight·linearFalloff(distance) over hazards within DangerRadius.
        /// A hazard on the centroid contributes ~weight; one at the edge ~0. Also reports whether
        /// a guard pack (escort/patrol) sits within the danger radius.
        /// </summary>
        public static float DestinationThreat(WoWPoint centroid, uint mapId, int playerLevel, out bool guardPack)
        {
            List<MobSpawn> hazards = GrindMobsRepository.QueryHazardsNear(
                mapId, centroid, S.DangerRadius, playerLevel, S.DangerLevelMargin);

            float threat = AccumulateThreat(centroid, hazards, S.DangerRadius);
            guardPack = HasGuardPack(hazards);
            return threat;
        }

        /// <summary>
        /// Danger of the route to a spot, given a precomputed path. Densely samples the path and
        /// accumulates threat from hazards within CorridorRadius. A guard pack on the path ⇒ +∞.
        /// The caller supplies the path (and treats a null/empty path as unreachable) so one
        /// GeneratePath call serves both the reachability gate and this danger check.
        /// </summary>
        public static float PathDanger(WoWPoint[] path, uint mapId, int playerLevel)
        {
            // Caller pre-checks reachability (null/empty path); guard anyway.
            if (path == null || path.Length == 0)
                return float.PositiveInfinity;

            WoWPoint from = path[0];
            WoWPoint centroid = path[path.Length - 1];

            // One DB pull covering the straight-line region, then in-memory checks per sample.
            WoWPoint mid = new WoWPoint((from.X + centroid.X) / 2f, (from.Y + centroid.Y) / 2f, (from.Z + centroid.Z) / 2f);
            float span = from.Distance2D(centroid) / 2f + S.CorridorRadius;
            List<MobSpawn> hazards = GrindMobsRepository.QueryHazardsNear(
                mapId, mid, span, playerLevel, S.DangerLevelMargin);
            if (hazards.Count == 0)
                return 0f;

            // Gauntlet test is ELITES ONLY and scoped to the actual path: a route is impassable only
            // if it runs within CorridorRadius of an elite pack — not because over-level normals exist
            // somewhere in the (necessarily large) search circle.
            var elites = hazards.Where(h => h.Rank >= 1).ToList();
            float corridorSq = S.CorridorRadius * S.CorridorRadius;

            float worst = 0f;
            foreach (WoWPoint sample in Densify(path, 20f))
            {
                if (S.ElitePackCount > 0 && elites.Count >= S.ElitePackCount)
                {
                    int near = 0;
                    foreach (MobSpawn e in elites)
                        if (sample.DistanceSqr(e.Point) <= corridorSq)
                            near++;
                    if (near >= S.ElitePackCount)
                        return float.PositiveInfinity;   // path threads through an elite pack
                }

                float local = AccumulateThreat(sample, hazards, S.CorridorRadius);
                if (local > worst)
                    worst = local;
            }
            return worst;
        }

        /// <summary>
        /// Hard body-pull gate. An over-level *hostile* (proximity-aggro) mob within <paramref name="buffer"/>
        /// of any kill position aggros from outside our pull range the moment we walk in to fight or loot —
        /// and it's too strong to clear. Such a spot is Dangerous no matter how low the soft threat sum is.
        /// Neutral over-level mobs don't count — they won't aggro on a walk-by.
        /// </summary>
        public static bool OverlevelHostileInAggro(List<MobSpawn> killPositions, WoWPoint centroid,
            uint mapId, int playerLevel, FactionResolver factions, float buffer)
        {
            if (buffer <= 0f || killPositions == null || killPositions.Count == 0)
                return false;

            List<MobSpawn> hazards = GrindMobsRepository.QueryHazardsNear(
                mapId, centroid, S.GrindRadius + buffer, playerLevel, S.DangerLevelMargin);

            float b2 = buffer * buffer;
            foreach (MobSpawn h in hazards)
            {
                if (!factions.HostileFactions.Contains(h.Faction))
                    continue;
                foreach (MobSpawn k in killPositions)
                    if (h.Point.DistanceSqr(k.Point) <= b2)
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Over-level hostile *gauntlet* on the travel route. The elites-only PathDanger gate can't see a
        /// corridor of over-level normal hostiles (e.g. lvl 16-17 raptors to a lvl 13) — yet body-pulling
        /// several of those while running past is lethal to a squishy lowbie. Counts DISTINCT over-level
        /// hostiles (faction-hostile, max_level &gt; playerLevel+levelMargin) whose aggro range (buffer)
        /// the densified path enters. Neutrals are ignored (no walk-by aggro). The caller treats a count
        /// at/above its tolerance as Dangerous. 0 when buffer/path is empty or nothing qualifies.
        /// </summary>
        public static int OverlevelHostilesOnPath(WoWPoint[] path, uint mapId, int playerLevel,
            FactionResolver factions, float buffer, int levelMargin)
        {
            if (buffer <= 0f || path == null || path.Length == 0 || factions == null)
                return 0;

            WoWPoint from = path[0];
            WoWPoint to = path[path.Length - 1];
            WoWPoint mid = new WoWPoint((from.X + to.X) / 2f, (from.Y + to.Y) / 2f, (from.Z + to.Z) / 2f);
            float span = from.Distance2D(to) / 2f + buffer;

            List<MobSpawn> hazards = GrindMobsRepository.QueryHazardsNear(mapId, mid, span, playerLevel, levelMargin);
            if (hazards.Count == 0)
                return 0;

            // Over-level AND hostile only: the rank>=1 elites QueryHazardsNear also returns are handled by
            // PathDanger's elite gauntlet; here we want the normal-but-over-level hostiles it misses.
            var hostiles = hazards
                .Where(h => h.MaxLevel > playerLevel + levelMargin && factions.HostileFactions.Contains(h.Faction))
                .ToList();
            if (hostiles.Count == 0)
                return 0;

            var samples = Densify(path, 20f).ToList();
            float b2 = buffer * buffer;
            int crossed = 0;
            foreach (MobSpawn h in hostiles)
            {
                foreach (WoWPoint s in samples)
                {
                    if (s.DistanceSqr(h.Point) <= b2) { crossed++; break; }   // path enters this mob's aggro
                }
            }
            return crossed;
        }

        public static SpotClass Classify(float threat, bool guardPack, float pathDanger, bool liveContested)
        {
            if (guardPack || float.IsInfinity(pathDanger) || pathDanger > S.CorridorDangerCap)
                return SpotClass.Dangerous;
            if (threat > S.SafeThreatThreshold || liveContested)
                return SpotClass.Risky;
            return SpotClass.Safe;
        }

        // ---- helpers ----

        private static float AccumulateThreat(WoWPoint center, List<MobSpawn> hazards, float radius)
        {
            float total = 0f;
            float r = radius <= 1f ? 1f : radius;
            foreach (MobSpawn h in hazards)
            {
                // 3D: a mob in a cave directly below/above (XY≈0) is not a surface hazard. Matches
                // how Densify measures segment length, and avoids over-weighting layered terrain.
                float dist = center.Distance(h.Point);
                if (dist >= r)
                    continue;
                total += HazardWeight(h.Rank) * (1f - dist / r);
            }
            return total;
        }

        private static bool HasGuardPack(List<MobSpawn> hazards)
        {
            // A guard pack = a cluster of ELITES (escort/patrol). Over-level normals don't count,
            // or a busy low-level zone would look like wall-to-wall guard packs.
            var elites = hazards.Where(h => h.Rank >= 1).ToList();
            int need = S.ElitePackCount;
            if (elites.Count < need)
                return false;
            float r2 = S.ElitePackRadius * S.ElitePackRadius;
            for (int i = 0; i < elites.Count; i++)
            {
                int near = 1; // self
                for (int j = 0; j < elites.Count; j++)
                {
                    if (i == j) continue;
                    if (elites[i].Point.DistanceSqr(elites[j].Point) <= r2)
                        near++;
                }
                if (near >= need)
                    return true;
            }
            return false;
        }

        /// <summary>Walk the path, emitting points every ~stepYards so sparse waypoints don't miss hazards.</summary>
        private static IEnumerable<WoWPoint> Densify(WoWPoint[] path, float stepYards)
        {
            for (int i = 0; i < path.Length - 1; i++)
            {
                WoWPoint a = path[i];
                WoWPoint b = path[i + 1];
                float segLen = a.Distance(b);
                // Ceiling, not floor: a 1.9×step segment must sample its interior, not just the start
                // (floor gave 1 step → an unsampled mid-segment gap wide enough to hide a corridor mob).
                int steps = System.Math.Max(1, (int)System.Math.Ceiling(segLen / stepYards));
                for (int s = 0; s < steps; s++)
                {
                    float t = (float)s / steps;
                    yield return new WoWPoint(
                        a.X + (b.X - a.X) * t,
                        a.Y + (b.Y - a.Y) * t,
                        a.Z + (b.Z - a.Z) * t);
                }
            }
            yield return path[path.Length - 1];
        }
    }
}
