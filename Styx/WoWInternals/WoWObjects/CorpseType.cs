using System;

namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Type de corps (corpse) dans WoW 3.3.5a
    /// </summary>
    [Flags]
    public enum CorpseType : uint
    {
        /// <summary>
        /// Squelette uniquement (bones) - ne peut pas être ressuscité
        /// </summary>
        Bones = 0,
        
        /// <summary>
        /// Corps ressuscitable après mort PvE
        /// </summary>
        ResurrectablePvE = 1,
        
        /// <summary>
        /// Corps ressuscitable après mort PvP
        /// </summary>
        ResurrectablePvP = 2
    }
}
