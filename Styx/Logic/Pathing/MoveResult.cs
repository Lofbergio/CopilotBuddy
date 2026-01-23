using System;

namespace Styx.Logic.Pathing
{
    public enum MoveResult
    {
        ReachedDestination,
        PathGenerationFailed,
        PathGenerated,
        UnstuckAttempt,
        Moved,
        Failed
    }
}
