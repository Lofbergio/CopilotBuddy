using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Styx.Helpers;
using Styx.Logic.Profiles;

namespace Styx.Logic.Pathing
{
    /// <summary>
    /// Manages blackspots - areas to avoid during navigation.
    /// Blackspots can be added from profiles or dynamically at runtime.
    /// </summary>
    public static class BlackspotManager
    {
        private static readonly List<Blackspot> _blackspots = new List<Blackspot>();
        private static readonly List<GlobalBlackspot> _globalBlackspots = new List<GlobalBlackspot>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets all current blackspots (profile + runtime added).
        /// </summary>
        public static ReadOnlyCollection<Blackspot> Blackspots
        {
            get
            {
                lock (_lock)
                {
                    return _blackspots.AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Gets all global blackspots (persisted across sessions).
        /// </summary>
        public static ReadOnlyCollection<GlobalBlackspot> GlobalBlackspots
        {
            get
            {
                lock (_lock)
                {
                    return _globalBlackspots.AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Checks if a location is within any blackspot.
        /// </summary>
        /// <param name="location">The location to check.</param>
        /// <param name="radius">Additional radius to add to the check.</param>
        /// <returns>True if the location is blackspotted.</returns>
        public static bool IsBlackspotted(WoWPoint location, float radius = 0f)
        {
            lock (_lock)
            {
                // Check profile blackspots
                foreach (var spot in _blackspots)
                {
                    if (IsInBlackspot(location, spot, radius))
                        return true;
                }

                // Check global blackspots for current map
                uint currentMap = StyxWoW.Me?.MapId ?? 0;
                foreach (var globalSpot in _globalBlackspots)
                {
                    if (globalSpot.MapId == currentMap && IsInBlackspot(location, globalSpot.Blackspot, radius))
                        return true;
                }

                return false;
            }
        }

        private static bool IsInBlackspot(WoWPoint location, Blackspot spot, float extraRadius)
        {
            float totalRadius = spot.Radius + extraRadius;
            float dx = location.X - spot.Location.X;
            float dy = location.Y - spot.Location.Y;
            float dz = location.Z - spot.Location.Z;

            // Check horizontal distance
            if (dx * dx + dy * dy > totalRadius * totalRadius)
                return false;

            // Check vertical distance
            return Math.Abs(dz) <= spot.Height;
        }

        /// <summary>
        /// Adds a blackspot at the specified location.
        /// </summary>
        public static void AddBlackspot(WoWPoint location, float radius, float height)
        {
            AddBlackspots(new[] { new Blackspot(location, radius, height) });
        }

        /// <summary>
        /// Adds multiple blackspots.
        /// </summary>
        public static void AddBlackspots(IEnumerable<Blackspot> blackspots)
        {
            if (blackspots == null)
                return;

            lock (_lock)
            {
                foreach (var spot in blackspots)
                {
                    if (!_blackspots.Contains(spot))
                    {
                        _blackspots.Add(spot);
                    }
                }
            }
        }

        /// <summary>
        /// Removes a blackspot.
        /// </summary>
        public static void RemoveBlackspot(Blackspot spot)
        {
            lock (_lock)
            {
                _blackspots.Remove(spot);
            }
        }

        /// <summary>
        /// Removes multiple blackspots.
        /// </summary>
        public static void RemoveBlackspots(IEnumerable<Blackspot> spots)
        {
            if (spots == null)
                return;

            lock (_lock)
            {
                var spotList = spots.ToList();
                _blackspots.RemoveAll(s => spotList.Contains(s));
            }
        }

        /// <summary>
        /// Clears all non-global blackspots.
        /// </summary>
        public static void ClearBlackspots()
        {
            lock (_lock)
            {
                _blackspots.Clear();
            }
        }

        /// <summary>
        /// Adds a global blackspot that persists across sessions.
        /// </summary>
        public static void AddGlobalBlackspot(WoWPoint location, float radius, float height)
        {
            uint mapId = StyxWoW.Me?.MapId ?? 0;
            AddGlobalBlackspot(new GlobalBlackspot(location, radius, height, mapId));
        }

        /// <summary>
        /// Adds a global blackspot.
        /// </summary>
        public static void AddGlobalBlackspot(GlobalBlackspot blackspot)
        {
            if (blackspot == null)
                return;

            lock (_lock)
            {
                if (_globalBlackspots.Contains(blackspot))
                    return;

                if (IsBlackspotted(blackspot.Blackspot.Location, blackspot.Blackspot.Radius))
                    return;

                _globalBlackspots.Add(blackspot);
                SaveGlobalBlackspots();
            }
        }

        /// <summary>
        /// Removes a global blackspot.
        /// </summary>
        public static void RemoveGlobalBlackspot(GlobalBlackspot blackspot)
        {
            lock (_lock)
            {
                _globalBlackspots.Remove(blackspot);
                SaveGlobalBlackspots();
            }
        }

        /// <summary>
        /// Loads global blackspots from file.
        /// </summary>
        public static void LoadGlobalBlackspots()
        {
            string path = Path.Combine(Logging.ApplicationPath, "GlobalStuckBlackspots.xml");
            if (!File.Exists(path))
                return;

            try
            {
                var doc = XDocument.Load(path);
                var root = doc.Element("GlobalBlackspots");
                if (root == null)
                    return;

                var loaded = GlobalBlackspot.GetBlackspotsFromXml(root);
                lock (_lock)
                {
                    _globalBlackspots.Clear();
                    _globalBlackspots.AddRange(loaded);
                }

                Logging.Write($"Loaded {loaded.Count} global blackspots");
            }
            catch (Exception ex)
            {
                Logging.Write($"Error loading global blackspots: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves global blackspots to file.
        /// </summary>
        private static void SaveGlobalBlackspots()
        {
            try
            {
                var root = new XElement("GlobalBlackspots");
                foreach (var spot in _globalBlackspots)
                {
                    root.Add(spot.GetXml());
                }

                string path = Path.Combine(Logging.ApplicationPath, "GlobalStuckBlackspots.xml");
                root.Save(path);
            }
            catch (Exception ex)
            {
                Logging.Write($"Error saving global blackspots: {ex.Message}");
            }
        }

        /// <summary>
        /// Represents a global blackspot that is saved across sessions.
        /// </summary>
        public class GlobalBlackspot : IEquatable<GlobalBlackspot>
        {
            public Blackspot Blackspot { get; set; }
            public uint MapId { get; set; }

            public GlobalBlackspot(WoWPoint location, float radius, float height, uint mapId)
            {
                Blackspot = new Blackspot(location, radius, height);
                MapId = mapId;
            }

            public XElement GetXml()
            {
                return new XElement("GlobalBlackspot",
                    new XAttribute("X", Blackspot.Location.X),
                    new XAttribute("Y", Blackspot.Location.Y),
                    new XAttribute("Z", Blackspot.Location.Z),
                    new XAttribute("Radius", Blackspot.Radius),
                    new XAttribute("Height", Blackspot.Height),
                    new XAttribute("MapId", MapId));
            }

            public static List<GlobalBlackspot> GetBlackspotsFromXml(XElement xml)
            {
                var result = new List<GlobalBlackspot>();

                foreach (var element in xml.Elements("GlobalBlackspot"))
                {
                    try
                    {
                        float x = Convert.ToSingle(element.Attribute("X")?.Value, CultureInfo.InvariantCulture);
                        float y = Convert.ToSingle(element.Attribute("Y")?.Value, CultureInfo.InvariantCulture);
                        float z = Convert.ToSingle(element.Attribute("Z")?.Value, CultureInfo.InvariantCulture);
                        float radius = Convert.ToSingle(element.Attribute("Radius")?.Value, CultureInfo.InvariantCulture);
                        float height = Convert.ToSingle(element.Attribute("Height")?.Value, CultureInfo.InvariantCulture);
                        uint mapId = Convert.ToUInt32(element.Attribute("MapId")?.Value, CultureInfo.InvariantCulture);

                        result.Add(new GlobalBlackspot(new WoWPoint(x, y, z), radius, height, mapId));
                    }
                    catch
                    {
                        // Skip invalid entries
                    }
                }

                return result;
            }

            public bool Equals(GlobalBlackspot? other)
            {
                if (other is null)
                    return false;
                if (ReferenceEquals(this, other))
                    return true;
                return Blackspot.Equals(other.Blackspot) && MapId == other.MapId;
            }

            public override bool Equals(object? obj)
            {
                return obj is GlobalBlackspot other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Blackspot, MapId);
            }

            public static bool operator ==(GlobalBlackspot? left, GlobalBlackspot? right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(GlobalBlackspot? left, GlobalBlackspot? right)
            {
                return !Equals(left, right);
            }
        }
    }
}
