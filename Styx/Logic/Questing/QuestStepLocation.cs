#nullable disable

namespace Styx.Logic.Questing
{
    /// <summary>
    /// Represents the location data for a quest step or objective.
    /// Used to track where the player needs to go for the current quest objective.
    /// </summary>
    public struct QuestStepLocation
    {
        /// <summary>
        /// Reserved field (unused).
        /// </summary>
        private uint reserved;

        /// <summary>
        /// The map ID where this quest step takes place.
        /// </summary>
        public uint MapID;

        /// <summary>
        /// The index of the objective this location relates to.
        /// </summary>
        public uint ObjectiveIndex;

        /// <summary>
        /// The floor ID for multi-floor maps (dungeons, buildings).
        /// </summary>
        public uint FloorID;

        /// <summary>
        /// The 2D position coordinates for this quest step.
        /// </summary>
        public Vector2i Position;

        /// <summary>
        /// Gets whether this location data is valid.
        /// </summary>
        public bool IsValid => MapID != 0 || Position.X != 0 || Position.Y != 0;
    }
}
