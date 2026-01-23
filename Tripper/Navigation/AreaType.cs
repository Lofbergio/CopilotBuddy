using System;

namespace Tripper.Navigation
{
    /// <summary>
    /// Defines navigation area types for Detour navmesh polygons.
    /// These values correspond to Detour polygon area types and determine traversal rules.
    /// </summary>
    public enum AreaType : byte
    {
        /// <summary>Ground surface - standard walkable terrain.</summary>
        Ground = 1,

        /// <summary>Water surface - requires swimming ability.</summary>
        Water = 2,

        /// <summary>Lava surface - typically avoided or requires special handling.</summary>
        Lava = 3,

        /// <summary>Road surface - optimized for faster travel.</summary>
        Road = 4,

        /// <summary>Falling area - used for jumps or drops.</summary>
        Fall = 5,

        /// <summary>Elevator platform - vertical transport mechanism.</summary>
        Elevator = 6,

        /// <summary>Gate area - battleground gates or similar barriers.</summary>
        Gate = 7,

        /// <summary>Portal area - teleportation or zone transition.</summary>
        Portal = 8,

        /// <summary>Defenders portal - specific to battleground defenders.</summary>
        DefendersPortal = 9,

        /// <summary>Horde portal - Horde-specific portal.</summary>
        HordePortal = 10,

        /// <summary>Alliance portal - Alliance-specific portal.</summary>
        AlliancePortal = 11,

        /// <summary>Blocked area - permanently unwalkable.</summary>
        Blocked = 12,

        /// <summary>Interactive unit area - requires unit interaction.</summary>
        InteractUnit = 13,

        /// <summary>Interactive object area - requires object interaction.</summary>
        InteractObject = 14,

        /// <summary>Horde-only area - restricted to Horde faction.</summary>
        Horde = 15,

        /// <summary>Alliance-only area - restricted to Alliance faction.</summary>
        Alliance = 16,

        /// <summary>Blackspot area - manually marked as unwalkable.</summary>
        Blackspot = 17,

        /// <summary>Miscellaneous area type 1.</summary>
        Misc1 = 20,

        /// <summary>Miscellaneous area type 2.</summary>
        Misc2 = 21,

        /// <summary>Miscellaneous area type 3.</summary>
        Misc3 = 22,

        /// <summary>Miscellaneous area type 4.</summary>
        Misc4 = 23
    }
}
