using System;

namespace Styx
{
    /// <summary>
    /// Exception thrown when an LFG dungeon instance cannot be found for a given mapId.
    /// HB 4.3.4: thrown by GetLfgDungeonIdFromMapId when no matching entry exists in LfgDungeons.dbc.
    /// </summary>
    public class InstanceNotFoundException : Exception
    {
        public InstanceNotFoundException() { }

        public InstanceNotFoundException(string message) : base(message) { }

        public InstanceNotFoundException(string message, Exception inner) : base(message, inner) { }
    }
}
