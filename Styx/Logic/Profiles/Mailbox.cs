#nullable disable
using System;
using System.Xml.Linq;
using Styx.Logic.Pathing;

namespace Styx.Logic.Profiles
{
    /// <summary>
    /// Represents a mailbox location in a profile.
    /// </summary>
    public class Mailbox : IEquatable<Mailbox>
    {
        /// <summary>
        /// Gets the location of the mailbox.
        /// </summary>
        public WoWPoint Location { get; private set; }

        /// <summary>
        /// Creates a new mailbox from an XML element.
        /// </summary>
        /// <param name="element">The XML element containing mailbox data.</param>
        public Mailbox(XElement element)
        {
            Location = ProfileHelper.ParseLocation(element);
        }

        /// <summary>
        /// Creates a new mailbox at the specified location.
        /// </summary>
        /// <param name="location">The mailbox location.</param>
        public Mailbox(WoWPoint location)
        {
            Location = location;
        }

        /// <summary>
        /// Determines whether this mailbox equals another.
        /// </summary>
        public bool Equals(Mailbox other)
        {
            if (other == null) return false;
            return Location == other.Location;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Mailbox);
        }

        public override int GetHashCode()
        {
            return Location.GetHashCode();
        }

        public override string ToString()
        {
            return $"[Mailbox Location: {Location}]";
        }
    }
}
