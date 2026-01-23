namespace Styx.Logic.Profiles
{
	/// <summary>
	/// Extension methods for Vendor.VendorType.
	/// </summary>
	public static class VendorTypeExtensions
	{
		/// <summary>
		/// Converts a VendorType to its corresponding UnitNPCFlags value.
		/// </summary>
		public static UnitNPCFlags AsNpcFlag(this Vendor.VendorType vt)
		{
			return vt switch
			{
				Vendor.VendorType.Repair => UnitNPCFlags.Repair,
				Vendor.VendorType.Food => UnitNPCFlags.FoodVendor,
				Vendor.VendorType.Sell => UnitNPCFlags.AnyVendor,
				Vendor.VendorType.Train => UnitNPCFlags.ClassTrainer,
				Vendor.VendorType.FlightMaster => UnitNPCFlags.Flightmaster,
				_ => UnitNPCFlags.None
			};
		}
	}
}
