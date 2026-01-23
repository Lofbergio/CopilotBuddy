#nullable disable
using GreenMagic;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Wrapper class for reading Lua table nodes from WoW memory.
    /// A node contains both a key and a value in the hash part of a table.
    /// </summary>
    public class LuaNode
    {
        private readonly NativeLuaNode _native;
        private LuaTValue _cachedValue;
        private LuaTKey _cachedKey;

        /// <summary>
        /// Creates a new LuaNode wrapper from a memory address.
        /// </summary>
        internal LuaNode(uint address)
        {
            Address = address;
            _native = ObjectManager.Wow.Read<NativeLuaNode>(address);
        }

        /// <summary>
        /// Gets the size of the native structure.
        /// </summary>
        public static int NativeSize => FastSize<NativeLuaNode>.Size;

        /// <summary>
        /// Gets the memory address of this node.
        /// </summary>
        public uint Address { get; }

        /// <summary>
        /// Gets the value stored at this node.
        /// </summary>
        public LuaTValue Value => _cachedValue ??= new LuaTValue(Address);

        /// <summary>
        /// Gets the key for this node.
        /// </summary>
        public LuaTKey Key => _cachedKey ??= new LuaTKey(Address + (uint)LuaTValue.NativeSize);
    }
}
