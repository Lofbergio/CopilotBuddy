#nullable disable
using System.Runtime.InteropServices;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Native structure representing a node in a Lua table's hash part.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NativeLuaNode
    {
        /// <summary>
        /// The value stored at this node.
        /// </summary>
        public NativeLuaTValue Value;

        /// <summary>
        /// The key for this node.
        /// </summary>
        public NativeLuaTKey Key;
    }
}
