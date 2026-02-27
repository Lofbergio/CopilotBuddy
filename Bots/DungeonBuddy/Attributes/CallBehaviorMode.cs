namespace Bots.DungeonBuddy.Attributes
{
    public enum CallBehaviorMode
    {
        /// <summary>
        /// Behavior appelé quand le boss est ciblé en combat
        /// </summary>
        Combat,
        
        /// <summary>
        /// Behavior appelé quand le joueur est à proximité du boss
        /// </summary>
        Proximity,
        
        /// <summary>
        /// Behavior appelé quand c'est le boss actuel à tuer
        /// </summary>
        CurrentBoss
    }
}