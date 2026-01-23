#nullable disable
using System.Text;
using GreenMagic;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Wrapper class for reading Lua strings from WoW memory.
    /// </summary>
    public class LuaTString
    {
        private readonly NativeLuaTString _native;
        private string _cachedValue;

        /// <summary>
        /// Creates a new LuaTString wrapper from a memory address.
        /// </summary>
        internal LuaTString(uint address)
        {
            Address = address;
            _native = ObjectManager.Wow.Read<NativeLuaTString>(address);
        }

        /// <summary>
        /// Gets the size of the native structure.
        /// </summary>
        public static int NativeSize => FastSize<NativeLuaTString>.Size;

        /// <summary>
        /// Gets the memory address of this string.
        /// </summary>
        public uint Address { get; }

        /// <summary>
        /// Gets the hash value of this string.
        /// </summary>
        public uint Hash => _native.Hash;

        /// <summary>
        /// Gets the string value.
        /// </summary>
        public string Value
        {
            get
            {
                if (_cachedValue == null)
                {
                    int length = (int)_native.Length;
                    if (length > 0 && length < 10000)
                    {
                        uint stringDataAddress = Address + (uint)NativeSize;
                        _cachedValue = ObjectManager.Wow.ReadString(Encoding.UTF8, stringDataAddress, length);
                    }
                    else
                    {
                        _cachedValue = string.Empty;
                    }
                }
                return _cachedValue;
            }
        }

        /// <summary>
        /// Returns the string value.
        /// </summary>
        public override string ToString() => Value;
    }
}
