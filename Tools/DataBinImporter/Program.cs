using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using MySqlConnector;

// DataBinImporter — supplements data.bin with NPC data from TrinityCore 3.3.5a world DB.
//
// Strategy:
//   1. Read all existing entry IDs from data.bin.
//   2. Query TrinityCore for NPCs with useful flags not already in data.bin.
//   3. Insert new rows directly into data.bin (encrypted SQLite).
//
// Only adds entries whose entry ID doesn't already exist in data.bin (conservative,
// preserves all existing HB data).
//
// Must run as x86 — System.Data.SQLite.dll in Lib/ is x86-only.

class Program
{
    // --- data.bin (the RUNTIME copy — sync datadb/data.bin afterwards) ---
    static readonly string DataBinPath =
        Environment.GetEnvironmentVariable("DATABIN_PATH") ?? @"E:\!Games\World of Warcraft\CopilotBuddy\data.bin";
    const string DataBinPassword = "JkejXP5_fG2vN-jlFVME";

    // --- world DB (AzerothCore defaults; override via env like GrindMobsExtractor) ---
    static readonly string MySqlHost = Environment.GetEnvironmentVariable("ACORE_DB_HOST") ?? "127.0.0.1";
    const int MySqlPort = 3306;
    static readonly string MySqlUser = Environment.GetEnvironmentVariable("ACORE_DB_USER") ?? "acore";
    static readonly string MySqlPass = Environment.GetEnvironmentVariable("ACORE_DB_PASS") ?? "acore";
    static readonly string MySqlDb   = Environment.GetEnvironmentVariable("ACORE_DB_WORLD") ?? "acore_world";

    // NPC flags that are useful for the bot:
    // Trainer=16, ClassTrainer=32, ProfTrainer=64,
    // Vendor=128, AmmoVendor=256, FoodVendor=512, PoisonVendor=1024, ReagentVendor=2048,
    // Repair=4096, Flightmaster=8192, Innkeeper=65536
    const uint UsefulFlagMask = 16 | 32 | 64 | 128 | 256 | 512 | 1024 | 2048 | 4096 | 8192 | 65536;

    static void Main()
    {
        Console.WriteLine("=== DataBinImporter for TrinityCore 3.3.5a ===\n");

        // Step 1 — collect existing entry IDs from data.bin
        var existingEntries = LoadExistingEntries();
        Console.WriteLine($"Existing entries in data.bin: {existingEntries.Count}\n");

        // Step 2 — pull candidate NPCs from TrinityCore
        var candidates = LoadFromTrinityCore(existingEntries);
        Console.WriteLine($"New NPCs to import from TrinityCore: {candidates.Count}\n");

        if (candidates.Count == 0)
        {
            Console.WriteLine("Nothing to import. data.bin is already up to date.");
            return;
        }

        // Step 3 — insert into data.bin
        int inserted = InsertIntoDataBin(candidates);

        Console.WriteLine($"\nDone. {inserted} new NPC spawn rows added to data.bin.");
        Console.WriteLine($"Run DataBinInspector to verify the new totals.");
    }

    // -------------------------------------------------------------------------
    // Step 1 — read existing entry IDs from data.bin
    // -------------------------------------------------------------------------
    static HashSet<uint> LoadExistingEntries()
    {
        Console.WriteLine("Reading existing data.bin...");
        var entries = new HashSet<uint>();

        var builder = new SQLiteConnectionStringBuilder
        {
            DataSource = DataBinPath,
            Password = DataBinPassword,
            ReadOnly = true
        };

        using var conn = new SQLiteConnection(builder.ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT entry FROM npcs";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            entries.Add((uint)reader.GetInt64(0));

        return entries;
    }

    // -------------------------------------------------------------------------
    // Step 2 — query TrinityCore world DB for new NPCs
    // -------------------------------------------------------------------------
    static List<NpcRow> LoadFromTrinityCore(HashSet<uint> existingEntries)
    {
        Console.WriteLine("Connecting to TrinityCore world DB...");
        var rows = new List<NpcRow>();

        var connStr = new MySqlConnectionStringBuilder
        {
            Server   = MySqlHost,
            Port     = (uint)MySqlPort,
            UserID   = MySqlUser,
            Password = MySqlPass,
            Database = MySqlDb,
            AllowUserVariables = true
        }.ConnectionString;

        using var conn = new MySqlConnection(connStr);
        conn.Open();
        Console.WriteLine("MySQL connected OK.");

        // Query: creature_template + creature (spawn positions) + optional trainer info.
        // One row per creature SPAWN — not per template — so the bot can find the nearest one.
        //
        // npcflag: use template flag OR'd with per-spawn override (creature.npcflag).
        // trainer_class: from trainer.Requirement via creature_default_trainer.
        // trainer_type:  from trainer.Type.
        // faction: creature_template.faction.
        const string sql = @"
SELECT
    ct.entry,
    ct.name,
    COALESCE(ct.subname, '')            AS title,
    (ct.npcflag | c.npcflag)            AS flag,
    ct.faction,
    c.map,
    c.position_x                        AS x,
    c.position_y                        AS y,
    c.position_z                        AS z,
    ct.maxlevel                         AS level,
    COALESCE(t.Type, 0)                 AS trainer_type,
    COALESCE(t.Requirement, 0)          AS trainer_class
FROM creature_template  ct
JOIN creature           c   ON  c.id  = ct.entry
LEFT JOIN creature_default_trainer cdt ON cdt.CreatureId = ct.entry
LEFT JOIN trainer       t   ON  t.Id  = cdt.TrainerId
WHERE (ct.npcflag | c.npcflag) & @mask != 0
  -- Never import event-gated spawns: eventEntry > 0 = exists ONLY while the event runs. The Arena
  -- Tournament ghosts (generic 'Hunter Trainer' 26325 etc., game_event 31 which never runs) would
  -- otherwise land in data.bin and send bots to empty grass.
  AND NOT EXISTS (SELECT 1 FROM game_event_creature g WHERE g.guid = c.guid AND g.eventEntry > 0)
ORDER BY ct.entry, c.guid";

        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@mask", UsefulFlagMask);

        int skipped = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            uint entry = reader.GetUInt32("entry");

            // Skip if already in data.bin
            if (existingEntries.Contains(entry))
            {
                skipped++;
                continue;
            }

            rows.Add(new NpcRow
            {
                Entry        = entry,
                Name         = reader.GetString("name"),
                Title        = reader.GetString("title"),
                Flag         = reader.GetUInt32("flag"),
                Faction      = reader.GetUInt32("faction"),
                Map          = reader.GetUInt16("map"),
                X            = reader.GetFloat("x"),
                Y            = reader.GetFloat("y"),
                Z            = reader.GetFloat("z"),
                Level        = reader.GetByte("level"),
                TrainerType  = reader.GetByte("trainer_type"),
                TrainerClass = reader.GetByte("trainer_class"),
            });
        }

        Console.WriteLine($"  TrinityCore query returned {rows.Count + skipped} spawn rows.");
        Console.WriteLine($"  Skipped {skipped} spawns (entry already in data.bin).");
        return rows;
    }

    // -------------------------------------------------------------------------
    // Step 3 — insert new rows into data.bin
    // -------------------------------------------------------------------------
    static int InsertIntoDataBin(List<NpcRow> rows)
    {
        Console.WriteLine($"\nInserting {rows.Count} rows into data.bin...");

        var builder = new SQLiteConnectionStringBuilder
        {
            DataSource = DataBinPath,
            Password = DataBinPassword,
            ReadOnly = false
        };

        using var conn = new SQLiteConnection(builder.ConnectionString);
        conn.Open();

        // Wrap in a transaction — dramatically faster for bulk insert
        using var tx = conn.BeginTransaction();

        const string insertSql = @"
INSERT INTO npcs (entry, name, title, flag, faction, map, x, y, z, level, trainer_type, trainer_class)
VALUES (@entry, @name, @title, @flag, @faction, @map, @x, @y, @z, @level, @trainer_type, @trainer_class)";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = insertSql;
        cmd.Transaction = tx;

        // Pre-create parameters once, then rebind per row
        cmd.Parameters.Add("@entry",        System.Data.DbType.Int32);
        cmd.Parameters.Add("@name",         System.Data.DbType.String);
        cmd.Parameters.Add("@title",        System.Data.DbType.String);
        cmd.Parameters.Add("@flag",         System.Data.DbType.Int32);
        cmd.Parameters.Add("@faction",      System.Data.DbType.Int32);
        cmd.Parameters.Add("@map",          System.Data.DbType.Int32);
        cmd.Parameters.Add("@x",            System.Data.DbType.Single);
        cmd.Parameters.Add("@y",            System.Data.DbType.Single);
        cmd.Parameters.Add("@z",            System.Data.DbType.Single);
        cmd.Parameters.Add("@level",        System.Data.DbType.Int32);
        cmd.Parameters.Add("@trainer_type", System.Data.DbType.Int32);
        cmd.Parameters.Add("@trainer_class",System.Data.DbType.Int32);

        int count = 0;
        foreach (var row in rows)
        {
            cmd.Parameters["@entry"].Value         = (int)row.Entry;
            cmd.Parameters["@name"].Value          = row.Name;
            cmd.Parameters["@title"].Value         = row.Title;
            cmd.Parameters["@flag"].Value          = (int)row.Flag;
            cmd.Parameters["@faction"].Value       = (int)row.Faction;
            cmd.Parameters["@map"].Value           = (int)row.Map;
            cmd.Parameters["@x"].Value             = row.X;
            cmd.Parameters["@y"].Value             = row.Y;
            cmd.Parameters["@z"].Value             = row.Z;
            cmd.Parameters["@level"].Value         = (int)row.Level;
            cmd.Parameters["@trainer_type"].Value  = (int)row.TrainerType;
            cmd.Parameters["@trainer_class"].Value = (int)row.TrainerClass;

            cmd.ExecuteNonQuery();
            count++;

            if (count % 500 == 0)
                Console.Write($"  {count}/{rows.Count} inserted...\r");
        }

        tx.Commit();
        Console.WriteLine();
        return count;
    }
}

// -------------------------------------------------------------------------
// Data transfer object
// -------------------------------------------------------------------------
struct NpcRow
{
    public uint   Entry;
    public string Name;
    public string Title;
    public uint   Flag;
    public uint   Faction;
    public ushort Map;
    public float  X;
    public float  Y;
    public float  Z;
    public byte   Level;
    public byte   TrainerType;
    public byte   TrainerClass;
}
