using System.Collections.Generic;

namespace Styx.WoWInternals.DBC
{
    /// <summary>
    /// FEAT-36: Represents a dungeon entry from LfgDungeons.dbc.
    /// WotLK 3.3.5a introduced the LFD (Looking for Dungeon) system in patch 3.3.
    /// Ported from HB 4.3.4 LfgDungeons.
    /// </summary>
    public class LfgDungeons
    {
        private static Dictionary<uint, LfgDungeons>? _dungeons;

        public uint Id { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public uint MinLevel { get; private set; }
        public uint MaxLevel { get; private set; }
        public uint RecommendedLevel { get; private set; }
        public uint MapId { get; private set; }
        public int Difficulty { get; private set; }
        public uint Flags { get; private set; }
        public uint TypeId { get; private set; }
        public uint FactionId { get; private set; }
        public string Description { get; private set; } = string.Empty;
        public uint ExpansionId { get; private set; }
        public uint GroupId { get; private set; }
        public uint OrderIndex { get; private set; }

        /// <summary>
        /// Whether this is a holiday event dungeon.
        /// </summary>
        public bool IsHolidayEvent => TypeId == 3;

        /// <summary>
        /// Whether this is a random dungeon entry (e.g., "Random Classic Dungeon").
        /// </summary>
        public bool IsRandomDungeon => TypeId == 6;

        /// <summary>
        /// Gets the Map entry for this dungeon's map.
        /// </summary>
        public Map? Map
        {
            get
            {
                try
                {
                    // Would need Map.GetById or similar
                    return null;
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// Gets a dungeon by its LFG ID.
        /// </summary>
        public static LfgDungeons? GetById(uint id)
        {
            EnsureLoaded();
            if (_dungeons != null && _dungeons.TryGetValue(id, out var dungeon))
                return dungeon;
            return null;
        }

        /// <summary>
        /// Gets all loaded LFG dungeons.
        /// </summary>
        public static IReadOnlyDictionary<uint, LfgDungeons> All
        {
            get
            {
                EnsureLoaded();
                return _dungeons ?? new Dictionary<uint, LfgDungeons>();
            }
        }

        /// <summary>
        /// Gets the number of random dungeons available via Lua.
        /// </summary>
        public static int RandomDungeonCount
        {
            get
            {
                try
                {
                    return Lua.GetReturnVal<int>("return GetNumRandomDungeons()", 0);
                }
                catch { return 0; }
            }
        }

        /// <summary>
        /// Gets info about the LFD queue via Lua.
        /// Returns (hasData, leaderNeeds, tankNeeds, healerNeeds, dpsNeeds).
        /// </summary>
        public static (bool hasData, int tank, int healer, int dps) GetLFGQueueStats()
        {
            try
            {
                var results = Lua.GetReturnValues(
                    "local h,l,t,he,d = GetLFGQueueStats(); return h and 1 or 0, t or 0, he or 0, d or 0");
                if (results != null && results.Count >= 4)
                {
                    return (
                        Lua.ParseLuaValue<int>(results[0]) != 0,
                        Lua.ParseLuaValue<int>(results[1]),
                        Lua.ParseLuaValue<int>(results[2]),
                        Lua.ParseLuaValue<int>(results[3])
                    );
                }
            }
            catch { }
            return (false, 0, 0, 0);
        }

        private static void EnsureLoaded()
        {
            if (_dungeons != null) return;
            _dungeons = new Dictionary<uint, LfgDungeons>();
            // DBC loading would go here with proper offsets
        }

        public override string ToString()
        {
            return $"[LfgDungeon: {Name} (ID: {Id}, MapId: {MapId}, Level: {MinLevel}-{MaxLevel})]";
        }
    }
}
