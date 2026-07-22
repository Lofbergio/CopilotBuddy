using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Bots.Vibes.Shared;

namespace Bots.Vibes.Shared.GrindData
{
    /// <summary>
    /// Read-only access to GrindMobs.db (creature metadata + spawn positions) for VibeGrinder
    /// spot selection. The DB is static, so it is loaded ONCE into an in-memory spatial grid per
    /// map: spot selection issues hundreds of spatial queries per pick (3+ per candidate cluster ×
    /// hundreds of clusters × the leniency ladder), and per-query SQLite round-trips froze the
    /// worker — and with it the client frame — for 5-7s at L9 (log 2026-07-10_1154). All public
    /// methods keep their exact SQL-era predicate semantics (circle+Z-band vs box-only per method).
    /// False-safe when the DB is absent.
    /// </summary>
    public static class GrindMobsRepository
    {
        private static SQLiteConnection _connection;
        private static bool _initialized;
        private static bool _isAvailable;

        // Critters are never grind targets; 8 == CREATURE_TYPE_CRITTER (3.3.5a).
        private const int CritterType = 8;

        /// <summary>
        /// Vertical band every box query trims to. A cave 40yd under a mesa is not "near" the mesa —
        /// the same column-query Z bug class the blackspots fixed. It is a property of the query, not
        /// of the bot asking: a caller that could choose it would only ever choose this.
        /// </summary>
        private const float QueryZBand = 40f;

        /// <summary>
        /// unit_flags that make a creature untargetable/unattackable in practice (NON_ATTACKABLE |
        /// IMMUNE_TO_PLAYER | NOT_SELECTABLE). Lives here because it screens creature TEMPLATE rows,
        /// which is what this file owns; every query applied the identical mask when callers passed it.
        /// </summary>
        public const long ImmuneUnitFlagMask = 0x2L | 0x100L | 0x2000000L;

        private sealed class MobMeta
        {
            public int MinLevel, MaxLevel, Faction, Rank, Type;
            public long NpcFlag, UnitFlags;
        }

        private sealed class SpawnRec
        {
            public uint Entry;
            public float X, Y, Z;
            public MobMeta Meta;   // null when the spawn has no mobs row (SQL joins excluded it; raw counts include it)
        }

        private sealed class MapSnapshot
        {
            // Cell must be >= the largest common query radius so a box scan touches few cells.
            public const float Cell = 128f;
            public readonly List<SpawnRec> Spawns = new List<SpawnRec>();
            public readonly Dictionary<long, List<SpawnRec>> Grid = new Dictionary<long, List<SpawnRec>>();

            private static long Key(int cx, int cy) { return ((long)cx << 32) ^ (uint)cy; }

            public void Add(SpawnRec r)
            {
                Spawns.Add(r);
                long key = Key((int)Math.Floor(r.X / Cell), (int)Math.Floor(r.Y / Cell));
                if (!Grid.TryGetValue(key, out List<SpawnRec> cell))
                    Grid[key] = cell = new List<SpawnRec>();
                cell.Add(r);
            }

            /// <summary>All spawns whose (X,Y) lies inside the axis-aligned box (exact, post-grid trim).</summary>
            public IEnumerable<SpawnRec> InBox(float minX, float maxX, float minY, float maxY)
            {
                int cx0 = (int)Math.Floor(minX / Cell), cx1 = (int)Math.Floor(maxX / Cell);
                int cy0 = (int)Math.Floor(minY / Cell), cy1 = (int)Math.Floor(maxY / Cell);
                for (int cx = cx0; cx <= cx1; cx++)
                    for (int cy = cy0; cy <= cy1; cy++)
                    {
                        if (!Grid.TryGetValue(Key(cx, cy), out List<SpawnRec> cell))
                            continue;
                        for (int i = 0; i < cell.Count; i++)
                        {
                            SpawnRec r = cell[i];
                            if (r.X >= minX && r.X <= maxX && r.Y >= minY && r.Y <= maxY)
                                yield return r;
                        }
                    }
            }
        }

        private static Dictionary<uint, MobMeta> _mobs;                                  // full mobs table
        private static readonly Dictionary<uint, MapSnapshot> _maps = new Dictionary<uint, MapSnapshot>();

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
                LoadMobs();
                _isAvailable = true;
                Logging.Write("[GrindMobs] Database loaded successfully ({0} creature templates)", _mobs.Count);
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[GrindMobs] Failed to load: {0}", ex.Message);
                _isAvailable = false;
            }
        }

        /// <summary>Close the shared connection and drop the snapshots so a later Start re-opens
        /// cleanly and no handle leaks across stop/restart. Call from VibeGrinder.Stop().</summary>
        public static void Shutdown()
        {
            if (_connection != null)
            {
                try { _connection.Close(); _connection.Dispose(); }
                catch { /* best-effort */ }
                _connection = null;
            }
            _mobs = null;
            _maps.Clear();
            _initialized = false;
            _isAvailable = false;
        }

        private static void LoadMobs()
        {
            _mobs = new Dictionary<uint, MobMeta>();
            using var cmd = new SQLiteCommand(
                "SELECT entry, min_level, max_level, faction, rank, npcflag, unit_flags, type FROM mobs", _connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                _mobs[(uint)reader.GetInt32(0)] = new MobMeta
                {
                    MinLevel = reader.GetInt32(1),
                    MaxLevel = reader.GetInt32(2),
                    Faction = reader.GetInt32(3),
                    Rank = reader.GetInt32(4),
                    NpcFlag = reader.GetInt64(5),
                    UnitFlags = reader.GetInt64(6),
                    Type = reader.GetInt32(7),
                };
            }
        }

        private static MapSnapshot GetMap(uint mapId)
        {
            if (_maps.TryGetValue(mapId, out MapSnapshot snap))
                return snap;

            snap = new MapSnapshot();
            var sw = Stopwatch.StartNew();
            try
            {
                using var cmd = new SQLiteCommand("SELECT entry, x, y, z FROM spawns WHERE map_id = @map", _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    uint entry = (uint)reader.GetInt32(0);
                    _mobs.TryGetValue(entry, out MobMeta meta);
                    snap.Add(new SpawnRec
                    {
                        Entry = entry,
                        X = reader.GetFloat(1),
                        Y = reader.GetFloat(2),
                        Z = reader.GetFloat(3),
                        Meta = meta,
                    });
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[GrindMobs] map {0} snapshot load error: {1}", mapId, ex.Message);
            }
            Logging.WriteDebug("[GrindMobs] map {0} snapshot: {1} spawns in {2} ms", mapId, snap.Spawns.Count, sw.ElapsedMilliseconds);
            _maps[mapId] = snap;
            return snap;
        }

        /// <summary>
        /// Eligible grind targets on a map: normal rank, level band intersects [lvlMin,lvlMax],
        /// non-critter, not a service NPC, not flagged immune/non-attackable. Faction is filtered
        /// against the attackable set (computed live by FactionResolver).
        /// </summary>
        public static List<MobSpawn> QueryEligibleSpawns(
            uint mapId, int lvlMin, int lvlMax, FactionResolver factions)
        {
            EnsureInitialized();
            var result = new List<MobSpawn>();
            if (!_isAvailable || factions == null) return result;

            // Band overlap: mob[min,max] intersects [lvlMin,lvlMax]  ==  min<=lvlMax AND max>=lvlMin.
            foreach (SpawnRec r in GetMap(mapId).Spawns)
            {
                MobMeta m = r.Meta;
                if (m == null || m.Rank != 0 || m.MinLevel > lvlMax || m.MaxLevel < lvlMin) continue;
                if (m.Type == CritterType || m.NpcFlag != 0 || (m.UnitFlags & ImmuneUnitFlagMask) != 0) continue;
                // Two-tier safety lives in FactionResolver (hostile any type; neutral non-humanoid only).
                if (!factions.IsAttackable(m.Faction, m.Type)) continue;
                result.Add(new MobSpawn(r.Entry, new WoWPoint(r.X, r.Y, r.Z), m.MaxLevel, m.Rank, m.Faction));
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
            return _mobs.TryGetValue(entry, out MobMeta m) ? m.UnitFlags : -1;
        }

        /// <summary>
        /// Total spawn count (all creatures, unfiltered) within a circle + Z band — the denominator
        /// for a spot's purity score. Circle + Z band, not just the box (audit 2026-07-05): the box
        /// corners reach r√2 out and a Z-blind count let a cave 40yd under a mesa deflate purity with
        /// mobs that can't be seen or reached.
        /// </summary>
        public static int CountSpawnsNear(uint mapId, WoWPoint center, float radius)
        {
            EnsureInitialized();
            if (!_isAvailable) return 0;

            float zBand = QueryZBand;
            float r2 = radius * radius;
            int total = 0;
            foreach (SpawnRec r in GetMap(mapId).InBox(center.X - radius, center.X + radius, center.Y - radius, center.Y + radius))
            {
                float dx = r.X - center.X, dy = r.Y - center.Y;
                if (dx * dx + dy * dy <= r2 && Math.Abs(r.Z - center.Z) <= zBand)
                    total++;
            }
            return total;
        }

        /// <summary>
        /// Population-weighted average max_level of ATTACKABLE mobs around a centroid (circle + Z band,
        /// same trim as CountSpawnsNear), ignoring the grind band — so a couple of band-edge anchors
        /// surrounded by gray lowbies produce a low average and the spot scores down. 0 if none / DB absent.
        /// </summary>
        public static float AverageAttackableLevelNear(uint mapId, WoWPoint center, float radius,
            FactionResolver factions)
        {
            EnsureInitialized();
            if (!_isAvailable || factions == null) return 0f;

            float zBand = QueryZBand;
            float r2 = radius * radius;
            long levelSum = 0, count = 0;
            foreach (SpawnRec r in GetMap(mapId).InBox(center.X - radius, center.X + radius, center.Y - radius, center.Y + radius))
            {
                MobMeta m = r.Meta;
                if (m == null || m.Rank != 0 || m.Type == CritterType || m.NpcFlag != 0 || (m.UnitFlags & ImmuneUnitFlagMask) != 0)
                    continue;
                float dx = r.X - center.X, dy = r.Y - center.Y;
                if (dx * dx + dy * dy > r2 || Math.Abs(r.Z - center.Z) > zBand)
                    continue;
                if (!factions.IsAttackable(m.Faction, m.Type)) continue;
                levelSum += m.MaxLevel;
                count++;
            }
            return count > 0 ? (float)levelSum / count : 0f;
        }

        /// <summary>
        /// Hazard spawns within a bounding box + Z band: elites/rares (rank &gt;= 1) OR mobs above the
        /// player's safe level (max_level &gt; level + dangerMargin). Used by DangerEvaluator.
        /// Z-band trim (audit 2026-07-05): a hazard 40yd straight down contributed ~73% weight via the
        /// 3D falloff despite being unreachable/non-aggroing. (Note: rank/level prefilter means this
        /// structurally can't see at-level normal packs — that gap is covered by the selection
        /// bubble-knot gate, not here.)
        /// </summary>
        public static List<MobSpawn> QueryHazardsNear(
            uint mapId, WoWPoint center, float radius, int level, int dangerMargin)
        {
            EnsureInitialized();
            var result = new List<MobSpawn>();
            if (!_isAvailable) return result;

            float zBand = QueryZBand;
            int dangerLevel = level + dangerMargin;
            foreach (SpawnRec r in GetMap(mapId).InBox(center.X - radius, center.X + radius, center.Y - radius, center.Y + radius))
            {
                MobMeta m = r.Meta;
                if (m == null || Math.Abs(r.Z - center.Z) > zBand) continue;
                if (m.Rank < 1 && m.MaxLevel <= dangerLevel) continue;
                result.Add(new MobSpawn(r.Entry, new WoWPoint(r.X, r.Y, r.Z), m.MaxLevel, m.Rank, m.Faction));
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

            int total = 0;
            foreach (SpawnRec r in GetMap(mapId).InBox(center.X - radius, center.X + radius, center.Y - radius, center.Y + radius))
            {
                if (r.Meta != null && factions.HostileFactions.Contains(r.Meta.Faction))
                    total++;
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

            foreach (SpawnRec r in GetMap(mapId).InBox(center.X - radius, center.X + radius, center.Y - radius, center.Y + radius))
            {
                MobMeta m = r.Meta;
                if (m == null || m.MaxLevel < minLevel || !factions.HostileFactions.Contains(m.Faction)) continue;
                result.Add(new MobSpawn(r.Entry, new WoWPoint(r.X, r.Y, r.Z), m.MaxLevel, m.Rank, m.Faction));
            }
            return result;
        }

        /// <summary>Distinct creature factions present on a map — seeds FactionResolver.</summary>
        public static List<int> DistinctFactionsOnMap(uint mapId)
        {
            EnsureInitialized();
            var result = new List<int>();
            if (!_isAvailable) return result;

            var seen = new HashSet<int>();
            foreach (SpawnRec r in GetMap(mapId).Spawns)
            {
                if (r.Meta != null && seen.Add(r.Meta.Faction))
                    result.Add(r.Meta.Faction);
            }
            return result;
        }
    }
}
