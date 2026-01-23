using System;

namespace Styx.Helpers
{
    public class PerFrameCachedValue<T>
    {
        private readonly Func<T> _producer;
        private T _cachedValue;
        private uint _lastFrameCount;
        
        public PerFrameCachedValue(Func<T> producer)
        {
            if (producer == null)
                throw new ArgumentNullException("producer");
            _producer = producer;
            _lastFrameCount = uint.MaxValue;
            _cachedValue = default!;
        }
        
        public T Value
        {
            get
            {
                // Frame-based caching removed as FrameCount is no longer available
                // Always call producer for fresh value
                return _producer();
            }
        }
        
        public static implicit operator T(PerFrameCachedValue<T> pfcv)
        {
            return pfcv.Value;
        }
    }
}
