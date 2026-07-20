using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using MySqlConnector;

// VendorStockExtractor — builds VendorStock.db (what every vendor actually SELLS) from a local
// AzerothCore 3.3.5a world DB.
//
// Why this exists: data.bin carries only npcflags, and an npcflag is a role hint, not an inventory —
// a General Supplies NPC carries AmmoVendor while stocking no projectiles. Without stock data the bot
// could only discover a vendor's inventory by walking to it, and a barren vendor had to be remembered
// (a blacklist) rather than never chosen. This DB lets the resolver ask "who sells Bullets I can use"
// BEFORE committing to the trip.
//
// Runtime consumer: Styx.Database.VendorStock. Copy VendorStock.db next to CopilotBuddy.exe.
// Must run x86 (System.Data.SQLite.dll in Lib/ is x86-only). Excluded from the main build (Tools/**).

class Program
{
    const string OutPath = "VendorStock.db";

    // --- MySQL connection: from env vars, with non-secret AzerothCore defaults. NEVER hardcode real
    // creds here — set ACORE_DB_HOST/PORT/USER/PASS in your environment to override. ---
    static readonly string MySqlHost = Environment.GetEnvironmentVariable("ACORE_DB_HOST") ?? "127.0.0.1";
    static readonly int    MySqlPort = int.TryParse(Environment.GetEnvironmentVariable("ACORE_DB_PORT"), out var p) ? p : 3306;
    static readonly string MySqlUser = Environment.GetEnvironmentVariable("ACORE_DB_USER") ?? "acore";
    static readonly string MySqlPass = Environment.GetEnvironmentVariable("ACORE_DB_PASS") ?? "acore";

    static void Main()
    {
        Console.WriteLine("=== VendorStockExtractor for AzerothCore 3.3.5a ===\n");

        string worldDb = DetectWorldSchema();
        Console.WriteLine($"World schema: {worldDb}");

        // Column names drift across cores/forks (ExtendedCost vs extendedcost, maxcount vs maxCount),
        // and npc_vendor.type only exists where currency vendors were backported. Detect, don't assume.
        var vendorCols = GetColumns(worldDb, "npc_vendor");
        var itemCols = GetColumns(worldDb, "item_template");
        string? extCostCol = Pick(vendorCols, "ExtendedCost", "extendedcost", "extended_cost");
        string? maxCountCol = Pick(vendorCols, "maxcount", "maxCount", "max_count");
        string? typeCol = Pick(vendorCols, "type");
        string? reqLevelCol = Pick(itemCols, "RequiredLevel", "requiredlevel", "required_level");
        string? buyPriceCol = Pick(itemCols, "BuyPrice", "buyprice", "buy_price");
        Console.WriteLine($"npc_vendor: ext_cost={extCostCol ?? "-"} max_count={maxCountCol ?? "-"} type={typeCol ?? "-"}");
        Console.WriteLine($"item_template: req_level={reqLevelCol ?? "-"} buy_price={buyPriceCol ?? "-"}\n");

        var rows = LoadVendorStock(worldDb, extCostCol, maxCountCol, typeCol, reqLevelCol, buyPriceCol);
        Console.WriteLine($"npc_vendor rows joined to item_template: {rows.Count}");

        BuildDatabase(rows, worldDb);
        Report(rows);

        Console.WriteLine($"\nDone. Wrote {Path.GetFullPath(OutPath)}.");
        Console.WriteLine("Copy VendorStock.db into the CopilotBuddy runtime root (next to data.bin).");
    }

    // -------------------------------------------------------------------------
    // Schema / column auto-detection
    // -------------------------------------------------------------------------
    static string DetectWorldSchema()
    {
        using var conn = new MySqlConnection(ConnStr("information_schema"));
        conn.Open();
        // Prefer a schema literally named like a world DB, else any schema that has npc_vendor.
        const string sql = @"
SELECT TABLE_SCHEMA,
       (CASE WHEN TABLE_SCHEMA LIKE '%world%' THEN 0 ELSE 1 END) AS pref
FROM information_schema.TABLES
WHERE TABLE_NAME = 'npc_vendor'
ORDER BY pref, TABLE_SCHEMA
LIMIT 1";
        using var cmd = new MySqlCommand(sql, conn);
        object? result = cmd.ExecuteScalar();
        if (result == null)
            throw new Exception("No schema with an 'npc_vendor' table found. Is the world DB imported?");
        return (string)result;
    }

    static HashSet<string> GetColumns(string worldDb, string table)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new MySqlConnection(ConnStr("information_schema"));
        conn.Open();
        using var cmd = new MySqlCommand(
            "SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@t", conn);
        cmd.Parameters.AddWithValue("@db", worldDb);
        cmd.Parameters.AddWithValue("@t", table);
        using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(0));
        if (cols.Count == 0)
            throw new Exception($"`{worldDb}`.{table} not found or has no columns.");
        return cols;
    }

    static string? Pick(HashSet<string> cols, params string[] candidates)
    {
        foreach (var c in candidates)
            if (cols.Contains(c)) return c;
        return null;
    }

    // -------------------------------------------------------------------------
    // npc_vendor JOIN item_template → stock rows
    // -------------------------------------------------------------------------
    static List<StockRow> LoadVendorStock(string worldDb, string? extCostCol, string? maxCountCol,
                                          string? typeCol, string? reqLevelCol, string? buyPriceCol)
    {
        Console.WriteLine("Reading npc_vendor JOIN item_template...");
        var rows = new List<StockRow>();

        using var conn = new MySqlConnection(ConnStr(worldDb));
        conn.Open();

        // npc_vendor.type 2 = currency (not an item_template row) where the core has the column at all —
        // excluded so the join can't silently drop or mis-key those. Missing column ⇒ item-only core.
        string typeFilter = typeCol != null ? $" WHERE nv.`{typeCol}` <> 2" : "";
        string sql = $@"
SELECT nv.entry AS vendor_entry,
       nv.item  AS item_id,
       it.class AS item_class,
       it.subclass AS item_subclass,
       {(reqLevelCol != null ? $"it.`{reqLevelCol}`" : "0")} AS req_level,
       {(buyPriceCol != null ? $"it.`{buyPriceCol}`" : "0")} AS buy_price,
       {(extCostCol  != null ? $"nv.`{extCostCol}`"  : "0")} AS ext_cost,
       {(maxCountCol != null ? $"nv.`{maxCountCol}`" : "0")} AS max_count,
       it.name AS item_name
FROM npc_vendor nv
JOIN item_template it ON it.entry = nv.item{typeFilter}";

        using var cmd = new MySqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new StockRow
            {
                VendorEntry = Convert.ToUInt32(r["vendor_entry"]),
                ItemId      = Convert.ToUInt32(r["item_id"]),
                ItemClass   = Convert.ToInt32(r["item_class"]),
                ItemSubclass= Convert.ToInt32(r["item_subclass"]),
                ReqLevel    = Convert.ToInt32(r["req_level"]),
                BuyPrice    = Convert.ToInt64(r["buy_price"]),
                ExtCost     = Convert.ToInt32(r["ext_cost"]),
                MaxCount    = Convert.ToInt32(r["max_count"]),
                Name        = r["item_name"] == DBNull.Value ? "" : Convert.ToString(r["item_name"]) ?? "",
            });
        }
        return rows;
    }

    // -------------------------------------------------------------------------
    // Write VendorStock.db
    // -------------------------------------------------------------------------
    static void BuildDatabase(List<StockRow> rows, string worldDb)
    {
        if (File.Exists(OutPath))
            File.Delete(OutPath);

        SQLiteConnection.CreateFile(OutPath);

        var builder = new SQLiteConnectionStringBuilder { DataSource = OutPath };
        using var conn = new SQLiteConnection(builder.ConnectionString);
        conn.Open();

        // ext_cost != 0 = priced in badges/honor/tokens, not coin: kept rather than dropped so the
        // runtime can say "he stocks it but we can't buy it" instead of silently seeing an empty shelf.
        // max_count > 0 = limited stock that can be sold out; 0 = unlimited.
        ExecNonQuery(conn, @"
CREATE TABLE vendor_items(
  vendor_entry  INTEGER NOT NULL,
  item_id       INTEGER NOT NULL,
  item_class    INTEGER NOT NULL,
  item_subclass INTEGER NOT NULL,
  req_level     INTEGER NOT NULL,
  buy_price     INTEGER NOT NULL,
  ext_cost      INTEGER NOT NULL,
  max_count     INTEGER NOT NULL,
  name          TEXT,
  PRIMARY KEY(vendor_entry, item_id));
CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT);");

        Console.WriteLine("Inserting vendor stock...");
        using (var tx = conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            // Same vendor+item can appear twice across fork data; the shelf is a set, so last write wins.
            cmd.CommandText = @"INSERT OR REPLACE INTO vendor_items
                (vendor_entry,item_id,item_class,item_subclass,req_level,buy_price,ext_cost,max_count,name)
                VALUES(@v,@i,@c,@s,@rl,@bp,@ec,@mc,@n)";
            AddP(cmd, "@v", "@i", "@c", "@s", "@rl", "@bp", "@ec", "@mc", "@n");
            foreach (var s in rows)
            {
                cmd.Parameters["@v"].Value = (int)s.VendorEntry;
                cmd.Parameters["@i"].Value = (int)s.ItemId;
                cmd.Parameters["@c"].Value = s.ItemClass;
                cmd.Parameters["@s"].Value = s.ItemSubclass;
                cmd.Parameters["@rl"].Value = s.ReqLevel;
                cmd.Parameters["@bp"].Value = s.BuyPrice;
                cmd.Parameters["@ec"].Value = s.ExtCost;
                cmd.Parameters["@mc"].Value = s.MaxCount;
                cmd.Parameters["@n"].Value = s.Name;
                cmd.ExecuteNonQuery();
            }

            using var meta = conn.CreateCommand();
            meta.Transaction = tx;
            meta.CommandText = "INSERT OR REPLACE INTO meta(key,value) VALUES(@k,@v)";
            AddP(meta, "@k", "@v");
            void SetMeta(string k, string v)
            {
                meta.Parameters["@k"].Value = k;
                meta.Parameters["@v"].Value = v;
                meta.ExecuteNonQuery();
            }
            SetMeta("source_schema", worldDb);
            SetMeta("generated_utc", DateTime.UtcNow.ToString("O"));
            SetMeta("row_count", rows.Count.ToString());

            tx.Commit();
        }

        Console.WriteLine("Creating indexes...");
        // The hot query is "which vendors stock class/subclass X at or below my level" — that index
        // order matches it exactly, so the lookup never scans the table.
        ExecNonQuery(conn, @"
CREATE INDEX ix_vendor_items_cat ON vendor_items(item_class, item_subclass, req_level);
CREATE INDEX ix_vendor_items_vendor ON vendor_items(vendor_entry);");
    }

    // -------------------------------------------------------------------------
    // Eyeball validation — the categories the bot actually routes errands for.
    // -------------------------------------------------------------------------
    static void Report(List<StockRow> rows)
    {
        var vendors = new HashSet<uint>();
        int ammoRows = 0, foodRows = 0, reagentRows = 0, extCostRows = 0;
        var ammoVendors = new HashSet<uint>();
        var foodVendors = new HashSet<uint>();
        var reagentVendors = new HashSet<uint>();

        foreach (var s in rows)
        {
            vendors.Add(s.VendorEntry);
            if (s.ExtCost != 0) extCostRows++;
            if (s.ItemClass == 6) { ammoRows++; ammoVendors.Add(s.VendorEntry); }                       // Projectile
            if (s.ItemClass == 0 && s.ItemSubclass == 5) { foodRows++; foodVendors.Add(s.VendorEntry); } // Food & Drink
            if (s.ItemClass == 9 || (s.ItemClass == 0 && s.ItemSubclass == 1))                          // Reagent
            { reagentRows++; reagentVendors.Add(s.VendorEntry); }
        }

        Console.WriteLine($"\n=== SUMMARY ===");
        Console.WriteLine($"distinct vendors with stock: {vendors.Count}");
        Console.WriteLine($"rows priced in tokens/honor (ext_cost != 0): {extCostRows}");
        Console.WriteLine($"projectiles (class 6): {ammoRows} rows across {ammoVendors.Count} vendors");
        Console.WriteLine($"food/drink (0/5):      {foodRows} rows across {foodVendors.Count} vendors");
        Console.WriteLine($"reagents (class 9):    {reagentRows} rows across {reagentVendors.Count} vendors");

        // Known-good spot check: the Thelsamar/Ironforge vendors from the ammo-routing bug report.
        Console.WriteLine("\n=== spotlight: vendors from the Loch Modan ammo bug ===");
        foreach (uint entry in new uint[] { 1682, 1469, 1685, 1687 })
        {
            var stock = new List<string>();
            foreach (var s in rows)
                if (s.VendorEntry == entry && s.ItemClass == 6)
                    stock.Add($"{s.Name}(sub {s.ItemSubclass}, req {s.ReqLevel})");
            int total = 0;
            foreach (var s in rows) if (s.VendorEntry == entry) total++;
            Console.WriteLine($"  [{entry}] {total} items total, projectiles: "
                            + (stock.Count == 0 ? "NONE" : string.Join(", ", stock)));
        }
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

struct StockRow
{
    public uint VendorEntry; public uint ItemId;
    public int ItemClass; public int ItemSubclass; public int ReqLevel;
    public long BuyPrice; public int ExtCost; public int MaxCount;
    public string Name;
}
