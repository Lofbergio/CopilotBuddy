#nullable disable

namespace Styx.WoWInternals
{
    /// <summary>
    /// Represents the player's full inventory.
    /// Contains sub-bags for equipped items, backpack, bank, buyback, keyring, and currency.
    /// </summary>
    public class WoWPlayerInventory : WoWBag
    {
        internal WoWPlayerInventory(BagStructure bag) : base(bag)
        {
            Equipped = new WoWPaperDoll(bag, 0U, 23U);
            Backpack = new WoWBag(bag, 23U, 16U);
            Bank = new WoWBag(bag, 39U, 35U);
            Buyback = new WoWBag(bag, 74U, 12U);
            Keyring = new WoWBag(bag, 86U, 32U);
            Currency = new WoWBag(bag, 118U, 32U);
        }

        /// <summary>
        /// Equipped items (slots 0-22): Head, Neck, Shoulders, etc.
        /// </summary>
        public WoWPaperDoll Equipped { get; private set; }

        /// <summary>
        /// Backpack items (slots 23-38): 16 slots in default backpack.
        /// </summary>
        public WoWBag Backpack { get; private set; }

        /// <summary>
        /// Bank items (slots 39-73): 28 bank slots + 7 bag slots.
        /// </summary>
        public WoWBag Bank { get; private set; }

        /// <summary>
        /// Buyback items from vendors (slots 74-85): 12 slots.
        /// </summary>
        public WoWBag Buyback { get; private set; }

        /// <summary>
        /// Keyring items (slots 86-117): 32 key slots.
        /// </summary>
        public WoWBag Keyring { get; private set; }

        /// <summary>
        /// Currency tokens (slots 118-149): 32 currency slots.
        /// </summary>
        public WoWBag Currency { get; private set; }
    }
}
