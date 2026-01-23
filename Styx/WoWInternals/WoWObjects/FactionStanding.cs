using System;
using System.Runtime.InteropServices;

namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Represents a player's standing with a faction.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FactionStanding
    {
        /// <summary>
        /// The faction ID this standing is for.
        /// </summary>
        public uint FactionId;

        /// <summary>
        /// Reputation flags for this faction.
        /// </summary>
        [MarshalAs(UnmanagedType.U1)]
        public ReputationFlags Flags;

        /// <summary>
        /// Padding bytes.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        private byte[] _padding;

        /// <summary>
        /// Base reputation value.
        /// </summary>
        public int Reputation;

        /// <summary>
        /// Bonus reputation from buffs, etc.
        /// </summary>
        public int ReputationBonus;

        /// <summary>
        /// Gets the total reputation (base + bonus).
        /// </summary>
        public int TotalReputation => Reputation + ReputationBonus;

        /// <summary>
        /// Checks if the specified reputation flag is set.
        /// </summary>
        public bool HasFlag(ReputationFlags flag)
        {
            return (Flags & flag) != 0;
        }
    }

    /// <summary>
    /// Reputation flags for faction standings.
    /// </summary>
    [Flags]
    public enum ReputationFlags : byte
    {
        None = 0x00,
        Visible = 0x01,
        AtWar = 0x02,
        Hidden = 0x04,
        Invisible = 0x08,
        Inactive = 0x10,
        ShowPropagated = 0x20
    }
}
