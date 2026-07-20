using System;
using System.Collections.Generic;
using Bots.VibeGrinder;
using Bots.VibeGrinder.Data;
using Bots.VibeGrinder.Selection;
using Bots.Vibes.Shared;
using Styx;
using Styx.Logic.Pathing;
using Styx.WoWInternals.WoWObjects;
using Bots.Vibes.Shared.QuestData;

namespace Bots.Vibes.VibeQuester2.Planning
{
    /// <summary>
    /// Selection-time danger gating for quests — the grinder's math applied to quest geometry, all
    /// against GrindMobs.db (no live OM). A quest is rejected when the work it requires sits in
    /// lethal country. Principle carried verbatim from VibeGrinder: Dangerous is NEVER relaxed
    /// (lethality), only inconvenience is. Rejections re-check naturally on level-up (every gate is
    /// level-relative). Knobs are shared with VibeGrinder (one tuning surface).
    /// </summary>
    public class DangerScreen
    {
        private static VibeGrinderSettings S => VibeGrinderSettings.Instance;

        // The SAME knob VibeGrinder groups spawns by — reference it, don't duplicate the literal (the
        // class header's "one tuning surface" promise was false while this was a hardcoded 90f).
        private static float ClusterRadius => S.GrindRadius;
        private const int MaxClustersPerObjective = 4;   // bound the DB queries per quest

        /// <summary>Null = doable; else a short reject reason for the [VQ2-Plan] log.</summary>
        public string Reject(QuestEntry q, QuestDatabase db, LocalPlayer me, FactionResolver factions)
        {
            if (factions == null || !GrindMobsRepository.IsAvailable)
                return null;   // no faction/DB context yet — fail open, the runtime backstop still guards

            uint map = me.MapId;
            int level = me.Level;

            // 1. Giver/ender ground: enemy territory or an over-level zone around the interaction NPC
            //    (the VendorLocationSafe gates applied to quest NPCs — same failure geometry).
            foreach (QuestGiverEntry g in db.QuestGivers)
            {
                if (g.QuestId != q.Id) continue;
                string reason = ScreenInteractionPoint(g.GiverId, db, me, factions, "giver");
                if (reason != null) return reason;
            }
            foreach (QuestEnderEntry e in db.QuestEnders)
            {
                if (e.QuestId != q.Id) continue;
                string reason = ScreenInteractionPoint(e.EnderId, db, me, factions, "ender");
                if (reason != null) return reason;
            }

            // 2. Objective work areas: cluster the objective spawns; every REQUIRED objective must have
            //    at least one non-Dangerous cluster to work in.
            foreach (QuestObjective obj in q.Objectives)
            {
                int entityId = obj.Type == ObjectiveType.CollectFromGameObject ? obj.GameObjectId : obj.MobId;
                if (entityId <= 0) continue;   // sourceless collect — pickup+turn-in only, no work area
                var spawns = SpawnsOnMap(entityId, db, (int)map,
                    obj.Type == ObjectiveType.CollectFromGameObject);
                if (spawns.Count == 0) continue;   // resolvability is the noKill gate's job, not danger's

                // Nearest-first so the MaxClustersPerObjective cap keeps the clusters the player would
                // actually work in — not whatever order the DB returned rows (a safe near cluster could be
                // #5 and never examined, wrongly rejecting a workable quest).
                WoWPoint here = me.Location;
                spawns.Sort((a, b) => here.Distance2D(a).CompareTo(here.Distance2D(b)));
                var clusters = GreedyCluster(spawns);
                bool anySafe = false;
                string worst = null;
                foreach (var cluster in clusters)
                {
                    string danger = ClusterDanger(cluster, map, level, factions);
                    if (danger == null) { anySafe = true; break; }
                    worst = danger;
                }
                if (!anySafe)
                    return string.Format("obj{0} {1}: {2}", obj.Index, obj.Type, worst ?? "no workable cluster");
            }

            return null;
        }

        private string ScreenInteractionPoint(int entityId, QuestDatabase db, LocalPlayer me,
                                              FactionResolver factions, string kind)
        {
            // Screen every same-map spawn of the interaction NPC/GO; ANY safe spawn is enough
            // (multi-spawn givers: we go to the safe one — the planner emits nearest anyway).
            List<SpawnPoint> spawns = null;
            if (!db.CreatureSpawns.TryGetValue(entityId.ToString(), out spawns))
                db.GameObjectSpawns.TryGetValue(entityId.ToString(), out spawns);
            if (spawns == null) return null;

            string lastReason = null;
            foreach (SpawnPoint sp in spawns)
            {
                if (sp.Map != (int)me.MapId) continue;
                var p = new WoWPoint((float)sp.X, (float)sp.Y, (float)sp.Z);

                if (S.VendorHostileThreshold > 0)
                {
                    int hostiles = GrindMobsRepository.HostileSpawnCountNear(me.MapId, p, S.VendorHostileRadius, factions);
                    if (hostiles >= S.VendorHostileThreshold)
                    {
                        lastReason = string.Format("{0} in enemy territory ({1} hostile spawns)", kind, hostiles);
                        continue;
                    }
                }
                if (S.VendorAreaLevelMargin > 0)
                {
                    float areaLevel = GrindMobsRepository.AverageAttackableLevelNear(
                        me.MapId, p, S.VendorAreaScanRadius, factions, SpotSelector.ImmuneUnitFlagMask);
                    if (areaLevel > me.Level + S.VendorAreaLevelMargin)
                    {
                        lastReason = string.Format("{0} in over-level zone (avg {1:F0})", kind, areaLevel);
                        continue;
                    }
                }
                return null;   // this spawn is fine — quest is reachable
            }
            return lastReason;   // null when there were no same-map spawns (giverFar's job, not danger's)
        }

        private static List<WoWPoint> SpawnsOnMap(int entityId, QuestDatabase db, int map, bool gameObject)
        {
            var result = new List<WoWPoint>();
            var source = gameObject ? db.GameObjectSpawns : db.CreatureSpawns;
            if (source.TryGetValue(entityId.ToString(), out List<SpawnPoint> spawns))
                foreach (SpawnPoint sp in spawns)
                    if (sp.Map == map)
                        result.Add(new WoWPoint((float)sp.X, (float)sp.Y, (float)sp.Z));
            return result;
        }

        private static List<List<WoWPoint>> GreedyCluster(List<WoWPoint> points)
        {
            var clusters = new List<List<WoWPoint>>();
            var used = new bool[points.Count];
            for (int i = 0; i < points.Count && clusters.Count < MaxClustersPerObjective; i++)
            {
                if (used[i]) continue;
                var cluster = new List<WoWPoint> { points[i] };
                used[i] = true;
                for (int j = i + 1; j < points.Count; j++)
                {
                    if (used[j]) continue;
                    if (points[i].Distance2D(points[j]) <= ClusterRadius)
                    {
                        cluster.Add(points[j]);
                        used[j] = true;
                    }
                }
                clusters.Add(cluster);
            }
            return clusters;
        }

        /// <summary>
        /// Null = workable. Dangerous when (a) an over-level HOSTILE sits within body-pull reach of a
        /// work point (OverlevelHostileInAggro semantics — it aggros on every loot/roam walk and can't
        /// be cleared), or (b) a bubble-knot: ≥ SpotBubbleDangerCount hostile aggro bubbles cover one
        /// work point (the swarm/camp signature the falloff sum structurally misses).
        /// </summary>
        private static string ClusterDanger(List<WoWPoint> cluster, uint map, int level, FactionResolver factions)
        {
            // One box query around the cluster centroid covers all member checks.
            float cx = 0, cy = 0, cz = 0;
            foreach (var p in cluster) { cx += p.X; cy += p.Y; cz += p.Z; }
            var centroid = new WoWPoint(cx / cluster.Count, cy / cluster.Count, cz / cluster.Count);
            float span = 0;
            foreach (var p in cluster) span = Math.Max(span, (float)centroid.Distance2D(p));

            List<MobSpawn> hostiles = GrindMobsRepository.QueryHostileSpawnsNear(
                map, centroid, span + 60f, factions, 1);
            if (hostiles.Count == 0) return null;

            foreach (var p in cluster)
            {
                int bubbles = 0;
                foreach (MobSpawn h in hostiles)
                {
                    float aggro = TrekSafety.ServerAggroRadius(h.MaxLevel, level);
                    double d = h.Point.Distance2D(p);
                    if (h.MaxLevel > level + S.DangerLevelMargin && d <= aggro + S.AggroAvoidBuffer)
                        return string.Format("over-level hostile (L{0}) in aggro reach", h.MaxLevel);
                    if (d <= aggro + S.ExposurePad && Math.Abs(h.Point.Z - p.Z) < 5f)
                        bubbles++;
                }
                if (S.SpotBubbleDangerCount > 0 && bubbles >= S.SpotBubbleDangerCount)
                    return string.Format("bubble-knot ({0} overlapping aggro bubbles)", bubbles);
            }
            return null;
        }
    }
}
