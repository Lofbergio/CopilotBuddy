using System.Collections.Generic;

namespace Styx.Logic
{
    /// <summary>
    /// Event handler delegate for buying items from vendor.
    /// </summary>
    public delegate void BuyItemsEventHandler(BuyItemsEventArgs args);

    /// <summary>
    /// Event args for buying items.
    /// </summary>
    public class BuyItemsEventArgs
    {
        /// <summary>
        /// Dictionary of item IDs and quantities to buy.
        /// Key = Item ID, Value = Quantity.
        /// </summary>
        public Dictionary<uint, int> BuyItemsIds { get; } = new Dictionary<uint, int>();
    }
}
