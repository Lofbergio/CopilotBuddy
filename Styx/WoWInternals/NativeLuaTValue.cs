#nullable disable
using System.Runtime.InteropServices;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Native structure representing a tagged Lua value (value + type information).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
    public struct NativeLuaTValue
    {
        /// <summary>
        /// The actual value data.
        /// </summary>
        public NativeLuaValue Value;

        /// <summary>
        /// The type of the Lua value.
        /// </summary>
        public LuaType Type;

        /// <summary>
        /// Padding/reserved field.
        /// </summary>
        private uint _reserved;
    }
}
