#nullable disable
using GreenMagic;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Wrapper class for reading Lua values from WoW memory.
    /// Provides typed access to the underlying NativeLuaValue structure.
    /// </summary>
    public class LuaValue
    {
        private readonly NativeLuaValue _native;
        private LuaTString _cachedString;
        private LuaTable _cachedTable;

        /// <summary>
        /// Creates a new LuaValue wrapper from a memory address.
        /// </summary>
        internal LuaValue(uint address)
        {
            Address = address;
            _native = ObjectManager.Wow.Read<NativeLuaValue>(address);
        }

        /// <summary>
        /// Gets the size of the native structure.
        /// </summary>
        public static int NativeSize => FastSize<NativeLuaValue>.Size;

        /// <summary>
        /// Gets the memory address of this value.
        /// </summary>
        public uint Address { get; }

        /// <summary>
        /// Gets the value as a double.
        /// </summary>
        public double Double => _native.Number;

        /// <summary>
        /// Gets the value as a boolean.
        /// </summary>
        public bool Bool => _native.Bool;

        /// <summary>
        /// Gets the value as a pointer.
        /// </summary>
        public uint Pointer => _native.Pointer;

        /// <summary>
        /// Gets the value as a Lua string.
        /// </summary>
        public LuaTString String => _cachedString ??= new LuaTString(_native.Pointer);

        /// <summary>
        /// Gets the value as a Lua table.
        /// </summary>
        public LuaTable Table => _cachedTable ??= new LuaTable(_native.Pointer);
    }
}
