#nullable disable
using System;
using System.Xml.Linq;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Pathing;

namespace Styx.Logic.Profiles
{
    /// <summary>
    /// Represents a trainer NPC in a profile.
    /// </summary>
    public class Trainer : IEquatable<Trainer>
    {
        /// <summary>
        /// Gets the NPC entry ID.
        /// </summary>
        public int Entry { get; private set; }

        /// <summary>
        /// Gets the NPC name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the trainer location.
        /// </summary>
        public WoWPoint Location { get; private set; }

        /// <summary>
        /// Gets the class this trainer trains.
        /// </summary>
        public WoWClass TrainClass { get; private set; }

        /// <summary>
        /// Creates a trainer from parameters.
        /// </summary>
        public Trainer(int entry, string name, WoWPoint location, WoWClass trainClass)
        {
            Entry = entry;
            Name = name;
            Location = location;
            TrainClass = trainClass;
        }

        /// <summary>
        /// Creates a trainer from XML.
        /// </summary>
        public Trainer(XElement xml)
        {
            Location = ProfileHelper.ParseLocation(xml);
            Name = string.Empty;
            TrainClass = WoWClass.None;

            foreach (var attr in xml.Attributes())
            {
                switch (attr.Name.LocalName.ToLowerInvariant())
                {
                    case "name":
                        Name = attr.Value;
                        break;
                    case "id":
                    case "entry":
                        if (int.TryParse(attr.Value, out int entry))
                            Entry = entry;
                        break;
                    case "trainclass":
                    case "class":
                        if (Enum.TryParse(attr.Value, true, out WoWClass wowClass))
                            TrainClass = wowClass;
                        break;
                }
            }
        }

        /// <summary>
        /// Determines whether this trainer equals another.
        /// </summary>
        public bool Equals(Trainer other)
        {
            if (other == null) return false;
            return Entry == other.Entry && TrainClass == other.TrainClass;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Trainer);
        }

        public override int GetHashCode()
        {
            return Entry.GetHashCode() ^ TrainClass.GetHashCode();
        }

        public override string ToString()
        {
            return $"[Trainer Name: {Name}, Entry: {Entry}, Class: {TrainClass}, Location: {Location}]";
        }
    }
}
