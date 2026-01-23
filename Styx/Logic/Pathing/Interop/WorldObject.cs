using System;
using System.Collections.Generic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Tripper.XNAMath;

namespace Styx.Logic.Pathing.Interop
{
    /// <summary>
    /// Types of world objects for navigation purposes.
    /// </summary>
    public enum WorldObjectType
    {
        Other = 0,
        Portal = 1,
        Transport = 2,
        Elevator = 3
    }

    /// <summary>
    /// Interface for world objects used in navigation.
    /// </summary>
    public interface IWorldObject
    {
        string Name { get; }
        uint ID { get; }
        string Model { get; }
        Vector3 Location { get; }
        WorldObjectType Type { get; }
        void Interact();
    }

    /// <summary>
    /// Adapts a WoWGameObject to the IWorldObject interface.
    /// </summary>
    public class WorldObject : IWorldObject
    {
        private readonly WoWGameObject _gameObject;
        private static readonly HashSet<uint> PortalDisplayIds = new()
        {
            8244, 9086
        };

        /// <summary>
        /// Creates a new WorldObject from a WoWGameObject.
        /// </summary>
        public WorldObject(WoWGameObject gameObject)
        {
            _gameObject = gameObject ?? throw new ArgumentNullException(nameof(gameObject));
        }

        /// <summary>Gets the name of the game object.</summary>
        public string Name => _gameObject.Name ?? string.Empty;

        /// <summary>Gets the display ID of the game object.</summary>
        public uint ID => _gameObject.DisplayId;

        /// <summary>Gets the model name (always empty for now).</summary>
        public string Model => string.Empty;

        /// <summary>
        /// Gets the location of the game object.
        /// </summary>
        public Vector3 Location
        {
            get
            {
                var loc = _gameObject.Location;
                return new Vector3(loc.X, loc.Y, loc.Z);
            }
        }

        /// <summary>
        /// Gets the type of the game object.
        /// </summary>
        public WorldObjectType Type
        {
            get
            {
                if (PortalDisplayIds.Contains(ID))
                    return WorldObjectType.Portal;

                // Check if it's a transport object
                if (_gameObject.SubType.HasFlag(WoWGameObjectType.Transport))
                    return WorldObjectType.Transport;

                return WorldObjectType.Other;
            }
        }

        /// <summary>Interacts with the game object.</summary>
        public void Interact()
        {
            _gameObject.Interact();
        }
    }
}
