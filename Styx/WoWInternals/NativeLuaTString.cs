#nullable disable
using System;
using System.Runtime.InteropServices;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Native structure representing a Lua string in memory.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 20)]
    public struct NativeLuaTString
    {
        /// <summary>
        /// Common GC header.
        /// </summary>
        public NativeLuaCommonHeader Header;

        /// <summary>
        /// Reserved byte.
        /// </summary>
        private byte _reserved1;

        /// <summary>
        /// Reserved byte.
        /// </summary>
        private byte _reserved2;

        /// <summary>
        /// Hash value for string comparison.
        /// </summary>
        public uint Hash;

        /// <summary>
        /// Length of the string.
        /// </summary>
        public IntPtr Length;
    }
}
