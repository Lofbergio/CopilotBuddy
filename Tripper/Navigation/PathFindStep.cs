namespace Tripper.Navigation
{
    /// <summary>
    /// Represents the steps in a pathfinding operation.
    /// Used to track progress and identify where pathfinding fails.
    /// </summary>
    public enum PathFindStep
    {
        /// <summary>No step or initial state.</summary>
        None = 0,

        /// <summary>Finding the starting polygon on the navmesh.</summary>
        FindStartPoly = 1,

        /// <summary>Finding the ending polygon on the navmesh.</summary>
        FindEndPoly = 2,

        /// <summary>Initializing the pathfinding query.</summary>
        InitPathFind = 3,

        /// <summary>Updating/calculating the pathfinding corridor.</summary>
        UpdatePathFind = 4,

        /// <summary>Finalizing the pathfinding result.</summary>
        FinalizePathFind = 5,

        /// <summary>Snapping partial path to the end point.</summary>
        SnapPartialPathToEnd = 6,

        /// <summary>Finding the straight path from polygon corridor.</summary>
        FindStraightPath = 7
    }
}
