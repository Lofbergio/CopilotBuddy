using System;

namespace Styx
{
    /// <summary>
    /// Helpers for working with <see cref="PulseFlags"/> that mirror the
    /// convenience methods used throughout Honorbuddy.
    /// </summary>
    public static class PulseFlagsExtensions
    {
        /// <summary>
        /// Clear the specified mask from the flags (equivalent to flags & ~mask).
        /// </summary>
        public static PulseFlags Remove(this PulseFlags flags, PulseFlags mask)
        {
            return flags & ~mask;
        }

        /// <summary>
        /// Add the specified mask to the flags (equivalent to flags | mask).
        /// </summary>
        public static PulseFlags Add(this PulseFlags flags, PulseFlags mask)
        {
            return flags | mask;
        }

        /// <summary>
        /// Reset all flags to zero.
        /// </summary>
        public static PulseFlags Reset(this PulseFlags flags)
        {
            return 0;
        }
    }
}