using System;
using Styx.WoWInternals;

namespace Styx.Helpers
{
    /// <summary>
    /// Exact port of HB 5.4.8/6.2.3 PerFrameCachedValue.
    /// Caches a value per EndScene frame using Executor.FrameCount.
    /// Multiple accesses in the same frame return the cached value (0 RPM).
    /// </summary>
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
                // HB 5.4.8: StyxWoW.Memory.Executor.FrameCount
                // In CopilotBuddy: ObjectManager.Executor.FrameCount
                uint frameCount = ObjectManager.Executor?.FrameCount ?? 0;
                if (_lastFrameCount != frameCount)
                {
                    _cachedValue = _producer();
                    _lastFrameCount = frameCount;
                }
                return _cachedValue;
            }
        }

        /// <summary>Forces a cache refresh on the next access.</summary>
        public void Invalidate()
        {
            _lastFrameCount = uint.MaxValue;
        }
        
        public static implicit operator T(PerFrameCachedValue<T> pfcv)
        {
            return pfcv.Value;
        }
    }
}
