namespace Bots.DungeonBuddy.Enums
{
    /// <summary>
    /// Mode de fonctionnement du bot
    /// </summary>
    public enum DungeonMode
    {
        /// <summary>
        /// Utilise le Dungeon Finder (LFG)
        /// </summary>
        LookingForGroup,
        
        /// <summary>
        /// Farm solo (pas de queue, entrer manuellement)
        /// </summary>
        Farm
    }
}