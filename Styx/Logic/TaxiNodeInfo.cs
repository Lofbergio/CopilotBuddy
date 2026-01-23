// TaxiNodeInfo.cs - Reads TaxiNodes.dbc for flight path locations
// WotLK 3.3.5a compatible - Uses WoWDb to read DBC data
// Structure based on wowdev.wiki TaxiNodes.dbc format

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Styx.Logic.Pathing;
using Styx.Patchables;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic
{
    /// <summary>
    /// TaxiNodeInfo - Represents a flight path node from TaxiNodes.dbc
    /// Contains world location, name, and faction-specific mount information
    /// </summary>
    public class TaxiNodeInfo
    {
        #region Fields

        private readonly TaxiNodeRecord _record;
        private string _name;

        #endregion

        #region Constructors

        /// <summary>
        /// Create TaxiNodeInfo from DBC row
        /// </summary>
        public TaxiNodeInfo(WoWDb.Row row)
        {
            if (row != null && row.IsValid)
            {
                _record = row.GetStruct<TaxiNodeRecord>();
            }
            Location = new WoWPoint(_record.X, _record.Y, _record.Z);
        }

        /// <summary>
        /// Create TaxiNodeInfo by ID
        /// </summary>
        public TaxiNodeInfo(uint id) : this(GetRow(id))
        {
        }

        #endregion

        #region Properties

        /// <summary>
        /// Whether this node is valid
        /// </summary>
        public bool IsValid => Id != 0;

        /// <summary>
        /// Node ID from TaxiNodes.dbc
        /// </summary>
        public int Id => _record.Id;

        /// <summary>
        /// Map/Continent ID where this node is located
        /// </summary>
        public int MapId => _record.MapId;

        /// <summary>
        /// World location of the flight master (real coordinates)
        /// </summary>
        public WoWPoint Location { get; private set; }

        /// <summary>
        /// Name of the flight path (e.g., "Stormwind, Elwynn")
        /// Read from string pointer in memory
        /// </summary>
        public string Name
        {
            get
            {
                if (_name == null)
                {
                    if (_record.NamePtr == 0U)
                    {
                        _name = string.Empty;
                    }
                    else
                    {
                        try
                        {
                            _name = ObjectManager.Wow?.Read<string>(_record.NamePtr) ?? string.Empty;
                        }
                        catch
                        {
                            _name = string.Empty;
                        }
                    }
                }
                return _name;
            }
        }

        /// <summary>
        /// Creature ID for Horde flight mount
        /// </summary>
        public int HordeMountCreatureId => _record.HordeMountId;

        /// <summary>
        /// Creature ID for Alliance flight mount
        /// </summary>
        public int AllianceMountCreatureId => _record.AllianceMountId;

        /// <summary>
        /// True if this is an Alliance-only flight path (no Horde mount)
        /// </summary>
        public bool AllianceOnly => HordeMountCreatureId == 0;

        /// <summary>
        /// True if this is a Horde-only flight path (no Alliance mount)
        /// </summary>
        public bool HordeOnly => AllianceMountCreatureId == 0;

        /// <summary>
        /// True if this is a valid flight path (has at least one mount)
        /// </summary>
        public bool IsFlightPath => !AllianceOnly || !HordeOnly;

        #endregion

        #region Static Methods

        /// <summary>
        /// Get DBC table for TaxiNodes
        /// </summary>
        private static WoWDb.DbTable GetTable()
        {
            return StyxWoW.Db?[ClientDb.TaxiNodes];
        }

        /// <summary>
        /// Get row from TaxiNodes.dbc by ID
        /// </summary>
        private static WoWDb.Row GetRow(uint id)
        {
            return GetTable()?.GetRow(id);
        }

        /// <summary>
        /// Get TaxiNodeInfo by ID
        /// </summary>
        public static TaxiNodeInfo FromId(uint id)
        {
            var row = GetRow(id);
            if (row == null || !row.IsValid)
                return null;
            return new TaxiNodeInfo(row);
        }

        /// <summary>
        /// Find TaxiNodeInfo by name (partial match)
        /// </summary>
        public static TaxiNodeInfo FindByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var table = GetTable();
            if (table == null)
                return null;

            for (uint i = (uint)table.MinIndex; i <= (uint)table.MaxIndex; i++)
            {
                var row = table.GetRow(i);
                if (row == null || !row.IsValid)
                    continue;

                var node = new TaxiNodeInfo(row);
                if (node.IsValid && !string.IsNullOrEmpty(node.Name) &&
                    node.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// Get all known taxi nodes
        /// </summary>
        public static List<TaxiNodeInfo> GetAll()
        {
            var result = new List<TaxiNodeInfo>();
            var table = GetTable();
            if (table == null)
                return result;

            for (uint i = (uint)table.MinIndex; i <= (uint)table.MaxIndex; i++)
            {
                var row = table.GetRow(i);
                if (row == null || !row.IsValid)
                    continue;

                var node = new TaxiNodeInfo(row);
                if (node.IsValid)
                    result.Add(node);
            }

            return result;
        }

        #endregion

        #region Overrides

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("ID: {0}, Map: {1}, Name: {2}", Id, MapId, Name);

            if (Location == WoWPoint.Empty)
                sb.Append(", Type: Transport");
            else
                sb.AppendFormat(", Location: {0}", Location);

            return sb.ToString();
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// TaxiNodeRecord - Raw DBC structure for TaxiNodes.dbc (WotLK 3.3.5a)
        /// Based on wowdev.wiki documentation
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TaxiNodeRecord
        {
            /// <summary>Node ID</summary>
            public readonly int Id;

            /// <summary>Map/Continent ID</summary>
            public readonly int MapId;

            /// <summary>X world coordinate</summary>
            public readonly float X;

            /// <summary>Y world coordinate</summary>
            public readonly float Y;

            /// <summary>Z world coordinate</summary>
            public readonly float Z;

            /// <summary>Pointer to localized name string</summary>
            public readonly uint NamePtr;

            /// <summary>Horde flight mount creature ID</summary>
            public readonly int HordeMountId;

            /// <summary>Alliance flight mount creature ID</summary>
            public readonly int AllianceMountId;
        }

        #endregion
    }
}
