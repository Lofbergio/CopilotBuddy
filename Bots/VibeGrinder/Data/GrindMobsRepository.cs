using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using Bots.VibeGrinder.Selection;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Bots.VibeGrinder.Data
{
    /// <summary>
    /// Read-only access to GrindMobs.db (creature metadata + spawn positions) for VibeGrinder
    /// spot selection. Mirrors Styx.Database.CreatureSpawnQueries (lazy ReadOnly connection,
    /// cached parameterized commands, false-safe when the DB is absent).
    /// </summary>
    public static class GrindMobsRepository
    {
        private static SQLiteConnection _connection;
        private static bool _initialized;
        private static bool _isAvailable;

        // Critters are never grind targets; 8 == CREATURE_TYPE_CRITTER (3.3.5a).
        private const int CritterType = 8;

        public static bool IsAvailable
        {
            get { EnsureInitialized(); return _isAvailable; }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var dbPath = Path.Combine(Logging.ApplicationPath, "GrindMobs.db");
                if (!File.Exists(dbPath))
                {
                    Logging.WriteDebug("[GrindMobs] Database not found: {0}", dbPath);
                    return;
                }

                var builder = new SQLiteConnectionStringBuilder { DataSource = dbPath, ReadOnly = true };
                _connection = new SQLiteConnection(builder.ConnectionString);
                _connection.Open();
                _isAvailable = true;
                Logging.Write("[GrindMobs] Database loaded successfully");
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[GrindMobs] Failed to load: {0}", ex.Message);
                _isAvailable = false;
            }
        }

        /// <summary>
        /// Eligible grind targets on a map: normal rank, level band intersects [lvlMin,lvlMax],
        /// non-critter, not a service NPC, not flagged immune/non-attackable. Faction is filtered
        /// in-memory against the attackable set (computed live by FactionResolver).
        /// </summary>
        public static List<MobSpawn> QueryEligibleSpawns(
            uint mapId, int lvlMin, int lvlMax, HashSet<int> attackableFactions, long immuneUnitFlagMask)
        {
            EnsureInitialized();
            var result = new List<MobSpawn>();
            if (!_isAvailable) return result;

            // Band overlap: mob[min,max] intersects [lvlMin,lvlMax]  ==  min<=lvlMax AND max>=lvlMin.
            const string sql = @"
SELECT s.x, s.y, s.z, m.entry, m.max_level, m.rank, m.faction
FROM spawns s JOIN mobs m ON m.entry = s.entry
WHERE s.map_id = @map
  AND m.rank = 0
  AND m.min_level <= @lvlMax
  AND m.max_level >= @lvlMin
  AND m.type <> @critter
  AND m.npcflag = 0
  AND (m.unit_flags & @immune) = 0";

            try
            {
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                cmd.Parameters.AddWithValue("@lvlMax", lvlMax);
                cmd.Parameters.AddWithValue("@lvlMin", lvlMin);
                cmd.Parameters.AddWithValue("@critter", CritterType);
                cmd.Parameters.AddWithValue("@immune", immuneUnitFlagMask);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int faction = reader.GetInt32(6);
                    if (attackableFactions != null && !attackableFactions.Contains(faction))
                        continue;

                    result.Add(new MobSpawn(
                        (uint)reader.GetInt32(3),
                        new WoWPoint(reader.GetFloat(0), reader.GetFloat(1), reader.GetFloat(2)),
                        reader.GetInt32(4),
                        reader.GetInt32(5),
                        faction));
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[GrindMobs] QueryEligibleSpawns error: {0}", ex.Message);
            }
            return result;
        }

        /// <summary>
        /// Total spawn count (all creatures, unfiltered) within a bounding box — the denominator
        /// for a spot's purity score. Bounding box is cheap and good enough for purity.
        /// </summary>
        public static int CountSpawnsNear(uint mapId, WoWPoint center, float radius)
        {
            EnsureInitialized();
            if (!_isAvailable) return 0;

            const string sql = @"
SELECT COUNT(*) FROM spawns
WHERE map_id = @map AND x BETWEEN @minX AND @maxX AND y BETWEEN @minY AND @maxY";
            try
            {
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                cmd.Parameters.AddWithValue("@minX", center.X - radius);
                cmd.Parameters.AddWithValue("@maxX", center.X + radius);
                cmd.Parameters.AddWithValue("@minY", center.Y - radius);
                cmd.Parameters.AddWithValue("@maxY", center.Y + radius);
                object scalar = cmd.ExecuteScalar();
                return scalar == null ? 0 : Convert.ToInt32(scalar);
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[GrindMobs] CountSpawnsNear error: {0}", ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Hazard spawns within a bounding box: elites/rares (rank &gt;= 1) OR mobs above the
        /// player's safe level (max_level &gt; level + dangerMargin). Used by DangerEvaluator.
        /// </summary>
        public static List<MobSpawn> QueryHazardsNear(
            uint mapId, WoWPoint center, float radius, int level, int dangerMargin)
        {
            EnsureInitialized();
            var result = new List<MobSpawn>();
            if (!_isAvailable) return result;

            const string sql = @"
SELECT s.x, s.y, s.z, m.entry, m.max_level, m.rank, m.faction
FROM spawns s JOIN mobs m ON m.entry = s.entry
WHERE s.map_id = @map
  AND s.x BETWEEN @minX AND @maxX AND s.y BETWEEN @minY AND @maxY
  AND (m.rank >= 1 OR m.max_level > @dangerLevel)";
            try
            {
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                cmd.Parameters.AddWithValue("@minX", center.X - radius);
                cmd.Parameters.AddWithValue("@maxX", center.X + radius);
                cmd.Parameters.AddWithValue("@minY", center.Y - radius);
                cmd.Parameters.AddWithValue("@maxY", center.Y + radius);
                cmd.Parameters.AddWithValue("@dangerLevel", level + dangerMargin);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new MobSpawn(
                        (uint)reader.GetInt32(3),
                        new WoWPoint(reader.GetFloat(0), reader.GetFloat(1), reader.GetFloat(2)),
                        reader.GetInt32(4),
                        reader.GetInt32(5),
                        reader.GetInt32(6)));
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[GrindMobs] QueryHazardsNear error: {0}", ex.Message);
            }
            return result;
        }

        /// <summary>Distinct creature factions present on a map — seeds FactionResolver.</summary>
        public static List<int> DistinctFactionsOnMap(uint mapId)
        {
            EnsureInitialized();
            var result = new List<int>();
            if (!_isAvailable) return result;

            const string sql = @"
SELECT DISTINCT m.faction
FROM spawns s JOIN mobs m ON m.entry = s.entry
WHERE s.map_id = @map";
            try
            {
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    result.Add(reader.GetInt32(0));
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[GrindMobs] DistinctFactionsOnMap error: {0}", ex.Message);
            }
            return result;
        }
    }
}
