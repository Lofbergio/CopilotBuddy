using System;

namespace Bots.DungeonBuddy.Enums
{
    [Flags]
    public enum PartyRole
    {
        None = 0,
        Tank = 1,
        Healer = 2,
        Dps = 4
    }
}