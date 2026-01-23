#nullable disable

namespace Styx.Helpers
{
    /// <summary>
    /// A simple pair of two values of different types.
    /// </summary>
    public struct ValuePair<T1, T2>
    {
        public T1 Value1;
        public T2 Value2;

        public ValuePair(T1 val1, T2 val2)
        {
            Value1 = val1;
            Value2 = val2;
        }
    }
}
