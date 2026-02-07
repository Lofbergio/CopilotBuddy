using System;
using System.IO;
using Styx;
using Styx.Helpers;

namespace Bots.Gather
{
    /// <summary>
    /// Persistent settings for GatherBuddy.
    /// Saved to Settings/GatherBuddySettings_{CharacterName}.xml
    /// </summary>
    public class GatherBuddySettings : Settings
    {
        private static GatherBuddySettings? _instance;
        
        public static GatherBuddySettings Instance => 
            _instance ??= new GatherBuddySettings();

        public GatherBuddySettings()
            : base(Path.Combine(
                Logging.ApplicationPath, 
                $"Settings\\GatherBuddySettings_{StyxWoW.Me?.Name ?? "Unknown"}.xml"))
        {
            Load();
        }

        // ═══════════════════════════════════════════════════════════
        // GATHERING
        // ═══════════════════════════════════════════════════════════
        
        [Setting, DefaultValue(true)]
        public bool GatherHerbs { get; set; }

        [Setting, DefaultValue(true)]
        public bool GatherMinerals { get; set; }

        // ═══════════════════════════════════════════════════════════
        // NAVIGATION
        // ═══════════════════════════════════════════════════════════
        
        [Setting, DefaultValue(PathType.Circle)]
        public PathType PathingType { get; set; }
        
        /// <summary>
        /// Maximum detection range for nodes (yards)
        /// </summary>
        [Setting, DefaultValue(70f)]
        public float NodeDetectionRange { get; set; }
        
        /// <summary>
        /// Height modifier for flying (yards above ground)
        /// </summary>
        [Setting, DefaultValue(0f)]
        public float HeightModifier { get; set; }

        // ═══════════════════════════════════════════════════════════
        // COMBAT
        // ═══════════════════════════════════════════════════════════
        
        /// <summary>
        /// Loot killed mobs during gathering
        /// </summary>
        [Setting, DefaultValue(false)]
        public bool LootMobs { get; set; }
        
        /// <summary>
        /// Ignore Elite mobs (do not pull them)
        /// </summary>
        [Setting, DefaultValue(true)]
        public bool IgnoreElites { get; set; }

        /// <summary>
        /// Face nodes before interacting
        /// </summary>
        [Setting, DefaultValue(true)]
        public bool FaceNodes { get; set; }

        // ═══════════════════════════════════════════════════════════
        // ANTI-NINJA
        // ═══════════════════════════════════════════════════════════
        
        /// <summary>
        /// Do not steal nodes from other players
        /// </summary>
        [Setting, DefaultValue(true)]
        public bool NoNinja { get; set; }
        
        /// <summary>
        /// Blacklist duration for failed nodes (seconds)
        /// </summary>
        [Setting, DefaultValue(20)]
        public int BlacklistTimer { get; set; }

        // ═══════════════════════════════════════════════════════════
        // VENDOR/MAIL (Optional - Phase 2)
        // ═══════════════════════════════════════════════════════════
        
        [Setting, DefaultValue(false)]
        public bool MailToAlt { get; set; }
        
        /// <summary>
        /// Mail recipient character name
        /// </summary>
        [Setting, DefaultValue("")]
        public string MailRecipient { get; set; } = string.Empty;
    }
}
