using System;

namespace Styx.Helpers
{
    /// <summary>
    /// Simple one-shot cache.  Value is computed once by the supplied producer and
    /// stored until Reset() is called.
    /// This mirrors HonorBuddy's CachedValue<T> class.
    /// </summary>
    public class CachedValue<T>
    {
        private readonly Func<T> func_0;
        private bool bool_0;
        private T gparam_0;

        public CachedValue(Func<T> producer)
        {
            if (producer == null)
                throw new ArgumentNullException("producer");
            func_0 = producer;
        }

        public T Value
        {
            get
            {
                if (!bool_0)
                {
                    gparam_0 = func_0();
                    bool_0 = true;
                }
                return gparam_0;
            }
        }

        public void Reset()
        {
            bool_0 = false;
        }

        public static implicit operator T(CachedValue<T> pfcv)
        {
            return pfcv.Value;
        }
    }
}