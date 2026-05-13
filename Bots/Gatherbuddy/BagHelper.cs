using Styx;
using Styx.WoWInternals.WoWObjects;

namespace Bots.Gatherbuddy
{
    /// <summary>
    /// Counts free bag slots restricted to the bag families relevant to each gather type.
    /// Port of HB 4.3.4 Bots.GatherBuddy.BagHelper.
    ///
    /// Normal bags (BagType.Normal) can hold anything, so they always count.
    /// Mining bags (BagType.Mining) only hold minerals — they count for mine slots, not herb slots.
    /// Herb bags  (BagType.Herb)   only hold herbs   — they count for herb slots, not mine slots.
    /// </summary>
    public static class BagHelper
    {
        /// <summary>
        /// Free slots available for minerals: backpack + Normal bags + Mining bags.
        /// </summary>
        public static uint EmptyMineSlots
        {
            get
            {
                uint free = StyxWoW.Me.Inventory.Backpack.FreeSlots;
                for (uint i = 0U; i < 4U; i++)
                {
                    WoWContainer bag = StyxWoW.Me.GetBagAtIndex(i);
                    if (bag != null && (bag.BagType == BagType.Mining || bag.BagType == BagType.Normal))
                        free += bag.FreeSlots;
                }
                return free;
            }
        }

        /// <summary>
        /// Free slots available for herbs: backpack + Normal bags + Herb bags.
        /// </summary>
        public static uint EmptyHerbSlots
        {
            get
            {
                uint free = StyxWoW.Me.Inventory.Backpack.FreeSlots;
                for (uint i = 0U; i < 4U; i++)
                {
                    WoWContainer bag = StyxWoW.Me.GetBagAtIndex(i);
                    if (bag != null && (bag.BagType == BagType.Herb || bag.BagType == BagType.Normal))
                        free += bag.FreeSlots;
                }
                return free;
            }
        }
    }
}
