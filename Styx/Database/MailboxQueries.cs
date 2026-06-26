using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Styx.Database
{
    /// <summary>
    /// Read-only access to Mailboxes.db — mailbox gameobject locations extracted offline (by
    /// GrindMobsExtractor) from the world DB. Shared infrastructure: any bot (VibeGrinder,
    /// VibeQuester, …) can ask for the mailboxes on a map. Lazy ReadOnly connection; an absent or
    /// empty DB simply yields no mailboxes (no error), so callers degrade to "can't mail here".
    /// Mirrors the ItemLootQueries connection pattern.
    /// </summary>
    public static class MailboxQueries
    {
        private static SQLiteConnection _connection;
        private static bool _initialized;
        private static bool _isAvailable;

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
                var dbPath = Path.Combine(Logging.ApplicationPath, "Mailboxes.db");
                if (!File.Exists(dbPath))
                    return;

                var builder = new SQLiteConnectionStringBuilder { DataSource = dbPath, ReadOnly = true };
                _connection = new SQLiteConnection(builder.ConnectionString);
                _connection.Open();
                _isAvailable = true;
                Logging.Write("[Mailboxes] Database loaded: Mailboxes.db");
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[Mailboxes] init failed: {0}", ex.Message);
            }
        }

        /// <summary>All mailbox locations on a map. Empty if Mailboxes.db is absent.</summary>
        public static List<WoWPoint> GetMailboxesOnMap(uint mapId)
        {
            EnsureInitialized();
            var result = new List<WoWPoint>();
            if (!_isAvailable) return result;

            const string sql = "SELECT x, y, z FROM mailboxes WHERE map_id = @map";
            try
            {
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    result.Add(new WoWPoint(reader.GetFloat(0), reader.GetFloat(1), reader.GetFloat(2)));
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[Mailboxes] query failed: {0}", ex.Message);
            }
            return result;
        }

        /// <summary>
        /// Mailboxes on a map together with the factions of faction-aligned NPCs near each one
        /// (extracted offline). Callers resolve those factions live against the player to skip
        /// enemy-territory mailboxes. On an older DB without the mailbox_factions table, every
        /// record comes back with an empty faction list — i.e. "no faction info, keep it" — so the
        /// feature degrades safely instead of erroring.
        /// </summary>
        public static List<MailboxRecord> GetMailboxesWithFactionsOnMap(uint mapId)
        {
            EnsureInitialized();
            var result = new List<MailboxRecord>();
            if (!_isAvailable) return result;

            if (!HasFactionTable())
            {
                foreach (WoWPoint pt in GetMailboxesOnMap(mapId))
                    result.Add(new MailboxRecord(pt, new List<int>()));
                return result;
            }

            const string sql = @"
SELECT m.id, m.x, m.y, m.z, f.faction
FROM mailboxes m LEFT JOIN mailbox_factions f ON f.mailbox_id = m.id
WHERE m.map_id = @map";
            try
            {
                var byId = new Dictionary<long, MailboxRecord>();
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@map", (int)mapId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    if (!byId.TryGetValue(id, out var rec))
                    {
                        rec = new MailboxRecord(
                            new WoWPoint(reader.GetFloat(1), reader.GetFloat(2), reader.GetFloat(3)),
                            new List<int>());
                        byId[id] = rec;
                        result.Add(rec);
                    }
                    if (!reader.IsDBNull(4))
                        rec.NearbyFactions.Add(reader.GetInt32(4));
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[Mailboxes] faction query failed: {0}", ex.Message);
            }
            return result;
        }

        private static bool? _hasFactionTable;

        private static bool HasFactionTable()
        {
            if (_hasFactionTable.HasValue) return _hasFactionTable.Value;
            try
            {
                using var cmd = new SQLiteCommand(
                    "SELECT 1 FROM sqlite_master WHERE type='table' AND name='mailbox_factions'", _connection);
                _hasFactionTable = cmd.ExecuteScalar() != null;
            }
            catch
            {
                _hasFactionTable = false;
            }
            return _hasFactionTable.Value;
        }
    }

    /// <summary>A mailbox location plus the factions of faction-aligned NPCs spawned near it.</summary>
    public class MailboxRecord
    {
        public WoWPoint Location { get; }
        public List<int> NearbyFactions { get; }

        public MailboxRecord(WoWPoint location, List<int> nearbyFactions)
        {
            Location = location;
            NearbyFactions = nearbyFactions;
        }
    }
}
