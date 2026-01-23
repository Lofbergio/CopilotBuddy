#nullable disable
using System.Runtime.InteropServices;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Native structure representing a Lua table key.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NativeLuaTKey
    {
        /// <summary>
        /// The key value.
        /// </summary>
        public NativeLuaValue Value;

        /// <summary>
        /// The type of the key.
        /// </summary>
        public LuaType Type;

        /// <summary>
        /// Reserved field.
        /// </summary>
        private uint _reserved1;

        /// <summary>
        /// Pointer to the next node in the hash chain.
        /// </summary>
        public uint NextNodePtr;

        /// <summary>
        /// Reserved field.
        /// </summary>
        private uint _reserved2;
    }
}
