using System;

namespace GreenMagic
{
    /// <summary>
    /// Extension methods for IntPtr conversions.
    /// </summary>
    public static class Extensions
    {
        public static float ToFloat(this IntPtr val)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(val.ToInt32()), 0);
        }

        public static uint ToUInt32(this IntPtr val)
        {
            return BitConverter.ToUInt32(BitConverter.GetBytes(val.ToInt32()), 0);
        }

        public static ulong ToUInt64(this IntPtr val)
        {
            return BitConverter.ToUInt64(BitConverter.GetBytes(val.ToInt64()), 0);
        }
    }
}
