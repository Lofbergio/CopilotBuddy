using System.Collections.Generic;

namespace VibeQuester
{
    public class VendorDatabase
    {
        public List<VendorEntry> Vendors { get; set; } = new List<VendorEntry>();
    }

    public class VendorEntry
    {
        public string Type { get; set; }
        public int Entry { get; set; }
        public string Name { get; set; }
        public string TrainClass { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public int Map { get; set; }
    }

    public class QuestDatabase
    {
        public List<QuestEntry> Quests { get; set; } = new List<QuestEntry>();
        public List<QuestGiverEntry> QuestGivers { get; set; } = new List<QuestGiverEntry>();
        public List<QuestEnderEntry> QuestEnders { get; set; } = new List<QuestEnderEntry>();
        public Dictionary<string, List<SpawnPoint>> CreatureSpawns { get; set; } = new Dictionary<string, List<SpawnPoint>>();
        public Dictionary<string, List<SpawnPoint>> GameObjectSpawns { get; set; } = new Dictionary<string, List<SpawnPoint>>();
    }

    public class ZoneQuestData
    {
        public int ZoneId { get; set; }
        public string ZoneName { get; set; }
        public List<int> SubzoneIds { get; set; } = new List<int>();
        public ZoneBoundary Boundary { get; set; }
        public List<QuestEntry> Quests { get; set; } = new List<QuestEntry>();
        public List<QuestGiverEntry> QuestGivers { get; set; } = new List<QuestGiverEntry>();
        public List<QuestEnderEntry> QuestEnders { get; set; } = new List<QuestEnderEntry>();
        public Dictionary<string, List<SpawnPoint>> CreatureSpawns { get; set; } = new Dictionary<string, List<SpawnPoint>>();
        public Dictionary<string, List<SpawnPoint>> GameObjectSpawns { get; set; } = new Dictionary<string, List<SpawnPoint>>();
    }

    public class ZoneBoundary
    {
        public double X1 { get; set; }
        public double X2 { get; set; }
        public double Y1 { get; set; }
        public double Y2 { get; set; }
    }

    public class QuestEntry
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int QuestLevel { get; set; }
        public int MinLevel { get; set; }
        public int AllowableRaces { get; set; }
        public int AllowableClasses { get; set; }
        public int Flags { get; set; }
        public int QuestSortID { get; set; }
        public int QuestInfoID { get; set; }
        public int RequiredFactionId1 { get; set; }
        public int RequiredFactionId2 { get; set; }
        public int PrevQuestID { get; set; }
        public int NextQuestID { get; set; }
        public int ExclusiveGroup { get; set; }
        public int SpecialFlags { get; set; }
        public int StartItem { get; set; }
        public List<int> PreviousQuestsIds { get; set; } = new List<int>();
        public List<QuestObjective> Objectives { get; set; } = new List<QuestObjective>();
    }

    public class QuestObjective
    {
        public ObjectiveType Type { get; set; }
        public int MobId { get; set; }
        public int ItemId { get; set; }
        public int GameObjectId { get; set; }
        public string GameObjectName { get; set; }
        public int KillCount { get; set; }
        public int CollectCount { get; set; }
        public int Index { get; set; }
        public int MobLevel { get; set; }   // creature maxlevel of the objective mob (0 if none); for the level gate
        // Explore only: the areatrigger centre + radius the character must enter (server fires credit).
        public int Map { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Radius { get; set; }
    }

    public enum ObjectiveType
    {
        KillMob,
        CollectItem,
        CollectFromGameObject,
        TurnInOnly,
        Explore
    }

    public class QuestGiverEntry
    {
        public int QuestId { get; set; }
        public int GiverId { get; set; }
        public string GiverName { get; set; }
        public QuestObjectType GiverType { get; set; }
    }

    public class QuestEnderEntry
    {
        public int QuestId { get; set; }
        public int EnderId { get; set; }
        public string EnderName { get; set; }
        public QuestObjectType EnderType { get; set; }
    }

    public enum QuestObjectType
    {
        Creature,
        GameObject
    }

    public class SpawnPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public int Map { get; set; }
    }

    public class ZoneIndex
    {
        public int ZoneId { get; set; }
        public string ZoneName { get; set; }
        public string FileName { get; set; }
        public List<int> SubzoneIds { get; set; } = new List<int>();
    }
}
