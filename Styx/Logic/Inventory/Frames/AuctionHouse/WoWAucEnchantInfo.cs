namespace Styx.Logic.Inventory.Frames.AuctionHouse
{
	/// <summary>
	/// Enchant information for an auction item.
	/// </summary>
	public struct WoWAucEnchantInfo
	{
		/// <summary>
		/// The enchant spell ID.
		/// </summary>
		public uint EnchantId;

		/// <summary>
		/// Duration of the enchant (for temporary enchants).
		/// </summary>
		public uint EnchantDuration;

		/// <summary>
		/// Charges remaining on the enchant.
		/// </summary>
		public uint EnchantCharges;
	}
}
