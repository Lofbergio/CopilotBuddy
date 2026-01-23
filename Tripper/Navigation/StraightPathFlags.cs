using System;

namespace Tripper.Navigation
{
    /// <summary>
    /// Straight path flags returned by Detour findStraightPath.
    /// Matches Detour native dtStraightPathFlags (DetourNavMesh.h).
    /// Used to detect segment types and off-mesh connections in navigation paths.
    /// </summary>
    [Flags]
    public enum StraightPathFlags : byte
    {
        /// <summary>No flags specified.</summary>
        None = 0,

        /// <summary>
        /// Start of a path segment (DT_STRAIGHTPATH_START).
        /// </summary>
        Start = 1,

        /// <summary>
        /// End of a path segment (DT_STRAIGHTPATH_END).
        /// </summary>
        End = 2,

        /// <summary>
        /// Off-mesh connection point (DT_STRAIGHTPATH_OFFMESH_CONNECTION).
        /// Indicates elevator, portal, or special navigation at this waypoint.
        /// Critical flag for detecting and handling offmesh traversal.
        /// </summary>
        OffMeshConnection = 4
    }
}
