#nullable disable
using System.Runtime.InteropServices;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Common header structure for Lua garbage-collected objects.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 10)]
    public struct NativeLuaCommonHeader
    {
        /// <summary>
        /// Internal header data (next pointer, type info, gc flags).
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        private byte[] _headerData;
    }
}
