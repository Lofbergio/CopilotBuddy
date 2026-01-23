using System;
using Styx.Logic.Pathing;

namespace Styx.WoWInternals.World
{
    /// <summary>
    /// Represents a line in 3D world space for trace line operations.
    /// WoW 3.3.5a build 12340.
    /// </summary>
    public struct WorldLine
    {
        /// <summary>
        /// Start point of the line.
        /// </summary>
        public WoWPoint Start;

        /// <summary>
        /// End point of the line.
        /// </summary>
        public WoWPoint End;

        /// <summary>
        /// Creates a new WorldLine from start to end point.
        /// </summary>
        public WorldLine(WoWPoint start, WoWPoint end)
        {
            this.Start = start;
            this.End = end;
        }
    }
}
