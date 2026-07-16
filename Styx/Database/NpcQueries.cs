#nullable disable
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Styx.Combat.CombatRoutine;
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

        // Navigation caches - same as HB 4.3.4 dictionary_0 / dictionary_1
        // Keyed by NPC entry, NOT NpcResult: NpcResult is a class with no Equals/GetHashCode, so a
        // Dictionary<NpcResult,bool> used reference equality — every query builds fresh NpcResult objects,
        // so the cache NEVER hit and CanNavigateFully (a full Detour pathfind) ran for every candidate on
        // every call (×4 vendor types per roam tick → severe FPS drop while roaming). Entry-keyed = real cache.
        // Positives are stable (mesh connectivity), but negatives EXPIRE: CanNavigateFully fails on
        // partial paths, which happen legitimately from far/unloaded tiles — a permanently cached false
        // hid the CLOSEST trainer for the whole session (bot ran 1300yd past Tuluun to Firmanvaar, 2026-07-10).
        private static readonly Dictionary<int, (bool ok, DateTime when)> _trainerNavCache = new Dictionary<int, (bool, DateTime)>();
        private static readonly Dictionary<int, (bool ok, DateTime when)> _npcNavCache = new Dictionary<int, (bool, DateTime)>();
        private static readonly TimeSpan NavNegativeTtl = TimeSpan.FromMinutes(3);

        private static bool CanNavigateCached(Dictionary<int, (bool ok, DateTime when)> cache, int entry, WoWPoint from, WoWPoint to)
        {
            if (cache.TryGetValue(entry, out var c) && (c.ok || DateTime.UtcNow - c.when < NavNegativeTtl))
                return c.ok;
            bool canNav = Navigator.CanNavigateFully(from, to);
            cache[entry] = (canNav, DateTime.UtcNow);
            return canNav;
        }

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
            NpcResult result = QueryNearestTrainer(mapId, searchLocation, searchClass, (int)StyxWoW.Me.Level, excludeEntries);
            if (result == null)
                result = QueryNearestTrainer(mapId, searchLocation, searchClass, StyxWoW.Me.Level > 10 ? 10 : 0, excludeEntries);
            return result;
        }

        private static NpcResult QueryNearestTrainer(uint mapId, WoWPoint searchLocation, WoWClass searchClass, int minLevel, HashSet<int> excludeEntries)
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

            while (reader.Read())
            {
                NpcResult result = new NpcResult(reader);
                if (excludeEntries != null && excludeEntries.Contains(result.Entry))
                    continue; // blacklisted (e.g. couldn't be reached/interacted last time)
                if (!string.IsNullOrEmpty(result.Name) && result.Name.IndexOf("[DND]", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue; // [DND] placeholder/disabled NPC, not a real trainer
                if ((result.NpcFlags & 32U) != 0U && IsFactionUsable(result.Faction))
                {
                    if (!CanNavigateCached(_trainerNavCache, result.Entry, location, result.Location))
                        continue;
                    return result;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the nearest NPC with specific flags.
        /// </summary>
        /// <param name="myFaction">The player's faction.</param>
        /// <param name="mapId">The map ID.</param>
        /// <param name="searchLocation">The search location.</param>
        /// <param name="npcFlags">The NPC flags to search for.</param>
        /// <returns>The nearest NPC, or null if not found.</returns>
        public static NpcResult GetNearestNpc(WoWFaction myFaction, uint mapId, WoWPoint searchLocation, UnitNPCFlags npcFlags, HashSet<int> excludeEntries = null)
        {
            EnsureInitialized();
            if (_getNearestNpcCmd == null) return null;

            WoWClass myClass = StyxWoW.Me.Class;

            // If looking for class trainer, use specialized query
            if ((npcFlags & UnitNPCFlags.ClassTrainer) != UnitNPCFlags.None)
            {
                return GetNearestTrainer(myFaction, mapId, searchLocation, myClass, excludeEntries);
            }

            using var reader = Connection.ExecuteReader(_getNearestNpcCmd,
                mapId,
                (uint)npcFlags,
                searchLocation.X,
                searchLocation.Y,
                searchLocation.Z);

            if (reader == null) return null;

            WoWPoint location = StyxWoW.Me.Location;

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
                    if (!CanNavigateCached(_npcNavCache, result.Entry, location, result.Location))
                        continue;
                    return result;
                }
            }
            return null;
        }

        #endregion
    }
}
