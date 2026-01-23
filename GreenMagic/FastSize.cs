using System.Runtime.InteropServices;

namespace GreenMagic
{
    /// <summary>
    /// Fast cached Marshal.SizeOf for generic types.
    /// Avoids repeated reflection calls.
    /// </summary>
    public static class FastSize<T>
    {
        public static readonly int Size;
        public static readonly uint SizeU;

        static FastSize()
        {
            Size = Marshal.SizeOf(typeof(T));
            SizeU = (uint)Size;
        }
    }
}
