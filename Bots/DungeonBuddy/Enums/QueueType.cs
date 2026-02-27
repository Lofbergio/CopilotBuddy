namespace Bots.DungeonBuddy.Enums
{
    /// <summary>
    /// Type de queue LFG
    /// </summary>
    public enum QueueType
    {
        /// <summary>
        /// Donjon spécifique choisi
        /// </summary>
        Specific,
        
        /// <summary>
        /// Donjon aléatoire normal (bonus emblem quotidien)
        /// </summary>
        RandomDungeon,
        
        /// <summary>
        /// Donjon héroïque aléatoire (bonus Frost Emblems)
        /// </summary>
        RandomHeroic,
        
        /// <summary>
        /// Mode solo farm (pas de LFG)
        /// </summary>
        SoloFarm
    }
}