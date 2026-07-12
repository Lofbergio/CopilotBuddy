using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MySqlConnector;

// WeaponMasterExtractor — export weapon masters (who, where, which proficiencies they teach) from the
// live acore_world DB into Data\WeaponMasters.xml for the bot's weapon-skill training gate.
// Selection is by TAUGHT SPELL (the 3.3.5a weapon-proficiency spell set below), not by subname —
// then subname='Weapon Master' is cross-checked and any mismatch is printed for a human to judge.
// Event-gated spawns are excluded (the GrindMobsExtractor rule: eventEntry>0 NPCs aren't "there").

class Program
{
    static readonly string Host = Environment.GetEnvironmentVariable("ACORE_DB_HOST") ?? "127.0.0.1";
    static readonly string User = Environment.GetEnvironmentVariable("ACORE_DB_USER") ?? "acore";
    static readonly string Pass = Environment.GetEnvironmentVariable("ACORE_DB_PASS") ?? "acore";
    static readonly string Db   = Environment.GetEnvironmentVariable("ACORE_DB_WORLD") ?? "acore_world";

    // 3.3.5a weapon-proficiency spells → skill line id + name (wands are innate, never trained).
    static readonly Dictionary<uint, (uint line, string name)> Proficiencies = new Dictionary<uint, (uint, string)>
    {
        {   196, ( 44, "One-Handed Axes")   },
        {   197, (172, "Two-Handed Axes")   },
        {   198, ( 54, "One-Handed Maces")  },
        {   199, (160, "Two-Handed Maces")  },
        {   200, (229, "Polearms")          },
        {   201, ( 43, "One-Handed Swords") },
        {   202, ( 55, "Two-Handed Swords") },
        {   227, (136, "Staves")            },
        {   264, ( 45, "Bows")              },
        {   266, ( 46, "Guns")              },
        {  1180, (173, "Daggers")           },
        {  2567, (176, "Thrown")            },
        {  5011, (226, "Crossbows")         },
        { 15590, (473, "Fist Weapons")      },
    };

    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0]
            : @"E:\!Games\World of Warcraft\CopilotBuddy\Data\WeaponMasters.xml";

        string cs = new MySqlConnectionStringBuilder { Server = Host, UserID = User, Password = Pass, Database = Db }.ConnectionString;
        using var conn = new MySqlConnection(cs);
        conn.Open();

        // trainer schema probe: acore uses trainer/trainer_spell/creature_default_trainer; old repacks npc_trainer.
        bool modern = TableExists(conn, "creature_default_trainer") && TableExists(conn, "trainer_spell");
        Console.WriteLine($"trainer schema: {(modern ? "trainer_spell + creature_default_trainer" : "npc_trainer")}");

        // creature entry → taught proficiency (spell, cost)
        var teaches = new Dictionary<uint, List<(uint spell, uint cost)>>();
        string inList = string.Join(",", Proficiencies.Keys);
        string teachSql = modern
            ? "SELECT cdt.CreatureId, ts.SpellId, ts.MoneyCost FROM trainer_spell ts " +
              $"JOIN creature_default_trainer cdt ON cdt.TrainerId = ts.TrainerId WHERE ts.SpellId IN ({inList})"
            : $"SELECT ID, SpellID, MoneyCost FROM npc_trainer WHERE SpellID IN ({inList})";
        using (var cmd = new MySqlCommand(teachSql, conn))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                uint entry = Convert.ToUInt32(r.GetValue(0));
                if (!teaches.TryGetValue(entry, out var list)) teaches[entry] = list = new List<(uint, uint)>();
                list.Add((Convert.ToUInt32(r.GetValue(1)), Convert.ToUInt32(r.GetValue(2))));
            }
        Console.WriteLine($"trainers teaching weapon proficiencies: {teaches.Count}");

        // names + subname cross-check + faction (for the Alliance/Horde side tag)
        var names = new Dictionary<uint, (string name, string sub, uint faction)>();
        using (var cmd = new MySqlCommand("SELECT entry, name, IFNULL(subname,''), faction FROM creature_template", conn))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names[Convert.ToUInt32(r.GetValue(0))] = (r.GetString(1), r.GetString(2), Convert.ToUInt32(r.GetValue(3)));

        foreach (var kv in names.Where(n => n.Value.sub.Contains("Weapon Master", StringComparison.OrdinalIgnoreCase)))
            if (!teaches.ContainsKey(kv.Key))
                Console.WriteLine($"  ⚠ subname 'Weapon Master' but teaches nothing in the set: {kv.Key} {kv.Value.name}");
        foreach (var e in teaches.Keys)
            if (names.TryGetValue(e, out var n) && !n.sub.Contains("Weapon Master", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"  note: teaches weapon skills without the subname: {e} {n.name} '{n.sub}'");

        // spawns, event-ghosts excluded
        string entryCol = "id1";
        try { using var probe = new MySqlCommand("SELECT id1 FROM creature LIMIT 1", conn); probe.ExecuteScalar(); }
        catch { entryCol = "id"; }
        var spawns = new List<(uint entry, int map, double x, double y, double z)>();
        using (var cmd = new MySqlCommand(
            $"SELECT `{entryCol}`, map, position_x, position_y, position_z FROM creature c " +
            $"WHERE `{entryCol}` IN ({string.Join(",", teaches.Keys)}) AND NOT EXISTS " +
            "(SELECT 1 FROM game_event_creature g WHERE g.guid = c.guid AND g.eventEntry > 0)", conn))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                spawns.Add((Convert.ToUInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)),
                    Convert.ToDouble(r.GetValue(2)), Convert.ToDouble(r.GetValue(3)), Convert.ToDouble(r.GetValue(4))));
        Console.WriteLine($"live spawns: {spawns.Count}");
        foreach (var e in teaches.Keys.Where(e => !spawns.Any(s => s.entry == e)))
            Console.WriteLine($"  ⚠ no live spawn (event-only or unspawned): {e} {(names.TryGetValue(e, out var n) ? n.name : "?")}");

        // one <Master> per SPAWN (an entry can spawn in several places); skills nested
        var root = new XElement("WeaponMasters",
            new XAttribute("generated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")),
            new XAttribute("source", Db));
        foreach (var s in spawns.OrderBy(s => s.map).ThenBy(s => s.entry))
        {
            names.TryGetValue(s.entry, out var n);
            string side = FactionSide(s.entry, n.faction);
            var m = new XElement("Master",
                new XAttribute("entry", s.entry),
                new XAttribute("name", n.name ?? s.entry.ToString()),
                new XAttribute("side", side),
                new XAttribute("map", s.map),
                new XAttribute("x", s.x.ToString("F1", CultureInfo.InvariantCulture)),
                new XAttribute("y", s.y.ToString("F1", CultureInfo.InvariantCulture)),
                new XAttribute("z", s.z.ToString("F1", CultureInfo.InvariantCulture)));
            foreach (var (spell, cost) in teaches[s.entry].OrderBy(t => t.spell))
                m.Add(new XElement("Skill",
                    new XAttribute("spell", spell),
                    new XAttribute("line", Proficiencies[spell].line),
                    new XAttribute("name", Proficiencies[spell].name),
                    new XAttribute("cost", cost)));
            root.Add(m);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        root.Save(outPath);
        Console.WriteLine($"wrote {outPath} ({spawns.Count} masters)");
    }

    // Faction-template → team for the city trainers this export actually yields (weapon masters live in
    // capitals). Unknown ids print loudly and tag side="Unknown" — the runtime treats that as unusable,
    // so a bad mapping can never walk a bot into a hostile city.
    static readonly Dictionary<uint, string> FactionSides = new Dictionary<uint, string>
    {
        {   11, "Alliance" },  // Stormwind
        {   12, "Alliance" },  // Stormwind
        {   55, "Alliance" },  // Ironforge
        {   57, "Alliance" },  // Ironforge
        {   79, "Alliance" },  // Darnassus
        {   80, "Alliance" },  // Darnassus
        { 1638, "Alliance" },  // Exodar
        {   29, "Horde" },     // Orgrimmar
        {   65, "Horde" },     // Orgrimmar
        {  876, "Horde" },     // Darkspear troll (Hanashi, Orgrimmar Valley of Honor)
        {   68, "Horde" },     // Undercity
        {   71, "Horde" },     // Undercity
        {  104, "Horde" },     // Thunder Bluff
        {  105, "Horde" },     // Thunder Bluff
        { 1604, "Horde" },     // Silvermoon
    };

    static string FactionSide(uint entry, uint faction)
    {
        if (FactionSides.TryGetValue(faction, out string side)) return side;
        Console.WriteLine($"  ⚠ unmapped faction {faction} on {entry} — tagged Unknown (runtime will skip it)");
        return "Unknown";
    }

    static bool TableExists(MySqlConnection conn, string table)
    {
        using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @t", conn);
        cmd.Parameters.AddWithValue("@t", table);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }
}
