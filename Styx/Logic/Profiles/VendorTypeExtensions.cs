using System.Collections.Generic;
using Styx.Database;
using Styx.Logic.Inventory;

namespace Styx.Logic.Profiles
{
	/// <summary>
	/// Extension methods for Vendor.VendorType.
	/// </summary>
	public static class VendorTypeExtensions
	{
		/// <summary>
		/// NPC entries that can actually SERVE this errand, from the server's own vendor stock.
		/// Null = unconstrained: either the errand doesn't care what the vendor stocks (anyone buys our
		/// loot and repairs our gear) or VendorStock.db is absent. An EMPTY set means we asked and
		/// nobody on the server qualifies — a real answer the caller must not read as "unconstrained".
		///
		/// This is what makes the npcflag safe to route on. The flag says "ammo vendor" about NPCs that
		/// stock no projectiles; intersecting it with real stock means the resolver can only ever pick a
		/// vendor that works, so there is no barren-vendor trip to remember afterwards.
		/// </summary>
		public static HashSet<int> RequiredStockEntries(this Vendor.VendorType vt)
		{
			int level = (int)StyxWoW.Me.Level;
			switch (vt)
			{
				case Vendor.VendorType.Ammo:
					return VendorStock.VendorsStockingProjectile(Consumable.NeededAmmoClass(), level);
				case Vendor.VendorType.Food:
				case Vendor.VendorType.Restock:
					return VendorStock.VendorsStockingFoodDrink(level);
				default:
					// Sell/Repair/Train/FlightMaster/InnKeeper: the service is the npcflag, not the shelf.
					return null;
			}
		}

		/// <summary>
		/// Converts a VendorType to its corresponding UnitNPCFlags value.
		/// </summary>
		public static UnitNPCFlags AsNpcFlag(this Vendor.VendorType vt)
		{
			return vt switch
			{
				Vendor.VendorType.Repair => UnitNPCFlags.Repair,
				Vendor.VendorType.Food => UnitNPCFlags.FoodVendor,
				Vendor.VendorType.Ammo => UnitNPCFlags.AmmoVendor,   // was None → ammo vendors were unresolvable
				Vendor.VendorType.Sell => UnitNPCFlags.AnyVendor,
				Vendor.VendorType.Train => UnitNPCFlags.ClassTrainer,
				Vendor.VendorType.FlightMaster => UnitNPCFlags.Flightmaster,
				_ => UnitNPCFlags.None
			};
		}
	}
}
