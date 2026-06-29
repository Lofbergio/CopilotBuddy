using System.Windows.Forms;
using Bots.Grind;
using Bots.VibeGrinder.Data;
using Bots.VibeGrinder.Selection;
using Bots.VibeGrinder.Supervision;
using Bots.VibeGrinder.Synthesis;
using Bots.Vibes.Shared;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Inventory;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Bots.VibeGrinder
{
    /// <summary>
    /// VibeGrinder — a profile-free overnight grinder. On Start it picks the best safe grind spot
    /// near the character's live position/level/faction from GrindMobs.db, synthesizes a GrindArea +
    /// minimal Profile, and runs LevelBot's reused combat/loot/death/vendor trees plus a Supervisor
    /// that relocates when the spot goes bad. Every Start recomputes from live state — stop, play
    /// your toon, start again later, and it re-picks cleanly with no carried-over state.
    /// </summary>
    public class VibeGrinder : BotBase
    {
        private FactionResolver _factions;
        private MailboxService _mailboxes;
        private SpotSelector _selector;
        private GrindAreaSynthesizer _synth;
        private GrindSupervisor _supervisor;
        private RestGovernor _restGovernor;
        private PrioritySelector _root;
        private BotEvents.Player.MobKilledDelegate _onKill;
        private bool _spotInstalled;
        private ulong _peelGuid;        // committed transit-peel target (see TransitPeel) — don't re-pick per tick
        private ulong _committedGuid;   // committed grind-pull target (see ApplyPullCommitment)
        private readonly System.Diagnostics.Stopwatch _committedTimer = new System.Diagnostics.Stopwatch();
        private double _committedLastDist = double.MaxValue;   // last distance to committed mob (progress watchdog)
        private readonly System.Diagnostics.Stopwatch _selectRetry = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _safeRest = new System.Diagnostics.Stopwatch();
        private bool _resting;                  // committed rest state (sticky: enter at Min*, exit at RestDonePct/cap)
        private WoWPoint _restSpot = WoWPoint.Empty;   // committed safe-rest destination (picked once per rest)
        private const int RestHysteresisPct = 12;  // resume at Min*+this (modest hysteresis), NOT a flat 85% top-off
        private const int RestMaxSeconds = 45;  // safety cap: if not recovered by now, give up resting and resume

        public override string Name => "VibeGrinder";

        public override bool IsPrimaryType => true;

        public override bool RequiresProfile => false;

        public override bool RequirementsMet => GrindMobsRepository.IsAvailable;

        public override PulseFlags PulseFlags => PulseFlags.All;

        public override Form ConfigurationForm => new VibeGrinderSettingsForm();

        public override Composite Root
        {
            get
            {
                if (_root == null)
                {
                    _root = new PrioritySelector(
                        // Defer spot selection to the first tick: the navmesh isn't loaded until
                        // RaiseBotStart fires (after BotBase.Start). Holds the tree until a spot
                        // is installed; once installed this gate falls through.
                        new Decorator(ctx => !_spotInstalled, new Action(ctx => EnsureSpotSelected())),
                        LevelBot.CreateDeathBehavior(),
                        // Safe-rest positioning: before Singular sits to eat/drink in the middle of a camp,
                        // back off to a clear spot. Returns Failure (→ falls through to normal combat/rest)
                        // when already clear, boxed in, in combat, or not resting — so it can't deadlock.
                        new Action(ctx => SafeRestReposition()),
                        LevelBot.CreateCombatBehavior(),
                        // Transit discipline (only while OnVendorRun): don't detour to loot corpses on the
                        // way to a vendor — stay moving, out of the danger corridor sooner (drops are
                        // grabbed later when grinding).
                        new Decorator(ctx => !OnVendorRun(), LevelBot.CreateLootBehavior()),
                        // ...and during a VENDOR RUN ONLY, deliberately single-pull the most isolated hostile
                        // in pull range instead of body-pulling blind into a pack. Vendor-only is load-bearing:
                        // on a vendor run Roam's find-target + ApplyPullCommitment are DISABLED (vendor POI is
                        // excluded from Roam, commitment is !OnVendorRun), so TransitPeel is the SOLE target
                        // driver. On a grind trek those are active and already commit to one mob — running
                        // TransitPeel there too put TWO drivers on one pull (TransitPeel→closest-in-LoS vs
                        // ShouldClearPoiForBetterTarget→committed FirstUnit) and thrashed the POI between two
                        // mobs every tick, pulling both. Grind treks instead only SURFACE foreign hostiles
                        // (TransitIncludeHostiles, InTransit) and let the single existing commitment pull them.
                        new Decorator(ctx => OnVendorRun() && !StyxWoW.Me.Combat, new Action(ctx => TransitPeel())),
                        LevelBot.CreateVendorBehavior(),
                        // Rest commitment: while we've decided to rest and aren't topped off yet, OWN the tick so
                        // Relocate/Roam below cannot pull us off the rest spot to grind — the rest↔roam oscillation.
                        // The actual eat/drink happens above in CombatBehavior's not-in-combat RestBehavior.
                        new Action(ctx => RestRoamBlock()),
                        _supervisor != null ? _supervisor.RelocationCheck() : new Action(ctx => RunStatus.Failure),
                        LevelBot.CreateRoamBehavior(),
                        new Action(ctx => RunStatus.Success) // idle
                    );
                }
                return _root;
            }
        }

        public override void Start()
        {
            // --- Start contract: clean slate. Spot selection is deferred to the first tick
            // (EnsureSpotSelected) because the navmesh isn't loaded yet during Start(). ---
            VibeGrinderSettings.Instance.Sanitize();   // clamp any out-of-range user values
            LevelBot.ResetState();
            BotPoi.Clear("VibeGrinder start");
            _root = null;
            _spotInstalled = false;
            _peelGuid = 0;
            _committedGuid = 0;
            _committedTimer.Reset();
            _committedLastDist = double.MaxValue;
            _resting = false;
            _restSpot = WoWPoint.Empty;
            _safeRest.Reset();
            _selectRetry.Reset();

            _factions = new FactionResolver();
            _mailboxes = new MailboxService();
            _synth = new GrindAreaSynthesizer(_mailboxes);
            _selector = new SpotSelector(_factions);
            _supervisor = new GrindSupervisor(_selector, _synth, _factions);
            _restGovernor = new RestGovernor();   // dynamic rest thresholds; SafeRest reads these

            // Reuse LevelBot's target/loot filters (faction + blackspot + loot rules).
            Targeting.Instance.IncludeTargetsFilter += LevelBot.LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter += LevelBot.LevelbotIncludeLootsFilter;
            // Pull discipline: bias FirstUnit toward isolated mobs so we don't open on a pack.
            Targeting.Instance.WeighTargetsFilter += WeighTargetsAvoidPacks;
            // Transit discipline: surface nearby hostiles during vendor runs (the LevelBot filter adds
            // none then → uncontrolled body-pulls) so TransitPeel can single them off deliberately.
            Targeting.Instance.IncludeTargetsFilter += TransitIncludeHostiles;

            _onKill = args => _supervisor.RecordKill();
            BotEvents.Player.OnMobKilled += _onKill;

            // Loot disposition: VibeGrinder owns sell/mail item selection via ItemDisposition (one source
            // of truth, category-aware). The sell hook protects everything that isn't Vendor; the mail hook
            // queues everything that is Mail. The synthetic profile's sell mask + zeroed mail flags make
            // these hooks the deciders (see GrindAreaSynthesizer.EnsureProfile and Loot/CLAUDE.md).
            Vendors.OnVendorItems += OnVendorSweep;
            Vendors.OnMailItems += OnMailSweep;

            Bots.DungeonBuddy.Avoidance.WorldObstacleManager.Initialize();
        }

        /// <summary>
        /// First-tick spot selection. Waits for the navmesh to load (it isn't ready during Start),
        /// then selects + installs a spot once. On "no spot" it idles and retries (every 10s) rather
        /// than calling TreeRoot.Stop() — stopping from the worker thread interrupts it mid-log and
        /// can crash the app. Returns Success so nothing else runs until a spot is installed.
        /// </summary>
        private RunStatus EnsureSpotSelected()
        {
            if (!Navigator.IsNavigatorLoaded)
            {
                TreeRoot.StatusText = "VibeGrinder: waiting for navmesh to load...";
                return RunStatus.Success;
            }

            // Throttle re-selection so a genuine "no spot here" doesn't run a full scan every tick.
            if (_selectRetry.IsRunning && _selectRetry.Elapsed.TotalSeconds < 10)
            {
                TreeRoot.StatusText = "VibeGrinder: no acceptable spot — retrying...";
                return RunStatus.Success;
            }

            var me = StyxWoW.Me;
            uint map = me.MapId;
            _factions.Build(map);

            GrindSpot spot = _selector.SelectBest(map, me.Location, me.Level, _supervisor.ActiveBlacklist(), _supervisor.CrowdCautionFactor);
            if (spot == null)
            {
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder] No acceptable spot near you right now — idling, will retry. " +
                    "Move closer to mobs, widen the level band, or relax danger settings.");
                _selectRetry.Restart();
                return RunStatus.Success;
            }

            _synth.Install(spot, me.Level);
            _supervisor.OnInstalled(spot);
            _spotInstalled = true;
            _selectRetry.Reset();
            return RunStatus.Success;
        }

        /// <summary>
        /// Pull discipline: deprioritise (never remove) mobs that have *hostile* neighbours within
        /// PullCrowdRadius, so FirstUnit — the mob we open on — is the most isolated one available.
        /// Only proximity-aggro mobs (reaction ≤ Hostile) count: neutral wildlife — the bulk of
        /// low-level grind targets — won't add when you single-pull one of a pack, so a dense beast
        /// camp must NOT be penalised. (Caveat: an AoE opener like War Stomp can still aggro nearby
        /// neutrals — that's a routine concern, not a pull-selection one.) A squishy lowbie can't
        /// survive real adds; selection rewards density, so without this the bot opens on whichever
        /// hostile is nearest and drags its friends in. The penalty scales down with level and turns
        /// off past PullCrowdLevelCeiling — by then you have AoE/cooldowns and density is upside.
        /// Pre-pull only: once in combat we leave weighting alone so the routine can retarget to
        /// whatever's actually hitting us.
        /// </summary>
        private void WeighTargetsAvoidPacks(System.Collections.Generic.List<Targeting.TargetPriority> units)
        {
            var me = StyxWoW.Me;
            if (me == null) return;

            // In combat, LOCK onto the mob we're actually fighting: pin it to the top so FirstUnit == current
            // target and LevelBot's ShouldClearPoiForBetterTarget can't hop the POI to a "better" mob mid-fight
            // and pull an add (the "fighting a giraffe, then Flame Shock a Kolkar" switch). Finish it, then
            // re-acquire next tick. NOT a 2nd target driver — it only suppresses the in-combat hop; the routine
            // still cleaves/AoEs via its own combat logic. (Pre-combat selection/commitment is below.)
            if (me.Combat)
            {
                PinCurrentTarget(units, me);
                return;
            }

            // 1. Quality weighting decides which mob is the *cleanest* to open on (runs in transit too —
            //    TransitPeel relies on the resulting isolation ordering).
            ApplyCrowdAndNeutralWeighting(units, me);

            // 2. Commitment pins that choice so we actually pull it through, instead of re-deciding every
            //    tick. Grind pull only — during a vendor run TransitPeel owns the transit target.
            if (!OnVendorRun())
                ApplyPullCommitment(units, me);
        }

        /// <summary>
        /// In-combat target lock. Pin the mob we're currently fighting to the top of the score so it stays
        /// FirstUnit — LevelBot's ShouldClearPoiForBetterTarget only clears the Kill POI when currentTarget !=
        /// FirstUnit, so keeping them equal stops the mid-combat hop that pulls adds. When the target dies
        /// CurrentTargetGuid changes and the next tick pins the new one (or, if none, normal re-acquire). A
        /// no-op if our target somehow isn't a candidate (no worse than today's behavior).
        /// </summary>
        private void PinCurrentTarget(System.Collections.Generic.List<Targeting.TargetPriority> units, WoWUnit me)
        {
            ulong ct = me.CurrentTargetGuid;
            if (ct == 0) return;
            for (int i = 0; i < units.Count; i++)
                if (units[i].Object != null && units[i].Object.Guid == ct)
                {
                    units[i].Score += VibeGrinderSettings.Instance.PullCommitBoost;
                    break;
                }
        }

        /// <summary>
        /// Pre-combat QUALITY weighting: deprioritise crowded pulls (capped tiebreaker) and bury neutrals
        /// beside hostiles, so the mob we COMMIT to is the cleanest available. Only shapes the initial pick
        /// — once committed (ApplyPullCommitment) the bot sticks regardless of later score wobble.
        /// </summary>
        private void ApplyCrowdAndNeutralWeighting(System.Collections.Generic.List<Targeting.TargetPriority> units, WoWUnit me)
        {
            var s = VibeGrinderSettings.Instance;
            if (s.PullCrowdPenalty <= 0f || s.PullCrowdRadius <= 0f) return;

            // Squishy-lowbie concern: full strength at/below PullCrowdFullLevel, tapering off by
            // PullCrowdLevelCeiling (you have AoE/cooldowns; density is upside).
            float levelScale = s.CrowdLevelScale(me.Level);
            if (levelScale <= 0f) return;
            // Adaptive: scale up the more we've been dying to packs (eased by progress).
            float caution = _supervisor != null ? (float)_supervisor.CrowdCautionFactor : 1f;
            float penalty = s.PullCrowdPenalty * levelScale * caution;

            // Snapshot ALL live hostiles from the object manager — NOT just the current target list. An
            // un-aggroed Kolkar isn't a candidate (units) yet, but it WILL proximity-aggro the instant we
            // engage a mob beside it; counting only list members is exactly how we opened on a giraffe with
            // hostiles "next door" that weren't in the list yet, then ate the adds.
            var hostiles = new System.Collections.Generic.List<WoWUnit>();
            foreach (WoWUnit h in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
                if (h != null && h is not WoWPlayer && !h.Dead && h.MyReaction <= WoWUnitReaction.Hostile)
                    hostiles.Add(h);

            float crowdR2 = s.PullCrowdRadius * s.PullCrowdRadius;
            float neutR2 = s.NeutralHostileAvoidRadius * s.NeutralHostileAvoidRadius;
            // Iterate BACKWARD so the hard-veto can RemoveAt(i) safely.
            for (int i = units.Count - 1; i >= 0; i--)
            {
                WoWUnit a = units[i].Object.ToUnit();
                if (a == null) continue;
                bool neutral = a.MyReaction > WoWUnitReaction.Hostile;

                // A hostile already targeting us, or one we're already inside the aggro bubble of, is a
                // COMMITTED threat — it will be in the fight the instant we move, so pull the nearest such
                // mob rather than walk past it to a "cleaner" one farther off (that's how we pulled a distant
                // mob while a nearer one aggroed onto us). Leave it alone — never veto a mob already on us.
                if (!neutral && (a.CurrentTargetGuid == me.Guid || a.Distance <= a.MyAggroRange + 5f))
                    continue;

                int addRisk = 0;
                bool hostileInNeutralRange = false;
                foreach (WoWUnit b in hostiles)
                {
                    if (b.Guid == a.Guid) continue;
                    double d2 = a.Location.DistanceSqr(b.Location);
                    if (d2 <= crowdR2) addRisk++;
                    if (neutral && d2 <= neutR2) hostileInNeutralRange = true;
                }

                // HARD VETO a genuine camp: PullPackVetoCount+ hostile neighbours within the crowd radius.
                // The capped penalty below is only a tiebreaker (so we don't walk past a near mob to a far
                // one) — it CANNOT refuse a camp, which is how a squishy caster fed itself into a 4-mob Kolkar
                // bonfire camp and died. Drop camped hostiles from the candidate list entirely while squishy:
                // we won't OPEN on a camp, and if the area is all camps the empty list drives a relocate.
                if (!neutral && addRisk >= s.PullPackVetoCount)
                {
                    units.RemoveAt(i);
                    continue;
                }

                // CAP the crowd penalty so it stays a TIEBREAKER among similarly-close mobs and can never
                // outweigh proximity (base score loses only 2/yd). Without the cap a near mob with neighbours
                // loses to a far isolated one and the bot walks INTO/past the near pack to reach it.
                if (addRisk > 0)
                    units[i].Score -= System.Math.Min(addRisk * penalty, s.PullCrowdPenaltyCap);

                // A NEUTRAL beside hostiles is pure downside: it won't come to us, so we'd walk INTO the
                // hostile bubble to hit it (and an AoE opener like War Stomp drags them in) — the 3-mob fight
                // we started by opening on a giraffe in a Kolkar camp. Bury it so we never OPEN on it while a
                // cleaner target (or resting) exists. Hostiles aren't buried — they're the grind targets.
                if (hostileInNeutralRange)
                    units[i].Score -= s.NeutralNearHostileVeto;
            }
        }

        /// <summary>
        /// Engagement commitment — the fix for the whole class of "the bot keeps changing its mind" bugs
        /// (pulled a far mob while a nearer one aggroed; switched target mid-pull; opened on the wrong mob).
        /// The base game re-scores every target every tick, so FirstUnit wobbles as we and the mobs move,
        /// and LevelBot dutifully re-targets/re-pulls whatever is momentarily top. Instead: once we COMMIT
        /// to a mob, pin it to the top of the score so it stays FirstUnit — LevelBot then walks to and pulls
        /// THAT mob, start to finish — and only re-pick when it's dead, gone, or proven unreachable. Quality
        /// scoring above still chooses the initial commit; after that, stability beats re-optimising.
        /// (In combat the routine owns target choice, so this only governs the pre-pull approach.)
        /// </summary>
        private void ApplyPullCommitment(System.Collections.Generic.List<Targeting.TargetPriority> units, WoWUnit me)
        {
            var s = VibeGrinderSettings.Instance;

            // Don't chase a mob into water. Oasis turtles (Oasis Snapjaw) sit in the pool below the bank, so
            // the commitment locks onto one, wades us in, and we tread water at an unreachable target. If we're
            // swimming, blacklist the committed mob and drop — Roam then heads back to the on-land hotspot.
            if (me.IsSwimming)
            {
                if (_committedGuid != 0)
                {
                    Logging.Write(System.Drawing.Color.Khaki,
                        "[VibeGrinder/Commit] swimming after {0:X} — blacklisting and dropping (won't chase into water).",
                        _committedGuid);
                    Blacklist.Add(_committedGuid, System.TimeSpan.FromMinutes(2));
                    _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue;
                }
                return;
            }

            // Validate the standing commitment: still a live candidate? And have we been genuinely unable to
            // engage it (stuck/unreachable) — NOT merely slow to walk there? The give-up clock is PROGRESS-
            // based: while we're still closing the distance it resets, so it only expires after
            // PullCommitMaxSeconds of NO approach progress. (Pure wall-clock blacklisted a giraffe we were
            // just walking to and re-targeted a Kolkar mid-approach.) Once in range we enter combat and this
            // whole method stands down — PinCurrentTarget takes over — so this governs the approach only.
            if (_committedGuid != 0)
            {
                WoWUnit held = FindCandidate(units, _committedGuid);
                bool valid = held != null && !held.Dead;
                if (valid)
                {
                    double d = held.Location.Distance(me.Location);
                    if (d < _committedLastDist - 0.5)   // closing the gap = progress; reset the give-up clock
                        _committedTimer.Restart();
                    _committedLastDist = d;

                    if (_committedTimer.Elapsed.TotalSeconds > s.PullCommitMaxSeconds)
                    {
                        // DIAG (Bug-A): give-up is the dominant failure (87/run). Capture WHY: which mob we
                        // were locked to, how far, its reaction, and the nearest hostile that existed — a
                        // far/neutral commit while a near hostile sat vetoed out of the list is the smoking gun.
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] GIVE UP on {0} (reaction={1}, d={2:F1}, noProgressFor={3:F0}s, " +
                            "moving={4}, inLoS={5}) — blacklisting 2m. nearestHostile={6} candidates={7}",
                            held.Name, held.MyReaction, d, _committedTimer.Elapsed.TotalSeconds,
                            me.IsMoving, held.InLineOfSpellSight, NearestHostileDesc(me), units.Count);
                        Blacklist.Add(_committedGuid, System.TimeSpan.FromMinutes(2));
                        valid = false;
                    }
                }
                else if (_committedGuid != 0)
                {
                    Logging.WriteDebug("[VibeGrinder/Commit] drop {0:X} — {1}", _committedGuid,
                        held == null ? "no longer a candidate" : "dead");
                }
                if (!valid) { _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue; }
            }

            // Acquire: commit to the best-scored candidate (after the quality weighting above).
            if (_committedGuid == 0)
            {
                Targeting.TargetPriority best = null;
                for (int i = 0; i < units.Count; i++)
                    if (units[i].Object != null && (best == null || units[i].Score > best.Score))
                        best = units[i];
                if (best != null)
                {
                    _committedGuid = best.Object.Guid;
                    _committedTimer.Restart();
                    _committedLastDist = double.MaxValue;
                    // DIAG (Bug-A): what we chose to open on. If this is repeatedly a far/neutral mob while
                    // NearestHostileDesc shows a closer hostile, the crowd/neutral veto is starving us of
                    // pullable near targets and committing us to stragglers we can't catch.
                    WoWUnit bu = best.Object.ToUnit();
                    if (bu != null)
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] ACQUIRE {0} (reaction={1}, d={2:F1}, score={3:F0}) " +
                            "nearestHostile={4} candidates={5}",
                            bu.Name, bu.MyReaction, bu.Distance, best.Score, NearestHostileDesc(me), units.Count);
                }
            }

            // Hold: pin the committed mob to the top so it stays FirstUnit and the pull sees it through.
            if (_committedGuid != 0)
                for (int i = 0; i < units.Count; i++)
                    if (units[i].Object != null && units[i].Object.Guid == _committedGuid)
                    {
                        units[i].Score += s.PullCommitBoost;
                        break;
                    }
        }

        private static WoWUnit FindCandidate(System.Collections.Generic.List<Targeting.TargetPriority> units, ulong guid)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].Object != null && units[i].Object.Guid == guid)
                    return units[i].Object.ToUnit();
            return null;
        }

        /// <summary>
        /// True while travelling to service a vendor (sell/repair/mail/buy/train/flight master). The
        /// transit discipline (no loot detours, hostile-only peel) applies only in this window; once a
        /// peel sets a Kill POI or we're grinding/roaming this is false, so normal play is untouched.
        /// </summary>
        /// <summary>
        /// Safe-rest positioning. Singular's Rest just MoveStops + eats where it stands (Rest.cs), so after a
        /// fight it sits at low HP/mana inside the camp and the next spawn kills it. Before that, if a hostile
        /// is within SafeRestDangerRange, back off directly away from the nearest one to a reachable point and
        /// re-check next tick. Returns Success while repositioning; Failure (→ Rest proceeds in place) when
        /// already clear, already eating/drinking, in combat, can't path away, or after an 8s cap — so it
        /// never deadlocks or interrupts an in-progress sit.
        /// </summary>
        /// <summary>
        /// Committed rest — the fix for the rest↔roam oscillation (163× "backing off to rest safely" ↔
        /// "Moving to hotspot" per run). We enter a STICKY rest state at the Min* band and stay in it until
        /// topped off (RestDonePct) or a safety cap — no per-tick re-decide. This branch only handles the
        /// MOVE to a committed safe spot (exclusive); once parked it returns Failure so CombatBehavior's
        /// not-in-combat RestBehavior eats/drinks, and RestRoamBlock (below Vendor) stops Roam/Relocate from
        /// dragging us back into the camp mid-rest. Combat/death preempt rest (Fight is higher priority).
        /// </summary>
        private RunStatus SafeRestReposition()
        {
            var me = StyxWoW.Me;
            if (me == null) return RunStatus.Failure;

            UpdateRestingState(me);
            // Not resting, or can't/shouldn't reposition (in combat handled above; already eating/drinking;
            // mounted) → fall through; CombatBehavior's RestBehavior + RestRoamBlock take it from here.
            if (!_resting || me.Combat || me.Mounted || me.HasAura("Food") || me.HasAura("Drink"))
                return RunStatus.Failure;

            var s = VibeGrinderSettings.Instance;

            // Pick a committed safe spot ONCE, only if a hostile is too close to rest where we stand.
            if (_restSpot == WoWPoint.Empty)
            {
                WoWUnit nearest = NearestHostileWithin(me, s.SafeRestDangerRange);
                if (nearest == null) return RunStatus.Failure;                  // already clear → rest in place
                if (!TryPickSafeSpot(me, s.SafeRestDangerRange, nearest, out _restSpot))
                {
                    _restSpot = WoWPoint.Empty;
                    return RunStatus.Failure;                                   // boxed in → rest in place (roam-block holds us)
                }
                // DIAG (Bug-B secondary): repeated picks per rest = chasing a receding "safe" spot across a
                // dense camp (TryPickSafeSpot only dodges the SINGLE nearest hostile). Watch the cadence.
                Logging.Write(System.Drawing.Color.Khaki,
                    "[VibeGrinder/Rest] safe-spot pick: backing off {0:F0}yd from {1} (d={2:F0})",
                    me.Location.Distance(_restSpot), nearest.Name, nearest.Distance);
            }

            // Drive to the committed spot (exclusive — owns movement so nothing below interleaves).
            if (me.Location.Distance(_restSpot) <= 3f)
            {
                if (me.IsMoving) WoWMovement.MoveStop();
                _restSpot = WoWPoint.Empty;                                     // arrived → rest here
                Logging.WriteDebug("[VibeGrinder/Rest] reached safe spot — resting in place");
                return RunStatus.Failure;
            }
            TreeRoot.StatusText = "VibeGrinder: moving to a safe spot to rest";
            Navigator.MoveTo(_restSpot);
            return RunStatus.Success;
        }

        /// <summary>Sticky rest state: enter at the Min* band, exit only at RestDonePct (hysteresis) or the cap.</summary>
        private void UpdateRestingState(WoWUnit me)
        {
            // Never rest while swimming — eat/drink silently fail underwater and the routine loops on them
            // for the whole 45s cap (the oasis "Drinking Melon Juice" stall). Stay un-rested so Roam keeps us
            // moving back to land, where a normal rest can happen.
            if (me.Combat || me.IsDead || me.IsGhost || me.IsSwimming)
            {
                EndRest(me.IsSwimming ? "swimming" : "combat/death");
                return;
            }

            int minHp = _restGovernor?.MinHealth ?? 55;
            int minMana = _restGovernor?.MinMana ?? 45;
            // Mana only governs rest when we actually have water: with no drink, waiting for mana to recover is
            // a pointless freeze (RestRoamBlock holds us still for the full 45s cap while mana crawls up on
            // regen), so rest on HP alone and resume at whatever mana — the routine has instants/melee. Vendor
            // buying restocks water; until then, don't stand frozen for it.
            bool manaGoverns = me.PowerType == WoWPowerType.Mana && minMana > 0
                               && Styx.Logic.Inventory.Consumable.GetBestDrink(false) != null;

            if (!_resting)
            {
                bool need = me.HealthPercent <= minHp || (manaGoverns && me.ManaPercent <= minMana);
                if (need)
                {
                    _resting = true; _restSpot = WoWPoint.Empty; _safeRest.Restart();
                    // DIAG (Bug-B): rest enter/exit cadence + the live band + whether mana is even gating.
                    Logging.Write(System.Drawing.Color.Khaki,
                        "[VibeGrinder/Rest] ENTER hp={0}%/min{1} mana={2}%/min{3} water={4}",
                        me.HealthPercent, minHp, manaGoverns ? me.ManaPercent : -1, manaGoverns ? minMana : 0, manaGoverns);
                }
                return;
            }

            // Resume at a modest hysteresis above the enter band — NOT the old flat 85% top-off (RestDonePct),
            // which froze a no-water caster for the full cap every rest (mana crawled 67->85 on regen while
            // Roam was blocked — the 11:13 stall). Caps below keep a high-caution band from chasing near-full.
            int doneHp = System.Math.Min(95, minHp + RestHysteresisPct);
            int doneMana = System.Math.Min(90, minMana + RestHysteresisPct);
            bool recovered = me.HealthPercent >= doneHp && (!manaGoverns || me.ManaPercent >= doneMana);
            if (recovered) EndRest("recovered");
            else if (_safeRest.Elapsed.TotalSeconds > RestMaxSeconds) EndRest("cap");
        }

        private void EndRest(string reason = null)
        {
            if (_resting && reason != null)
            {
                var me = StyxWoW.Me;
                Logging.Write(System.Drawing.Color.Khaki, "[VibeGrinder/Rest] EXIT ({0}) hp={1}% mana={2}% after {3:F0}s",
                    reason, me?.HealthPercent ?? -1,
                    me != null && me.PowerType == WoWPowerType.Mana ? me.ManaPercent : -1,
                    _safeRest.Elapsed.TotalSeconds);
            }
            _resting = false; _restSpot = WoWPoint.Empty; _safeRest.Reset();
        }

        /// <summary>While committed to resting, own the tick so Roam/Relocate below can't pull us off to grind.</summary>
        private RunStatus RestRoamBlock() => _resting ? RunStatus.Success : RunStatus.Failure;

        private static WoWUnit NearestHostileWithin(WoWUnit me, float range)
        {
            WoWUnit nearest = null;
            double best = double.MaxValue;
            foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(true, false))
            {
                if (u == null || u.Dead || !u.IsHostile) continue;
                double d = u.Distance;
                if (d <= range && d < best) { best = d; nearest = u; }
            }
            return nearest;
        }

        /// <summary>DIAG: nearest live hostile of any distance, for commitment logging ("near hostile existed
        /// but we committed to a far straggler"). Returns "name d=NN reaction=R" or "none".</summary>
        private static string NearestHostileDesc(WoWUnit me)
        {
            WoWUnit n = null;
            double best = double.MaxValue;
            foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
            {
                if (u == null || u is WoWPlayer || u.Dead || u.MyReaction > WoWUnitReaction.Hostile) continue;
                double d = u.Distance;
                if (d < best) { best = d; n = u; }
            }
            return n == null ? "none" : string.Format("{0} d={1:F0} inLoS={2}", n.Name, best, n.InLineOfSpellSight);
        }

        // Back off directly away from the nearest hostile to just past the danger range; commit only if reachable.
        private static bool TryPickSafeSpot(WoWUnit me, float danger, WoWUnit nearest, out WoWPoint spot)
        {
            float dx = me.Location.X - nearest.Location.X;
            float dy = me.Location.Y - nearest.Location.Y;
            float len = (float)System.Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.1f) { dx = 1f; dy = 0f; len = 1f; }   // degenerate: any direction
            float scale = (danger + 12f) / len;
            WoWPoint away = new WoWPoint(me.Location.X + dx * scale, me.Location.Y + dy * scale, me.Location.Z);
            WoWPoint[] path = Navigator.GeneratePath(me.Location, away);
            if (path != null && path.Length > 0) { spot = away; return true; }
            spot = WoWPoint.Empty;
            return false;
        }

        private static bool OnVendorRun()
        {
            switch (BotPoi.Current.Type)
            {
                case PoiType.Sell:
                case PoiType.Repair:
                case PoiType.Mail:
                case PoiType.Buy:
                case PoiType.Train:
                case PoiType.Fly:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True while trekking to a grind hotspot we haven't reached yet — the initial run-in AND every
        /// relocation (both leave CurrentHotSpot != LastHotSpot until we arrive within PathPrecision, then it
        /// flips false). The LevelBot target filter only surfaces grind-faction/in-band mobs, so a FOREIGN
        /// hostile pack on the path is invisible to Targeting and the bot bodyblocks/runs into it. Gating the
        /// transit peel on this clears the path during treks without touching settled in-camp grinding.
        /// </summary>
        private static bool TrekkingToSpot()
        {
            var me = StyxWoW.Me;
            if (me == null || me.Combat) return false;
            var ga = StyxWoW.AreaManager?.CurrentGrindArea;
            return ga != null && ga.HotspotChanged;
        }

        /// <summary>A transit leg where the body-pull-avoidance peel applies: a vendor run OR a spot trek.</summary>
        private static bool InTransit() => OnVendorRun() || TrekkingToSpot();

        /// <summary>
        /// Transit targeting: on any transit leg (vendor run OR spot trek) LevelBotIncludeTargetsFilter
        /// surfaces too little — NO targets on a vendor run (HB 6.2.3: "only aggro mobs"), and only
        /// grind-faction/in-band mobs while trekking — so a foreign hostile pack on the path is invisible
        /// and the bot body-pulls/runs into it. Re-add nearby *hostiles* within pull range (neutrals don't
        /// proximity-aggro, so they never add on a trek) so the caution weighting (WeighTargetsAvoidPacks)
        /// orders them and the puller single-pulls the most isolated one. WHO pulls differs by leg: on a
        /// vendor run TransitPeel (Roam/commitment are disabled there); on a grind trek the normal Roam
        /// find-target + ApplyPullCommitment that are already active — do NOT also run TransitPeel on a trek,
        /// or the two drivers thrash the POI between mobs. Pre-combat, in-transit only.
        /// </summary>
        private void TransitIncludeHostiles(System.Collections.Generic.List<WoWObject> incoming,
                                            System.Collections.Generic.HashSet<WoWObject> outgoing)
        {
            if (!InTransit() || StyxWoW.Me.Combat) return;
            double pull = Targeting.PullDistance;
            if (pull <= 0) return;
            foreach (WoWObject obj in incoming)
            {
                if (obj is WoWUnit u && obj is not WoWPlayer
                    && !u.Dead
                    && u.MyReaction <= WoWUnitReaction.Hostile   // hostiles only — neutrals won't body-pull us
                    && u.Distance <= pull)
                {
                    outgoing.Add(obj);
                }
            }
        }

        /// <summary>
        /// Transit peel: on a VENDOR RUN, before anything body-pulls us, deliberately single-pull the most
        /// isolated hostile in pull range (TargetList is caution-ordered: FirstUnit == [0]) by handing it to
        /// the existing CombatBehavior as a Kill POI. Returns Success once a POI is set (next tick pulls);
        /// Failure (→ VendorBehavior resumes travel) when there's nothing to peel. Skips when essentially at
        /// the vendor so it interacts instead of starting a fight on the doorstep. Vendor-run ONLY — on a
        /// grind trek the normal Roam find-target + ApplyPullCommitment own the pull, and adding a second
        /// driver here thrashes the POI between targets (don't re-gate this to grind treks).
        /// </summary>
        private RunStatus TransitPeel()
        {
            var me = StyxWoW.Me;
            // At the destination — let VendorBehavior interact; don't peel on the doorstep.
            if (BotPoi.Current.Location.Distance(me.Location) <= 12f) return RunStatus.Failure;

            double pull = Targeting.PullDistance;
            if (pull <= 0) return RunStatus.Failure;

            // COMMIT to the mob we already peeled: keep it until it's dead/gone, don't re-scan for a "more
            // isolated" target every tick. The pull's first cast aggroes the mob but doesn't flag combat for
            // ~1s, and in that window the bot moves — so an un-committed re-scan picks a *different* nearest
            // mob and pulls IT too (we aggroed a Stormer, then switched to a Wrangler mid-cast → 2-mob fight).
            if (_peelGuid != 0)
            {
                WoWUnit cur = ObjectManager.GetObjectByGuid<WoWUnit>(_peelGuid);
                if (cur != null && !cur.Dead && cur.Distance <= pull * 1.5)
                {
                    if (me.CurrentTargetGuid != _peelGuid) cur.Target();
                    BotPoi.Current = new BotPoi(cur, PoiType.Kill);
                    return RunStatus.Success;
                }
                _peelGuid = 0;   // committed mob dead or out of reach — free to pick a fresh one
            }

            foreach (WoWUnit u in Targeting.Instance.TargetList)   // caution-ordered: most isolated first
            {
                if (u == null || u.Dead) continue;
                if (u.MyReaction > WoWUnitReaction.Hostile) continue;   // hostiles only — neutrals won't body-pull
                if (u.Distance > pull || !u.InLineOfSpellSight) continue;
                // Target it: CanPull()/Singular key off CurrentTarget, and CombatBehavior only calls
                // Target() when switching off a different POI — ours already is FirstUnit, so without this
                // the mob is never targeted, the pull never fires, and the Kill/vendor POI thrashes.
                _peelGuid = u.Guid;
                u.Target();
                BotPoi.Current = new BotPoi(u, PoiType.Kill);
                return RunStatus.Success;
            }
            return RunStatus.Failure;
        }

        public override void Stop()
        {
            Targeting.Instance.IncludeTargetsFilter -= LevelBot.LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter -= LevelBot.LevelbotIncludeLootsFilter;
            Targeting.Instance.WeighTargetsFilter -= WeighTargetsAvoidPacks;
            Targeting.Instance.IncludeTargetsFilter -= TransitIncludeHostiles;
            if (_onKill != null)
            {
                BotEvents.Player.OnMobKilled -= _onKill;
                _onKill = null;
            }
            Vendors.OnVendorItems -= OnVendorSweep;
            Vendors.OnMailItems -= OnMailSweep;
            _synth?.RestoreCharacterSettings();   // undo the global FoodAmount/DrinkAmount seeding
            GrindMobsRepository.Shutdown();   // release the DB handle so a later Start re-opens cleanly
            Bots.DungeonBuddy.Avoidance.WorldObstacleManager.Shutdown();
        }

        public override void Pulse()
        {
            var me = StyxWoW.Me;
            if (me == null) return;   // null on loading screens / zone transitions

            Navigator.PathPrecision = System.Math.Clamp(me.MovementInfo.CurrentSpeed * 0.15f, 1.5f, 10f);
            _restGovernor?.Pulse(me);
            _supervisor?.Pulse();

            if (VibeGrinderSettings.Instance.EnableMailing)
                _mailboxes?.CheckCurrentMailboxSafety();   // shared runtime backstop (see MailboxService)
        }

        /// <summary>
        /// Sell hook (Vendors.OnVendorItems): protect every carried item whose disposition isn't Vendor.
        /// The profile sell mask is wide (grey→blue), so SellAllItems would otherwise sell white cloth,
        /// BoE greens, etc. — protecting non-Vendor items leaves exactly the junk to be sold.
        /// </summary>
        private void OnVendorSweep(SellItemsEventArgs args)
        {
            var me = StyxWoW.Me;
            if (me == null) return;

            int protectedCount = 0, willMail = 0;
            foreach (WoWItem item in me.CarriedItems)
            {
                if (item == null) continue;
                DispositionAction action;
                try
                {
                    action = ItemDisposition.Classify(item);
                }
                catch (System.Exception ex)
                {
                    // Fail safe: an item we can't classify must NOT be sold (the sell mask is wide).
                    action = DispositionAction.Keep;
                    Logging.WriteDebug("[VibeGrinder] disposition classify failed for {0} ({1}) — protecting.",
                        item.Name, ex.Message);
                }
                if (action != DispositionAction.Vendor && !args.IdExceptions.Contains(item.Entry))
                {
                    args.IdExceptions.Add(item.Entry);
                    protectedCount++;
                    if (action == DispositionAction.Mail) willMail++;
                }
                Logging.WriteDebug("[VibeGrinder] disposition: {0} [{1}/{2}{3}] -> {4}",
                    item.Name, item.ItemInfo?.ItemClass, item.ItemInfo?.Quality,
                    item.IsSoulbound ? "/SB" : "", action);
            }
            if (protectedCount > 0)
                Logging.Write("[VibeGrinder] Vendor sweep: protecting {0} item(s) from sale ({1} queued to mail, rest kept).",
                    protectedCount, willMail);
        }

        /// <summary>
        /// Mail hook (Vendors.OnMailItems): queue every carried item whose disposition is Mail. The
        /// classifier has already excluded soulbound/epics, so everything here is mailable BoE value.
        /// </summary>
        private void OnMailSweep(MailItemsEventArgs args)
        {
            var me = StyxWoW.Me;
            if (me == null) return;

            int queued = 0;
            foreach (WoWItem item in me.CarriedItems)
            {
                if (item == null) continue;
                if (ItemDisposition.Classify(item) == DispositionAction.Mail && !args.AdditionalItems.Contains(item))
                {
                    args.AdditionalItems.Add(item);
                    queued++;
                }
            }
            if (queued > 0)
                Logging.Write("[VibeGrinder] Mail run: queuing {0} valuable item(s) for the bank.", queued);
        }

    }
}
