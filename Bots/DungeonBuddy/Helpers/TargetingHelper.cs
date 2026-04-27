using System.Linq;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.DungeonBuddy.Helpers
{
    public static class TargetingHelper
    {
        public static int GetCountWithin(WoWPoint position, float range, Extra extra)
        {
            float rangeSqr = range * range;
            return ObjectManager.GetObjectsOfType<WoWUnit>()
                .Count(u => u.IsAlive && u.Location.DistanceSqr(position) < rangeSqr && extra(u));
        }

        public static int GetCountWithin(WoWPoint position, float range)
        {
            return GetCountWithin(position, range, u => true);
        }
    }
}
