using System.ComponentModel;
using System.IO;
using Styx;
using Styx.Helpers;

namespace Bots.VibeGrinder
{
    /// <summary>
    /// VibeGrinder tunables, persisted to Settings/VibeGrinderSettings_{Name}.xml.
    /// Defaults are tuned for unattended overnight grinding: conservative on danger,
    /// lenient on contention/scarcity. See the design doc for the rationale per field.
    /// </summary>
    public class VibeGrinderSettings : Settings
    {
        private static VibeGrinderSettings _instance;

        /// <summary>Lazily created on first access (after game attach, so Me.Name is valid).</summary>
        public static VibeGrinderSettings Instance => _instance ??= new VibeGrinderSettings();

        public VibeGrinderSettings()
            : base(Path.Combine(Logging.ApplicationPath,
                string.Format("Settings\\VibeGrinderSettings_{0}.xml",
                    StyxWoW.Me != null ? StyxWoW.Me.Name : "")))
        {
        }

        // ---- Level band ----
        [Setting, Styx.Helpers.DefaultValue(3)]
        [Category("Spot"), Description("How many levels below the character a target mob may be.")]
        public int LevelBandBelow { get; set; }

        [Setting, Styx.Helpers.DefaultValue(3)]
        [Category("Spot"), Description("How many levels above the character a target mob may be.")]
        public int LevelBandAbove { get; set; }

        // ---- Spot geometry ----
        [Setting, Styx.Helpers.DefaultValue(2500f)]
        [Category("Spot"), Description("Max distance (yd) the bot will travel to a spot. Same-map only.")]
        public float MaxTravelDistance { get; set; }

        [Setting, Styx.Helpers.DefaultValue(90f)]
        [Category("Spot"), Description("Cluster/grind radius (yd) defining a spot.")]
        public float GrindRadius { get; set; }

        [Setting, Styx.Helpers.DefaultValue(6)]
        [Category("Spot"), Description("Minimum eligible mobs for a cluster to count as a spot.")]
        public int MinMobsPerSpot { get; set; }

        [Setting, Styx.Helpers.DefaultValue(5)]
        [Category("Spot"), Description("How many top candidates get the expensive path-danger check.")]
        public int TopCandidatesForPathCheck { get; set; }

        // ---- Danger ----
        [Setting, Styx.Helpers.DefaultValue(150f)]
        [Category("Danger"), Description("Radius (yd) around a centroid scanned for hazards.")]
        public float DangerRadius { get; set; }

        [Setting, Styx.Helpers.DefaultValue(4)]
        [Category("Danger"), Description("A normal mob above (character level + this) counts as a hazard.")]
        public int DangerLevelMargin { get; set; }

        [Setting, Styx.Helpers.DefaultValue(3)]
        [Category("Danger"), Description("Hazards clustered this tightly form a 'guard pack' (Dangerous).")]
        public int ElitePackCount { get; set; }

        [Setting, Styx.Helpers.DefaultValue(25f)]
        [Category("Danger"), Description("Radius (yd) for guard-pack clustering.")]
        public float ElitePackRadius { get; set; }

        [Setting, Styx.Helpers.DefaultValue(30f)]
        [Category("Danger"), Description("Half-width (yd) of the travel corridor scanned for hazards.")]
        public float CorridorRadius { get; set; }

        [Setting, Styx.Helpers.DefaultValue(20f)]
        [Category("Danger"), Description("Threat above this on the route marks a spot Dangerous.")]
        public float CorridorDangerCap { get; set; }

        [Setting, Styx.Helpers.DefaultValue(2f)]
        [Category("Danger"), Description("Destination threat at/below this is considered Safe.")]
        public float SafeThreatThreshold { get; set; }

        // ---- Scarcity (leniency for inconvenience only — never danger) ----
        [Setting, Styx.Helpers.DefaultValue(2)]
        [Category("Scarcity"), Description("At/below this candidate count, relax contention/density.")]
        public int ScarcityCandidateFloor { get; set; }

        [Setting, Styx.Helpers.DefaultValue(10)]
        [Category("Scarcity"), Description("At/below this character level, relax contention/density.")]
        public int ScarcityLevelCeiling { get; set; }

        // ---- Supervisor ----
        [Setting, Styx.Helpers.DefaultValue(15)]
        [Category("Supervisor"), Description("Seconds between relocation evaluations.")]
        public int SupervisorIntervalSec { get; set; }

        [Setting, Styx.Helpers.DefaultValue(60)]
        [Category("Supervisor"), Description("Seconds a player must linger in-spot before relocating.")]
        public int IntrusionSeconds { get; set; }

        [Setting, Styx.Helpers.DefaultValue(3f)]
        [Category("Supervisor"), Description("Relocate if kills/min drops below this.")]
        public float MinKillsPerMin { get; set; }

        [Setting, Styx.Helpers.DefaultValue(45)]
        [Category("Supervisor"), Description("Seconds with no targets at a hotspot before depletion fires.")]
        public int EmptyTargetSeconds { get; set; }

        [Setting, Styx.Helpers.DefaultValue(3)]
        [Category("Supervisor"), Description("Deaths within the window that trip the death-loop relocate.")]
        public int DeathLoopCount { get; set; }

        [Setting, Styx.Helpers.DefaultValue(5)]
        [Category("Supervisor"), Description("Death-loop window in minutes.")]
        public int DeathLoopWindowMin { get; set; }

        [Setting, Styx.Helpers.DefaultValue(45)]
        [Category("Supervisor"), Description("Minutes an abandoned spot stays blacklisted.")]
        public int BlacklistMinutes { get; set; }

        [Setting, Styx.Helpers.DefaultValue(true)]
        [Category("Supervisor"), Description("Relocate when a player camps the spot.")]
        public bool EnableIntrusionRelocate { get; set; }

        [Setting, Styx.Helpers.DefaultValue(true)]
        [Category("Supervisor"), Description("Relocate when out-leveling the spot.")]
        public bool EnableLevelDriftRelocate { get; set; }

        [Setting, Styx.Helpers.DefaultValue(true)]
        [Category("Supervisor"), Description("Relocate when the spot is depleted.")]
        public bool EnableDepletionRelocate { get; set; }

        [Setting, Styx.Helpers.DefaultValue(true)]
        [Category("Supervisor"), Description("Relocate on a death loop.")]
        public bool EnableDeathLoopRelocate { get; set; }

        // ---- Survival ----
        [Setting, Styx.Helpers.DefaultValue(2)]
        [Category("Survival"), Description("Free bag slots to keep before a vendor run.")]
        public int MinFreeBagSlots { get; set; }

        [Setting, Styx.Helpers.DefaultValue(0.35f)]
        [Category("Survival"), Description("Durability fraction (0-1) before a repair run.")]
        public float MinDurability { get; set; }

        [Setting, Styx.Helpers.DefaultValue(false)]
        [Category("Survival"), Description("Allow flight-path travel between continents (off for overnight).")]
        public bool AllowTaxiTravel { get; set; }
    }
}
