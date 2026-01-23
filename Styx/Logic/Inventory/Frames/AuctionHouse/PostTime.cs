namespace Styx.Logic.Inventory.Frames.AuctionHouse
{
	/// <summary>
	/// Auction duration in minutes.
	/// </summary>
	public enum PostTime
	{
		/// <summary>
		/// 12 hours (720 minutes).
		/// </summary>
		Short = 720,

		/// <summary>
		/// 24 hours (1440 minutes).
		/// </summary>
		Medium = 1440,

		/// <summary>
		/// 48 hours (2880 minutes).
		/// </summary>
		Long = 2880
	}
}
