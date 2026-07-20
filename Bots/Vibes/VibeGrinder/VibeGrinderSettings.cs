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
    public class VibeGrinderSettings : Settings, Bots.Vibes.Shared.IVibeTuning
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

        // Flight has NO VibeGrinder-local switch (removed 2026-07-06 — it force-seeded the core flags and
        // was redundant): taxi travel follows CharacterSettings.UseFlightPaths, the learning detour
        // additionally requires CharacterSettings.LearnFlightPaths — the two standard Settings checkboxes.
        [Setting, Styx.Helpers.DefaultValue(400)]
        [Category("Survival"), Description("Detour to an unlearned flight master this many yards away to learn it (opportunistic, while 'Use'+'Learn Flight Paths' are on in Settings).")]
        public int FlightLearnRadius { get; set; }

        // Flight internals (see GrindSupervisor.FlightLearnCheck / FlightTravelCheck).
        [Browsable(false)] public int FlightLearnScanSeconds => 5;         // how often to look for a nearby unlearned master
        [Browsable(false)] public int FlightLearnApproachSeconds => 90;    // give up the walk-up after this (then ban)
        [Browsable(false)] public int FlightLearnBanMinutes => 30;         // don't re-attempt a node (learned/failed/unreachable) for this long
        [Browsable(false)] public float FlightMaxTravelDistance => 50000f; // far-spot fallback searches the whole continent
        [Browsable(false)] public int FlightPreTakeoffSeconds => 150;      // abort a taxi hop that never takes off (unreachable master / taxi won't open)

        // ---- Loot disposition ----
        // NOT stored here: the whole Vibe suite classifies items through the same ItemDisposition, so the
        // policy lives in Bots/Vibes/Shared/VibesLootSettings and is surfaced here as an expandable node.
        // Editing it from either bot's panel edits the one shared policy.
        [Category("Loot"), TypeConverter(typeof(ExpandableObjectConverter))]
        [Description("Loot policy (Keep/Vendor/Mail per category) shared by every Vibe bot. Mailing turns on by setting MailRecipient in General settings — there is no separate enable flag.")]
        public Bots.Vibes.Shared.VibesLootSettings Loot => Bots.Vibes.Shared.VibesLootSettings.Instance;

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

        // ---- Water / drowning ----
        // Underwater spots are a poor grind (totems fail, can't rest/eat, drowning risk, and the swimming
        // drop-pin logic won't even fight submerged mobs). DEVALUE, don't veto: multiply a submerged spot's
        // score by 1/(1+SpotWaterPenalty) so land spots always outrank it, but a water camp can still be
        // chosen as a last resort when nothing on land qualifies. 0 disables. Detected by a liquid traceline
        // over the cluster centroid at selection time (top gated candidates only — cheap). See SpotSelector.
        [Browsable(false)] public float SpotWaterPenalty => 4f;
        // Drowning safety net: when the breath bar is draining and under this many seconds remain, surface
        // (JumpAscend) and hold everything else until we can breathe. Rare in practice (VibeGrinder avoids
        // water), but the only guard against a genuine drown. See VibeGrinder.SurfaceIfDrowning.
        [Browsable(false)] public int BreathPanicSeconds => 12;

        // ---- Vendor-run survival floor ----
        // A committed vendor errand suppresses rest (RestGovernor.Suppressed) so it doesn't sit-drink at
        // moderate levels and crawl to the vendor. But a LONG hostile trek (near vendors all in enemy
        // territory → far safe one) then becomes a no-rest death march. Instead of zeroing the thresholds,
        // Suppressed floors them here: rest only fires when CRITICALLY low, so it still travels promptly but
        // won't fight to OOM/death on the way. Low by design (won't rest at 40%).
        [Browsable(false)] public int EmergencyMinHealth => 30;
        [Browsable(false)] public int EmergencyMinMana => 10;

        // Cap PullDistance so the bot walks into the routine's cast range before engaging, instead of
        // stalling on a distant/stationary mob that's "in pull range" but out of cast range (27 ≈ 3yd
        // inside a 30yd caster cast range; clamps DOWN only, so a deliberate melee value is untouched).
        [Browsable(false)] public int MaxPullDistance => 27;

        // Path defense (don't body-pull while traveling). IncludeNearbyHostiles surfaces a foreign hostile
        // this far out — WIDER than MaxPullDistance (27) because at walk speed a 27yd ring leaves only ~1s of
        // lead before a ~19yd aggro radius, too little for the commit->target->cast pull pipeline, so the mob
        // aggros first (the "body-pulled as if he didn't see them" bug). 40yd gives ~3s of deliberate-pull lead.
        [Browsable(false)] public int IncidentalHostileRadius => 40;
        // A hostile within its OWN aggro range + this buffer is "about to body-pull us" (~25yd for a same-level
        // mob). ApplyPullCommitment preempts a farther commitment for such a mob so we engage the path threat
        // deliberately instead of walking past it. Narrow on purpose — only mobs inside their aggro bubble — so
        // it doesn't reopen the commitment wobble it's meant to prevent.
        [Browsable(false)] public float PreemptAggroBuffer => 6f;

        // Vendor-run commitment (see VibeGrinder.UpdateVendorRun). Hold vendor mode this long past the last
        // vendor-POI/combat tick — long enough to ride out a peel fight's POI flip + brief gaps, short enough
        // that we resume grinding promptly after the transaction. Abort (resume grind) if the errand can't
        // complete within VendorRunAbortSeconds, so an unreachable/unaffordable vendor never wedges us.
        [Browsable(false)] public int VendorRunStickySeconds => 4;
        [Browsable(false)] public int VendorRunAbortSeconds => 300;

        // Vendor safety: reject a resolved vendor with at least VendorHostileThreshold player-HOSTILE creature
        // spawns within VendorHostileRadius yd — it's in enemy territory (e.g. a repair NPC the DB flags inside
        // an Alliance camp). The vendor is blacklisted and the resolver picks the next-nearest safe one. 0 disables.
        [Browsable(false)] public float VendorHostileRadius => 45f;
        [Browsable(false)] public int VendorHostileThreshold => 3;

        // Stay-in-your-zone: vendor resolution is continent-wide (the Barrens, Dustwallow, Mulgore are all map
        // 1), so the nearest vendor can sit one zone over in much higher-level country (a lvl-21 toon routed
        // from the Barrens into Dustwallow Marsh dies crossing the border). Reject a vendor whose surrounding
        // wild mobs (within VendorAreaScanRadius yd) average more than VendorAreaLevelMargin above our level.
        // Over-level is the gate, NOT distance — a far SAME-level vendor is fine; nearest-first then keeps us in
        // the current zone among the level-OK options. 0 margin... keep >=4 so a band-edge zone isn't vetoed.
        [Browsable(false)] public float VendorAreaScanRadius => 200f;
        [Browsable(false)] public int VendorAreaLevelMargin => 7;
        // Topology gate (the Caverns-of-Time Yarley case): reject a vendor whose WALK is this many times
        // the straight-line distance (and past the floor — short trips never trip). 0 disables.
        [Browsable(false)] public float VendorDetourFactor => 2.5f;
        [Browsable(false)] public float VendorDetourMinYd => 600f;

        // ---- Supervisor timings ----
        [Browsable(false)] public int SupervisorIntervalSec => 15;
        [Browsable(false)] public int IntrusionSeconds => 60;
        [Browsable(false)] public float MinKillsPerMin => 3f;
        [Browsable(false)] public int EmptyTargetSeconds => 45;
        [Browsable(false)] public float DepletionRecheckBuffer => 10f;   // extend the spot by this many yards to confirm it's truly empty before relocating
        [Browsable(false)] public float EngageGraceSeconds => 3f;        // hold the ENGAGING latch this long past the last combat/commit tick (hysteresis vs the travel↔kill oscillation)
        [Browsable(false)] public int DeathLoopCount => 3;
        // 5 → 30 (2026-07-06): the Tanaris night's 4 deaths spanned 29 min — a 5-min window never saw
        // more than 1, so the spot was never abandoned and the durability bleed set up the repair-run
        // wedge. "3 deaths in 30 min at one spot" = leave. _deaths is cleared on every spot install, so
        // the wider window stays per-spot (any intermediate relocate also resets it — accepted).
        [Browsable(false)] public int DeathLoopWindowMin => 30;
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

        // Hard dead-man's switch — the unconditional floor UNDER StallSeconds. If NO real progress (no
        // kill, no level-up, no meaningful travel of HardProgressRadius yd) happens for this long, FORCE
        // an escape regardless of state — mounted, resting, vendoring, a fight it can't close, ANY reason,
        // no exemptions: nuke every latch/POI, big-blacklist the current area, relocate anywhere else. The
        // guarantee that the bot can never sit doing nothing for more than ~10 min (the "mounted in water,
        // oscillating forever" trap the soft watchdog missed — it exempts mounted and resets on any jitter).
        // Long fuse + NET-travel progress so a legit slow patch or a long cross-zone trek never trips it.
        [Browsable(false)] public int HardStallMinutes => 10;
        [Browsable(false)] public float HardProgressRadius => 150f;        // net ground covered that counts as "going somewhere" (yd)
        [Browsable(false)] public float HardStallBlacklistRadius => 400f;  // blacklist this big on a hard escape so selection can't re-pick the trap

        // Water-trap relocate (see GrindSupervisor.SwimTrapCheck): fast, reactive escape from a coastal camp whose
        // mobs sit in the water. NOT a selection ban — it fires only after the bot proves it can't work the spot:
        // SwimTrapDrops commit→swim cycles with ZERO kills since arriving (any kill clears the count). Bounds the
        // waste to ~SwimTrapSeconds instead of the 10-min hard switch; blacklists wide enough to step off the strip.
        [Browsable(false)] public int SwimTrapDrops => 3;
        [Browsable(false)] public int SwimTrapSeconds => 60;
        [Browsable(false)] public float SwimTrapBlacklistRadius => 150f;

        // EXPERIMENTAL (2026-07-04, Den of Flame timber fence): persistent wedge blackspot. Styx's stuck handler
        // only blackspots as the LAST step of its unstick escalation and ResetUnstickAttempts() wipes it on any
        // ≥10yd jitter, so a recurring TERRAIN wedge (a protruding timber the mesh thinks is passable) never gets
        // marked and the bot re-walks into it forever. This is a jitter-robust NET-travel detector: not resting/
        // vendoring/mounted/in combat, no kill, and within WedgeMoveRadius of an anchor for WedgeSeconds = wedged
        // on geometry → drop a SOFT (60× cost) session-length blackspot so the router bends around it. Soft so a
        // wedge that IS the only way in/out still stays traversable. Marks persist across relocations (the timber
        // stays bad) and are removed on Stop. Disable with EnableWedgeBlackspot=false.
        [Browsable(false)] public bool EnableWedgeBlackspot => true;
        [Browsable(false)] public int WedgeSeconds => 40;                   // no kill + no net travel this long = wedged
        [Browsable(false)] public float WedgeMoveRadius => 12f;             // net travel under this over the window = "not covering ground"
        [Browsable(false)] public float WedgeBlackspotRadius => 10f;        // soft-cost circle radius (push the route off the timber)
        [Browsable(false)] public float WedgeBlackspotHeight => 12f;
        [Browsable(false)] public float WedgeMinSeparation => 15f;          // don't re-mark within this of an existing wedge mark
        [Browsable(false)] public int MaxWedgeBlackspots => 40;             // session cap
        [Browsable(false)] public int WedgeBlackspotTtlMinutes => 40;       // self-heal: a mark this old is dropped (a misfire can't outlive it; the real timber re-marks in ~40s)

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
        // Open-moment guard: a hostile can wander within range during the (often long) walk to a committed
        // neutral, so right before we OPEN on it, defer the neutral if a hostile is within this radius — wider
        // than the commit-time veto (22) because the threat closes during approach. Stops the "shot the giraffe,
        // then a thunder-lizard 25yd away aggroed → 2-mob pull" — the bot takes the hostile first, neutral later.
        [Browsable(false)] public float NeutralOpenAvoidRadius => 30f;
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
        // Entry ban (the caged Theramore Prisoners, log 2026-07-03): this many DISTINCT same-entry mobs
        // each failing an in-place engage (give-up in range/LoS without ever opening) = the NAME is
        // unengageable here (server-LoS trap / scripted NPC) → ban the entry for EntryBanMinutes. A kill
        // of the entry resets its strikes, so a grind mob that fails one bad corner never accumulates.
        [Browsable(false)] public int EntryBanGiveUps => 3;
        [Browsable(false)] public int EntryBanMinutes => 45;
        // EXPERIMENTAL (2026-07-04, Den of Flame timber-fence trap): a mob we committed to but that fell off
        // the candidate list without us ever closing distance or opening on it is almost always physically
        // unreachable (the navmesh says reachable but a world object wedges us short). The flap resets the
        // PullCommitMaxSeconds give-up clock so it never matures to a blacklist — so ban at the drop edge.
        // Per-guid, session-length. If this bans reachable mobs in the wild, flip EnableExperimentalDropBan
        // to false (and consider deleting the branch — see ApplyPullCommitment).
        [Browsable(false)] public bool EnableExperimentalDropBan => true;
        [Browsable(false)] public int ExperimentalDropBanMinutes => 240;
        [Browsable(false)] public int PullCrowdFullLevel => 15;
        [Browsable(false)] public int PullCrowdLevelCeiling => AddAvoidanceMode == AddAvoidance.Aggressive ? 70 : 50;

        // ---- Fight-position pack lookahead (2026-07-05, Lost Rigger 11-mob pack death) ----
        // "If I fight this mob, who else joins?" — see CLAUDE.md "Fight-position pack lookahead" and
        // docs/superpowers/specs/2026-07-05-pack-lookahead-design.md. Per-layer kill switches: flip
        // false + rebuild to disable a misfiring layer without a revert.
        [Browsable(false)] public bool EnableExposureGate => true;       // acquire / preempt / mid-approach gates
        [Browsable(false)] public bool EnableBubbleVeto => true;         // bubble-overlap veto in the weighting
        [Browsable(false)] public bool EnableCorridorCheck => true;      // path-crossing count at acquire
        [Browsable(false)] public bool EnableCampWallRelocate => true;   // reject-knot → survival relocate
        [Browsable(false)] public bool EnableDeathRearm => true;         // post-res BlacklistMobsInRadius re-arm
        // Hysteresis pad on a mob's server aggro range when counting bubbles (matches the old
        // committed-threat exemption's +5).
        [Browsable(false)] public float ExposurePad => 5f;
        // AzerothCore CONFIG_CREATURE_FAMILY_ASSISTANCE_RADIUS = 10 (+slack). Deliberately COARSE and
        // conservative: real assist is faction-gated and re-broadcasts ~2s centered on the ENGAGED
        // (moving) mob — do not "tighten" this thinking it's exact.
        [Browsable(false)] public float AssistRadius => 12f;
        [Browsable(false)] public int ExposureRejectSeconds => 45;
        // Camp wall: this many DISTINCT exposure-rejected mobs inside CampWallWindowSeconds (position-
        // ungated — a 30yd knot test never summed a 100+yd camp; count clears on install) ⇒ the camp can't
        // be nibbled — survival-relocate away. Kills do NOT clear it (the camp-edge 1v1 nibble loop this
        // breaks IS a stream of kills). Blacklist radius = max(floor below, reject spread + GrindRadius).
        [Browsable(false)] public int CampWallRejects => 3;
        [Browsable(false)] public int CampWallWindowSeconds => 180;
        [Browsable(false)] public float CampWallBlacklistRadius => 150f;
        // Selection-side bubble-knot gate: a candidate where ≥ this many hostiles' server aggro bubbles
        // cover one kill position is Dangerous — survival, never level-tapered, never ladder-relaxed.
        [Browsable(false)] public int SpotBubbleDangerCount => 4;
        // Vertical band for the spot-selection box queries (purity / level-avg / hazards): a cave 40yd
        // under a mesa is not "near" — the same column-query Z bug class the blackspots already fixed.
        [Browsable(false)] public float SpotQueryZBand => 40f;

        /// <summary>
        /// Fear-meter fight-size cap: max TOTAL mobs in a fight we CHOOSE (target + adds). Confident
        /// (caution 1) tolerates 2 adds; each pack death tightens via CrowdCautionFactor down to
        /// singles-only — same confidence-ready caution signal SpotCrowdPenalty uses.
        /// </summary>
        public int MaxFightCompany(double caution) =>
            (int)System.Math.Clamp(3.0 - System.Math.Round(caution - 1.0), 1.0, 3.0);

        [Browsable(false)] public int PackDeathAttackers => 2;
        [Browsable(false)] public float CrowdCautionStep => AddAvoidanceMode == AddAvoidance.Off ? 0f : 0.75f;
        [Browsable(false)] public float CrowdCautionMax => 6f;   // was 3 — let swarm deaths climb fear higher (caution step now scales with crowd size)

        // Death-cluster escalation (flee a death-trap region, don't hop camp-to-camp inside it). A Silithid hive
        // swarms 20+ mobs and produced 53 deaths in one log because each death only blacklisted ONE 90yd camp.
        // Now the blacklist radius per pack death = GrindRadius × (deaths clustered in this region) × crowdFactor,
        // capped at MaxDeathBlacklistRadius. crowdFactor scales with swarm size (attackers / PackDeathAttackers,
        // clamped to DeathCrowdRadiusMax) so a 20-mob death blacklists a BIG area on the first death; a couple of
        // them blacklist the whole cluster and force a far relocate. DeathClusterAbandonCount regional deaths =
        // "abandon the area" (logged). Death spots are remembered across spot-hops (NOT cleared on install).
        [Browsable(false)] public float DeathRegionRadius => 500f;        // deaths within this of each other = one cluster
        [Browsable(false)] public int DeathRegionWindowMin => 15;          // remember clustered deaths this long
        [Browsable(false)] public float MaxDeathBlacklistRadius => 700f;   // cap — "the whole cluster"
        [Browsable(false)] public float DeathCrowdRadiusMax => 5f;         // crowd-size multiplier clamp on the radius
        [Browsable(false)] public int DeathClusterAbandonCount => 3;       // this many regional deaths ⇒ flee the area
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
