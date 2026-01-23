#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Styx.Helpers
{
    /// <summary>
    /// Extension methods for BinaryReader and BinaryWriter to handle struct serialization.
    /// </summary>
    public static class BinaryExtensions
    {
        private static readonly byte[] _buffer = new byte[2048];

        /// <summary>
        /// Reads a struct from the binary stream.
        /// </summary>
        /// <typeparam name="T">The struct type to read.</typeparam>
        /// <param name="reader">The BinaryReader to read from.</param>
        /// <returns>The deserialized struct.</returns>
        public static unsafe T ReadStruct<T>(this BinaryReader reader) where T : struct
        {
            Type type = typeof(T);
            int size = Marshal.SizeOf(type);

            lock (_buffer)
            {
                reader.Read(_buffer, 0, size);
                
                fixed (byte* ptr = _buffer)
                {
                    return (T)Marshal.PtrToStructure(new IntPtr(ptr), type);
                }
            }
        }

        /// <summary>
        /// Writes a struct to the binary stream.
        /// </summary>
        /// <typeparam name="T">The struct type to write.</typeparam>
        /// <param name="writer">The BinaryWriter to write to.</param>
        /// <param name="data">The struct data to write.</param>
        public static unsafe void WriteStruct<T>(this BinaryWriter writer, T data) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));

            lock (_buffer)
            {
                fixed (byte* ptr = _buffer)
                {
                    Marshal.StructureToPtr(data, new IntPtr(ptr), false);
                }
                writer.Write(_buffer, 0, size);
            }
        }
    }
}
