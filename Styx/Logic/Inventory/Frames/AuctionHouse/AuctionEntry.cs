using System.Runtime.InteropServices;

namespace Styx.Logic.Inventory.Frames.AuctionHouse
{
	/// <summary>
	/// Represents an auction entry from memory.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct AuctionEntry
	{
		/// <summary>
		/// Unknown field.
		/// </summary>
		public uint Unk00;

		/// <summary>
		/// Unique auction ID.
		/// </summary>
		public uint AuctionId;

		/// <summary>
		/// Item entry ID.
		/// </summary>
		public uint ItemEntry;

		/// <summary>
		/// Enchant information (up to 7 enchants).
		/// </summary>
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
		public WoWAucEnchantInfo[] EnchantInfo;

		/// <summary>
		/// Random property ID for random enchants.
		/// </summary>
		public uint RandomPropertyId;

		/// <summary>
		/// Item suffix factor.
		/// </summary>
		public uint ItemSuffixFactor;

		/// <summary>
		/// Stack count.
		/// </summary>
		public uint Count;

		/// <summary>
		/// Spell charges on the item.
		/// </summary>
		public uint SpellCharges;

		/// <summary>
		/// Unknown field.
		/// </summary>
		public uint Unk70;

		/// <summary>
		/// Unknown field.
		/// </summary>
		public uint Unk74;

		/// <summary>
		/// GUID of the seller.
		/// </summary>
		public ulong SellerGuid;

		/// <summary>
		/// Starting bid amount.
		/// </summary>
		public uint StartBid;

		/// <summary>
		/// Minimum bid increment.
		/// </summary>
		public uint MinBidInc;

		/// <summary>
		/// Buyout price.
		/// </summary>
		public uint BuyOut;

		/// <summary>
		/// Time until auction expires.
		/// </summary>
		public uint ExpireTime;

		/// <summary>
		/// Current bidder GUID (low part).
		/// </summary>
		public uint BidderGuidB;

		/// <summary>
		/// Current bidder GUID (high part).
		/// </summary>
		public uint BidderGuidA;

		/// <summary>
		/// Current bid amount.
		/// </summary>
		public uint CurrentBid;

		/// <summary>
		/// Sale status.
		/// </summary>
		public uint SaleStatus;
	}
}
