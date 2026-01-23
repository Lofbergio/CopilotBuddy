using System.Runtime.InteropServices;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Internal structure for WoW bag representation in memory.
    /// Ported from HB 3.3.5a ns6.Struct23
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BagStructure
    {
        /// <summary>
        /// Number of slots in the bag
        /// </summary>
        public readonly uint Slots;

        /// <summary>
        /// Base address of bag items in memory
        /// </summary>
        public readonly uint ItemsBaseAddress;

        /// <summary>
        /// GUID of the bag container
        /// </summary>
        public readonly ulong Guid;

        /// <summary>
        /// Flag indicating if this is an inventory bag (1) or physical bag (0)
        /// </summary>
        public readonly byte IsInventory;
    }
}
