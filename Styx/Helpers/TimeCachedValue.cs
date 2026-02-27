using System;
using System.Diagnostics;

namespace Styx.Helpers
{
    /// <summary>
    /// Simple wrapper that caches the result of a producer delegate for a fixed amount of time.
    /// Ported directly from HonorBuddy 3.3.5a.
    /// </summary>
    public class TimeCachedValue<T>
    {
        private readonly TimeSpan timeSpan_0;
        private readonly Stopwatch stopwatch_0;
        private readonly Func<T> func_0;
        private T gparam_0;

        public TimeCachedValue(TimeSpan timeSpan, Func<T> producer)
        {
            if (producer == null)
                throw new ArgumentNullException("producer");
            func_0 = producer;
            timeSpan_0 = timeSpan;
            stopwatch_0 = new Stopwatch();
        }

        /// <summary>
        /// Gets the cached value, regenerating it if the interval has elapsed.
        /// </summary>
        public T Value
        {
            get
            {
                if (!stopwatch_0.IsRunning || stopwatch_0.Elapsed > timeSpan_0)
                {
                    gparam_0 = func_0();
                    stopwatch_0.Restart();
                }
                return gparam_0;
            }
        }

        public static implicit operator T(TimeCachedValue<T> tcv)
        {
            return tcv.Value;
        }

        /// <summary>
        /// Clears the cached data so that the next access will call the producer again.
        /// HonorBuddy recreated its cache objects on updates; we provide Reset for convenience.
        /// </summary>
        public void Reset()
        {
            stopwatch_0.Reset();
            gparam_0 = default!;
        }
    }
}