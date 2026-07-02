using System;
using System.Collections.Generic;
using Bots.VibeGrinder.Data;
using Bots.VibeGrinder.Selection;
using Styx;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;

namespace Bots.VibeGrinder.Supervision
{
    /// <summary>
    /// Trek safety — threat-aware routing for EVERY travel leg, not just grind-spot selection (which has its
    /// own gauntlet checks). THE STORY (user, 2026-07-02): from wherever we are, we decide to trek somewhere
    /// (next spot, vendor, anywhere); we build a route; en route we only ever choose fights we can win
    /// (level-band mobs that would aggro us anyway), and we treat RED/elite/pack hostiles as terrain — they
    /// aggro from far beyond pull range and can never be beaten, so the route itself must bend around their
    /// aggro bubbles. Worst case is a level-5 with no mount crossing a world of 45yd-aggro mobs.
    ///
    /// Implementation: at leg start, sample the generated path, pull hostile spawns along the corridor from
    /// GrindMobs.db, classify RED (level ≥ ours + RedLevelMargin, or elite rank) and PACK KNOTS (≥ PackKnotCount
    /// hostiles of meaningful level within PackKnotRadius), and mark each as a BlackspotManager circle sized to
    /// its SERVER aggro radius + pad. Blackspots are soft (60× path cost): the navigator prefers around, but a
    /// genuinely unavoidable gauntlet still paths through the cheapest line rather than wedging. Marks are
    /// replaced per leg and cleared on Stop.
    ///
    /// The aggro radius is the SERVER formula (AzerothCore Creature::GetAggroRange, verified from the local
    /// server source): base 20yd at equal level, +1yd/level the mob is above us (cap 45), −1/level below
    /// (floor 5). PullDistance is a CASTING concept and plays no role in hazard avoidance.
    ///
    /// Deliberate scope (v1): static DB spawns only — roaming patrols and live-OM reds are not tracked
    /// (the mount evade-classifier covers reacting to those); corpse-run legs are LevelBot-core and unmarked.
    /// </summary>
    public static class TrekSafety
    {
        private const int RedLevelMargin = 3;        // mob ≥ us+3 (orange/red) = unbeatable-by-policy → hazard
        private const int PackKnotCount = 4;         // ≥ this many meaningful hostiles clustered = a pack hazard
        private const float PackKnotRadius = 12f;    // cluster radius for the knot test (≈ overlapping aggro)
        private const int PackLevelFloor = 4;        // knot members must be ≥ us-4 (greys can't hurt us)
        private const float HazardPad = 8f;          // walk THIS far outside the server aggro bubble
        private const float CorridorRadius = 15f;    // hazards farther than (bubble+this) from the path are ignored
        private const float HazardHeight = 30f;      // blackspot vertical extent (slopes)
        private const int MaxHazardMarks = 60;       // bound the navmesh marking cost per leg

        private static readonly List<Blackspot> _marks = new List<Blackspot>();

        /// <summary>Server aggro radius (AzerothCore formula) for a DB spawn against our level.</summary>
        public static float ServerAggroRadius(int mobLevel, int myLevel)
            => Math.Clamp(20f + Math.Min(mobLevel - myLevel, 25), 5f, 45f);

        /// <summary>
        /// Mark red/pack hazards along the from→to corridor as navmesh cost circles. Replaces previous marks.
        /// Call at the START of a leg (spot install, vendor latch-on, return leg) — cheap enough per leg:
        /// one DB box query + one GeneratePath (usually already computed by the caller's flow).
        /// </summary>
        public static void MarkLeg(FactionResolver factions, WoWPoint from, WoWPoint to, int myLevel, uint mapId, string legName)
        {
            Clear();
            if (factions == null || !GrindMobsRepository.IsAvailable) return;

            float legDist = from.Distance2D(to);
            if (legDist < 40f) return;   // trivial hop — nothing to plan

            // Corridor samples: the actual nav path when available, else the straight segment.
            WoWPoint[] path = Navigator.GeneratePath(from, to);
            if (path == null || path.Length == 0)
                path = new[] { from, to };

            // One box query around the leg, then classify.
            WoWPoint mid = new WoWPoint((from.X + to.X) / 2f, (from.Y + to.Y) / 2f, (from.Z + to.Z) / 2f);
            float half = legDist / 2f + 60f;
            List<MobSpawn> hostiles = GrindMobsRepository.QueryHostileSpawnsNear(mapId, mid, half, factions, myLevel - PackLevelFloor);
            if (hostiles.Count == 0) return;

            var hazards = new List<Blackspot>();
            var inKnots = new HashSet<int>();

            // PACK KNOTS first (greedy cluster, small n): ≥PackKnotCount meaningful hostiles within PackKnotRadius.
            // Even at-level mobs are lethal in fours — transit must not thread a camp (destination-threat logic
            // deliberately scores those ~0 because they're the grind; TRANSIT-threat must not).
            for (int i = 0; i < hostiles.Count; i++)
            {
                if (inKnots.Contains(i)) continue;
                var members = new List<int> { i };
                for (int j = i + 1; j < hostiles.Count; j++)
                {
                    if (inKnots.Contains(j)) continue;
                    if (hostiles[i].Point.Distance2D(hostiles[j].Point) <= PackKnotRadius) members.Add(j);
                }
                if (members.Count < PackKnotCount) continue;
                float cx = 0, cy = 0, cz = 0; int maxLvl = 0;
                foreach (int m in members)
                {
                    cx += hostiles[m].Point.X; cy += hostiles[m].Point.Y; cz += hostiles[m].Point.Z;
                    if (hostiles[m].MaxLevel > maxLvl) maxLvl = hostiles[m].MaxLevel;
                    inKnots.Add(m);
                }
                var knotCenter = new WoWPoint(cx / members.Count, cy / members.Count, cz / members.Count);
                hazards.Add(new Blackspot(knotCenter, ServerAggroRadius(maxLvl, myLevel) + HazardPad, HazardHeight));
            }

            // REDS/elites: individually unbeatable → individual bubbles. Knot members are already covered.
            for (int i = 0; i < hostiles.Count; i++)
            {
                if (inKnots.Contains(i)) continue;
                var s = hostiles[i];
                bool red = s.MaxLevel >= myLevel + RedLevelMargin;
                bool elite = s.Rank == 1 || s.Rank == 2 || s.Rank == 3;   // elite/rare-elite/boss (plain rare 4 excluded)
                if (!red && !elite) continue;
                hazards.Add(new Blackspot(s.Point, ServerAggroRadius(s.MaxLevel, myLevel) + HazardPad, HazardHeight));
            }
            if (hazards.Count == 0) return;

            // Keep only hazards whose bubble actually threatens the corridor, nearest-to-path first, bounded.
            var relevant = new List<(Blackspot spot, float minDist)>();
            foreach (var h in hazards)
            {
                float best = float.MaxValue;
                foreach (var p in path)
                {
                    float d = h.Location.Distance2D(p);
                    if (d < best) best = d;
                }
                if (best <= h.Radius + CorridorRadius) relevant.Add((h, best));
            }
            if (relevant.Count == 0) return;
            relevant.Sort((a, b) => a.minDist.CompareTo(b.minDist));
            if (relevant.Count > MaxHazardMarks) relevant.RemoveRange(MaxHazardMarks, relevant.Count - MaxHazardMarks);

            foreach (var (spot, _) in relevant) _marks.Add(spot);
            BlackspotManager.AddBlackspots(_marks);
            Logging.Write(System.Drawing.Color.Khaki,
                "[TrekSafety] {0}: marked {1} hazard bubble(s) along the {2:F0}yd leg ({3} candidates) — routing around reds/packs.",
                legName, _marks.Count, legDist, hazards.Count);
        }

        /// <summary>Remove this leg's hazard marks (restores the navmesh polys).</summary>
        public static void Clear()
        {
            if (_marks.Count == 0) return;
            BlackspotManager.RemoveBlackspots(_marks);
            _marks.Clear();
        }
    }
}
