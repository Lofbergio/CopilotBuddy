using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GreenMagic;
using Styx.Helpers;
using Styx.Patchables;

namespace Styx.WoWInternals.DBC
{
    /// <summary>
    /// Represents a dungeon entry from LfgDungeons.dbc.
    /// Ported from HB 4.3.4 LfgDungeons — reads DBC rows from game memory.
    /// </summary>
    public class LfgDungeons
    {
        #region Fields

        private static Dictionary<uint, LfgDungeons>? _dungeons;
        private readonly WoWDb.Row? _row;
        private readonly LfgDungeonsRecord _record;
        private Map? _map;

        #endregion

        #region Constructor

        public LfgDungeons(uint id)
        {
            var table = StyxWoW.Db?[ClientDb.LfgDungeons];
            if (table == null) return;

            _row = table.GetRow(id);
            if (_row != null && _row.IsValid)
                _record = _row.GetStruct<LfgDungeonsRecord>();
        }

        #endregion

        #region Properties

        public bool IsValid => _row != null && _row.IsValid && _record.Id != 0;

        public uint Id => _record.Id;

        public string Name
        {
            get
            {
                var wow = ObjectManager.Wow;
                if (wow == null || _record.NamePtr == 0) return string.Empty;
                try { return wow.Read<string>(_record.NamePtr); }
                catch { return string.Empty; }
            }
        }

        public uint MinLevel => _record.MinLevel;

        public uint MaxLevel => _record.MaxLevel;

        public uint RecommendedLevel => _record.RecommendedLevel;

        public uint QueueMinLevel => _record.QueueMinLevel;

        public uint QueueMaxLevel => _record.QueueMaxLevel;

        public int MapId => _record.MapId;

        public Map? Map
        {
            get
            {
                if (MapId == -1) return null;
                return _map ??= new Map((uint)MapId);
            }
        }

        public uint Difficulty => _record.Difficulty;

        private uint Flags => _record.Flags;

        /// <summary>Flags & 4 — holiday event dungeon.</summary>
        public bool IsHolidayEvent => (Flags & 4U) != 0U;

        public uint TypeId => _record.TypeId;

        /// <summary>TypeId == 6 — random dungeon entry (e.g. "Random Wrath Heroic").</summary>
        public bool IsRandomDungeon => TypeId == 6;

        public uint FactionId => _record.FactionId;

        public string TextureFilename
        {
            get
            {
                var wow = ObjectManager.Wow;
                if (wow == null || _record.TextureFilenamePtr == 0) return string.Empty;
                try { return wow.Read<string>(_record.TextureFilenamePtr); }
                catch { return string.Empty; }
            }
        }

        public uint ExpansionId => _record.ExpansionId;

        public uint OrderIndex => _record.OrderIndex;

        public uint GroupId => _record.GroupId;

        public string Description
        {
            get
            {
                var wow = ObjectManager.Wow;
                if (wow == null || _record.DescriptionPtr == 0) return string.Empty;
                try { return wow.Read<string>(_record.DescriptionPtr); }
                catch { return string.Empty; }
            }
        }

        #endregion

        #region Static Access

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
        /// Gets all loaded LFG dungeons from the DBC.
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
        /// Forces a reload of the dungeon cache (e.g. after attaching to a new process).
        /// </summary>
        public static void InvalidateCache()
        {
            _dungeons = null;
        }

        /// <summary>
        /// Gets the number of random dungeons available via Lua.
        /// </summary>
        public static int RandomDungeonCount
        {
            get
            {
                try { return Lua.GetReturnVal<int>("return GetNumRandomDungeons()", 0); }
                catch { return 0; }
            }
        }

        /// <summary>
        /// Gets info about the LFD queue via Lua.
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

        #endregion

        #region Loading

        private static void EnsureLoaded()
        {
            if (_dungeons != null) return;
            _dungeons = new Dictionary<uint, LfgDungeons>();

            var table = StyxWoW.Db?[ClientDb.LfgDungeons];
            if (table == null || !table.IsLoaded)
            {
                Logging.WriteDiagnostic("[LfgDungeons] DBC table not available");
                return;
            }

            for (uint i = (uint)table.MinIndex; i <= (uint)table.MaxIndex; i++)
            {
                var dungeon = new LfgDungeons(i);
                if (dungeon.IsValid)
                    _dungeons[dungeon.Id] = dungeon;
            }

            Logging.WriteDiagnostic("[LfgDungeons] Loaded {0} dungeons from DBC", _dungeons.Count);
        }

        #endregion

        public override string ToString()
        {
            return $"[LfgDungeon: {Name} (ID: {Id}, MapId: {MapId}, Level: {MinLevel}-{MaxLevel})]";
        }

        #region DBC Record Layout

        /// <summary>
        /// LfgDungeons.dbc row layout. Field order from HB 4.3.4.
        /// String fields are uint pointers into the string block — dereference via Memory.Read&lt;string&gt;().
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct LfgDungeonsRecord
        {
            public uint Id;              // 0  — entry ID
            public uint NamePtr;         // 1  — string pointer
            public uint MinLevel;        // 2
            public uint MaxLevel;        // 3
            public uint RecommendedLevel;// 4
            public uint QueueMinLevel;   // 5
            public uint QueueMaxLevel;   // 6
            public int  MapId;           // 7  — -1 = invalid
            public uint Difficulty;      // 8  — 0=Normal, 1=Heroic
            public uint Flags;           // 9  — bitmask (0x4 = holiday)
            public uint TypeId;          // 10 — 6=Random, 3=Holiday event
            public uint FactionId;       // 11
            public uint TextureFilenamePtr; // 12 — string pointer
            public uint ExpansionId;     // 13 — 0=Classic, 1=BC, 2=WotLK
            public uint OrderIndex;      // 14
            public uint GroupId;         // 15
            public uint DescriptionPtr;  // 16 — string pointer
        }

        #endregion
    }
}
