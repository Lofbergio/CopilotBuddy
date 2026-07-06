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

        /// <summary>Close the shared connection so a later Start re-opens cleanly and no handle leaks
        /// across stop/restart. Call from VibeGrinder.Stop().</summary>
        public static void Shutdown()
        {
            if (_connection != null)
            {
                try { _connection.Close(); _connection.Dispose(); }
                catch { /* best-effort */ }
                _connection = null;
            }
            _initialized = false;
            _isAvailable = false;
        }

        /// <summary>
        /// Eligible grind targets on a map: normal rank, level band intersects [lvlMin,lvlMax],
        /// non-critter, not a service NPC, not flagged immune/non-attackable. Faction is filtered
        /// in-memory against the attackable set (computed live by FactionResolver).
        /// </summary>
        public static List<MobSpawn> QueryEligibleSpawns(
            uint mapId, int lvlMin, int lvlMax, FactionResolver factions, long immuneUnitFlagMask)
        {
            EnsureInitialized();
            var result = new List<MobSpawn>();
            if (!_isAvailable || factions == null) return result;

            // Band overlap: mob[min,max] intersects [lvlMin,lvlMax]  ==  min<=lvlMax AND max>=lvlMin.
            const string sql = @"
SELECT s.x, s.y, s.z, m.entry, m.max_level, m.rank, m.faction, m.type
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
                    int type = reader.GetInt32(7);
                    // Two-tier safety lives in FactionResolver (hostile any type; neutral non-humanoid only).
                    if (!factions.IsAttackable(faction, type))
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
        /// Template unit_flags for one entry (-1 = unknown/DB unavailable). The DB carries the AUTHORED
        /// intent — the live spawn can drop flags the template has (Theramore Prisoner 23720 ships 0x8100
        /// but spawns 0x8000 live, log 2026-07-03), so live-flag checks miss what this catches.
        /// </summary>
        public static long GetTemplateUnitFlags(uint entry)
        {
            EnsureInitialized();
            if (!_isAvailable) return -1;
            try
            {
                using var cmd = new SQLiteCommand("SELECT unit_flags FROM mobs WHERE entry = @entry", _connection);
                cmd.Parameters.AddWithValue("@entry", (long)entry);
                object v = cmd.ExecuteScalar();
                return v == null || v == DBNull.Value ? -1 : Convert.ToInt64(v);
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[GrindMobs] GetTemplateUnitFlags error: {0}", ex.Message);
                return -1;
            }
        }

        /// <summary>
        /// Total spawn count (all creatures, unfiltered) within a bounding box — the denominator
        /// for a spot's purity score. Bounding box is cheap and good enough for purity.
        /// </summary>
        public static int CountSpawnsNear(uint mapId, WoWPoint center, float radius)
        {
            EnsureInitialized();
            if (!_isAvailable) return 0;

            // Circle + Z band, not just the box (audit 2026-07-05): the box corners reach r√2 out and the
            // query was Z-blind — a cave 40yd under a mesa deflated purity with mobs that can't be seen
            // or reached (numerator is a true circle; both sides must measure the same region). The
            // BETWEEN pair stays as the index prefilter; the exact clauses trim inside it.
            const string sql = @"
SELECT COUNT(*) FROM spawns
WHERE map_id = @map AND x BETWEEN @minX AND @maxX AND y BETWEEN @minY AND @maxY
  AND ((x - @cx) * (x - @cx) + (y - @cy) * (y - @cy)) <= @r2
  AND ABS(z - @cz) <= @zBand";
            try
            {
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                cmd.Parameters.AddWithValue("@minX", center.X - radius);
                cmd.Parameters.AddWithValue("@maxX", center.X + radius);
                cmd.Parameters.AddWithValue("@minY", center.Y - radius);
                cmd.Parameters.AddWithValue("@maxY", center.Y + radius);
                cmd.Parameters.AddWithValue("@cx", center.X);
                cmd.Parameters.AddWithValue("@cy", center.Y);
                cmd.Parameters.AddWithValue("@cz", center.Z);
                cmd.Parameters.AddWithValue("@r2", radius * radius);
                cmd.Parameters.AddWithValue("@zBand", VibeGrinderSettings.Instance.SpotQueryZBand);
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
        /// Population-weighted average max_level of ATTACKABLE mobs in a box around a centroid,
        /// ignoring the grind band — so a couple of band-edge anchors surrounded by gray lowbies
        /// produce a low average and the spot scores down. Returns 0 if none / DB absent.
        /// </summary>
        public static float AverageAttackableLevelNear(uint mapId, WoWPoint center, float radius,
            FactionResolver factions, long immuneUnitFlagMask)
        {
            EnsureInitialized();
            if (!_isAvailable || factions == null) return 0f;

            // Same circle + Z-band trim as CountSpawnsNear (audit 2026-07-05) — the level average was
            // contaminated by vertically-separated content (cave under a mesa).
            const string sql = @"
SELECT m.max_level, m.faction, m.type, COUNT(*) AS cnt
FROM spawns s JOIN mobs m ON m.entry = s.entry
WHERE s.map_id = @map
  AND s.x BETWEEN @minX AND @maxX
  AND s.y BETWEEN @minY AND @maxY
  AND ((s.x - @cx) * (s.x - @cx) + (s.y - @cy) * (s.y - @cy)) <= @r2
  AND ABS(s.z - @cz) <= @zBand
  AND m.rank = 0
  AND m.type <> @critter
  AND m.npcflag = 0
  AND (m.unit_flags & @immune) = 0
GROUP BY m.entry, m.max_level, m.faction, m.type";

            long levelSum = 0, count = 0;
            try
            {
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                cmd.Parameters.AddWithValue("@minX", center.X - radius);
                cmd.Parameters.AddWithValue("@maxX", center.X + radius);
                cmd.Parameters.AddWithValue("@minY", center.Y - radius);
                cmd.Parameters.AddWithValue("@maxY", center.Y + radius);
                cmd.Parameters.AddWithValue("@cx", center.X);
                cmd.Parameters.AddWithValue("@cy", center.Y);
                cmd.Parameters.AddWithValue("@cz", center.Z);
                cmd.Parameters.AddWithValue("@r2", radius * radius);
                cmd.Parameters.AddWithValue("@zBand", VibeGrinderSettings.Instance.SpotQueryZBand);
                cmd.Parameters.AddWithValue("@critter", CritterType);
                cmd.Parameters.AddWithValue("@immune", immuneUnitFlagMask);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int maxLevel = reader.GetInt32(0);
                    int faction = reader.GetInt32(1);
                    int type = reader.GetInt32(2);
                    int cnt = reader.GetInt32(3);
                    if (!factions.IsAttackable(faction, type)) continue;
                    levelSum += (long)maxLevel * cnt;
                    count += cnt;
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[GrindMobs] AverageAttackableLevelNear error: {0}", ex.Message);
                return 0f;
            }
            return count > 0 ? (float)levelSum / count : 0f;
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

            // Z-band trim (audit 2026-07-05): a hazard 40yd straight down contributed ~73% weight via the
            // 3D falloff despite being unreachable/non-aggroing. (Note: rank/level prefilter means this
            // query structurally can't see at-level normal packs — that gap is covered by the selection
            // bubble-knot gate, not here.)
            const string sql = @"
SELECT s.x, s.y, s.z, m.entry, m.max_level, m.rank, m.faction
FROM spawns s JOIN mobs m ON m.entry = s.entry
WHERE s.map_id = @map
  AND s.x BETWEEN @minX AND @maxX AND s.y BETWEEN @minY AND @maxY
  AND ABS(s.z - @cz) <= @zBand
  AND (m.rank >= 1 OR m.max_level > @dangerLevel)";
            try
            {
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                cmd.Parameters.AddWithValue("@minX", center.X - radius);
                cmd.Parameters.AddWithValue("@maxX", center.X + radius);
                cmd.Parameters.AddWithValue("@minY", center.Y - radius);
                cmd.Parameters.AddWithValue("@maxY", center.Y + radius);
                cmd.Parameters.AddWithValue("@cz", center.Z);
                cmd.Parameters.AddWithValue("@zBand", VibeGrinderSettings.Instance.SpotQueryZBand);
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

        /// <summary>
        /// Count creature SPAWNS within `radius` (box half-width) of `center` whose faction is HOSTILE to the
        /// player — i.e. would aggro on sight (FactionResolver.HostileFactions). Used to reject a vendor sitting
        /// in enemy territory: the DB flags some repair/sell NPCs inside opposing-faction camps (e.g. Prospector
        /// Khazgorm at the Alliance dig site Bael Modan), and a Horde bot would be torn apart trying to shop
        /// there. Returns 0 if the DB is absent or no factions are hostile.
        /// </summary>
        public static int HostileSpawnCountNear(uint mapId, WoWPoint center, float radius, FactionResolver factions)
        {
            EnsureInitialized();
            if (!_isAvailable || factions == null || factions.HostileFactions.Count == 0) return 0;

            const string sql = @"
SELECT m.faction, COUNT(*) AS cnt
FROM spawns s JOIN mobs m ON m.entry = s.entry
WHERE s.map_id = @map
  AND s.x BETWEEN @minX AND @maxX AND s.y BETWEEN @minY AND @maxY
GROUP BY m.faction";
            int total = 0;
            try
            {
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                cmd.Parameters.AddWithValue("@minX", center.X - radius);
                cmd.Parameters.AddWithValue("@maxX", center.X + radius);
                cmd.Parameters.AddWithValue("@minY", center.Y - radius);
                cmd.Parameters.AddWithValue("@maxY", center.Y + radius);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (factions.HostileFactions.Contains(reader.GetInt32(0)))
                        total += reader.GetInt32(1);
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[GrindMobs] HostileSpawnCountNear error: {0}", ex.Message);
                return 0;
            }
            return total;
        }

        /// <summary>
        /// All hostile (player-attacking faction) spawns of meaningful level within a box — the trek-safety
        /// corridor feed (TrekSafety). minLevel floors out greys that can't hurt us; NO upper level bound
        /// (reds are exactly what we're looking for). rank included so elites can be weighted.
        /// </summary>
        public static List<MobSpawn> QueryHostileSpawnsNear(uint mapId, WoWPoint center, float radius,
            FactionResolver factions, int minLevel)
        {
            EnsureInitialized();
            var result = new List<MobSpawn>();
            if (!_isAvailable || factions == null || factions.HostileFactions.Count == 0) return result;

            const string sql = @"
SELECT s.x, s.y, s.z, m.entry, m.max_level, m.rank, m.faction
FROM spawns s JOIN mobs m ON m.entry = s.entry
WHERE s.map_id = @map
  AND s.x BETWEEN @minX AND @maxX AND s.y BETWEEN @minY AND @maxY
  AND m.max_level >= @minLevel";
            try
            {
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                cmd.Parameters.AddWithValue("@minX", center.X - radius);
                cmd.Parameters.AddWithValue("@maxX", center.X + radius);
                cmd.Parameters.AddWithValue("@minY", center.Y - radius);
                cmd.Parameters.AddWithValue("@maxY", center.Y + radius);
                cmd.Parameters.AddWithValue("@minLevel", minLevel);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int faction = reader.GetInt32(6);
                    if (!factions.HostileFactions.Contains(faction)) continue;
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
                Logging.WriteDebug("[GrindMobs] QueryHostileSpawnsNear error: {0}", ex.Message);
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
