#nullable disable
using System.Runtime.InteropServices;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Native structure representing a Lua value in memory.
    /// Uses explicit layout to overlay different value interpretations.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 8)]
    public struct NativeLuaValue
    {
        /// <summary>
        /// The value interpreted as a pointer/address.
        /// </summary>
        [FieldOffset(0)]
        public uint Pointer;

        /// <summary>
        /// The value interpreted as a double precision number.
        /// </summary>
        [FieldOffset(0)]
        public double Number;

        /// <summary>
        /// The value interpreted as an integer (for boolean checks).
        /// </summary>
        [FieldOffset(0)]
        private int _intValue;

        /// <summary>
        /// Gets the value as a boolean.
        /// </summary>
        public bool Bool => _intValue != 0;
    }
}
