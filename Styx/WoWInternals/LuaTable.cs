#nullable disable
using System.Text;
using GreenMagic;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Wrapper class for reading Lua tables from WoW memory.
    /// Provides access to both array and hash parts of a Lua table.
    /// </summary>
    public class LuaTable
    {
        private readonly NativeLuaTable _native;
        private LuaTable _cachedMetaTable;
        private bool _metaTableInitialized;

        // HB 3.3.5a offset for the dummy/empty node pointer
        private const uint DummyNodePtr = 10781312U;

        /// <summary>
        /// Creates a new LuaTable wrapper from a memory address.
        /// </summary>
        internal LuaTable(uint address)
        {
            Address = address;
            _native = ObjectManager.Wow.Read<NativeLuaTable>(address);
        }

        /// <summary>
        /// Gets the size of the native structure.
        /// </summary>
        public static int NativeSize => FastSize<NativeLuaTable>.Size;

        /// <summary>
        /// Gets the memory address of this table.
        /// </summary>
        public uint Address { get; }

        /// <summary>
        /// Gets the table flags.
        /// </summary>
        public byte Flags => _native.Flags;

        /// <summary>
        /// Gets the metatable for this table, or null if none.
        /// </summary>
        public LuaTable MetaTable
        {
            get
            {
                if (!_metaTableInitialized)
                {
                    _cachedMetaTable = _native.MetaTablePtr != 0 ? new LuaTable(_native.MetaTablePtr) : null;
                    _metaTableInitialized = true;
                }
                return _cachedMetaTable;
            }
        }

        /// <summary>
        /// Gets the number of values in the array part.
        /// </summary>
        internal uint ValuesCount => _native.ArraySize;

        /// <summary>
        /// Gets the number of nodes in the hash part.
        /// </summary>
        internal uint NodesCount => _native.NodesCount;

        /// <summary>
        /// Gets the count of elements in this table (Lua # operator behavior).
        /// </summary>
        public int Count => CalculateCount();

        /// <summary>
        /// Computes hash for a string key.
        /// </summary>
        private static uint HashString(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            uint hash = (uint)s.Length;
            uint step = (hash >> 5) + 1;
            for (uint i = hash; i >= step; i -= step)
            {
                hash ^= (hash << 5) + (hash >> 2) + bytes[i - 1];
            }
            return hash;
        }

        /// <summary>
        /// Computes hash for a numeric key.
        /// </summary>
        private static unsafe uint HashNumber(double n)
        {
            n += 1.0;
            uint* ptr = (uint*)(&n);
            return ptr[1] + *ptr;
        }

        /// <summary>
        /// Gets a field by string key.
        /// </summary>
        public LuaTValue GetField(string key)
        {
            uint hash = HashString(key);
            LuaNode node = GetNode(hash & (NodesCount - 1));

            while (node.Key.Type != LuaType.String || !string.Equals(key, node.Key.Value.String.Value))
            {
                node = node.Key.Next;
                if (node == null)
                    return null;
            }
            return node.Value;
        }

        /// <summary>
        /// Gets a field by numeric key.
        /// </summary>
        public LuaTValue GetField(double key)
        {
            uint intKey = (uint)key;
            double hashKey;

            if (intKey == key)
            {
                if (intKey < ValuesCount)
                    return GetValue(intKey);
                hashKey = intKey;
            }
            else
            {
                hashKey = key;
            }

            uint hash = HashNumber(hashKey);
            uint index = hash % ((NodesCount - 1) | 1);
            LuaNode node = GetNode(index);

            while (node.Key.Type != LuaType.Number || node.Key.Value.Double != key)
            {
                node = node.Key.Next;
                if (node == null)
                    return null;
            }
            return node.Value;
        }

        /// <summary>
        /// Gets a range of values from the array part.
        /// </summary>
        internal LuaTValue[] GetValues(int index, int count)
        {
            if (count == 0)
                return new LuaTValue[0];

            var natives = ObjectManager.Wow.ReadStructArray<NativeLuaTValue>(
                _native.ValuesPtr + (uint)(index * FastSize<NativeLuaTValue>.Size), 
                count);
            
            var result = new LuaTValue[count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new LuaTValue(
                    _native.ValuesPtr + (uint)((index + i) * FastSize<NativeLuaTValue>.Size), 
                    natives[i]);
            }
            return result;
        }

        /// <summary>
        /// Gets all values from the array part.
        /// </summary>
        private LuaTValue[] GetAllValues()
        {
            var natives = ObjectManager.Wow.ReadStructArray<NativeLuaTValue>(_native.ValuesPtr, (int)ValuesCount);
            var result = new LuaTValue[natives.Length];
            
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new LuaTValue(
                    _native.ValuesPtr + (uint)(i * FastSize<NativeLuaTValue>.Size), 
                    natives[i]);
            }
            return result;
        }

        /// <summary>
        /// Gets a value from the array part at the specified index.
        /// </summary>
        private LuaTValue GetValue(uint index)
        {
            return new LuaTValue(_native.ValuesPtr + (uint)(LuaTValue.NativeSize * index));
        }

        /// <summary>
        /// Gets a node from the hash part at the specified index.
        /// </summary>
        private LuaNode GetNode(uint index)
        {
            return new LuaNode(_native.NodePtr + (uint)(LuaNode.NativeSize * index));
        }

        /// <summary>
        /// Searches for the boundary in the hash part.
        /// </summary>
        private int SearchBoundary(LuaTValue[] values, uint j)
        {
            uint i = j;
            j++;

            LuaTValue field;
            while ((field = GetField(j - 1)) != null && field.Type != LuaType.Nil)
            {
                i = j;
                j *= 2;
                if (j > 2147483645U)
                {
                    i = 1;
                    while ((field = GetField(i - 1)) != null && field.Type != LuaType.Nil)
                    {
                        i++;
                    }
                    return (int)(i - 1);
                }
            }

            while (j - i > 1)
            {
                uint m = (i + j) / 2;
                if ((field = GetField(m - 1)) != null && field.Type == LuaType.Nil)
                    j = m;
                else
                    i = m;
            }
            return (int)i;
        }

        /// <summary>
        /// Calculates the count of elements (Lua # operator).
        /// </summary>
        private int CalculateCount()
        {
            uint n = ValuesCount;
            var values = GetAllValues();

            if (n > 0 && values[n - 1].Type == LuaType.Nil)
            {
                uint i = 0;
                while (n - i > 1)
                {
                    uint m = (i + n) / 2;
                    if (values[m - 1].Type == LuaType.Nil)
                        n = m;
                    else
                        i = m;
                }
                return (int)i;
            }

            if (_native.NodePtr == DummyNodePtr)
                return (int)n;

            return SearchBoundary(values, n);
        }
    }
}
