#nullable disable
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Styx.Helpers;

namespace Styx.Database
{
    /// <summary>
    /// What every vendor actually SELLS, from VendorStock.db (built by Tools/VendorStockExtractor
    /// out of the server's npc_vendor table).
    ///
    /// This exists because an npcflag is a ROLE HINT, NOT AN INVENTORY: a General Supplies NPC carries
    /// AmmoVendor while stocking no projectiles. Without stock data the only way to learn what a vendor
    /// sells was to walk there and look — which made a barren vendor something to REMEMBER (a blacklist)
    /// instead of something never chosen. Answering "who stocks this?" before committing to the trip is
    /// the doctrine's lookahead rule, and it removes the latch rather than tuning it.
    ///
    /// Stock is static server data, so every answer is cached for the session.
    /// Missing DB ⇒ every query returns null = "no constraint known", and callers fall back to the
    /// flag-only behaviour. Degrading to the old path is correct; refusing to route is not.
    /// </summary>
    public static class VendorStock
    {
        private const string FileName = "VendorStock.db";

        // item_template.class / .subclass, as stored by the extractor.
        public const int ItemClassConsumable = 0;
        public const int ItemClassProjectile = 6;
        public const int ItemClassReagent = 9;
        public const int SubclassFoodDrink = 5;      // consumable/5
        public const int SubclassReagent = 1;        // consumable/1 — where some cores file reagents

        private static SQLiteConnection _connection;
        private static bool _initialized;
        private static bool _available;

        // Keyed by the full query shape: two errands asking different level caps are different answers.
        private static readonly Dictionary<(int cls, int sub, int maxLevel), HashSet<int>> _cache =
            new Dictionary<(int, int, int), HashSet<int>>();

        public static bool IsAvailable
        {
            get { EnsureInitialized(); return _available; }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                string dbPath = System.IO.Path.Combine(Logging.ApplicationPath, FileName);
                if (!System.IO.File.Exists(dbPath))
                {
                    // Loud, once: without it the bot silently reverts to walking to barren vendors, and
                    // "why is my hunter touring the continent" is then a mystery rather than a message.
                    Logging.Write("[VendorStock] {0} not found — vendor stock is unknown, errands fall back to "
                                + "npcflags only (build it with Tools/VendorStockExtractor).", dbPath);
                    _available = false;
                    return;
                }

                var builder = new SQLiteConnectionStringBuilder { DataSource = dbPath, ReadOnly = true };
                _connection = new SQLiteConnection(builder.ConnectionString);
                _connection.Open();

                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM vendor_items", _connection))
                    Logging.Write(LogLevel.Normal, "[VendorStock] loaded {0} vendor stock rows.", cmd.ExecuteScalar());

                _available = true;
            }
            catch (Exception ex)
            {
                Logging.Write("[VendorStock] failed to load {0}: {1}", FileName, ex.Message);
                Logging.WriteDebug("[VendorStock] Exception: {0}", ex);
                _available = false;
                _connection = null;
            }
        }

        /// <summary>
        /// NPC entries stocking an item of this class/subclass usable at <paramref name="maxReqLevel"/>
        /// or below. Null (never empty) when stock data is unavailable — null means "unconstrained",
        /// an empty set means "asked, and genuinely nobody sells it", and callers must treat those
        /// differently or a missing DB reads as a world with no vendors in it.
        /// </summary>
        /// <param name="subclass">-1 to accept any subclass of the class.</param>
        public static HashSet<int> VendorsStocking(int itemClass, int subclass, int maxReqLevel)
        {
            EnsureInitialized();
            if (!_available) return null;

            var key = (itemClass, subclass, maxReqLevel);
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var entries = new HashSet<int>();
            try
            {
                // ext_cost != 0 = priced in badges/honor/tokens. Coin can't buy it, so for "can this
                // vendor serve a restock" it is not stock at all.
                string sql = "SELECT DISTINCT vendor_entry FROM vendor_items "
                           + "WHERE item_class = @cls AND req_level <= @lvl AND ext_cost = 0"
                           + (subclass >= 0 ? " AND item_subclass = @sub" : "");
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@cls", itemClass);
                cmd.Parameters.AddWithValue("@lvl", maxReqLevel);
                if (subclass >= 0) cmd.Parameters.AddWithValue("@sub", subclass);
                using var r = cmd.ExecuteReader();
                while (r.Read()) entries.Add(r.GetInt32(0));
            }
            catch (Exception ex)
            {
                Logging.Write("[VendorStock] query failed (class {0}/{1}, level {2}): {3}",
                    itemClass, subclass, maxReqLevel, ex.Message);
                return null;   // unknown, not "nobody" — see the null/empty distinction above
            }

            _cache[key] = entries;
            return entries;
        }

        /// <summary>Entries stocking projectiles of this class usable at our level.</summary>
        public static HashSet<int> VendorsStockingProjectile(WoWItemProjectileClass projectile, int level)
        {
            if (projectile == WoWItemProjectileClass.None) return null;
            return VendorsStocking(ItemClassProjectile, (int)projectile, level);
        }

        /// <summary>Entries stocking food or drink usable at our level.</summary>
        public static HashSet<int> VendorsStockingFoodDrink(int level)
        {
            return VendorsStocking(ItemClassConsumable, SubclassFoodDrink, level);
        }

        /// <summary>Entries stocking spell reagents usable at our level.</summary>
        public static HashSet<int> VendorsStockingReagents(int level)
        {
            var byClass = VendorsStocking(ItemClassReagent, -1, level);
            var bySubclass = VendorsStocking(ItemClassConsumable, SubclassReagent, level);
            if (byClass == null) return bySubclass;
            if (bySubclass == null) return byClass;
            var union = new HashSet<int>(byClass);
            union.UnionWith(bySubclass);
            return union;
        }

        /// <summary>
        /// What this vendor sells of a given class/subclass, cheapest-usable-first by required level.
        /// For deciding WHAT to buy once the merchant is open, without trusting the client item cache.
        /// </summary>
        public static List<StockedItem> ItemsAt(int vendorEntry, int itemClass, int subclass, int maxReqLevel)
        {
            EnsureInitialized();
            var items = new List<StockedItem>();
            if (!_available) return items;

            try
            {
                string sql = "SELECT item_id, req_level, name, buy_price FROM vendor_items "
                           + "WHERE vendor_entry = @v AND item_class = @cls AND req_level <= @lvl AND ext_cost = 0"
                           + (subclass >= 0 ? " AND item_subclass = @sub" : "")
                           + " ORDER BY req_level DESC";
                using var cmd = new SQLiteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@v", vendorEntry);
                cmd.Parameters.AddWithValue("@cls", itemClass);
                cmd.Parameters.AddWithValue("@lvl", maxReqLevel);
                if (subclass >= 0) cmd.Parameters.AddWithValue("@sub", subclass);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new StockedItem
                    {
                        ItemId = (uint)r.GetInt32(0),
                        RequiredLevel = r.GetInt32(1),
                        Name = r.IsDBNull(2) ? "" : r.GetString(2),
                        BuyPrice = r.GetInt64(3),
                    });
            }
            catch (Exception ex)
            {
                Logging.Write("[VendorStock] item query failed for vendor {0}: {1}", vendorEntry, ex.Message);
            }
            return items;
        }
    }

    /// <summary>One row of a vendor's shelf, as the server states it.</summary>
    public class StockedItem
    {
        public uint ItemId;
        public int RequiredLevel;
        public string Name;
        public long BuyPrice;
    }
}
