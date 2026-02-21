namespace Bots.Gatherbuddy
{
    /// <summary>
    /// Type of waypoint traversal pattern
    /// </summary>
    public enum PathType
    {
        /// <summary>
        /// Circular path: 1→2→3→1→2→3→...
        /// </summary>
        Circle,
        
        /// <summary>
        /// Bounce path: 1→2→3→2→1→2→...
        /// </summary>
        Bounce
    }
}
