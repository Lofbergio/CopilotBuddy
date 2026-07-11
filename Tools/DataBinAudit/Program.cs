using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using MySqlConnector;

// DataBinAudit — diff data.bin's vendor/trainer flags against the live acore_world DB.
// data.bin is HB-era data supplemented from a TrinityCore repack; the server is AzerothCore —
// wherever they disagree, data.bin is the liar (it sends bots to NPCs that aren't vendors here).

class Program
{
    const string DataBinPath = @"E:\!Games\World of Warcraft\CopilotBuddy\data.bin";
    const string DataBinPassword = "JkejXP5_fG2vN-jlFVME";

    static readonly string Host = Environment.GetEnvironmentVariable("ACORE_DB_HOST") ?? "127.0.0.1";
    static readonly string User = Environment.GetEnvironmentVariable("ACORE_DB_USER") ?? "acore";
    static readonly string Pass = Environment.GetEnvironmentVariable("ACORE_DB_PASS") ?? "acore";
    static readonly string Db   = Environment.GetEnvironmentVariable("ACORE_DB_WORLD") ?? "acore_world";

    static readonly (uint bit, string name)[] Bits =
    {
        (16, "Trainer"), (32, "ClassTrainer"), (64, "ProfTrainer"),
        (128, "Vendor"), (256, "AmmoVendor"), (512, "FoodVendor"), (1024, "PoisonVendor"),
        (2048, "ReagentVendor"), (4096, "Repair"), (8192, "Flightmaster"), (65536, "Innkeeper"),
    };
    const uint Mask = 16 | 32 | 64 | 128 | 256 | 512 | 1024 | 2048 | 4096 | 8192 | 65536;

    class BinNpc { public uint Entry; public string Name; public uint Flag; public long Map; public double X, Y; }

    static void Main()
    {
        // ---- 1. data.bin ----
        var bin = new List<BinNpc>();
        var b = new SQLiteConnectionStringBuilder { DataSource = DataBinPath, Password = DataBinPassword, ReadOnly = true };
        using (var conn = new SQLiteConnection(b.ConnectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT entry, name, flag, map, x, y FROM npcs";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                bin.Add(new BinNpc
                {
                    Entry = Convert.ToUInt32(r.GetValue(0)),
                    Name = r.IsDBNull(1) ? "" : Convert.ToString(r.GetValue(1)),
                    Flag = Convert.ToUInt32(r.GetValue(2)),
                    Map = r.IsDBNull(3) ? -1 : Convert.ToInt64(r.GetValue(3)),
                    X = r.IsDBNull(4) ? 0 : Convert.ToDouble(r.GetValue(4)),
                    Y = r.IsDBNull(5) ? 0 : Convert.ToDouble(r.GetValue(5)),
                });
        }
        Console.WriteLine($"data.bin npcs: {bin.Count} total, {bin.Count(n => (n.Flag & Mask) != 0)} with vendor/trainer flags");

        // ---- 2. acore_world ----
        string cs = new MySqlConnectionStringBuilder { Server = Host, UserID = User, Password = Pass, Database = Db }.ConnectionString;
        var tmpl = new Dictionary<uint, (string name, uint npcflag)>();
        var spawned = new HashSet<uint>();
        var vendorStock = new Dictionary<uint, int>();
        var foodSellers = new HashSet<uint>();
        using (var conn = new MySqlConnection(cs))
        {
            conn.Open();
            using (var cmd = new MySqlCommand("SELECT entry, name, npcflag FROM creature_template", conn))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) tmpl[Convert.ToUInt32(r.GetValue(0))] = (r.GetString(1), Convert.ToUInt32(r.GetValue(2)));

            string entryCol = "id1";
            try { using var probe = new MySqlCommand("SELECT id1 FROM creature LIMIT 1", conn); probe.ExecuteScalar(); }
            catch { entryCol = "id"; }

            // Exclude event-gated spawns (same rule as GrindMobsExtractor — event-only NPCs aren't "there").
            using (var cmd = new MySqlCommand(
                $"SELECT DISTINCT `{entryCol}` FROM creature c WHERE NOT EXISTS " +
                "(SELECT 1 FROM game_event_creature g WHERE g.guid = c.guid AND g.eventEntry > 0)", conn))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) spawned.Add(Convert.ToUInt32(r.GetValue(0)));

            using (var cmd = new MySqlCommand("SELECT entry, COUNT(*) FROM npc_vendor GROUP BY entry", conn))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) vendorStock[Convert.ToUInt32(r.GetValue(0))] = Convert.ToInt32(r.GetValue(1));

            // item class 0 (Consumable), subclass 5 (Food & Drink) — the same classifier the bot uses.
            using (var cmd = new MySqlCommand(
                "SELECT DISTINCT nv.entry FROM npc_vendor nv JOIN item_template it ON it.entry = nv.item " +
                "WHERE it.class = 0 AND it.subclass = 5", conn))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) foodSellers.Add(Convert.ToUInt32(r.GetValue(0)));
        }
        Console.WriteLine($"server ({Db}): {tmpl.Count} templates, {spawned.Count} spawned entries (event-gated excluded), " +
                          $"{vendorStock.Count} vendors with stock, {foodSellers.Count} food/drink sellers\n");

        // ---- 3. diff ----
        var csv = new StringBuilder("entry,name,map,x,y,binFlag,dbFlag,problems\n");
        int ghosts = 0, unspawned = 0, flagLiars = 0, emptyVendors = 0, foodLiars = 0;
        var perBit = new Dictionary<string, int>();
        var reports = new List<(BinNpc n, string problems)>();

        foreach (var n in bin)
        {
            if ((n.Flag & Mask) == 0) continue;
            var problems = new List<string>();
            uint dbFlag = 0;
            if (!tmpl.TryGetValue(n.Entry, out var t))
            {
                problems.Add("GHOST(no creature_template)");
                ghosts++;
            }
            else
            {
                dbFlag = t.npcflag;
                bool lied = false;
                foreach (var (bit, name) in Bits)
                    if ((n.Flag & bit) != 0 && (t.npcflag & bit) == 0)
                    {
                        problems.Add($"bin:{name} denied by server");
                        perBit[name] = perBit.GetValueOrDefault(name) + 1;
                        lied = true;
                    }
                if (lied) flagLiars++;
                if (!spawned.Contains(n.Entry)) { problems.Add("UNSPAWNED/event-only"); unspawned++; }
                if ((n.Flag & (128 | 256 | 512 | 1024 | 2048)) != 0 && (t.npcflag & 128) != 0 && !vendorStock.ContainsKey(n.Entry))
                { problems.Add("vendor, EMPTY npc_vendor stock"); emptyVendors++; }
                if ((n.Flag & 512) != 0 && (t.npcflag & 128) != 0 && vendorStock.ContainsKey(n.Entry) && !foodSellers.Contains(n.Entry))
                { problems.Add("FoodVendor sells no food/drink"); foodLiars++; }
            }
            if (problems.Count > 0)
            {
                string p = string.Join("; ", problems);
                reports.Add((n, p));
                csv.AppendLine($"{n.Entry},\"{n.Name}\",{n.Map},{n.X:0},{n.Y:0},{n.Flag},{dbFlag},\"{p}\"");
            }
        }

        Console.WriteLine("=== SUMMARY (data.bin vendor/trainer rows with problems) ===");
        Console.WriteLine($"ghost entries (no creature_template on server): {ghosts}");
        Console.WriteLine($"flag liars (bin claims a role the server's npcflag denies): {flagLiars}");
        foreach (var kv in perBit.OrderByDescending(k => k.Value)) Console.WriteLine($"    {kv.Key}: {kv.Value}");
        Console.WriteLine($"unspawned / event-only: {unspawned}");
        Console.WriteLine($"vendors with EMPTY npc_vendor stock: {emptyVendors}");
        Console.WriteLine($"FoodVendors selling no food/drink: {foodLiars}");

        Console.WriteLine("\n=== spotlight: map 0 within 600yd of Northshire Abbey ===");
        foreach (var (n, p) in reports.Where(x => x.n.Map == 0 && Math.Abs(x.n.X - -8913) < 600 && Math.Abs(x.n.Y - -137) < 600))
            Console.WriteLine($"  [{n.Entry}] {n.Name} (binFlag {n.Flag}) — {p}");

        string outPath = Path.Combine(AppContext.BaseDirectory, "databin-audit.csv");
        File.WriteAllText(outPath, csv.ToString());
        Console.WriteLine($"\nfull report ({reports.Count} rows): {outPath}");

        // ---- 4. --fix: clear the server-denied role bits (conservative — never ADDS bits: adding
        // Trainer flags without trainer_class data could misroute trainer runs). BG/arena "unspawned"
        // rows and empty-stock vendors are left alone (script-spawned / runtime defense handles them).
        if (Environment.GetCommandLineArgs().Contains("--fix"))
        {
            var fixes = new Dictionary<uint, uint>();   // entry -> bits to clear
            foreach (var n in bin)
            {
                if ((n.Flag & Mask) == 0 || !tmpl.TryGetValue(n.Entry, out var t)) continue;
                uint denied = 0;
                foreach (var (bit, _) in Bits)
                    if ((n.Flag & bit) != 0 && (t.npcflag & bit) == 0) denied |= bit;
                if (denied != 0) fixes[n.Entry] = denied;
            }
            Console.WriteLine($"\n--fix: clearing denied bits on {fixes.Count} entries...");

            var wb = new SQLiteConnectionStringBuilder { DataSource = DataBinPath, Password = DataBinPassword };
            using var wconn = new SQLiteConnection(wb.ConnectionString);
            wconn.Open();
            using var tx = wconn.BeginTransaction();
            int rows = 0;
            foreach (var kv in fixes)
            {
                using var cmd = wconn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE npcs SET flag = flag & ~@denied WHERE entry = @entry";
                cmd.Parameters.AddWithValue("@denied", (long)kv.Value);
                cmd.Parameters.AddWithValue("@entry", (long)kv.Key);
                rows += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            Console.WriteLine($"--fix: updated {rows} rows across {fixes.Count} entries. Re-run without --fix to verify.");
        }
    }
}
