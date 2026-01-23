using System;

namespace Styx.Logic
{
    /// <summary>
    /// Battleground queue/instance status for 3.3.5a
    /// </summary>
    public enum BattlegroundStatus : uint
    {
        None = 0,
        Queued = 1,
        Confirm = 2,
        Active = 3,
        Error = 4
    }
}
