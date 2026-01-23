using Styx.Logic.Inventory.Frames.Gossip;

namespace Styx.Logic.POI
{
	/// <summary>
	/// Extension methods for PoiType.
	/// </summary>
	public static class PoiTypeExtensions
	{
		/// <summary>
		/// Converts a PoiType to its corresponding GossipEntryType.
		/// </summary>
		public static GossipEntry.GossipEntryType GetGossipType(this PoiType poiType)
		{
			return poiType switch
			{
				PoiType.Buy or PoiType.Sell or PoiType.Repair => GossipEntry.GossipEntryType.Vendor,
				PoiType.Train => GossipEntry.GossipEntryType.Trainer,
				_ => GossipEntry.GossipEntryType.Unknown
			};
		}
	}
}
