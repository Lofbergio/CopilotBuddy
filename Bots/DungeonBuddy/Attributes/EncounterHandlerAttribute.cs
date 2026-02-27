using System;

namespace Bots.DungeonBuddy.Attributes
{
    /// <summary>
    /// Marque une méthode comme handler pour un boss spécifique.
    /// La méthode doit retourner un Composite (behavior tree).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public sealed class EncounterHandlerAttribute : Attribute
    {
        public EncounterHandlerAttribute(int bossEntryId)
            : this(bossEntryId, "")
        {
        }

        public EncounterHandlerAttribute(int bossEntryId, string bossDisplayName)
        {
            BossEntry = bossEntryId;
            BossName = bossDisplayName;
            BossRange = 75;
            Mode = CallBehaviorMode.Combat;
        }

        /// <summary>
        /// Entry ID du boss (from creature_template)
        /// </summary>
        public int BossEntry { get; set; }
        
        /// <summary>
        /// Nom d'affichage du boss
        /// </summary>
        public string BossName { get; set; }
        
        /// <summary>
        /// Distance de détection du boss (yards)
        /// </summary>
        public int BossRange { get; set; }
        
        public int BossRangeSqr => BossRange * BossRange;
        
        /// <summary>
        /// Mode d'appel du behavior
        /// </summary>
        public CallBehaviorMode Mode { get; set; }
    }
}