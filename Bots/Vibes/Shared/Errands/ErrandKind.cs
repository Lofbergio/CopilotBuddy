using Styx;
using Styx.Logic.POI;
using Styx.Logic.Profiles;

namespace Bots.Vibes.Shared.Errands
{
    /// <summary>
    /// The errands a trip can clear. Deliberately NOT a POI type: a POI says where the bot is walking,
    /// an errand says what it owes. Ammo is its own kind rather than a flavour of Buy because it has a
    /// different flag, a different vendor type, and a different satisfaction test.
    ///
    /// Fly is absent on purpose. It is not derived from demand — GrindSupervisor's flight learning and
    /// flight travel decide it — so it has no place in a demand-planned tour. ErrandRunner still
    /// SERVICES a Fly POI; deciding and servicing are different jobs.
    /// </summary>
    public enum ErrandKind
    {
        Sell,
        Repair,
        Buy,
        Ammo,
        Train,
        Mail
    }

    public static class ErrandKinds
    {
        /// <summary>Every kind a vendor-ish NPC can serve — i.e. everything but Mail.</summary>
        public static readonly ErrandKind[] NpcKinds =
        {
            ErrandKind.Sell, ErrandKind.Repair, ErrandKind.Buy, ErrandKind.Ammo, ErrandKind.Train
        };

        /// <summary>
        /// The capability flag an NPC must carry to serve this kind. data.bin npcflags state CAPABILITY
        /// (who repairs, who vends) and are legitimate pre-travel planning data — they are NOT a promise
        /// about STOCK. Reading a flag as inventory is the AmmoVendor livelock (docs/gotchas.md).
        /// </summary>
        public static UnitNPCFlags Flag(ErrandKind kind)
        {
            switch (kind)
            {
                case ErrandKind.Sell:   return UnitNPCFlags.AnyVendor;
                case ErrandKind.Repair: return UnitNPCFlags.Repair;
                case ErrandKind.Buy:    return UnitNPCFlags.FoodVendor;
                case ErrandKind.Ammo:   return UnitNPCFlags.AmmoVendor;
                case ErrandKind.Train:  return UnitNPCFlags.ClassTrainer;
                default:                return UnitNPCFlags.None;
            }
        }

        /// <summary>The POI type a stop wears while this kind is the one being served. The type is what
        /// Roam's exclusion list, the wedge watchdog's service exemption and Mount's dismount list all
        /// read, so it must stay one of the shapes they already know.</summary>
        public static PoiType Poi(ErrandKind kind)
        {
            switch (kind)
            {
                case ErrandKind.Sell:   return PoiType.Sell;
                case ErrandKind.Repair: return PoiType.Repair;
                case ErrandKind.Buy:
                case ErrandKind.Ammo:   return PoiType.Buy;
                case ErrandKind.Train:  return PoiType.Train;
                default:                return PoiType.Mail;
            }
        }

        /// <summary>
        /// The NPC entries that can actually serve this kind, from the server's vendor stock — the
        /// inventory half of the flag above. Null = unconstrained (the kind doesn't depend on stock, or
        /// VendorStock.db is absent); empty = nobody qualifies, which is a real answer, not a missing one.
        /// </summary>
        public static System.Collections.Generic.HashSet<int> RequiredStock(ErrandKind kind)
        {
            return VendorTypeExtensions.RequiredStockEntries(Vendor(kind));
        }

        /// <summary>The errand a blacklist entry should be scoped to. Entry-wide (Unknown) would cost us
        /// the sell vendor because a barren General Supplies NPC still buys loot.</summary>
        public static Vendor.VendorType Vendor(ErrandKind kind)
        {
            switch (kind)
            {
                case ErrandKind.Sell:   return Styx.Logic.Profiles.Vendor.VendorType.Sell;
                case ErrandKind.Repair: return Styx.Logic.Profiles.Vendor.VendorType.Repair;
                case ErrandKind.Buy:    return Styx.Logic.Profiles.Vendor.VendorType.Food;
                case ErrandKind.Ammo:   return Styx.Logic.Profiles.Vendor.VendorType.Ammo;
                case ErrandKind.Train:  return Styx.Logic.Profiles.Vendor.VendorType.Train;
                default:                return Styx.Logic.Profiles.Vendor.VendorType.Unknown;
            }
        }
    }
}
