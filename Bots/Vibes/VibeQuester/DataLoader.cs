using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace VibeQuester
{
    // Loads quest structure from QuestData.db (regenerated from the AzerothCore world DB by
    // Tools/QuestDataExtractor). Creature spawn coordinates come from the shipped CreatureSpawns.db
    // (complete, 237k rows); gameobject coordinates come from QuestData.db's gameobject_spawns table.
    // SQLite access goes through the reflection-based Sqlite helper (the drop-in can't link
    // System.Data.SQLite at compile time). Populates the same QuestDatabase model the rest of the
    // bot already consumes, so ProfileBuilder and QuestScheduler are unchanged.
    public class DataLoader
    {
        private readonly string _dbFile;
        private QuestDatabase _database;

        public QuestDatabase Database => _database;

        public DataLoader()
        {
            _dbFile = FindDataFile();
        }

        private static string FindDataFile()
        {
            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                string path = Path.Combine(asmDir, "QuestData.db");
                if (File.Exists(path)) return path;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "Bots", "VibeQuester", "QuestData.db"),
                Path.Combine(Environment.CurrentDirectory, "QuestData.db")
            };
            foreach (string candidate in candidates)
                if (File.Exists(candidate)) return candidate;

            return Path.Combine(baseDir, "Bots", "VibeQuester", "QuestData.db");
        }

        public QuestDatabase Load()
        {
            if (_database != null) return _database;
            if (!File.Exists(_dbFile) || !Sqlite.Available) return null;

            var db = new QuestDatabase();
            var byId = LoadQuests(db);
            LoadObjectives(byId);
            LoadGivers(db);
            LoadEnders(db);
            LoadGameObjectSpawns(db);
            LoadCreatureSpawns(db); // from CreatureSpawns.db, only entries this dataset references

            _database = db;
            return _database;
        }

        private Dictionary<int, QuestEntry> LoadQuests(QuestDatabase db)
        {
            var byId = new Dictionary<int, QuestEntry>();
            foreach (var r in Sqlite.Query(_dbFile,
                "SELECT id,name,quest_level,min_level,allowable_races,allowable_classes,start_item," +
                "prev_quest_id,next_quest_id,exclusive_group,flags,special_flags FROM quests"))
            {
                var q = new QuestEntry
                {
                    Id = Sqlite.I(r[0]),
                    Name = Sqlite.S(r[1]),
                    QuestLevel = Sqlite.I(r[2]),
                    MinLevel = Sqlite.I(r[3]),
                    AllowableRaces = Sqlite.I(r[4]),
                    AllowableClasses = Sqlite.I(r[5]),
                    StartItem = Sqlite.I(r[6]),
                    PrevQuestID = Sqlite.I(r[7]),
                    NextQuestID = Sqlite.I(r[8]),
                    ExclusiveGroup = Sqlite.I(r[9]),
                    Flags = Sqlite.I(r[10]),
                    SpecialFlags = Sqlite.I(r[11])
                };
                db.Quests.Add(q);
                byId[q.Id] = q;
            }

            foreach (var r in Sqlite.Query(_dbFile, "SELECT quest_id,prev_quest_id FROM quest_prereqs"))
                if (byId.TryGetValue(Sqlite.I(r[0]), out var q))
                    q.PreviousQuestsIds.Add(Sqlite.I(r[1]));

            return byId;
        }

        private void LoadObjectives(Dictionary<int, QuestEntry> byId)
        {
            foreach (var r in Sqlite.Query(_dbFile,
                "SELECT quest_id,idx,type,mob_id,item_id,gameobject_id,count,mob_level FROM quest_objectives"))
            {
                if (!byId.TryGetValue(Sqlite.I(r[0]), out var q)) continue;
                if (!TryParseType(Sqlite.S(r[2]), out ObjectiveType type)) continue;
                int count = Sqlite.I(r[6]);
                q.Objectives.Add(new QuestObjective
                {
                    Index = Sqlite.I(r[1]),
                    Type = type,
                    MobId = Sqlite.I(r[3]),
                    ItemId = Sqlite.I(r[4]),
                    GameObjectId = Sqlite.I(r[5]),
                    KillCount = count,
                    CollectCount = count,
                    MobLevel = Sqlite.I(r[7])
                });
            }
        }

        private static bool TryParseType(string s, out ObjectiveType type)
        {
            switch (s)
            {
                case "KillMob": type = ObjectiveType.KillMob; return true;
                case "CollectItem": type = ObjectiveType.CollectItem; return true;
                case "CollectFromGameObject": type = ObjectiveType.CollectFromGameObject; return true;
                case "TurnInOnly": type = ObjectiveType.TurnInOnly; return true;
                default: type = ObjectiveType.TurnInOnly; return false;
            }
        }

        private void LoadGivers(QuestDatabase db)
        {
            foreach (var r in Sqlite.Query(_dbFile,
                "SELECT quest_id,giver_id,giver_name,is_gameobject FROM quest_givers"))
                db.QuestGivers.Add(new QuestGiverEntry
                {
                    QuestId = Sqlite.I(r[0]),
                    GiverId = Sqlite.I(r[1]),
                    GiverName = Sqlite.S(r[2]),
                    GiverType = Sqlite.I(r[3]) == 1 ? QuestObjectType.GameObject : QuestObjectType.Creature
                });
        }

        private void LoadEnders(QuestDatabase db)
        {
            foreach (var r in Sqlite.Query(_dbFile,
                "SELECT quest_id,ender_id,ender_name,is_gameobject FROM quest_enders"))
                db.QuestEnders.Add(new QuestEnderEntry
                {
                    QuestId = Sqlite.I(r[0]),
                    EnderId = Sqlite.I(r[1]),
                    EnderName = Sqlite.S(r[2]),
                    EnderType = Sqlite.I(r[3]) == 1 ? QuestObjectType.GameObject : QuestObjectType.Creature
                });
        }

        private void LoadGameObjectSpawns(QuestDatabase db)
        {
            foreach (var r in Sqlite.Query(_dbFile, "SELECT entry,map_id,x,y,z FROM gameobject_spawns"))
            {
                string key = Sqlite.I(r[0]).ToString();
                if (!db.GameObjectSpawns.TryGetValue(key, out var list))
                    db.GameObjectSpawns[key] = list = new List<SpawnPoint>();
                list.Add(new SpawnPoint { Map = Sqlite.I(r[1]), X = Sqlite.D(r[2]), Y = Sqlite.D(r[3]), Z = Sqlite.D(r[4]) });
            }
        }

        // Pull creature coords from the shipped CreatureSpawns.db for the entries this dataset
        // references (creature givers/enders + objective mobs), capped per entry. Keyed by entry
        // string to match what ProfileBuilder/QuestScheduler already read.
        private void LoadCreatureSpawns(QuestDatabase db)
        {
            string spawnsDb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CreatureSpawns.db");
            if (!File.Exists(spawnsDb)) return;

            var entries = new HashSet<int>();
            foreach (var g in db.QuestGivers) if (g.GiverType == QuestObjectType.Creature) entries.Add(g.GiverId);
            foreach (var e in db.QuestEnders) if (e.EnderType == QuestObjectType.Creature) entries.Add(e.EnderId);
            foreach (var q in db.Quests)
                foreach (var o in q.Objectives)
                    if (o.MobId > 0) entries.Add(o.MobId);
            if (entries.Count == 0) return;

            const int capPerEntry = 20;
            var ids = new List<int>(entries);
            for (int i = 0; i < ids.Count; i += 900)
            {
                int n = Math.Min(900, ids.Count - i);
                string inList = string.Join(",", ids.GetRange(i, n));
                foreach (var r in Sqlite.Query(spawnsDb, $"SELECT entry,map_id,x,y,z FROM spawns WHERE entry IN ({inList})"))
                {
                    string key = Sqlite.I(r[0]).ToString();
                    if (!db.CreatureSpawns.TryGetValue(key, out var list))
                        db.CreatureSpawns[key] = list = new List<SpawnPoint>();
                    if (list.Count >= capPerEntry) continue;
                    list.Add(new SpawnPoint { Map = Sqlite.I(r[1]), X = Sqlite.D(r[2]), Y = Sqlite.D(r[3]), Z = Sqlite.D(r[4]) });
                }
            }
        }
    }
}
