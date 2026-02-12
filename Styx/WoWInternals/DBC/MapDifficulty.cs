using System.Collections.Generic;

namespace Styx.WoWInternals.DBC
{
    /// <summary>
    /// FEAT-36: Represents a map difficulty entry from MapDifficulty.dbc.
    /// Contains info about instance difficulty settings (normal/heroic, max players, etc.).
    /// Ported from HB 4.3.4 MapDifficulty.
    /// </summary>
    public class MapDifficulty
    {
        private static Dictionary<uint, MapDifficulty>? _entries;

        public uint Id { get; private set; }
        public uint MapId { get; private set; }
        public int Difficulty { get; private set; }
        public string AreaTriggerText { get; private set; } = string.Empty;
        public uint RaidDuration { get; private set; }
        public uint MaxPlayers { get; private set; }
        public string DifficultyString { get; private set; } = string.Empty;

        /// <summary>
        /// Whether this is heroic difficulty.
        /// In WotLK: 0=Normal 5-man, 1=Heroic 5-man, 0=10-man raid, 1=25-man raid,
        /// 2=10-man heroic, 3=25-man heroic.
        /// </summary>
        public bool IsHeroic => Difficulty == 1 || Difficulty == 2 || Difficulty == 3;

        /// <summary>
        /// Whether this is a raid difficulty (10-man or 25-man).
        /// </summary>
        public bool IsRaid => MaxPlayers >= 10;

        /// <summary>
        /// Gets the duration as a human-readable string.
        /// </summary>
        public string DurationString
        {
            get
            {
                if (RaidDuration == 0) return "None";
                int days = (int)(RaidDuration / 86400);
                return days > 0 ? $"{days} day(s)" : $"{RaidDuration / 3600} hour(s)";
            }
        }

        /// <summary>
        /// Gets a MapDifficulty entry by its ID.
        /// </summary>
        public static MapDifficulty? GetById(uint id)
        {
            EnsureLoaded();
            if (_entries != null && _entries.TryGetValue(id, out var entry))
                return entry;
            return null;
        }

        /// <summary>
        /// Gets MapDifficulty entries for a specific map.
        /// </summary>
        public static List<MapDifficulty> GetByMapId(uint mapId)
        {
            EnsureLoaded();
            var results = new List<MapDifficulty>();
            if (_entries != null)
            {
                foreach (var entry in _entries.Values)
                {
                    if (entry.MapId == mapId)
                        results.Add(entry);
                }
            }
            return results;
        }

        /// <summary>
        /// Gets the current instance difficulty via Lua.
        /// Returns (difficulty, maxPlayers): 1=Normal, 2=Heroic for 5-man; 1=10man, 2=25man for raids.
        /// </summary>
        public static (int difficulty, int maxPlayers) GetCurrentDifficulty()
        {
            try
            {
                var results = Lua.GetReturnValues(
                    "local d = GetInstanceDifficulty(); " +
                    "local _,_,diff,_,max = GetInstanceInfo(); " +
                    "return d or 0, max or 0");
                if (results != null && results.Count >= 2)
                {
                    return (Lua.ParseLuaValue<int>(results[0]),
                            Lua.ParseLuaValue<int>(results[1]));
                }
            }
            catch { }
            return (0, 0);
        }

        /// <summary>
        /// Gets all loaded entries.
        /// </summary>
        public static IReadOnlyDictionary<uint, MapDifficulty> All
        {
            get
            {
                EnsureLoaded();
                return _entries ?? new Dictionary<uint, MapDifficulty>();
            }
        }

        private static void EnsureLoaded()
        {
            if (_entries != null) return;
            _entries = new Dictionary<uint, MapDifficulty>();
            // DBC loading would go here with proper offsets
        }

        public override string ToString()
        {
            return $"[MapDifficulty: Map={MapId}, Difficulty={Difficulty}, MaxPlayers={MaxPlayers}]";
        }
    }
}
