#nullable disable
using GreenMagic;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Wrapper class for reading Lua table keys from WoW memory.
    /// </summary>
    public class LuaTKey
    {
        private readonly NativeLuaTKey _native;
        private LuaValue _cachedValue;
        private LuaNode _cachedNext;
        private bool _nextInitialized;

        /// <summary>
        /// Creates a new LuaTKey wrapper from a memory address.
        /// </summary>
        internal LuaTKey(uint address)
        {
            Address = address;
            _native = ObjectManager.Wow.Read<NativeLuaTKey>(address);
        }

        /// <summary>
        /// Gets the size of the native structure.
        /// </summary>
        public static int NativeSize => FastSize<NativeLuaTKey>.Size;

        /// <summary>
        /// Gets the memory address of this key.
        /// </summary>
        public uint Address { get; }

        /// <summary>
        /// Gets the type of this key.
        /// </summary>
        public LuaType Type => _native.Type;

        /// <summary>
        /// Gets the value of this key.
        /// </summary>
        public LuaValue Value => _cachedValue ??= new LuaValue(Address);

        /// <summary>
        /// Gets the next node in the hash chain, or null if this is the last node.
        /// </summary>
        public LuaNode Next
        {
            get
            {
                if (!_nextInitialized)
                {
                    _cachedNext = _native.NextNodePtr != 0 ? new LuaNode(_native.NextNodePtr) : null;
                    _nextInitialized = true;
                }
                return _cachedNext;
            }
        }
    }
}
