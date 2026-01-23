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
            Blacklist = new HashSet<Vendor>();
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
                RemoveBlacklisted();
                return (Lookup<Vendor.VendorType, Vendor>)_filteredVendors.ToLookup(v => v.Type);
            }
        }

        /// <summary>
        /// Gets the blacklisted vendors.
        /// </summary>
        public HashSet<Vendor> Blacklist { get; private set; }

        /// <summary>
        /// Gets the closest vendor of any type.
        /// </summary>
        public Vendor GetClosestVendor()
        {
            return GetClosestVendor(Vendor.VendorType.Unknown);
        }

        /// <summary>
        /// Gets the closest vendor of a specific type.
        /// </summary>
        public Vendor GetClosestVendor(Vendor.VendorType type)
        {
            try
            {
                List<Vendor> source = null;

                // Use forced vendors if available
                if (ForcedVendors != null && ForcedVendors.Count > 0)
                {
                    source = ForcedVendors.Where(v => type == Vendor.VendorType.Unknown || v.Type == type).ToList();
                }
                else
                {
                    if (type != Vendor.VendorType.Unknown)
                    {
                        if (Vendors != null)
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
                    // Try to find vendor from database
                    try
                    {
                        NpcResult nearestNpc = NpcQueries.GetNearestNpc(
                            StyxWoW.Me.FactionTemplate.Faction,
                            StyxWoW.Me.MapId,
                            StyxWoW.Me.Location,
                            type.AsNpcFlag());
                        if (nearestNpc != null)
                        {
                            return new Vendor(nearestNpc.Entry, nearestNpc.Name, type, nearestNpc.Location);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Write(ex.ToString());
                    }
                    return null;
                }

                WoWPoint location = ObjectManager.Me.Location;
                WoWClass playerClass = StyxWoW.Me.Class;

                if (type == Vendor.VendorType.Train)
                {
                    return source
                        .Where(v => v.TrainClass == playerClass)
                        .OrderBy(v => location.Distance(v.Location))
                        .FirstOrDefault();
                }
                else
                {
                    return source
                        .Where(v => !Blacklist.Contains(v))
                        .OrderBy(v => location.Distance(v.Location))
                        .FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                Logging.WriteException(ex);
                return null;
            }
        }

        /// <summary>
        /// Removes blacklisted vendors from the filtered list.
        /// </summary>
        private void RemoveBlacklisted()
        {
            _filteredVendors?.RemoveAll(v => Blacklist.Contains(v));
        }
    }
}
