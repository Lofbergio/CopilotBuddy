using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.Vibes.Shared.QuestData
{
    public class VendorDataLoader
    {
        private VendorDatabase _database;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public VendorDatabase Database => _database;

        public bool Load()
        {
            if (_database != null)
                return true;

            string dataFile = FindDataFile();
            if (!File.Exists(dataFile))
                return false;

            string json = File.ReadAllText(dataFile);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            VendorDatabase db = new VendorDatabase();

            if (root.TryGetProperty("Vendors", out JsonElement vendors))
            {
                foreach (JsonElement ve in vendors.EnumerateArray())
                    db.Vendors.Add(ParseVendorEntry(ve));
            }

            _database = db;
            return true;
        }

        private static VendorEntry ParseVendorEntry(JsonElement ve)
        {
            VendorEntry v = new VendorEntry
            {
                Type = ve.GetProperty("Type").GetString() ?? "",
                Entry = ve.GetProperty("Entry").GetInt32(),
                Name = ve.GetProperty("Name").GetString() ?? "",
                X = ve.GetProperty("X").GetDouble(),
                Y = ve.GetProperty("Y").GetDouble(),
                Z = ve.GetProperty("Z").GetDouble(),
                Map = ve.GetProperty("Map").GetInt32()
            };
            if (ve.TryGetProperty("TrainClass", out JsonElement tc))
                v.TrainClass = tc.GetString();
            return v;
        }

        public List<VendorEntry> GetNearestVendors(LocalPlayer me, string type, int count = 5, HashSet<int> blacklist = null)
        {
            if (_database == null)
                return new List<VendorEntry>();

            int playerMap = (int)me.MapId;
            string playerClass = me.Class.ToString();

            return _database.Vendors
                .Where(v => v.Map == playerMap && v.Type == type)
                .Where(v => type != "Train" || string.IsNullOrEmpty(v.TrainClass) || v.TrainClass == playerClass)
                .Where(v => blacklist == null || !blacklist.Contains(v.Entry))
                .OrderBy(v => Math.Sqrt(
                    (v.X - me.Location.X) * (v.X - me.Location.X) +
                    (v.Y - me.Location.Y) * (v.Y - me.Location.Y)))
                .Take(count)
                .ToList();
        }

        private static string FindDataFile()
        {
            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                string path = Path.Combine(asmDir, "vendor_data.json");
                if (File.Exists(path))
                    return path;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "Bots", "VibeQuester", "vendor_data.json"),
                Path.Combine(baseDir, "Plugins", "VibeQuester", "vendor_data.json"),
                Path.Combine(Environment.CurrentDirectory, "vendor_data.json")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(Environment.CurrentDirectory, "vendor_data.json");
        }
    }
}
