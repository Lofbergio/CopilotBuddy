using System;

namespace Styx.Logic
{
    /// <summary>
    /// Battleground join error codes for 3.3.5a
    /// </summary>
    public enum BattlegroundJoinError
    {
        None = 0,
        Nothing = -1,
        Deserter = -2,
        NotSameTeam = -3,
        Unknown = -4,
        StillEnqueued = -5,
        InRatedMatch = -6,
        TeamLeftQueue = -7,
        GroupJoinedNotEligible = -8
    }
}
