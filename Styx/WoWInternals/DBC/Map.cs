using System.Collections.Generic;
using System.Numerics;
using Styx.Patchables;

namespace Styx.WoWInternals.DBC
{
    /// <summary>
    /// Represents a WoW Map from the Map.dbc file.
    /// Based on HB 4.3.4 cleaned - Structure adapted for WotLK 3.3.5a.
    /// </summary>
    public class Map
    {
        private readonly WoWDb.Row? _row;
        private readonly MapDbc _data;
        private Map? _ghostEntranceMap;
        
        // List of raid/dungeon maps that allow mounts
        private static readonly HashSet<uint> MountAllowedDungeons = new HashSet<uint>
        {
            209,  // Zul'Farrak
            269,  // The Black Morass
            309,  // Zul'Gurub
            509,  // Ruins of Ahn'Qiraj
            534,  // The Battle for Mount Hyjal
            560,  // Old Hillsbrad Foothills
            568,  // Zul'Aman
            580,  // The Sunwell Plateau
            595,  // The Culling of Stratholme
            603,  // Ulduar
            615,  // The Obsidian Sanctum
            616   // The Eye of Eternity
        };
        
        // Continent map IDs
        private static readonly HashSet<uint> ContinentMaps = new HashSet<uint>
        {
            0,    // Eastern Kingdoms
            1,    // Kalimdor
            530,  // Outland
            571   // Northrend
        };
        
        /// <summary>
        /// Creates a Map instance from the specified Map ID.
        /// </summary>
        public Map(uint id)
        {
            var wow = ObjectManager.Wow;
            var table = StyxWoW.Db?[ClientDb.Map];
            _row = table?.GetRow(id);
            
            if (_row != null && _row.IsValid)
            {
                _data = _row.GetStruct<MapDbc>();
            }
            else
            {
                _data = default;
            }
        }
        
        /// <summary>
        /// Returns true if this Map data was successfully loaded from DBC.
        /// </summary>
        public bool IsValid => _row != null && _row.IsValid;
        
        /// <summary>
        /// The Map ID.
        /// </summary>
        public uint MapId => _data.MapId;
        
        /// <summary>
        /// The internal name of the map (e.g., "Azeroth", "Kalimdor").
        /// </summary>
        public string InternalName
        {
            get
            {
                var wow = ObjectManager.Wow;
                if (wow == null || _data.InternalNamePtr == 0) return string.Empty;
                try
                {
                    return wow.Read<string>(_data.InternalNamePtr);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }
        
        /// <summary>
        /// The type of map (Common, Instance, Raid, Battleground, Arena).
        /// </summary>
        public MapType MapType => (MapType)_data.MapTypeId;
        
        /// <summary>
        /// Map flags.
        /// </summary>
        public uint MapFlags => _data.MapFlags;
        
        /// <summary>
        /// Returns true if this map is a battleground.
        /// </summary>
        public bool IsBattleground => _data.IsBattleground == 1;
        
        /// <summary>
        /// The display name of the map.
        /// </summary>
        public string Name
        {
            get
            {
                var wow = ObjectManager.Wow;
                if (wow == null || _data.NamePtr == 0) return string.Empty;
                try
                {
                    return wow.Read<string>(_data.NamePtr);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }
        
        /// <summary>
        /// Area table ID for this map.
        /// </summary>
        public uint AreaTableId => _data.AreaTableId;
        
        /// <summary>
        /// The loading screen ID for this map.
        /// </summary>
        public uint LoadingScreenId => _data.LoadingScreenId;
        
        /// <summary>
        /// Battlefield map icon scale.
        /// </summary>
        public float BattlefieldMapIconScale => _data.BattlefieldMapIconScale;
        
        /// <summary>
        /// Ghost entrance map (the map you resurrect in if you die).
        /// </summary>
        public Map? GhostEntranceMap
        {
            get
            {
                if (!IsValid || _data.GhostEntranceMapId <= 0)
                    return null;
                    
                return _ghostEntranceMap ??= new Map((uint)_data.GhostEntranceMapId);
            }
        }
        
        /// <summary>
        /// Ghost entrance location on the ghost entrance map.
        /// </summary>
        public Vector2 GhostEntranceLocation => new Vector2(_data.GhostX, _data.GhostY);
        
        /// <summary>
        /// Time of day override (-1 = use normal time).
        /// </summary>
        public uint TimeOfDayOverride => _data.TimeOfDayOverride;
        
        /// <summary>
        /// Expansion ID (0 = Classic, 1 = TBC, 2 = WotLK).
        /// </summary>
        public uint ExpansionId => _data.ExpansionId;
        
        /// <summary>
        /// Raid reset offset.
        /// </summary>
        public uint RaidOffset => _data.RaidOffset;
        
        /// <summary>
        /// Maximum number of players for this map.
        /// </summary>
        public uint MaxPlayers => _data.MaxPlayers;
        
        /// <summary>
        /// Returns true if this is an arena map.
        /// </summary>
        public bool IsArena => MapType == MapType.Arena;
        
        /// <summary>
        /// Returns true if this is an instance (5-man dungeon).
        /// </summary>
        public bool IsInstance => MapType == MapType.Instance;
        
        /// <summary>
        /// Returns true if this is a raid.
        /// </summary>
        public bool IsRaid => MapType == MapType.Raid;
        
        /// <summary>
        /// Returns true if this is a dungeon (instance or raid).
        /// </summary>
        public bool IsDungeon => IsInstance || IsRaid;
        
        /// <summary>
        /// Returns true if mounts are allowed on this map.
        /// </summary>
        public bool IsMountAllowed => !IsDungeon || MountAllowedDungeons.Contains(MapId);
        
        /// <summary>
        /// Returns true if this is a continent map (open world).
        /// </summary>
        public bool IsContinent => !IsDungeon || ContinentMaps.Contains(MapId);
        
        public override string ToString() => InternalName;
        
        /// <summary>
        /// DBC structure for Map.dbc in WotLK 3.3.5a.
        /// Layout based on WoWDev wiki and HB 4.3.4.
        /// </summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        private struct MapDbc
        {
            public uint MapId;              // 0
            public uint InternalNamePtr;    // 1
            public uint MapTypeId;          // 2
            public uint MapFlags;           // 3
            private readonly uint _reserved1;  // 4
            public uint IsBattleground;     // 5
            public uint NamePtr;            // 6 (localized string ptr)
            public uint AreaTableId;        // 7
            private readonly uint _descPtr1;   // 8
            private readonly uint _descPtr2;   // 9
            public uint LoadingScreenId;    // 10
            public float BattlefieldMapIconScale; // 11
            public int GhostEntranceMapId;  // 12
            public float GhostX;            // 13
            public float GhostY;            // 14
            public uint TimeOfDayOverride;  // 15
            public uint ExpansionId;        // 16
            public uint RaidOffset;         // 17
            public uint MaxPlayers;         // 18
            private readonly uint _reserved2;  // 19
        }
    }
}
