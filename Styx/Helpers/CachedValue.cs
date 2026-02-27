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
        private readonly Func<T> _producer;
        private bool _hasValue;
        private T _value;

        public CachedValue(Func<T> producer)
        {
            if (producer == null)
                throw new ArgumentNullException("producer");
            _producer = producer;
        }

        public T Value
        {
            get
            {
                if (!_hasValue)
                {
                    _value = _producer();
                    _hasValue = true;
                }
                return _value;
            }
        }

        public void Reset()
        {
            _hasValue = false;
        }

        public static implicit operator T(CachedValue<T> pfcv)
        {
            return pfcv.Value;
        }
    }
}