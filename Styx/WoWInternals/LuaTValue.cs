#nullable disable
using GreenMagic;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Wrapper class for reading tagged Lua values from WoW memory.
    /// Contains both value and type information.
    /// </summary>
    public class LuaTValue
    {
        private readonly NativeLuaTValue _native;
        private LuaValue _cachedValue;

        /// <summary>
        /// Creates a new LuaTValue wrapper from a memory address.
        /// </summary>
        internal LuaTValue(uint address)
            : this(address, ObjectManager.Wow.Read<NativeLuaTValue>(address))
        {
        }

        /// <summary>
        /// Creates a new LuaTValue wrapper with an existing native value.
        /// </summary>
        internal LuaTValue(uint address, NativeLuaTValue native)
        {
            Address = address;
            _native = native;
        }

        /// <summary>
        /// Gets the size of the native structure.
        /// </summary>
        public static int NativeSize => FastSize<NativeLuaTValue>.Size;

        /// <summary>
        /// Gets the memory address of this value.
        /// </summary>
        public uint Address { get; }

        /// <summary>
        /// Gets the type of this Lua value.
        /// </summary>
        public LuaType Type => _native.Type;

        /// <summary>
        /// Gets the underlying value wrapper.
        /// </summary>
        public LuaValue Value => _cachedValue ??= new LuaValue(Address);

        /// <summary>
        /// Returns a string representation of this tagged value.
        /// </summary>
        public override string ToString()
        {
            return Type switch
            {
                LuaType.Boolean => $"Bool: {Value.Bool}",
                LuaType.Number => $"Number: {Value.Double}",
                LuaType.String => $"String: {Value.String.Value}",
                LuaType.Table => $"Table: 0x{Value.Pointer:X8}",
                LuaType.Nil => "Nil",
                _ => $"Type: {Type}"
            };
        }
    }
}
