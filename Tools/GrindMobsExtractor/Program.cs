using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using MySqlConnector;

// GrindMobsExtractor — builds GrindMobs.db (creature metadata + spawn positions) from a local
// AzerothCore 3.3.5a world DB, for the VibeGrinder botbase's offline spot selection.
//
// Self-contained: positions + metadata in one file (the user chose a standalone DB so the
// existing CreatureSpawns.db stays untouched). No faction_hostility table — VibeGrinder's
// FactionResolver computes attackability live via the client faction system (WoWFaction.RelationTo).
//
// Auto-detects the world schema (the one holding creature_template) and the creature spawn-entry
// column (id1 vs id), so it works across AzerothCore/TrinityCore schema variants without editing.
// Must run x86 (System.Data.SQLite.dll in Lib/ is x86-only). Excluded from the main build (Tools/**).

class Program
{
    const string OutPath = "GrindMobs.db";
    const string OutPathMailboxes = "Mailboxes.db";

    // --- MySQL connection: from env vars, with non-secret AzerothCore defaults. NEVER hardcode real
    // creds here — set ACORE_DB_HOST/PORT/USER/PASS in your environment to override. ---
    static readonly string MySqlHost = Environment.GetEnvironmentVariable("ACORE_DB_HOST") ?? "127.0.0.1";
    static readonly int    MySqlPort = int.TryParse(Environment.GetEnvironmentVariable("ACORE_DB_PORT"), out var p) ? p : 3306;
    static readonly string MySqlUser = Environment.GetEnvironmentVariable("ACORE_DB_USER") ?? "acore";
    static readonly string MySqlPass = Environment.GetEnvironmentVariable("ACORE_DB_PASS") ?? "acore";

    static void Main()
    {
        Console.WriteLine("=== GrindMobsExtractor for AzerothCore 3.3.5a ===\n");

        string worldDb = DetectWorldSchema();
        Console.WriteLine($"World schema: {worldDb}");

        string spawnEntryCol = DetectSpawnEntryColumn(worldDb);
        Console.WriteLine($"creature spawn-entry column: {spawnEntryCol}\n");

        var mobs = LoadCreatureTemplates(worldDb);
        Console.WriteLine($"creature_template rows: {mobs.Count}");

        var spawns = LoadSpawns(worldDb, spawnEntryCol);
        Console.WriteLine($"creature spawn rows: {spawns.Count}\n");

        var mailboxes = LoadMailboxes(worldDb);
        Console.WriteLine($"mailbox spawn rows: {mailboxes.Count}\n");

        BuildDatabase(mobs, spawns);
        BuildMailboxDatabase(mailboxes);

        Console.WriteLine($"\nDone. Wrote {Path.GetFullPath(OutPath)} and {Path.GetFullPath(OutPathMailboxes)}.");
        Console.WriteLine("Copy GrindMobs.db + Mailboxes.db into the CopilotBuddy runtime root (next to CreatureSpawns.db).");
    }

    // -------------------------------------------------------------------------
    // Schema / column auto-detection
    // -------------------------------------------------------------------------
    static string DetectWorldSchema()
    {
        using var conn = new MySqlConnection(ConnStr("information_schema"));
        conn.Open();
        // Prefer a schema literally named like a world DB, else any schema that has creature_template.
        const string sql = @"
SELECT TABLE_SCHEMA,
       (CASE WHEN TABLE_SCHEMA LIKE '%world%' THEN 0 ELSE 1 END) AS pref
FROM information_schema.TABLES
WHERE TABLE_NAME = 'creature_template'
ORDER BY pref, TABLE_SCHEMA
LIMIT 1";
        using var cmd = new MySqlCommand(sql, conn);
        object result = cmd.ExecuteScalar();
        if (result == null)
            throw new Exception("No schema with a 'creature_template' table found. Is the world DB imported?");
        return (string)result;
    }

    static string DetectSpawnEntryColumn(string worldDb)
    {
        using var conn = new MySqlConnection(ConnStr("information_schema"));
        conn.Open();
        const string sql = @"
SELECT COLUMN_NAME FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'creature' AND COLUMN_NAME IN ('id1','id')
ORDER BY (COLUMN_NAME = 'id1') DESC
LIMIT 1";
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@db", worldDb);
        object result = cmd.ExecuteScalar();
        if (result == null)
            throw new Exception($"`{worldDb}`.creature has neither an 'id1' nor 'id' column.");
        return (string)result;
    }

    // -------------------------------------------------------------------------
    // creature_template → mobs
    // -------------------------------------------------------------------------
    static List<MobRow> LoadCreatureTemplates(string worldDb)
    {
        Console.WriteLine("Reading creature_template...");
        var rows = new List<MobRow>();

        using var conn = new MySqlConnection(ConnStr(worldDb));
        conn.Open();

        // `rank` is a reserved keyword in MySQL 8 — must be backticked.
        const string sql = @"
SELECT entry, name, minlevel, maxlevel, faction, `rank`, npcflag, unit_flags, type, family
FROM creature_template";

        using var cmd = new MySqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new MobRow
            {
                Entry     = r.GetUInt32("entry"),
                Name      = r.IsDBNull(r.GetOrdinal("name")) ? "" : r.GetString("name"),
                MinLevel  = r.GetInt32("minlevel"),
                MaxLevel  = r.GetInt32("maxlevel"),
                Faction   = r.GetInt32("faction"),
                Rank      = r.GetInt32("rank"),
                NpcFlag   = r.GetInt64("npcflag"),
                UnitFlags = r.GetInt64("unit_flags"),
                Type      = r.GetInt32("type"),
                Family    = r.GetInt32("family"),
            });
        }
        return rows;
    }

    // -------------------------------------------------------------------------
    // creature → spawns
    // -------------------------------------------------------------------------
    static List<SpawnRow> LoadSpawns(string worldDb, string entryCol)
    {
        Console.WriteLine("Reading creature (spawns)...");
        var rows = new List<SpawnRow>();

        using var conn = new MySqlConnection(ConnStr(worldDb));
        conn.Open();

        string sql = $@"
SELECT `{entryCol}` AS entry, map, position_x, position_y, position_z
FROM creature";

        using var cmd = new MySqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new SpawnRow
            {
                Entry = r.GetUInt32("entry"),
                Map   = r.GetUInt16("map"),
                X     = r.GetFloat("position_x"),
                Y     = r.GetFloat("position_y"),
                Z     = r.GetFloat("position_z"),
            });
        }
        return rows;
    }

    // gameobject → mailboxes (gameobject_template.type = 19 = GAMEOBJECT_TYPE_MAILBOX)
    // -------------------------------------------------------------------------
    static List<SpawnRow> LoadMailboxes(string worldDb)
    {
        Console.WriteLine("Reading mailboxes (gameobject type 19)...");
        var rows = new List<SpawnRow>();

        using var conn = new MySqlConnection(ConnStr(worldDb));
        conn.Open();

        // AzerothCore: gameobject.id references gameobject_template.entry. If a fork uses a different
        // spawn-entry column, adjust g.id here (mirrors the creature id1/id detection).
        const string sql = @"
SELECT g.map, g.position_x, g.position_y, g.position_z
FROM gameobject g JOIN gameobject_template t ON t.entry = g.id
WHERE t.type = 19";

        using var cmd = new MySqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new SpawnRow
            {
                Entry = 0,
                Map   = r.GetUInt16("map"),
                X     = r.GetFloat("position_x"),
                Y     = r.GetFloat("position_y"),
                Z     = r.GetFloat("position_z"),
            });
        }
        return rows;
    }

    // -------------------------------------------------------------------------
    // Write GrindMobs.db (VibeGrinder mob-spot data only)
    // -------------------------------------------------------------------------
    static void BuildDatabase(List<MobRow> mobs, List<SpawnRow> spawns)
    {
        if (File.Exists(OutPath))
            File.Delete(OutPath);

        SQLiteConnection.CreateFile(OutPath);

        var builder = new SQLiteConnectionStringBuilder { DataSource = OutPath };
        using var conn = new SQLiteConnection(builder.ConnectionString);
        conn.Open();

        ExecNonQuery(conn, @"
CREATE TABLE mobs(
  entry      INTEGER PRIMARY KEY,
  name       TEXT,
  min_level  INTEGER,
  max_level  INTEGER,
  faction    INTEGER,
  rank       INTEGER,
  npcflag    INTEGER,
  unit_flags INTEGER,
  type       INTEGER,
  family     INTEGER);
CREATE TABLE spawns(
  id      INTEGER PRIMARY KEY AUTOINCREMENT,
  entry   INTEGER,
  map_id  INTEGER,
  x REAL, y REAL, z REAL);");

        Console.WriteLine("Inserting mobs...");
        using (var tx = conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO mobs(entry,name,min_level,max_level,faction,rank,npcflag,unit_flags,type,family)
                                VALUES(@e,@n,@mn,@mx,@f,@r,@nf,@uf,@t,@fam)";
            AddP(cmd, "@e", "@n", "@mn", "@mx", "@f", "@r", "@nf", "@uf", "@t", "@fam");
            foreach (var m in mobs)
            {
                cmd.Parameters["@e"].Value = (int)m.Entry;
                cmd.Parameters["@n"].Value = m.Name;
                cmd.Parameters["@mn"].Value = m.MinLevel;
                cmd.Parameters["@mx"].Value = m.MaxLevel;
                cmd.Parameters["@f"].Value = m.Faction;
                cmd.Parameters["@r"].Value = m.Rank;
                cmd.Parameters["@nf"].Value = m.NpcFlag;
                cmd.Parameters["@uf"].Value = m.UnitFlags;
                cmd.Parameters["@t"].Value = m.Type;
                cmd.Parameters["@fam"].Value = m.Family;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        Console.WriteLine("Inserting spawns...");
        using (var tx = conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO spawns(entry,map_id,x,y,z) VALUES(@e,@m,@x,@y,@z)";
            AddP(cmd, "@e", "@m", "@x", "@y", "@z");
            foreach (var s in spawns)
            {
                cmd.Parameters["@e"].Value = (int)s.Entry;
                cmd.Parameters["@m"].Value = (int)s.Map;
                cmd.Parameters["@x"].Value = s.X;
                cmd.Parameters["@y"].Value = s.Y;
                cmd.Parameters["@z"].Value = s.Z;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        Console.WriteLine("Creating indexes...");
        ExecNonQuery(conn, @"
CREATE INDEX ix_spawns_entry ON spawns(entry);
CREATE INDEX ix_spawns_map_entry ON spawns(map_id, entry);
CREATE INDEX ix_mobs_level ON mobs(min_level, max_level);");
    }

    // -------------------------------------------------------------------------
    // Write Mailboxes.db (shared gameobject/mailbox locations — any bot can read it)
    // -------------------------------------------------------------------------
    static void BuildMailboxDatabase(List<SpawnRow> mailboxes)
    {
        if (File.Exists(OutPathMailboxes))
            File.Delete(OutPathMailboxes);

        SQLiteConnection.CreateFile(OutPathMailboxes);

        var builder = new SQLiteConnectionStringBuilder { DataSource = OutPathMailboxes };
        using var conn = new SQLiteConnection(builder.ConnectionString);
        conn.Open();

        ExecNonQuery(conn, @"
CREATE TABLE mailboxes(
  id      INTEGER PRIMARY KEY AUTOINCREMENT,
  map_id  INTEGER,
  x REAL, y REAL, z REAL);");

        Console.WriteLine("Inserting mailboxes...");
        using (var tx = conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO mailboxes(map_id,x,y,z) VALUES(@m,@x,@y,@z)";
            AddP(cmd, "@m", "@x", "@y", "@z");
            foreach (var mb in mailboxes)
            {
                cmd.Parameters["@m"].Value = (int)mb.Map;
                cmd.Parameters["@x"].Value = mb.X;
                cmd.Parameters["@y"].Value = mb.Y;
                cmd.Parameters["@z"].Value = mb.Z;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        ExecNonQuery(conn, "CREATE INDEX ix_mailboxes_map ON mailboxes(map_id);");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    static string ConnStr(string database) => new MySqlConnectionStringBuilder
    {
        Server = MySqlHost, Port = (uint)MySqlPort,
        UserID = MySqlUser, Password = MySqlPass, Database = database,
    }.ConnectionString;

    static void ExecNonQuery(SQLiteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static void AddP(SQLiteCommand cmd, params string[] names)
    {
        foreach (var n in names)
            cmd.Parameters.Add(new SQLiteParameter(n));
    }
}

struct MobRow
{
    public uint Entry; public string Name;
    public int MinLevel; public int MaxLevel; public int Faction; public int Rank;
    public long NpcFlag; public long UnitFlags; public int Type; public int Family;
}

struct SpawnRow
{
    public uint Entry; public ushort Map; public float X; public float Y; public float Z;
}
