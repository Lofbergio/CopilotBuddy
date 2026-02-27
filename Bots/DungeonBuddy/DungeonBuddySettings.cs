using System;
using System.IO;
using Bots.DungeonBuddy.Enums;
using Styx;
using Styx.Helpers;

namespace Bots.DungeonBuddy
{
    public class DungeonBuddySettings : Settings
    {
        private static DungeonBuddySettings _instance;
        
        public static DungeonBuddySettings Instance => 
            _instance ?? (_instance = new DungeonBuddySettings());

        public DungeonBuddySettings()
            : base(Path.Combine(
                Logging.ApplicationPath, 
                $"Settings\\DungeonBuddySettings_{StyxWoW.Me?.Name ?? "Unknown"}.xml"))
        {
            Load();
        }

        // ═══════════════════════════════════════════════════════════
        // MODE
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(DungeonMode.LookingForGroup)]
        public DungeonMode Mode { get; set; }

        [Setting, DefaultValue(QueueType.RandomHeroic)]
        public QueueType QueueType { get; set; }

        /// <summary>
        /// IDs des donjons sélectionnés (pour QueueType.Specific)
        /// </summary>
        [Setting]
        public uint[] SelectedDungeonIds { get; set; } = Array.Empty<uint>();

        // ═══════════════════════════════════════════════════════════
        // ROLE
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(PartyRole.Dps)]
        public PartyRole PreferredRole { get; set; }

        // ═══════════════════════════════════════════════════════════
        // LOOT
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(LootMode.BossesOnly)]
        public LootMode LootMode { get; set; }

        [Setting, DefaultValue(3)]
        public int MinFreeBagSlots { get; set; }

        // ═══════════════════════════════════════════════════════════
        // BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Tue les boss optionnels
        /// </summary>
        [Setting, DefaultValue(false)]
        public bool KillOptionalBosses { get; set; }

        /// <summary>
        /// Requeue automatiquement après fin du donjon
        /// </summary>
        [Setting, DefaultValue(true)]
        public bool AutoRequeue { get; set; }

        /// <summary>
        /// Distance max pour suivre le tank
        /// </summary>
        [Setting, DefaultValue(30f)]
        public float FollowDistance { get; set; }
    }
}