#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Styx.Combat.CombatRoutine;
using Styx.Database;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

namespace Styx.Logic.Profiles
{
    /// <summary>
    /// Timed vendor blacklist (default TTL 30 min). Was a permanent HashSet — enemy-territory /
    /// unreachable rejects accumulated for the whole session and could exhaust every vendor on the
    /// continent with no way back; every other blacklist in the codebase is timed. HashSet-shaped
    /// API (Add/Contains/Count/enumeration) so call sites are unchanged.
    /// </summary>
    public class VendorBlacklist : IEnumerable<Vendor>
    {
        private readonly Dictionary<Vendor, DateTime> _until = new Dictionary<Vendor, DateTime>();

        public void Add(Vendor v) { Add(v, TimeSpan.FromMinutes(30)); }
        public void Add(Vendor v, TimeSpan ttl) { if (v != null) _until[v] = DateTime.UtcNow.Add(ttl); }

        public bool Contains(Vendor v)
        {
            if (v == null || !_until.TryGetValue(v, out DateTime until)) return false;
            if (DateTime.UtcNow < until) return true;
            _until.Remove(v);
            return false;
        }

        public int Count { get { Prune(); return _until.Count; } }

        private void Prune()
        {
            DateTime now = DateTime.UtcNow;
            foreach (Vendor k in _until.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                _until.Remove(k);
        }

        public IEnumerator<Vendor> GetEnumerator() { Prune(); return _until.Keys.ToList().GetEnumerator(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    /// <summary>
    /// Manages vendor NPCs from profiles.
    /// </summary>
    public class VendorManager
    {
        private readonly List<Vendor> _filteredVendors;

        /// <summary>
        /// Creates an empty vendor manager.
        /// </summary>
        public VendorManager()
        {
            Blacklist = new VendorBlacklist();
            AllVendors = new List<Vendor>();
            ForcedVendors = new List<Vendor>();
        }

        /// <summary>
        /// Creates a vendor manager from XML.
        /// </summary>
        public VendorManager(XElement element) : this()
        {
            foreach (var child in element.Elements().ToList())
            {
                try
                {
                    if (child.Name != "Vendor")
                        throw new ProfileUnknownElementException(child, "Vendor");

                    AllVendors.Add(new Vendor(child));
                }
                catch (ProfileException ex)
                {
                    Logging.WriteException(ex);
                }
            }
            _filteredVendors = AllVendors;
        }

        /// <summary>
        /// Gets or sets forced vendors that override profile vendors.
        /// </summary>
        public List<Vendor> ForcedVendors { get; set; }

        /// <summary>
        /// Gets all vendors in the profile.
        /// </summary>
        public List<Vendor> AllVendors { get; private set; }

        /// <summary>
        /// Gets vendors grouped by type, excluding blacklisted.
        /// </summary>
        public Lookup<Vendor.VendorType, Vendor> Vendors
        {
            get
            {
                if (_filteredVendors == null)
                    return null;
                // Filter blacklisted vendors at query time instead of mutating _filteredVendors,
                // so vendors that are later un-blacklisted remain available.
                return (Lookup<Vendor.VendorType, Vendor>)_filteredVendors
                    .Where(v => !Blacklist.Contains(v))
                    .ToLookup(v => v.Type);
            }
        }

        /// <summary>
        /// Gets the blacklisted vendors (timed — entries expire, see VendorBlacklist).
        /// </summary>
        public VendorBlacklist Blacklist { get; private set; }

        /// <summary>
        /// Gets the closest vendor of any type.
        /// </summary>
        public Vendor GetClosestVendor()
        {
            return GetClosestVendor(Vendor.VendorType.Unknown);
        }

        /// <summary>
        /// Gets the closest vendor of a specific type.
        /// For Sell type, also accepts Repair and Ammo vendors (they can all buy items).
        /// </summary>
        public Vendor GetClosestVendor(Vendor.VendorType type)
        {
            try
            {
                List<Vendor> source = null;

                // Use forced vendors if available
                if (ForcedVendors != null && ForcedVendors.Count > 0)
                {
                    source = ForcedVendors.Where(v => MatchesVendorType(v, type)).ToList();
                }
                else
                {
                    if (type != Vendor.VendorType.Unknown)
                    {
                        // For Sell type, also accept Repair and Ammo vendors (like HB 6.2.3)
                        if (type == Vendor.VendorType.Sell)
                        {
                            source = AllVendors?.Where(v => 
                                v.Type == Vendor.VendorType.Sell || 
                                v.Type == Vendor.VendorType.Repair ||
                                v.Type == Vendor.VendorType.Ammo).ToList();
                        }
                        else if (Vendors != null)
                        {
                            source = Vendors.Contains(type) ? Vendors[type].ToList() : null;
                        }
                    }
                    else
                    {
                        source = AllVendors;
                    }
                }

                if (source == null || source.Count == 0)
                {
                    // Only fall back to Data.bin if FindVendorsAutomatically is enabled
                    // AND the profile has no vendors defined at all
                    if (Styx.Helpers.CharacterSettings.Instance.FindVendorsAutomatically && 
                        (AllVendors == null || AllVendors.Count == 0))
                    {
                        try
                        {
                            // Honor the blacklist: when the auto-resolved NPC can't be reached/interacted
                            // (e.g. a [DND] pedestal wrongly flagged as a vendor in data.bin), the vendor
                            // behaviour blacklists it — without skipping it here the query would re-return the
                            // same NPC every tick → infinite "could not find vendor" loop.
                            HashSet<int> excluded = Blacklist != null && Blacklist.Count > 0
                                ? new HashSet<int>(Blacklist.Select(v => v.Entry))
                                : null;
                            NpcResult nearestNpc = NpcQueries.GetNearestNpc(
                                StyxWoW.Me.FactionTemplate.Faction,
                                StyxWoW.Me.MapId,
                                StyxWoW.Me.Location,
                                type.AsNpcFlag(),
                                excluded);
                            if (nearestNpc != null)
                            {
                                return new Vendor(nearestNpc.Entry, nearestNpc.Name, type, nearestNpc.Location);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Write(ex.ToString());
                        }
                    }
                    return null;
                }

                WoWPoint location = ObjectManager.Me.Location;
                WoWClass playerClass = StyxWoW.Me.Class;

                if (type == Vendor.VendorType.Train)
                {
                    var vendor = source
                        .Where(v => v.TrainClass == playerClass)
                        .OrderBy(v => location.Distance(v.Location))
                        .FirstOrDefault();
                    return vendor;
                }
                else
                {
                    var vendor = source
                        .Where(v => !Blacklist.Contains(v))
                        .OrderBy(v => location.Distance(v.Location))
                        .FirstOrDefault();
                    return vendor;
                }
            }
            catch (Exception ex)
            {
                Logging.WriteException(ex);
                return null;
            }
        }

        /// <summary>
        /// Checks if a vendor matches the requested type.
        /// For Sell type, also matches Repair and Ammo vendors.
        /// </summary>
        private static bool MatchesVendorType(Vendor vendor, Vendor.VendorType type)
        {
            if (type == Vendor.VendorType.Unknown)
                return true;
            
            // For Sell type, also accept Repair and Ammo vendors
            if (type == Vendor.VendorType.Sell)
            {
                return vendor.Type == Vendor.VendorType.Sell || 
                       vendor.Type == Vendor.VendorType.Repair ||
                       vendor.Type == Vendor.VendorType.Ammo;
            }
            
            return vendor.Type == type;
        }
    }
}
