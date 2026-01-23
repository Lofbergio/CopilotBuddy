using System.Collections.Generic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Pathing.Interop
{
    /// <summary>
    /// Interface for world information providers.
    /// </summary>
    public interface IWorldInfoProvider
    {
        List<IWorldObject> GetObjects();
    }

    /// <summary>
    /// Provides information about world objects (game objects, units, etc).
    /// </summary>
    public class WorldInfoProvider : IWorldInfoProvider
    {
        /// <summary>
        /// Gets all world objects currently loaded.
        /// </summary>
        public List<IWorldObject> GetObjects()
        {
            var result = new List<IWorldObject>();

            var gameObjects = ObjectManager.GetObjectsOfType<WoWGameObject>();
            foreach (var go in gameObjects)
            {
                result.Add(new WorldObject(go));
            }

            return result;
        }
    }
}
