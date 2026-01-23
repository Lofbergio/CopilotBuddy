#nullable disable
using System;
using System.Runtime.InteropServices;
using Styx.Patchables;
using Styx.WoWInternals.WoWObjects;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Represents a faction template from FactionTemplate.dbc.
    /// Used to determine reactions between units based on faction masks and relationships.
    /// </summary>
    public class WoWFactionTemplate
    {
        private WoWFactionTemplate(uint id, WoWDb.Row row)
        {
            Id = id;
            Record = row.GetStruct<FactionTemplateDbcRecord>();
        }

        public uint Id { get; private set; }

        public WoWFaction Faction => WoWFaction.FromId(Record.FactionId);

        public FactionTemplateDbcRecord Record { get; private set; }

        /// <summary>
        /// Converts faction standing value to WoWUnitReaction.
        /// </summary>
        private WoWUnitReaction GetReactionFromStanding(FactionStanding standing)
        {
            int totalRep = standing.TotalReputation;
            
            if (totalRep >= 42000) return WoWUnitReaction.Exalted;
            if (totalRep >= 21000) return WoWUnitReaction.Revered;
            if (totalRep >= 9000) return WoWUnitReaction.Honored;
            if (totalRep >= 3000) return WoWUnitReaction.Friendly;
            if (totalRep >= 0) return WoWUnitReaction.Neutral;
            if (totalRep >= -3000) return WoWUnitReaction.Unfriendly;
            if (totalRep >= -6000) return WoWUnitReaction.Hostile;
            return WoWUnitReaction.Hated;
        }

        /// <summary>
        /// Gets the reaction of this faction template towards a specific unit.
        /// Takes into account player controlled units, PvP flags, and reputation.
        /// </summary>
        public WoWUnitReaction GetReactionTowards(WoWUnit unit)
        {
            var otherFaction = unit.FactionTemplate;
            if (otherFaction == null)
                return WoWUnitReaction.Neutral;

            // Special handling for player-controlled units
            if (unit.PlayerControlled)
            {
                var controllingPlayer = unit.ControllingPlayer;
                if (controllingPlayer != null && controllingPlayer.IsMe)
                {
                    // Contested PvP flag check (bit 12 in FactionFlags)
                    if ((Record.FactionFlags & 4096U) != 0U && controllingPlayer.ContestedPvPFlagged)
                        return WoWUnitReaction.Hostile;

                    // Check reputation-based reaction (skip if unit ignores reputation via flags)
                    if (ObjectManager.Me.GetFactionStanding(Faction, out var standing))
                    {
                        return GetReactionFromStanding(standing);
                    }
                }
            }

            return GetReactionTowards(otherFaction);
        }

        /// <summary>
        /// Gets the reaction between two faction templates.
        /// Based on faction masks, enemy lists, and friendly lists.
        /// </summary>
        public WoWUnitReaction GetReactionTowards(WoWFactionTemplate otherFaction)
        {
            var myRecord = Record;
            var theirRecord = otherFaction.Record;

            // Check if their fight support mask matches our hostile mask
            if ((theirRecord.FightSupport & myRecord.HostileMask) != 0U)
                return WoWUnitReaction.Hostile;

            // Check our enemy factions list
            for (int i = 0; i < 4 && myRecord.EnemyFactions[i] != 0U; i++)
            {
                if (myRecord.EnemyFactions[i] == theirRecord.FactionId)
                    return WoWUnitReaction.Hostile;
            }

            // Check if their fight support mask matches our friendly mask
            if ((theirRecord.FightSupport & myRecord.FriendlyMask) != 0U)
                return WoWUnitReaction.Friendly;

            // Check our friendly factions list
            for (int i = 0; i < 4 && myRecord.FriendlyFactions[i] != 0U; i++)
            {
                if (myRecord.FriendlyFactions[i] == theirRecord.FactionId)
                    return WoWUnitReaction.Friendly;
            }

            // Check if our fight support mask matches their friendly mask
            if ((myRecord.FightSupport & theirRecord.FriendlyMask) != 0U)
                return WoWUnitReaction.Friendly;

            // Check their friendly factions list
            for (int i = 0; i < 4 && theirRecord.FriendlyFactions[i] != 0U; i++)
            {
                if (theirRecord.FriendlyFactions[i] == myRecord.FactionId)
                    return WoWUnitReaction.Friendly;
            }

            // Default: Neutral (bit manipulation from original: (~(FactionFlags >> 12) & 2) | 1 → 1 or 3)
            uint defaultReaction = (~(myRecord.FactionFlags >> 12) & 2U) | 1U;
            return (WoWUnitReaction)defaultReaction;
        }

        /// <summary>
        /// Creates a WoWFactionTemplate from a faction template ID.
        /// </summary>
        public static WoWFactionTemplate FromId(uint id)
        {
            var db = StyxWoW.Db[ClientDb.FactionTemplate];
            if (db == null) return null;

            var row = db.GetRow(id);
            if (row == null || !row.IsValid) return null;

            return new WoWFactionTemplate(id, row);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FactionTemplateDbcRecord
        {
            public uint Id;
            public uint FactionId;
            public uint FactionFlags;
            public uint FightSupport;
            public uint FriendlyMask;
            public uint HostileMask;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] EnemyFactions;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] FriendlyFactions;
        }
    }
}
