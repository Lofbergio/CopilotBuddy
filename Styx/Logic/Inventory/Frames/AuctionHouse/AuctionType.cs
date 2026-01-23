namespace Styx.Logic.Inventory.Frames.AuctionHouse
{
	/// <summary>
	/// Type of auction list to query.
	/// </summary>
	public enum AuctionType
	{
		/// <summary>
		/// Browse/search auction list.
		/// </summary>
		List,

		/// <summary>
		/// Auctions owned by the player.
		/// </summary>
		Owner,

		/// <summary>
		/// Auctions the player has bid on.
		/// </summary>
		Bidder
	}
}
