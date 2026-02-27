using Styx.Logic.Pathing;

namespace Bots.DungeonBuddy.Avoidance
{
    /// <summary>
    /// Classe de base pour une zone d'évitement active
    /// </summary>
    public abstract class Avoid
    {
        protected Avoid(AvoidInfo info)
        {
            Info = info;
        }

        public AvoidInfo Info { get; }
        
        public abstract WoWPoint Location { get; }
        
        public float Radius => Info.RadiusSelector();
        
        public float RadiusSqr => Radius * Radius;
        
        public abstract bool IsValid { get; }
        
        /// <summary>
        /// Met à jour l'état de l'avoid
        /// </summary>
        public abstract void Update();
    }
}