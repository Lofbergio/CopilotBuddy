using System.ComponentModel;
using System.IO;
using Styx.Helpers;

namespace Bots.Vibes.VibeQuester2
{
    /// <summary>
    /// v2's own quest-layer knobs. Engagement/danger knobs deliberately stay on VibeGrinderSettings
    /// (one tuning surface for the shared engine). Ints only — the float locale round-trip trap.
    /// </summary>
    public class VibeQuester2Settings : Settings
    {
        private static VibeQuester2Settings _instance;
        public static VibeQuester2Settings Instance => _instance ??= new VibeQuester2Settings();

        public VibeQuester2Settings()
            : base(Path.Combine(Logging.ApplicationPath, "Settings", "VibeQuester2Settings.xml"))
        {
        }

        [Setting, Styx.Helpers.DefaultValue(500)]
        [Description("Initial giver/objective scan radius (yd); expands by ScanStep when nothing is found.")]
        public int ScanStartDistance { get; set; }

        [Setting, Styx.Helpers.DefaultValue(500)]
        [Description("Scan radius growth per empty scan (yd).")]
        public int ScanStep { get; set; }

        [Setting, Styx.Helpers.DefaultValue(3000)]
        [Description("Maximum travel/scan distance for quest work (yd).")]
        public int MaxTravelDistance { get; set; }

        [Setting, Styx.Helpers.DefaultValue(3)]
        [Description("Skip quests whose objective mobs are more than this many levels above the character.")]
        public int MaxMobOverLevel { get; set; }

        [Setting, Styx.Helpers.DefaultValue(10)]
        [Description("Maximum quests planned at once.")]
        public int MaxQuestsPerPlan { get; set; }

        [Setting, Styx.Helpers.DefaultValue(1)]
        [Description("Arbiter: quest supply below this flips to grind-mode.")]
        public int SupplyLowWater { get; set; }

        [Setting, Styx.Helpers.DefaultValue(3)]
        [Description("Arbiter: quest supply at/above this flips back to quest-mode (hysteresis).")]
        public int SupplyHighWater { get; set; }

        [Setting, Styx.Helpers.DefaultValue(20)]
        [Description("Auto-blacklist TTL (minutes) for quests that failed at runtime (deaths / no progress).")]
        public int AutoBlacklistMinutes { get; set; }

        [Setting, Styx.Helpers.DefaultValue(3)]
        [Description("Deaths on one quest before it is abandoned and TTL-blacklisted.")]
        public int DeathBlacklistThreshold { get; set; }

        [Setting, Styx.Helpers.DefaultValue(6)]
        [Description("Task abandon: no objective progress for this many minutes abandons the quest.")]
        public int TaskStallMinutes { get; set; }
    }
}
