using System;
using System.Collections.Generic;
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
        /// Danger of the route to a spot. Densely samples the generated path and accumulates threat
        /// from hazards within CorridorRadius of the line. A guard pack on the path ⇒ +∞ (Dangerous).
        /// Returns 0 when no path (reachability is gated separately by the caller).
        /// </summary>
        public static float PathDanger(WoWPoint from, WoWPoint centroid, uint mapId, int playerLevel)
        {
            WoWPoint[] path = Navigator.GeneratePath(from, centroid);
            if (path == null || path.Length == 0)
                return 0f;

            // One DB pull covering the straight-line region, then in-memory checks per sample.
            WoWPoint mid = new WoWPoint((from.X + centroid.X) / 2f, (from.Y + centroid.Y) / 2f, (from.Z + centroid.Z) / 2f);
            float span = from.Distance2D(centroid) / 2f + S.CorridorRadius;
            List<MobSpawn> hazards = GrindMobsRepository.QueryHazardsNear(
                mapId, mid, span, playerLevel, S.DangerLevelMargin);
            if (hazards.Count == 0)
                return 0f;

            if (HasGuardPack(hazards))
                return float.PositiveInfinity;

            float worst = 0f;
            foreach (WoWPoint sample in Densify(path, 20f))
            {
                float local = AccumulateThreat(sample, hazards, S.CorridorRadius);
                if (local > worst)
                    worst = local;
            }
            return worst;
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
                float dist = center.Distance2D(h.Point);
                if (dist >= r)
                    continue;
                total += HazardWeight(h.Rank) * (1f - dist / r);
            }
            return total;
        }

        private static bool HasGuardPack(List<MobSpawn> hazards)
        {
            int need = S.ElitePackCount;
            float r2 = S.ElitePackRadius * S.ElitePackRadius;
            for (int i = 0; i < hazards.Count; i++)
            {
                int near = 1; // self
                for (int j = 0; j < hazards.Count; j++)
                {
                    if (i == j) continue;
                    if (hazards[i].Point.DistanceSqr(hazards[j].Point) <= r2)
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
                int steps = segLen <= stepYards ? 1 : (int)(segLen / stepYards);
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
