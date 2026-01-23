using System;

namespace Styx.Logic
{
    /// <summary>
    /// Battleground winner enum for 3.3.5a
    /// </summary>
    public enum BattlefieldWinner
    {
        Horde = 0,
        Alliance = 1,
        GreenTeam = 0,  // Arena team colors
        YellowTeam = 1,
        None = 2,
        NotFinished = 3
    }
}
