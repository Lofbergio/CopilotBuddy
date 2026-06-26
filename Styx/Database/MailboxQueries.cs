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
    }
}
