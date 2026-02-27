using System;

namespace Styx.Logic.Pathing
{
    [Obsolete("If deriving from this class, derive from NavigationProvider instead. If using this class, use wrapper functions in Navigator instead.")]
    public interface INavigationProvider
    {
        MoveResult MoveTo(WoWPoint location);

        float PathPrecision { get; set; }

        bool Clear();

        WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to);

        bool CanNavigateFully(WoWPoint from, WoWPoint to, int maxHops);

        IStuckHandler StuckHandler { get; set; }

        bool AtLocation(WoWPoint point1, WoWPoint point2);
    }
}