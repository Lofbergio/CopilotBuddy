using System;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Type de nœud de taxi (points de vol).
    /// WoW 3.3.5a build 12340.
    /// </summary>
    public enum TaxiNodeType
    {
        /// <summary>Position actuelle du joueur</summary>
        Current = 0,
        
        /// <summary>Nœud accessible</summary>
        Reachable = 1,
        
        /// <summary>Nœud distant (pas encore découvert)</summary>
        Distant = 2,
        
        /// <summary>Aucun nœud</summary>
        None = 3
    }
}
