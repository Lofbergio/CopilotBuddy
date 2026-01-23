#nullable disable
using System.Runtime.InteropServices;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Native structure representing a Lua table in memory.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NativeLuaTable
    {
        /// <summary>
        /// Common GC header.
        /// </summary>
        public NativeLuaCommonHeader Header;

        /// <summary>
        /// Table flags.
        /// </summary>
        public byte Flags;

        /// <summary>
        /// Log2 of the hash table size.
        /// </summary>
        private byte _log2HashSize;

        /// <summary>
        /// Pointer to the metatable.
        /// </summary>
        public uint MetaTablePtr;

        /// <summary>
        /// Pointer to the array part values.
        /// </summary>
        public uint ValuesPtr;

        /// <summary>
        /// Pointer to the hash part nodes.
        /// </summary>
        public uint NodePtr;

        /// <summary>
        /// Reserved field.
        /// </summary>
        private uint _reserved1;

        /// <summary>
        /// Reserved field.
        /// </summary>
        private uint _reserved2;

        /// <summary>
        /// Size of the array part.
        /// </summary>
        public uint ArraySize;

        /// <summary>
        /// Gets the number of nodes in the hash part.
        /// </summary>
        public uint NodesCount => 1U << _log2HashSize;
    }
}
