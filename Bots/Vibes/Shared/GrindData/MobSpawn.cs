using Styx.Logic.Pathing;

namespace Bots.Vibes.Shared.GrindData
{
    /// <summary>
    /// One creature spawn from GrindMobs.db, with the metadata spot-selection needs.
    /// </summary>
    public readonly struct MobSpawn
    {
        public readonly uint Entry;
        public readonly WoWPoint Point;
        public readonly int MaxLevel;
        public readonly int Rank;       // 0 normal, 1 elite, 2 rareelite, 3 boss, 4 rare
        public readonly int Faction;

        public MobSpawn(uint entry, WoWPoint point, int maxLevel, int rank, int faction)
        {
            Entry = entry;
            Point = point;
            MaxLevel = maxLevel;
            Rank = rank;
            Faction = faction;
        }
    }
}
