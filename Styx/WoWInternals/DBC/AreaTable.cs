using System.Collections.Generic;
using Styx.Helpers;

namespace Styx.WoWInternals.DBC
{
    /// <summary>
    /// FEAT-36: Represents an area/zone from the AreaTable.dbc.
    /// WotLK 3.3.5a structure.
    /// Ported from HB 4.3.4 AreaTable.
    /// </summary>
    public class AreaTable
    {
        private static Dictionary<uint, AreaTable>? _areas;

        public uint AreaId { get; private set; }
        public uint MapId { get; private set; }
        public uint ParentAreaId { get; private set; }
        public uint ExplorationBit { get; private set; }
        public uint Flags { get; private set; }
        public int AreaLevel { get; private set; }
        public string AreaName { get; private set; } = string.Empty;
        public uint FactionGroupMask { get; private set; }

        /// <summary>
        /// Whether this area is a city (sanctuary, inn, etc.).
        /// </summary>
        public bool IsCity => (Flags & 0x20) != 0; // AREA_FLAG_CITY

        /// <summary>
        /// Whether this area is a sanctuary (no PvP).
        /// </summary>
        public bool IsSanctuary => (Flags & 0x800) != 0; // AREA_FLAG_SANCTUARY

        /// <summary>
        /// Whether PvP is forced in this area.
        /// </summary>
        public bool IsPvPZone => (Flags & 0x40) != 0;

        /// <summary>
        /// Whether this is a sub-zone (has a parent).
        /// </summary>
        public bool IsSubZone => ParentAreaId != 0;

        /// <summary>
        /// Gets an AreaTable entry by area ID.
        /// Uses Lua fallback since DBC memory access requires offsets.
        /// </summary>
        public static AreaTable? GetAreaById(uint areaId)
        {
            EnsureLoaded();
            if (_areas != null && _areas.TryGetValue(areaId, out var area))
                return area;
            return null;
        }

        /// <summary>
        /// Gets the area name for a given area ID via Lua.
        /// </summary>
        public static string GetAreaName(uint areaId)
        {
            try
            {
                var results = Lua.GetReturnValues($"return GetAreaInfo({areaId})");
                if (results != null && results.Count > 0)
                    return results[0] ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        /// <summary>
        /// Gets the current zone name.
        /// </summary>
        public static string CurrentZoneName
        {
            get
            {
                try
                {
                    return Lua.GetReturnVal<string>("return GetZoneText()", 0) ?? string.Empty;
                }
                catch { return string.Empty; }
            }
        }

        /// <summary>
        /// Gets the current sub-zone name.
        /// </summary>
        public static string CurrentSubZoneName
        {
            get
            {
                try
                {
                    return Lua.GetReturnVal<string>("return GetSubZoneText()", 0) ?? string.Empty;
                }
                catch { return string.Empty; }
            }
        }

        /// <summary>
        /// Gets the current real zone text (minimap zone).
        /// </summary>
        public static string CurrentMinimapZone
        {
            get
            {
                try
                {
                    return Lua.GetReturnVal<string>("return GetMinimapZoneText()", 0) ?? string.Empty;
                }
                catch { return string.Empty; }
            }
        }

        private static void EnsureLoaded()
        {
            if (_areas != null) return;
            _areas = new Dictionary<uint, AreaTable>();
            // DBC loading would go here with proper offsets
            // For now areas are looked up via Lua on demand
        }

        public override string ToString()
        {
            return $"[Area: {AreaName} (ID: {AreaId}, MapId: {MapId})]";
        }
    }
}
