using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace VibeQuester
{
    // Thin read helper: a query → object[] rows plus null-safe converters, used by DataLoader and
    // VendorDataLoader. This was a reflection shim while VibeQuester was a Roslyn drop-in (which
    // can't link System.Data.SQLite at compile time); now that the bot is compiled into
    // CopilotBuddy.dll it calls the driver directly, same as GrindMobsRepository.
    internal static class Sqlite
    {
        public static bool Available => true;

        public static List<object[]> Query(string dbPath, string sql)
        {
            var rows = new List<object[]>();
            var builder = new SQLiteConnectionStringBuilder { DataSource = dbPath, ReadOnly = true };
            using var conn = new SQLiteConnection(builder.ConnectionString);
            conn.Open();
            using var cmd = new SQLiteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            int n = reader.FieldCount;
            while (reader.Read())
            {
                var vals = new object[n];
                reader.GetValues(vals);
                rows.Add(vals);
            }
            return rows;
        }

        public static int I(object o) => o == null || o is DBNull ? 0 : Convert.ToInt32(o);
        public static string S(object o) => o == null || o is DBNull ? "" : Convert.ToString(o);
        public static double D(object o) => o == null || o is DBNull ? 0.0 : Convert.ToDouble(o);
    }
}
