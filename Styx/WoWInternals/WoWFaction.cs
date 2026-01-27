#nullable disable
using System;
using System.Runtime.InteropServices;
using GreenMagic;
using Styx.Helpers;
using Styx.Patchables;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Represents a faction from Faction.dbc and FactionTemplate.dbc.
    /// </summary>
    public class WoWFaction
    {
        #region Fields

        private readonly FactionTemplateRecord _template;
        private FactionRecord _record;
        private string _name;
        private string _description;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a faction from a FactionTemplate ID.
        /// </summary>
        public WoWFaction(uint id) : this(id, true) { }

        /// <summary>
        /// Creates a faction from either FactionTemplate or Faction ID.
        /// </summary>
        /// <param name="id">The faction ID.</param>
        /// <param name="isTemplate">True if ID is from FactionTemplate.dbc, false if from Faction.dbc.</param>
        public WoWFaction(uint id, bool isTemplate)
        {
            Id = id;
            if (!IsValid)
                return;

            if (isTemplate)
            {
                // Get from FactionTemplate.dbc first
                var templateRow = StyxWoW.Db?[ClientDb.FactionTemplate]?.GetRow(id);
                if (templateRow != null)
                {
                    _template = templateRow.GetStruct<FactionTemplateRecord>();
                    // Then get the Faction record using FactionId from template
                    var factionRow = StyxWoW.Db?[ClientDb.Faction]?.GetRow((uint)_template.FactionId);
                    if (factionRow != null)
                    {
                        _record = factionRow.GetStruct<FactionRecord>();
                    }
                }
            }
            else
            {
                // Direct Faction.dbc lookup
                var factionRow = StyxWoW.Db?[ClientDb.Faction]?.GetRow(id);
                if (factionRow != null)
                {
                    _record = factionRow.GetStruct<FactionRecord>();
                }
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// The faction template ID.
        /// </summary>
        public uint Id { get; private set; }

        /// <summary>
        /// Whether this faction is valid.
        /// </summary>
        public bool IsValid => Id != 0;

        /// <summary>
        /// The Faction.dbc record.
        /// </summary>
        public FactionRecord Record => _record;

        /// <summary>
        /// The faction name.
        /// </summary>
        public string Name
        {
            get
            {
                if (_name == null)
                {
                    Memory wow = ObjectManager.Wow;
                    if (wow != null && _record.Name != 0)
                    {
                        try
                        {
                            _name = wow.Read<string>(_record.Name);
                        }
                        catch
                        {
                            _name = $"Faction_{Id}";
                        }
                    }
                    else
                    {
                        _name = $"Faction_{Id}";
                    }
                }
                return _name;
            }
        }

        /// <summary>
        /// The faction description.
        /// </summary>
        public string Description
        {
            get
            {
                if (_description == null)
                {
                    Memory wow = ObjectManager.Wow;
                    if (wow != null && _record.Description != 0)
                    {
                        try
                        {
                            _description = wow.Read<string>(_record.Description);
                        }
                        catch
                        {
                            _description = string.Empty;
                        }
                    }
                    else
                    {
                        _description = string.Empty;
                    }
                }
                return _description;
            }
        }

        /// <summary>
        /// The parent faction, if any.
        /// </summary>
        public WoWFaction ParentFaction
        {
            get
            {
                if (_record.ParentFactionId != 0)
                    return new WoWFaction(_record.ParentFactionId, false);
                return null;
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the reaction of this faction to another faction.
        /// </summary>
        public WoWUnitReaction RelationTo(WoWFaction other)
        {
            return CompareFactions(this, other);
        }

        /// <summary>
        /// Compares two factions to determine their relation.
        /// </summary>
        public static WoWUnitReaction CompareFactions(WoWFaction factionA, WoWFaction factionB)
        {
            // Null checks
            if (factionA == null || factionB == null)
                return WoWUnitReaction.Neutral;

            FactionTemplateRecord templateA = factionA._template;
            FactionTemplateRecord templateB = factionB._template;

            // Check if templates are valid (faction not found in DBC)
            if (templateA.FactionId == 0 || templateB.FactionId == 0)
                return WoWUnitReaction.Neutral;

            // Check if B's faction group is in A's enemy mask
            if ((templateB.FactionGroup & templateA.EnemyMask) != 0)
            {
                return WoWUnitReaction.Hostile;
            }

            // Check if B's faction ID is in A's enemy list
            for (int i = 0; i < 4; i++)
            {
                if (templateA.Enemies[i] == templateB.FactionId)
                {
                    return WoWUnitReaction.Hostile;
                }
                if (templateA.Enemies[i] == 0)
                {
                    break;
                }
            }

            // Check if B's faction group is in A's friend mask
            if ((templateB.FactionGroup & templateA.FriendMask) != 0)
            {
                return WoWUnitReaction.Friendly;
            }

            // Check if B's faction ID is in A's friend list
            for (int i = 0; i < 4; i++)
            {
                if (templateA.Friends[i] == templateB.FactionId)
                {
                    return WoWUnitReaction.Friendly;
                }
                if (templateA.Friends[i] == 0)
                {
                    break;
                }
            }

            // Check reverse: if A's faction group is in B's friend mask
            if ((templateA.FactionGroup & templateB.FriendMask) != 0)
            {
                return WoWUnitReaction.Friendly;
            }

            // Check reverse: if A's faction ID is in B's friend list
            for (int i = 0; i < 4; i++)
            {
                if (templateB.Friends[i] == templateA.FactionId)
                {
                    return WoWUnitReaction.Friendly;
                }
                if (templateB.Friends[i] == 0)
                {
                    break;
                }
            }

            // Default reaction based on flags
            // (flags >> 12) & 2 gives neutral/unfriendly status
            return (WoWUnitReaction)((~(byte)((uint)templateA.Flags >> 12) & 2) | 1);
        }

        public override string ToString()
        {
            if (!IsValid)
                return "N/A";

            var parent = ParentFaction;
            if (parent != null)
                return $"{Name} (Parent: {parent.Name})";
            return Name;
        }

        #endregion

        #region Static Factory

        /// <summary>
        /// Creates a WoWFaction from a Faction.dbc ID.
        /// </summary>
        public static WoWFaction FromId(uint id)
        {
            if (id == 0)
                return null;
            return new WoWFaction(id, false);
        }

        #endregion

        #region DBC Record Structures

        /// <summary>
        /// FactionTemplate.dbc record structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FactionTemplateRecord
        {
            public int Id;
            public int FactionId;
            public int Flags;
            public int FactionGroup;
            public int FriendMask;
            public int EnemyMask;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public int[] Enemies;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public int[] Friends;
        }

        /// <summary>
        /// Faction.dbc record structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FactionRecord
        {
            public int Id;
            public int RepGainId;
            public int Allied;
            public int AtWar;
            private readonly int _reserved0;
            private readonly int _reserved1;
            private readonly int _reserved2;
            private readonly int _reserved3;
            private readonly int _reserved4;
            private readonly int _reserved5;
            public int Reputation;
            public int Mod1;
            public int Mod2;
            public int Mod3;
            private readonly int _reserved6;
            private readonly int _reserved7;
            private readonly int _reserved8;
            private readonly int _reserved9;
            public uint ParentFactionId;
            private readonly int _reserved10;
            private readonly int _reserved11;
            private readonly int _reserved12;
            private readonly int _reserved13;
            public uint Name;
            public uint Description;
        }

        #endregion
    }
}
