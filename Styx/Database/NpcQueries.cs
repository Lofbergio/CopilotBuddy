#nullable disable
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

namespace Styx.Database
{
    /// <summary>
    /// Provides NPC lookup queries from the SQLite database.
    /// </summary>
    public static class NpcQueries
    {
        #region SQL Commands

        // SELECT * FROM npcs WHERE name = @name LIMIT 1
        private static SQLiteCommand _getNpcByNameCmd;

        // SELECT * FROM npcs WHERE entry = @entry LIMIT 1
        private static SQLiteCommand _getNpcByIdCmd;

        // SELECT * FROM npcs WHERE map = @map AND trainer_type = 0 AND trainer_class = @class
        // AND level >= @LEVEL ORDER BY VECTORDISTANCE(x, y, z, @x, @y, @z) ASC
        private static SQLiteCommand _getNearestTrainerCmd;

        // SELECT * FROM npcs WHERE map = @map AND (flag & @flags) != 0 
        // ORDER BY VECTORDISTANCE(x, y, z, @x, @y, @z) LIMIT 10
        private static SQLiteCommand _getNearestNpcCmd;

        private static bool _initialized = false;

        // The old per-entry CanNavigateFully cache is gone: both resolvers now measure real walk distance
        // with GeneratePath, which answers reachability as a side effect, and the result memo below covers
        // the cost the cache existed for. Its cautionary tale still applies to anything that caches a
        // reachability NEGATIVE — CanNavigateFully fails on partial paths from far/unloaded tiles, and a
        // permanently cached false hid the CLOSEST trainer for a whole session (bot ran 1300yd past Tuluun
        // to Firmanvaar, 2026-07-10).

        // data.bin `faction` is a faction-TEMPLATE id. WoWFaction.RelationTo reads Neutral for
        // everything here (gotchas.md: player side is template-less) — the DBC-correct check is
        // GetReactionTowards. Null template (odd/removed id) stays permissive like the old behavior.
        private static bool IsFactionUsable(uint factionTemplateId)
        {
            var tmpl = WoWFactionTemplate.FromId(factionTemplateId);
            var reaction = tmpl?.GetReactionTowards(StyxWoW.Me.FactionTemplate) ?? WoWUnitReaction.Neutral;
            return reaction >= WoWUnitReaction.Neutral;
        }

        #endregion

        #region Initialization

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // Access Instance to trigger initialization
            var conn = Connection.Instance;
            if (conn == null || !Connection.IsAvailable) return;

            // Initialize SQL commands - EXACT comme HB
            _getNpcByNameCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE name LIKE '%@NAME%' LIMIT 1");

            _getNpcByIdCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE entry = @ENTRY LIMIT 1");

            // trainer_type 0 = class trainer. Pet trainers (type 3) carry the SAME npcflags AND
            // trainer_class = Hunter (they serve hunters), so trainer_type is the only discriminator
            // — without it a hunter's nearest "class trainer" can resolve to a pet trainer.
            _getNearestTrainerCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE map = @MAP_ID AND trainer_type = 0 AND trainer_class = @TRAINER_CLASS AND level >= @LEVEL ORDER BY VECTORDISTANCE(x,y,z,@X,@Y,@Z) ASC");

            _getNearestNpcCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE map = @MAP_ID AND flag & @FLAG ORDER BY VECTORDISTANCE(x,y,z,@X,@Y,@Z) ASC LIMIT 25");
        }

        // The LIMIT is what forces the stock filter into SQL rather than a post-filter in C#: the nearest
        // 25 flag-carrying NPCs can easily contain zero that stock what we need (5 "AmmoVendor" General
        // Supplies NPCs in one town), and post-filtering would then report "no vendor" while a real one
        // sat 26th in line. Entries are ints straight from our own DB, so the inlined IN list can't inject.
        private static SQLiteDataReader OpenNearestNpcReader(uint mapId, UnitNPCFlags npcFlags,
                                                             WoWPoint searchLocation, HashSet<int> requireEntries)
        {
            if (requireEntries == null)
                return Connection.ExecuteReader(_getNearestNpcCmd,
                    mapId, (uint)npcFlags, searchLocation.X, searchLocation.Y, searchLocation.Z);

            if (requireEntries.Count == 0)
                return null;   // asked, and nobody stocks it — a real answer, not a missing one

            var sb = new System.Text.StringBuilder(
                "SELECT * FROM npcs WHERE map = @MAP_ID AND flag & @FLAG AND entry IN (");
            bool first = true;
            foreach (int e in requireEntries)
            {
                if (!first) sb.Append(',');
                sb.Append(e);
                first = false;
            }
            sb.Append(") ORDER BY VECTORDISTANCE(x,y,z,@X,@Y,@Z) ASC LIMIT 25");

            // NOT disposed here: SQLiteCommand.Dispose finalizes the statement the reader is reading from.
            SQLiteCommand cmd = Connection.CreateCommand(sb.ToString());
            if (cmd == null) return null;
            return Connection.ExecuteReader(cmd,
                mapId, (uint)npcFlags, searchLocation.X, searchLocation.Y, searchLocation.Z);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets an NPC by name.
        /// </summary>
        /// <param name="name">The name to search for.</param>
        /// <returns>The NPC result, or null if not found.</returns>
        public static NpcResult GetNpcByName(string name)
        {
            EnsureInitialized();
            if (_getNpcByNameCmd == null) return null;

            using var reader = Connection.ExecuteReader(_getNpcByNameCmd, name);
            if (reader != null && reader.Read())
            {
                return new NpcResult(reader);
            }
            return null;
        }

        /// <summary>
        /// Gets an NPC by entry ID.
        /// </summary>
        /// <param name="id">The entry ID.</param>
        /// <returns>The NPC result, or null if not found.</returns>
        public static NpcResult GetNpcById(uint id)
        {
            EnsureInitialized();
            if (_getNpcByIdCmd == null) return null;

            using var reader = Connection.ExecuteReader(_getNpcByIdCmd, id);
            if (reader != null && reader.Read())
            {
                return new NpcResult(reader);
            }
            return null;
        }

        /// <summary>
        /// Gets the nearest trainer of a specific class.
        /// </summary>
        /// <param name="myFaction">The player's faction.</param>
        /// <param name="mapId">The map ID.</param>
        /// <param name="searchLocation">The search location.</param>
        /// <param name="searchClass">The class to search for.</param>
        /// <returns>The nearest trainer, or null if not found.</returns>
        public static NpcResult GetNearestTrainer(WoWFaction myFaction, uint mapId, WoWPoint searchLocation, WoWClass searchClass, HashSet<int> excludeEntries = null)
        {
            EnsureInitialized();
            if (_getNearestTrainerCmd == null) return null;

            // Prefer a trainer whose NPC level >= ours: data.bin `level` tracks trainer tier
            // (starting-area 6, village 15, city 40-70), and tier-limited servers teach NOTHING at an
            // outgrown trainer (BuyAll buys 0 and the training silently "succeeds"). Fall back to the
            // old HB rule when the map has no such trainer (e.g. high-level chars, sparse maps).
            // Deliberately NOT subject to MaxVendorTravelYards. Class trainers live in cities, so any
            // cap tight enough to bound a restock trip (vendors are everywhere) would stop a levelling
            // character training at all — a worse failure than the walk it prevents. Trainers get the
            // shortest-real-walk pick; the distance itself is the cost of the errand.
            NpcResult result = QueryNearestTrainer(mapId, searchLocation, searchClass, (int)StyxWoW.Me.Level, excludeEntries);
            if (result == null)
                result = QueryNearestTrainer(mapId, searchLocation, searchClass, StyxWoW.Me.Level > 10 ? 10 : 0, excludeEntries);
            return result;
        }

        private static NpcResult QueryNearestTrainer(uint mapId, WoWPoint searchLocation, WoWClass searchClass, int minLevel,
                                                     HashSet<int> excludeEntries)
        {
            WoWPoint location = StyxWoW.Me.Location;

            using var reader = Connection.ExecuteReader(_getNearestTrainerCmd,
                mapId,
                (int)searchClass,
                minLevel,
                searchLocation.X,
                searchLocation.Y,
                searchLocation.Z);

            if (reader == null) return null;

            var viable = new List<NpcResult>();
            while (reader.Read())
            {
                NpcResult result = new NpcResult(reader);
                if (excludeEntries != null && excludeEntries.Contains(result.Entry))
                    continue; // blacklisted (e.g. couldn't be reached/interacted last time)
                if (!string.IsNullOrEmpty(result.Name) && result.Name.IndexOf("[DND]", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue; // [DND] placeholder/disabled NPC, not a real trainer
                if ((result.NpcFlags & 32U) != 0U && IsFactionUsable(result.Faction))
                {
                    viable.Add(result);
                    if (viable.Count >= WalkCandidates)
                        break;
                }
            }
            return NearestByWalk(viable, location, UnitNPCFlags.ClassTrainer, false, out _);
        }

        /// <summary>
        /// Gets the nearest NPC with specific flags.
        /// </summary>
        /// <param name="myFaction">The player's faction.</param>
        /// <param name="mapId">The map ID.</param>
        /// <param name="searchLocation">The search location.</param>
        /// <param name="npcFlags">The NPC flags to search for.</param>
        /// <returns>The nearest NPC, or null if not found.</returns>
        /// <param name="requireEntries">
        /// When non-null, only these entries qualify — the caller knows (from VendorStock) which NPCs
        /// actually stock what the errand needs. An npcflag is a role hint, not an inventory, so without
        /// this the resolver happily returns the nearest NPC that CAN'T serve the errand and the trip is
        /// wasted. Null = unconstrained (no stock data); an EMPTY set is a real answer meaning nobody
        /// qualifies, and must not be confused with null.
        /// </param>
        public static NpcResult GetNearestNpc(WoWFaction myFaction, uint mapId, WoWPoint searchLocation, UnitNPCFlags npcFlags, HashSet<int> excludeEntries = null, HashSet<int> requireEntries = null)
        {
            EnsureInitialized();
            if (_getNearestNpcCmd == null) return null;

            WoWClass myClass = StyxWoW.Me.Class;
            WoWPoint location = StyxWoW.Me.Location;

            // Memo sits ABOVE the trainer branch: that path measures walks too, and is asked just as
            // often. Level is part of the key because it decides which trainer tier qualifies and which
            // stock is usable — the one input here that changes on its own during a run.
            var memoKey = (mapId, npcFlags,
                           (int)(location.X / ResolveBucketYd), (int)(location.Y / ResolveBucketYd),
                           excludeEntries?.Count ?? 0, requireEntries?.Count ?? -1, (int)StyxWoW.Me.Level);
            if (_resolveMemo.TryGetValue(memoKey, out NpcResult memoised))
                return memoised;

            // If looking for class trainer, use specialized query
            if ((npcFlags & UnitNPCFlags.ClassTrainer) != UnitNPCFlags.None)
                return Memoise(memoKey, GetNearestTrainer(myFaction, mapId, searchLocation, myClass, excludeEntries));

            using var reader = OpenNearestNpcReader(mapId, npcFlags, searchLocation, requireEntries);

            if (reader == null)
                return Memoise(memoKey, null);

            var viable = new List<NpcResult>();

            while (reader.Read())
            {
                NpcResult result = new NpcResult(reader);

                if (excludeEntries != null && excludeEntries.Contains(result.Entry))
                    continue; // blacklisted (e.g. a bad/unspawned NPC we couldn't interact with last time)

                // [DND] = "Do Not Disturb" placeholder/disabled NPCs (e.g. tournament pedestals). They carry
                // junk npcflags and aren't spawned/interactable — never a real vendor.
                if (!string.IsNullOrEmpty(result.Name) && result.Name.IndexOf("[DND]", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                // Skip NPCs with invalid faction
                if (result.Faction == 0)
                    continue;
                
                // Check if class trainer matches our class (if applicable) and faction is friendly
                if (((npcFlags & UnitNPCFlags.ClassTrainer) == UnitNPCFlags.None || result.TrainerClass == (int)myClass) &&
                    IsFactionUsable(result.Faction))
                {
                    viable.Add(result);
                    if (viable.Count >= WalkCandidates)
                        break;
                }
            }
            return Memoise(memoKey, NearestByWalk(viable, location, npcFlags));
        }

        // How many straight-line-nearest candidates get their real walk measured. Straight line is a
        // cheap prefilter that LIES across canyons, water and cliffs, so the first one is not reliably
        // the closest — but each measurement is a full Detour pathfind, so this is deliberately small.
        private const int WalkCandidates = 5;

        // Resolve memo. NeedToBuy asks for the nearest vendor EVERY tick while a need is open, and each
        // ask is up to WalkCandidates pathfinds — unmemoised that is the same per-tick pathfind storm the
        // old nav cache existed to prevent. This caches the DERIVED ANSWER, not a record of failures: the
        // key is every input that can change it, so a stale answer is not representable. Position is
        // bucketed because the answer cannot meaningfully change while we stand still and fight.
        private const float ResolveBucketYd = 50f;
        private const int ResolveMemoMaxEntries = 512;
        private static readonly Dictionary<(uint map, UnitNPCFlags flags, int bx, int by, int excl, int req, int level), NpcResult> _resolveMemo =
            new Dictionary<(uint, UnitNPCFlags, int, int, int, int, int), NpcResult>();

        private static NpcResult Memoise(
            (uint, UnitNPCFlags, int, int, int, int, int) key, NpcResult result)
        {
            // An overnight run crosses a lot of buckets; this is a speed cache, not a record worth
            // keeping, so drop it wholesale rather than growing without bound.
            if (_resolveMemo.Count > ResolveMemoMaxEntries)
                _resolveMemo.Clear();
            _resolveMemo[key] = result;
            return result;
        }

        /// <summary>
        /// Picks the candidate with the shortest ACTUAL walk, rejecting anything past the travel budget.
        /// One GeneratePath per candidate answers reachability and distance together — asking
        /// CanNavigateFully first would pathfind twice for the same answer.
        ///
        /// The budget is what stops a restock becoming an expedition: with it, "no vendor within reach"
        /// is a real, loggable outcome and the bot keeps grinding instead of walking across the zone.
        /// </summary>
        private static NpcResult NearestByWalk(List<NpcResult> candidates, WoWPoint from, UnitNPCFlags npcFlags)
        {
            return NearestByWalk(candidates, from, npcFlags, true, out _);
        }

        /// <param name="applyBudget">
        /// False defers the cap to the caller. The trainer path needs that: it resolves in two passes
        /// (level-appropriate tier, then any tier), and rejecting an over-budget GOOD trainer inside
        /// pass 1 would hand the job to pass 2, which happily returns a nearby OUTGROWN trainer that
        /// teaches nothing and reports success. The cap belongs on the final pick, not on each pass.
        /// </param>
        /// <param name="walkYd">Walk distance of the returned candidate, so a deferred budget check
        /// doesn't have to pathfind all over again.</param>
        private static NpcResult NearestByWalk(List<NpcResult> candidates, WoWPoint from, UnitNPCFlags npcFlags,
                                               bool applyBudget, out float walkYd)
        {
            walkYd = 0f;
            float budget = applyBudget ? CharacterSettings.Instance.MaxVendorTravelYards : 0f;
            NpcResult best = null;
            float bestWalk = float.MaxValue;
            NpcResult nearestOverBudget = null;
            float nearestOverBudgetWalk = 0f;

            foreach (NpcResult c in candidates)
            {
                WoWPoint[] path = Navigator.GeneratePath(from, c.Location);
                if (path == null || path.Length == 0)
                    continue;   // unreachable
                // A path that stops well short didn't reach the NPC — Detour returning its best effort.
                if (path[path.Length - 1].Distance(c.Location) > PartialPathToleranceYd)
                    continue;

                float walk = 0f;
                for (int i = 1; i < path.Length; i++)
                    walk += (float)path[i - 1].Distance(path[i]);

                if (budget > 0 && walk > budget)
                {
                    if (nearestOverBudget == null || walk < nearestOverBudgetWalk)
                    {
                        nearestOverBudget = c;
                        nearestOverBudgetWalk = walk;
                    }
                    continue;
                }
                if (walk < bestWalk)
                {
                    bestWalk = walk;
                    best = c;
                }
            }

            // Silence here would read as "there are no vendors", which is a different problem with a
            // different fix. Say which NPC we declined and how far it was.
            if (best == null && nearestOverBudget != null)
                Logging.Write(System.Drawing.Color.Orange,
                    "[Vendors] nearest {0} is {1} at {2:F0}yd walk — past the {3:F0}yd travel budget, skipping the trip.",
                    npcFlags, nearestOverBudget.Name, nearestOverBudgetWalk, budget);

            walkYd = best != null ? bestWalk : 0f;
            return best;
        }

        // Detour returns its best effort rather than failing, so a path ending this far from the target
        // is a partial path, not an arrival. Same tolerance the Vibes errand screener uses.
        private const float PartialPathToleranceYd = 25f;

        /// <summary>
        /// Nearest NPCs carrying ANY of <paramref name="npcFlags"/>, nearest-first, with their raw
        /// npcflag mask intact — so a caller planning several errands can see that one NPC covers two
        /// of them. <see cref="GetNearestNpc"/> answers "the closest NPC for THIS errand" and is the
        /// wrong shape for that: asking it once per errand yields one NPC per errand and no way to
        /// notice they are the same NPC, which is how a bot ends up walking a town twice.
        ///
        /// Reachability is deliberately NOT checked here. CanNavigateFully is a full Detour pathfind;
        /// running it across a whole candidate list is a cost the caller may not want, and a caller
        /// that validates the stops it actually picks keeps ONE reachability authority instead of two.
        /// </summary>
        public static List<NpcResult> GetNearbyNpcs(uint mapId, WoWPoint searchLocation, UnitNPCFlags npcFlags,
                                                    int limit, HashSet<int> excludeEntries = null,
                                                    HashSet<int> requireEntries = null)
        {
            var results = new List<NpcResult>();
            EnsureInitialized();
            if (_getNearestNpcCmd == null || limit <= 0) return results;

            using var reader = OpenNearestNpcReader(mapId, npcFlags, searchLocation, requireEntries);
            if (reader == null) return results;

            while (reader.Read() && results.Count < limit)
            {
                NpcResult result = new NpcResult(reader);
                if (excludeEntries != null && excludeEntries.Contains(result.Entry))
                    continue;
                // [DND] placeholders carry junk npcflags and aren't interactable — see GetNearestNpc.
                if (!string.IsNullOrEmpty(result.Name) && result.Name.IndexOf("[DND]", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (result.Faction == 0 || !IsFactionUsable(result.Faction))
                    continue;
                results.Add(result);
            }
            return results;
        }

        #endregion
    }
}
