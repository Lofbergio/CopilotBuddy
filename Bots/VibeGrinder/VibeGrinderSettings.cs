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

        [Setting, Styx.Helpers.DefaultValue(250f)]
        [Category("Spot"), Description("Distance (yd) at which a spot scores half an identical one at your feet. Lower = prefer closer spots more strongly over denser-but-farther ones.")]
        public float ProximityHalfDistance { get; set; }

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
        [Category("Supervisor"), Description("Depletion confirmation: only relocate a target-empty spot if kills in the last minute were below this. Not a standalone trigger (kills/min is combat-bound at low level, not a supply signal).")]
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

        /// <summary>
        /// Add-avoidance level taper, shared by pull weighting and spot selection so they can't drift:
        /// 1 at/below PullCrowdFullLevel, linear down to 0 at/above PullCrowdLevelCeiling.
        /// </summary>
        public float CrowdLevelScale(int level)
        {
            if (PullCrowdLevelCeiling <= 0 || level >= PullCrowdLevelCeiling)
                return 0f;
            float span = System.Math.Max(1, PullCrowdLevelCeiling - PullCrowdFullLevel);
            return System.Math.Clamp((float)(PullCrowdLevelCeiling - level) / span, 0f, 1f);
        }

        // ---- Pull discipline (add avoidance) ----
        [Setting, Styx.Helpers.DefaultValue(12f)]
        [Category("Survival"), Description("Pull bias: a candidate mob with HOSTILE (proximity-aggro) neighbours within this radius (yd) is deprioritised, so the bot opens on isolated mobs and avoids dragging a pack. Neutral wildlife doesn't count (single-pull is safe). 0 disables.")]
        public float PullCrowdRadius { get; set; }

        [Setting, Styx.Helpers.DefaultValue(60f)]
        [Category("Survival"), Description("Score penalty per hostile add-risk neighbour when choosing what to pull. Higher = avoid packs harder. Deprioritises only, never blocks a pull. 0 disables.")]
        public float PullCrowdPenalty { get; set; }

        [Setting, Styx.Helpers.DefaultValue(15)]
        [Category("Survival"), Description("At/below this level pull add-avoidance runs at full strength — no class can handle hostile packs this early. Above it the penalty tapers linearly toward PullCrowdLevelCeiling (so it's roughly half-strength around 30, 'semi-afraid').")]
        public int PullCrowdFullLevel { get; set; }

        [Setting, Styx.Helpers.DefaultValue(50)]
        [Category("Survival"), Description("Level at/above which pull add-avoidance turns off entirely — by now most classes have AoE/cooldowns and density is pure upside. Between PullCrowdFullLevel and here it scales linearly. 0 disables avoidance everywhere.")]
        public int PullCrowdLevelCeiling { get; set; }

        // ---- Adaptive add-fear (escalate on pack deaths, ease on real progress) ----
        [Setting, Styx.Helpers.DefaultValue(2)]
        [Category("Survival"), Description("Attackers on you at the moment of death for it to count as a 'pack death' that raises crowd caution. 2 = an add helped kill you.")]
        public int PackDeathAttackers { get; set; }

        [Setting, Styx.Helpers.DefaultValue(0.75f)]
        [Category("Survival"), Description("How much the crowd-caution multiplier rises per pack death (it multiplies the pull penalty). 0 disables adaptive fear.")]
        public float CrowdCautionStep { get; set; }

        [Setting, Styx.Helpers.DefaultValue(3f)]
        [Category("Survival"), Description("Ceiling on the crowd-caution multiplier, so a bad night can't make pulling impossible.")]
        public float CrowdCautionMax { get; set; }

        [Setting, Styx.Helpers.DefaultValue(0.5f)]
        [Category("Survival"), Description("Amount crowd caution eases per progress event — a level-up, an area switch, or a completed clean kill streak. Event-driven, never idle wall-clock.")]
        public float CrowdCautionEase { get; set; }

        [Setting, Styx.Helpers.DefaultValue(25)]
        [Category("Survival"), Description("Kills with no death that complete a 'clean streak' and ease crowd caution one step. 0 disables streak easing.")]
        public int CrowdCautionKillStreak { get; set; }

        [Setting, Styx.Helpers.DefaultValue(0.15f)]
        [Category("Survival"), Description("How hard a spot's score is cut for hostile mobs packed within PullCrowdRadius — applied per extra hostile in the worst knot, scaled by the same level taper and crowd-caution as pulls. A factor, not a veto. 0 ignores spot crowding in selection.")]
        public float SpotCrowdPenalty { get; set; }
    }
}
