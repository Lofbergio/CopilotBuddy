using System;
using System.ComponentModel;
using System.IO;
using Styx;
using Styx.Helpers;

namespace Bots.VibeGrinder
{
    /// <summary>How hard the bot works to avoid pulling extra mobs.</summary>
    public enum AddAvoidance
    {
        /// <summary>Pull the nearest mob regardless of neighbours.</summary>
        Off,
        /// <summary>Solo-pull while low/squishy, relax as you out-level, tighten up after pack deaths.</summary>
        Auto,
        /// <summary>Always prefer isolated pulls — safer, slower XP.</summary>
        Aggressive,
    }

    /// <summary>When the supervisor is allowed to leave a spot.</summary>
    public enum RelocateMode
    {
        /// <summary>Never auto-relocate.</summary>
        Off,
        /// <summary>Only leave on a death loop (dying repeatedly).</summary>
        DeathLoopOnly,
        /// <summary>Leave on player camping, out-leveling, depletion, or a death loop.</summary>
        Auto,
    }

    /// <summary>
    /// VibeGrinder tunables, persisted to Settings/VibeGrinderSettings_{Name}.xml.
    ///
    /// Design: the UI exposes only knobs a player can reason about ("how far above me will I fight?",
    /// "avoid packs how hard?", "when do I leave a spot?"). The many formula constants the algorithm
    /// needs (scan radii, scoring weights, supervisor timings, the add-avoidance tuning curve) are
    /// fixed below as [Browsable(false)] computed properties — same values as before, just not
    /// user-facing, because no one can sanely tune "CorridorDangerCap=20 vs 22". Two enums
    /// (AddAvoidance, RelocateMode) replace ~14 individually-meaningless knobs.
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

        // =====================================================================================
        //  User-facing settings
        // =====================================================================================

        // ---- Spot ----
        [Setting, Styx.Helpers.DefaultValue(2)]
        [Category("Spot"), Description("Levels BELOW you the bot will still fight at a chosen spot (and how far you must out-level it before relocating). Spot discovery itself uses a fixed combat-safe ±2 window.")]
        public int LevelBandBelow { get; set; }

        [Setting, Styx.Helpers.DefaultValue(3)]
        [Category("Spot"), Description("Levels ABOVE you the bot will still fight at a chosen spot. Spot discovery itself uses a fixed combat-safe ±2 window.")]
        public int LevelBandAbove { get; set; }

        [Setting, Styx.Helpers.DefaultValue(2500)]
        [Category("Spot"), Description("Max distance (yd) the bot will travel to a spot. Same-map only.")]
        public int MaxTravelDistance { get; set; }

        [Setting, Styx.Helpers.DefaultValue(6)]
        [Category("Spot"), Description("Minimum eligible mobs for a cluster to count as a spot. Lower it on low-population servers; the bot auto-relaxes this when candidates are scarce or you're very low level.")]
        public int MinMobsPerSpot { get; set; }

        // ---- Danger ----
        [Setting, Styx.Helpers.DefaultValue(4)]
        [Category("Danger"), Description("A normal mob more than this many levels above you counts as a hazard. Lower = avoid tough areas harder.")]
        public int DangerLevelMargin { get; set; }

        [Setting, Styx.Helpers.DefaultValue(35)]
        [Category("Danger"), Description("Hard body-pull gate: an over-level HOSTILE mob within this distance (yd) of a kill position makes a spot Dangerous (it aggros from beyond pull range and is too strong to clear). 0 disables.")]
        public int AggroAvoidBuffer { get; set; }

        // ---- Survival ----
        [Setting, Styx.Helpers.DefaultValue(2)]
        [Category("Survival"), Description("Free bag slots to keep before a vendor run.")]
        public int MinFreeBagSlots { get; set; }

        [Setting, Styx.Helpers.DefaultValue(35)]
        [Category("Survival"), Description("Repair when durability drops below this percent (0-100).")]
        public int MinDurabilityPct { get; set; }

        [Setting, Styx.Helpers.DefaultValue(false)]
        [Category("Survival"), Description("Allow flight-path travel between continents (off for overnight).")]
        public bool AllowTaxiTravel { get; set; }

        // ---- Loot disposition (what to do with looted items on a vendor/mail run) ----
        // Decided by category, not quality: cloth (white) is income, a white sword is trash. Mail needs
        // EnableMailing + a recipient + a reachable mailbox; with mailing unavailable, Mail items just stay
        // in bags (never silently vendored). Soulbound items can't be mailed — a Mail action on a soulbound
        // item vendors it (epics are always kept). Grey is always vendored; quest/keys/bags/ammo/
        // consumables/reagents/heirlooms are always kept. See Loot/CLAUDE.md.
        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Mail)]
        [Category("Loot"), Description("Cloth, leather, ore, herbs, enchanting mats — the main grind income. Mail to bank by default.")]
        public DispositionAction TradeGoodsAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Vendor)]
        [Category("Loot"), Description("Cooking meat (a low-value trade good). Vendor by default.")]
        public DispositionAction MeatAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Vendor)]
        [Category("Loot"), Description("White (common) weapons/armor. Vendor by default (low value).")]
        public DispositionAction WhiteGearAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Vendor)]
        [Category("Loot"), Description("Green (uncommon) BoE gear. Vendor by default; set to Mail if you disenchant/AH them (soulbound greens are always vendored).")]
        public DispositionAction GreenGearAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Mail)]
        [Category("Loot"), Description("Blue (rare) gear. BoE mailed to bank by default; soulbound blues are vendored.")]
        public DispositionAction BlueGearAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Mail)]
        [Category("Loot"), Description("Epic gear. BoE mailed to bank; epics are NEVER auto-sold regardless of this (soulbound epics are kept).")]
        public DispositionAction EpicGearAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Mail)]
        [Category("Loot"), Description("Recipes/patterns and gems. Mail to bank by default.")]
        public DispositionAction RecipesGemsAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(AddAvoidance.Auto)]
        [Category("Survival"), Description("How hard to avoid pulling extra mobs. Auto: solo-pull while low/squishy, relax as you out-level, tighten up after deaths to adds. Aggressive: always prefer isolated pulls (safer, slower). Off: pull the nearest mob regardless of neighbours.")]
        public AddAvoidance AddAvoidanceMode { get; set; }

        // ---- Supervisor ----
        [Setting, Styx.Helpers.DefaultValue(RelocateMode.Auto)]
        [Category("Supervisor"), Description("When to leave a spot. Auto: relocate on player camping, out-leveling, depletion, or a death loop. DeathLoopOnly: only leave if you're dying repeatedly. Off: never auto-relocate.")]
        public RelocateMode Relocate { get; set; }

        [Setting, Styx.Helpers.DefaultValue(45)]
        [Category("Supervisor"), Description("Minutes an abandoned spot stays blacklisted before the bot will consider it again.")]
        public int BlacklistMinutes { get; set; }

        // ---- Mailing ----
        [Setting, Styx.Helpers.DefaultValue(false)]
        [Category("Mailing"), Description("Mail valuables to a bank alt during vendor runs. Requires MailRecipient set + MailWhite/MailGreen enabled in General settings, and mailbox locations in Mailboxes.db. Enemy-faction-territory mailboxes are skipped automatically. The bot's best food/drink are auto-protected. Off by default.")]
        public bool EnableMailing { get; set; }

        // =====================================================================================
        //  Fixed algorithm constants (not user-tunable; kept as members so call sites are stable)
        // =====================================================================================

        // ---- Spot geometry / scoring ----
        [Browsable(false)] public float GrindRadius => 90f;                 // cluster/grind radius (yd)
        [Browsable(false)] public float ProximityHalfDistance => 250f;      // half-score distance
        [Browsable(false)] public float LowLevelSpotPenalty => 0.4f;        // quadratic low-avg-level penalty
        [Browsable(false)] public int TopCandidatesForPathCheck => 25;      // path-danger budget
        [Browsable(false)] public int ScarcityCandidateFloor => 2;          // relax contention at/below
        [Browsable(false)] public int ScarcityLevelCeiling => 10;           // relax density at/below level

        // ---- Danger model ----
        [Browsable(false)] public float DangerRadius => 150f;
        [Browsable(false)] public int ElitePackCount => 3;
        [Browsable(false)] public float ElitePackRadius => 25f;
        [Browsable(false)] public float CorridorRadius => 30f;
        [Browsable(false)] public float CorridorDangerCap => 20f;
        [Browsable(false)] public float SafeThreatThreshold => 2f;
        // Path gauntlet: over-level HOSTILE normals on the route (elite-only PathDanger can't see them).
        // Margin 2 ⇒ counts hostiles > player+2 (i.e. +3 and up) — a body-pull while running past is lethal
        // to a squishy lowbie even at +3. Tolerance 2 ⇒ reject a route that crosses ≥2 of them; SelectBest
        // relaxes this (→ ×2 → off) when no safer-route spot qualifies, so it never idles forever.
        [Browsable(false)] public int PathHostileLevelMargin => 2;
        [Browsable(false)] public int PathGauntletTolerance => 2;
        // Safe-rest: before sitting to eat/drink, back off if a hostile is within SafeRestDangerRange.
        // The HP/mana trigger comes from RestGovernor (mirrors Singular's live MinHealth/MinMana), so
        // SafeRest fires on exactly the band the routine actually rests at — see SafeRestReposition.
        [Browsable(false)] public float SafeRestDangerRange => 30f;

        // Cap PullDistance so the bot walks into the routine's cast range before engaging, instead of
        // stalling on a distant/stationary mob that's "in pull range" but out of cast range (27 ≈ 3yd
        // inside a 30yd caster cast range; clamps DOWN only, so a deliberate melee value is untouched).
        [Browsable(false)] public int MaxPullDistance => 27;

        // ---- Supervisor timings ----
        [Browsable(false)] public int SupervisorIntervalSec => 15;
        [Browsable(false)] public int IntrusionSeconds => 60;
        [Browsable(false)] public float MinKillsPerMin => 3f;
        [Browsable(false)] public int EmptyTargetSeconds => 45;
        [Browsable(false)] public int DeathLoopCount => 3;
        [Browsable(false)] public int DeathLoopWindowMin => 5;
        // Freeze watchdog: a body that hasn't moved AND hasn't killed for this long (while alive, out
        // of combat, not eating/drinking, not on a vendor trek) is wedged — break it. Depletion only
        // catches an EMPTY target list; this catches "valid target present, even in range, but the
        // tree never pulls" (e.g. Singular Rest gating Pull when there's no drink to recover mana).
        // 180s clears a legit low-level no-drink rest (mana fills in <1min, then a kill resets the clock)
        // while an unbounded freeze trips. Runs from Pulse() so it's immune to tree-branch starvation.
        [Browsable(false)] public int StallSeconds => 180;
        [Browsable(false)] public float StallMoveEpsilon => 5f;   // moved less than this = "not moving" (yd)
        // Diagnostic fires this early (and once per stationary episode) to capture the short-lived
        // "standing with a valid target, not pulling" symptom before it resolves; the relocate break
        // still waits for StallSeconds. Set well above a normal pull wind-up / loot pause.
        [Browsable(false)] public int StallDiagSeconds => 12;

        // ---- Supervisor triggers (derived from Relocate mode) ----
        [Browsable(false)] public bool EnableIntrusionRelocate => Relocate == RelocateMode.Auto;
        [Browsable(false)] public bool EnableLevelDriftRelocate => Relocate == RelocateMode.Auto;
        [Browsable(false)] public bool EnableDepletionRelocate => Relocate == RelocateMode.Auto;
        [Browsable(false)] public bool EnableDeathLoopRelocate => Relocate != RelocateMode.Off;
        [Browsable(false)] public bool EnableStallRelocate => Relocate != RelocateMode.Off;

        // ---- Add-avoidance tuning (derived from AddAvoidanceMode) ----
        [Browsable(false)] public float PullCrowdRadius => AddAvoidanceMode == AddAvoidance.Off ? 0f : 12f;
        [Browsable(false)]
        public float PullCrowdPenalty => AddAvoidanceMode switch
        {
            AddAvoidance.Off => 0f,
            AddAvoidance.Aggressive => 120f,
            _ => 60f,
        };
        // Don't OPEN on a neutral that has a hostile within this radius — engaging it means walking into
        // the hostile bubble (or AoE drags them in). Wider than PullCrowdRadius: it's the approach/aggro
        // bubble, not the touching radius. The veto is large enough to bury the neutral below any clean
        // target so we only pull it when genuinely isolated.
        [Browsable(false)] public float NeutralHostileAvoidRadius => 22f;
        [Browsable(false)] public float NeutralNearHostileVeto => 1000f;
        // Cap on the hostile-crowd pull penalty: base score loses 2/yd, so 40 ≈ 20yd. Keeps add-avoidance a
        // tiebreaker among similarly-close mobs and stops it ever picking a farther mob over a nearer threat.
        [Browsable(false)] public float PullCrowdPenaltyCap => 40f;
        // HARD VETO: refuse to OPEN on a mob with this many hostile neighbours within PullCrowdRadius (a real
        // camp). The capped penalty above is only a tiebreaker and can't refuse a camp; this removes camped
        // mobs from the candidate list while squishy so we relocate rather than feed into a 4-mob bonfire camp.
        [Browsable(false)] public int PullPackVetoCount => 3;
        // Engagement commitment (ApplyPullCommitment): once we pick a mob to pull, add this to its score so
        // it stays FirstUnit and the pull sees it through instead of re-deciding every tick. Huge = absolute
        // pin. Drop the commitment after PullCommitMaxSeconds without engaging (unreachable → blacklist).
        [Browsable(false)] public float PullCommitBoost => 100000f;
        [Browsable(false)] public int PullCommitMaxSeconds => 20;
        [Browsable(false)] public int PullCrowdFullLevel => 15;
        [Browsable(false)] public int PullCrowdLevelCeiling => AddAvoidanceMode == AddAvoidance.Aggressive ? 70 : 50;
        [Browsable(false)] public int PackDeathAttackers => 2;
        [Browsable(false)] public float CrowdCautionStep => AddAvoidanceMode == AddAvoidance.Off ? 0f : 0.75f;
        [Browsable(false)] public float CrowdCautionMax => 3f;
        [Browsable(false)] public float CrowdCautionEase => 0.5f;
        [Browsable(false)] public int CrowdCautionKillStreak => 25;
        [Browsable(false)]
        public float SpotCrowdPenalty => AddAvoidanceMode switch
        {
            AddAvoidance.Off => 0f,
            AddAvoidance.Aggressive => 0.3f,
            _ => 0.15f,
        };

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

        /// <summary>Repair-gate durability as a 0-1 fraction (the profile/LevelBot consumes a fraction).</summary>
        public float MinDurabilityFraction => System.Math.Clamp(MinDurabilityPct / 100f, 0f, 0.99f);

        /// <summary>
        /// Clamp user-entered values into sane ranges. Call once at Start — a 0 or negative in the
        /// wrong place (bag slots, mob count, travel distance) silently breaks selection with no error.
        /// </summary>
        public void Sanitize()
        {
            LevelBandBelow = Math.Clamp(LevelBandBelow, 0, 30);
            LevelBandAbove = Math.Clamp(LevelBandAbove, 0, 30);
            MaxTravelDistance = Math.Clamp(MaxTravelDistance, 100, 50000);
            MinMobsPerSpot = Math.Clamp(MinMobsPerSpot, 1, 50);
            DangerLevelMargin = Math.Clamp(DangerLevelMargin, 0, 60);
            AggroAvoidBuffer = Math.Clamp(AggroAvoidBuffer, 0, 200);
            MinFreeBagSlots = Math.Clamp(MinFreeBagSlots, 0, 28);
            MinDurabilityPct = Math.Clamp(MinDurabilityPct, 0, 99);
            BlacklistMinutes = Math.Clamp(BlacklistMinutes, 0, 1440);
        }
    }
}
