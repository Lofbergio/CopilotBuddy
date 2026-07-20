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
    /// Timed vendor blacklist (default TTL 30 min), keyed by NPC entry + the ERRAND it's banned from.
    /// Timed because enemy-territory / unreachable rejects are transient, and a permanent set could
    /// exhaust every vendor on the continent with no way back.
    ///
    /// Purpose scoping: an NPC can be useless for one errand and fine for another. The AmmoVendor
    /// npcflag is a UI hint, not an inventory — a General Supplies NPC carries it while stocking no
    /// projectiles, yet it still buys our loot. Banning it entry-wide would cost us the sell vendor
    /// too (GetClosestVendor(Sell) accepts Ammo-type vendors). VendorType.Unknown = every errand.
    ///
    /// Entry-keyed, not Vendor-keyed: Vendor implements IEquatable but overrides no GetHashCode, so a
    /// Dictionary&lt;Vendor,_&gt; hashed by reference and could never match the freshly-built instances
    /// the data.bin resolver returns on every call.
    /// </summary>
    public class VendorBlacklist
    {
        private readonly Dictionary<(int Entry, Vendor.VendorType Purpose), DateTime> _until =
            new Dictionary<(int, Vendor.VendorType), DateTime>();

        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

        public void Add(Vendor v) { Add(v, Vendor.VendorType.Unknown, DefaultTtl); }
        public void Add(Vendor v, TimeSpan ttl) { Add(v, Vendor.VendorType.Unknown, ttl); }
        public void Add(Vendor v, Vendor.VendorType purpose) { Add(v, purpose, DefaultTtl); }

        public void Add(Vendor v, Vendor.VendorType purpose, TimeSpan ttl)
        {
            if (v != null) _until[(v.Entry, purpose)] = DateTime.UtcNow.Add(ttl);
        }

        /// <summary>Bans the NPC from this errand for the rest of the session. For facts the server
        /// states outright that can't change while we're logged in (e.g. "stocks no ammo") — a TTL
        /// there would just re-walk us to the same barren vendor on a timer.</summary>
        public void AddPermanent(Vendor v, Vendor.VendorType purpose)
        {
            if (v != null) _until[(v.Entry, purpose)] = DateTime.MaxValue;
        }

        /// <summary>True if banned from every errand.</summary>
        public bool Contains(Vendor v) { return Contains(v, Vendor.VendorType.Unknown); }

        /// <summary>True if banned entry-wide, or specifically from this errand.</summary>
        public bool Contains(Vendor v, Vendor.VendorType purpose)
        {
            if (v == null) return false;
            return IsBanned(v.Entry, Vendor.VendorType.Unknown)
                || (purpose != Vendor.VendorType.Unknown && IsBanned(v.Entry, purpose));
        }

        /// <summary>Entries the data.bin resolver must skip for this errand — that path filters by
        /// entry before it has a Vendor to test.</summary>
        public HashSet<int> ExcludedEntries(Vendor.VendorType purpose)
        {
            Prune();
            var excluded = new HashSet<int>();
            foreach (var key in _until.Keys)
                if (key.Purpose == Vendor.VendorType.Unknown || key.Purpose == purpose)
                    excluded.Add(key.Entry);
            return excluded;
        }

        /// <summary>Drops every ban scoped to this errand; entry-wide bans survive. For when the
        /// errand's premise changes and the old verdicts stop meaning anything — e.g. a ranged-weapon
        /// swap, after which "stocks no Bullets" says nothing about whether we can buy arrows.</summary>
        public void RemovePurpose(Vendor.VendorType purpose)
        {
            if (purpose == Vendor.VendorType.Unknown) return;
            foreach (var k in _until.Keys.Where(k => k.Purpose == purpose).ToList())
                _until.Remove(k);
        }

        public int Count { get { Prune(); return _until.Count; } }

        private bool IsBanned(int entry, Vendor.VendorType purpose)
        {
            var key = (entry, purpose);
            if (!_until.TryGetValue(key, out DateTime until)) return false;
            if (DateTime.UtcNow < until) return true;
            _until.Remove(key);
            return false;
        }

        private void Prune()
        {
            DateTime now = DateTime.UtcNow;
            foreach (var k in _until.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                _until.Remove(k);
        }
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
                    .Where(v => !Blacklist.Contains(v, v.Type))
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
                                ? Blacklist.ExcludedEntries(type)
                                : null;
                            // Constrain to NPCs the server says actually stock what this errand needs.
                            // The npcflag alone routes us to vendors that can't serve us (the AmmoVendor
                            // livelock); intersecting with real stock makes that trip impossible.
                            HashSet<int> required = type.RequiredStockEntries();
                            NpcResult nearestNpc = NpcQueries.GetNearestNpc(
                                StyxWoW.Me.FactionTemplate.Faction,
                                StyxWoW.Me.MapId,
                                StyxWoW.Me.Location,
                                type.AsNpcFlag(),
                                excluded,
                                required);
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
                        // The blacklist applies here too. Without it, a trainer that proved unreachable or
                        // sat in enemy territory was re-resolved every tick forever — the Train branch was
                        // the one resolver path that ignored its own reject list.
                        .Where(v => v.TrainClass == playerClass && !Blacklist.Contains(v, type))
                        .OrderBy(v => location.Distance(v.Location))
                        .FirstOrDefault();
                    return vendor;
                }
                else
                {
                    var vendor = source
                        .Where(v => !Blacklist.Contains(v, type))
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
