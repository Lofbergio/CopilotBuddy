using System;

namespace Styx.Logic
{
    /// <summary>
    /// Strand of the Ancients destroyed gate flags (bitmask)
    /// </summary>
    [Flags]
    public enum DestroyedGateFlags
    {
        None = 0,
        Red = 1,
        Blue = 2,
        Green = 4,
        Purple = 8,
        Yellow = 16,
        Ancient = 32
    }
}
