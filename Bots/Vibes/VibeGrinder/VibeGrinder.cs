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

        // Pull-failure events (fluid doctrine: the client TELLS us why a pull isn't working — don't burn the
        // 20s give-up clock inferring it). UI_ERROR_MESSAGE while pre-combat committed: "line of sight" /
        // "invalid target"-class errors count toward a FAST give-up (2 strikes → same blacklist+re-pick path
        // as the clock). English-client text match — this server/client is English. Handler fires from the
        // main Pulse event pump (same thread as the tree; no locking needed).
        private LuaEventHandlerDelegate _onUiError;
        private int _pullErrorCount;
        private string _lastPullError;
        private DateTime _pullErrorAt = DateTime.MinValue;
        private bool _spotInstalled;
        private ulong _peelGuid;        // committed transit-peel target (see TransitPeel) — don't re-pick per tick
        private ulong _committedGuid;   // committed grind-pull target (see ApplyPullCommitment)
        private readonly System.Diagnostics.Stopwatch _committedTimer = new System.Diagnostics.Stopwatch();
        private double _committedLastDist = double.MaxValue;   // last distance to committed mob (progress watchdog)
        private readonly System.Diagnostics.Stopwatch _selectRetry = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _safeRest = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _restMoveSw = new System.Diagnostics.Stopwatch();   // safe-spot walk cap (see SafeRestReposition)
        private readonly System.Diagnostics.Stopwatch _surfaceLogSw = new System.Diagnostics.Stopwatch();   // throttle incidental-hostile log
        private readonly System.Diagnostics.Stopwatch _eliteVetoLogSw = new System.Diagnostics.Stopwatch();   // throttle elite-veto log
        private bool _resting;                  // committed rest state (sticky: enter at Min*, exit at RestDonePct/cap)
        private WoWPoint _restSpot = WoWPoint.Empty;   // committed safe-rest destination (picked once per rest)
        private bool _restParked;               // positioning decision made for this rest — stop re-picking (see SafeRestReposition)
        private bool _drowning;                  // surfacing latch — log once per drowning episode (see SurfaceIfDrowning)
        private bool _vendorRun;                // committed vendor-errand latch (see UpdateVendorRun) — stable across the thrashing vendor POI
        private DateTime _vendorHoldUntil = DateTime.MinValue;   // hold the latch this long past the last vendor-POI/combat tick (rides out peel fights + brief POI gaps)
        private readonly System.Diagnostics.Stopwatch _vendorWatchdog = new System.Diagnostics.Stopwatch();   // abort a stuck errand (unreachable vendor / broke)
        private DateTime _engageUntil = DateTime.MinValue;   // ENGAGING activity latch held until here (see Engaging + CLAUDE.md "Stateful inter-spot movement")
        private uint _vendorCheckedEntry;       // last vendor entry that passed the enemy-territory safety check (re-check when it changes)
        private const int RestHysteresisPct = 12;  // resume at Min*+this (modest hysteresis), NOT a flat 85% top-off
        private const int RestMaxSeconds = 60;  // safety cap: if not recovered by now, give up resting and resume
                                                // (60 not 45: a no-drink mana caster recovers mana on natural regen
                                                //  only — ~2%->44% takes ~30s+; too tight a cap exits it still drained)
        private const int TrivialLevelGap = 5;  // a mob this many levels below us is ~grey/trivial — not worth a transit peel

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
                        // SURVIVAL: not drowning outranks everything below (grind/vendor/rest/combat). Surfaces
                        // if the breath bar is draining and nearly out; falls through the instant we can breathe.
                        new Action(ctx => SurfaceIfDrowning()),
                        // VENDOR MODE — committed errand. UpdateVendorRun() latches when a vendor need appears and
                        // HOLDS through the whole trip (incl. transient peel fights), so vendoring stops thrashing
                        // against grind/rest/roam — the bug where the bot "struggles with two decisions" (go for a
                        // drink AND run; re-deciding "repair" 60x/sec; the 3-min repair-deferral). Once latched this
                        // branch OWNS the tick: travel + transact, single-pulling only what's in the path, and the
                        // grind/rest/roam below are preempted. Strict by design — one errand at a time, see it through.
                        new Decorator(ctx => UpdateVendorRun(),
                            new PrioritySelector(
                                // CombatBehavior FIRST and unconditional — it drives the pre-combat PULL (CanPull->
                                // PullBehavior), so a peeled path threat actually gets engaged. (It was gated on
                                // Me.Combat, which left the bot targeting a mob and walking past it into a body-pull.)
                                // Its bundled rest is FLOORED here by RestGovernor.Suppressed (EmergencyMin* during the
                                // errand) — so no drink-then-run at moderate levels, but it still rests when critically
                                // low so a long hostile vendor trek isn't a death march, and the pull still runs.
                                LevelBot.CreateCombatBehavior(),
                                new Decorator(ctx => !StyxWoW.Me.Combat, new Action(ctx => TransitPeel())), // else set a Kill POI on the in-range path hostile — single-pull it, don't body-pull
                                LevelBot.CreateVendorBehavior(),                                            // else travel to + transact with the vendor
                                new Action(ctx => RunStatus.Success))),                                     // hold the tick while latched (don't fall through to grind in a brief idle gap)
                        // Safe-rest positioning: before Singular sits to eat/drink in the middle of a camp,
                        // back off to a clear spot. Returns Failure (→ falls through to normal combat/rest)
                        // when already clear, boxed in, in combat, or not resting — so it can't deadlock.
                        new Action(ctx => SafeRestReposition()),
                        LevelBot.CreateCombatBehavior(),
                        // Transit discipline (only while OnVendorRun): don't detour to loot corpses on the
                        // way to a vendor — stay moving, out of the danger corridor sooner (drops are
                        // grabbed later when grinding).
                        // Never loot while still in combat: a mob that FLED keeps us in combat, but CombatBehavior
                        // returns Failure for the tick it's out of cast range — without this gate a pending loot
                        // of a PREVIOUS kill's corpse slips through underneath and drags us off the runner before
                        // we finish it (the "looted the dead mob while the 2nd fled" bug). Finish the fight first.
                        new Decorator(ctx => !OnVendorRun() && !StyxWoW.Me.Combat, LevelBot.CreateLootBehavior()),
                        // ...and during a VENDOR RUN ONLY, deliberately single-pull the most isolated hostile
                        // in pull range instead of body-pulling blind into a pack. Vendor-only is load-bearing:
                        // on a vendor run Roam's find-target + ApplyPullCommitment are DISABLED (vendor POI is
                        // excluded from Roam, commitment is !OnVendorRun), so TransitPeel is the SOLE target
                        // driver. On a grind trek those are active and already commit to one mob — running
                        // TransitPeel there too put TWO drivers on one pull (TransitPeel→closest-in-LoS vs
                        // ShouldClearPoiForBetterTarget→committed FirstUnit) and thrashed the POI between two
                        // mobs every tick, pulling both. While grinding, foreign hostiles are instead just
                        // SURFACED (IncludeNearbyHostiles) and the single existing commitment pulls them.
                        new Decorator(ctx => OnVendorRun() && !StyxWoW.Me.Combat, new Action(ctx => TransitPeel())),
                        LevelBot.CreateVendorBehavior(),
                        // Rest commitment: while we've decided to rest and aren't topped off yet, OWN the tick so
                        // Relocate/Roam below cannot pull us off the rest spot to grind — the rest↔roam oscillation.
                        // The actual eat/drink happens above in CombatBehavior's not-in-combat RestBehavior.
                        new Action(ctx => RestRoamBlock()),
                        // SURVIVAL-CRITICAL flee (pack death / death loop) — ABOVE the ENGAGING gate on purpose:
                        // fleeing a camp that keeps killing us OUTRANKS any kill commitment. Regression this fixes:
                        // it used to live inside the !Engaging-gated RelocationCheck, so re-committing to a mob at
                        // the corpse on rez starved the abandon-relocate → the bot re-fought the swarm on loop.
                        _supervisor != null ? _supervisor.EmergencyRelocationCheck() : new Action(ctx => RunStatus.Failure),
                        // ENGAGING commitment — owns the wheel over travel so the DISCRETIONARY relocate and the
                        // kill-commit can't fight each other (see CLAUDE.md "Stateful inter-spot movement"):
                        //  (1) Don't relocate for a nearer/denser/contested spot while committed/fighting — finish
                        //      the kill, THEN re-evaluate. (The survival flee above is exempt.) Re-arms on disengage.
                        new Decorator(ctx => !Engaging,
                            _supervisor != null ? _supervisor.RelocationCheck() : new Action(ctx => RunStatus.Failure)),
                        //  (2) EngageHold: during a TRANSIENT no-target gap mid-engage (commit dropped this tick,
                        //      not yet in combat), OWN the tick so Roam's hotspot-move can't bolt toward the far
                        //      new spot — the travel↔kill ping-pong. FirstUnit present → fall through so Roam
                        //      approaches it normally (route-killing untouched). Not engaging → fall through to travel.
                        new Decorator(ctx => Engaging && Targeting.Instance.FirstUnit == null && !StyxWoW.Me.Combat,
                            new Action(ctx => RunStatus.Success)),
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
            _restParked = false;
            _vendorRun = false;
            _vendorHoldUntil = DateTime.MinValue;
            _vendorWatchdog.Reset();
            _vendorCheckedEntry = 0;
            _engageUntil = DateTime.MinValue;
            _safeRest.Reset();
            _selectRetry.Reset();

            _factions = new FactionResolver();
            _mailboxes = new MailboxService();
            _synth = new GrindAreaSynthesizer(_mailboxes);
            _selector = new SpotSelector(_factions);
            _supervisor = new GrindSupervisor(_selector, _synth, _factions);
            // Hard dead-man's switch: when the supervisor force-escapes a stall, drop every commitment latch
            // so nothing re-grabs the trap we're fleeing (pull/peel target, rest, vendor errand).
            _supervisor.OnForceEscape = () =>
            {
                _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue;
                _peelGuid = 0;
                _resting = false;
                _vendorRun = false;
            };
            _restGovernor = new RestGovernor();   // dynamic rest thresholds; SafeRest reads these
            _restGovernor.SuppressedFloorHealth = VibeGrinderSettings.Instance.EmergencyMinHealth;   // vendor-run survival floor
            _restGovernor.SuppressedFloorMana = VibeGrinderSettings.Instance.EmergencyMinMana;

            // Reuse LevelBot's target/loot filters (faction + blackspot + loot rules).
            Targeting.Instance.IncludeTargetsFilter += LevelBot.LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter += LevelBot.LevelbotIncludeLootsFilter;
            // Pull discipline: bias FirstUnit toward isolated mobs so we don't open on a pack.
            Targeting.Instance.WeighTargetsFilter += WeighTargetsAvoidPacks;
            // Surface incidental hostiles (attackers + nearby level-safe hostiles) so the bot SEES the mobs
            // off its grind list — otherwise it body-pulls them blind (see IncludeNearbyHostiles).
            Targeting.Instance.IncludeTargetsFilter += IncludeNearbyHostiles;

            _onKill = args => _supervisor.RecordKill();
            BotEvents.Player.OnMobKilled += _onKill;

            _onUiError = (sender, e) =>
            {
                if (_committedGuid == 0 || StyxWoW.Me == null || StyxWoW.Me.Combat) return;   // only pre-combat pull attempts
                string msg = e.Args.Length > 0 ? e.Args[0] as string : null;
                if (string.IsNullOrEmpty(msg)) return;
                // Errors that mean THIS pull can't work from here (out-of-range is excluded — movement fixes that).
                if (msg.IndexOf("line of sight", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("Invalid target", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("can't attack", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _pullErrorCount++;
                    _lastPullError = msg;
                    _pullErrorAt = DateTime.Now;
                }
            };
            Lua.Events.AttachEvent("UI_ERROR_MESSAGE", _onUiError);

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

            // 0. NEVER pre-combat commit/peel an ELITE. An unattended squishy can't win that fight — a roaming
            //    rare-elite patrol (Marcus Bel & co., Taurajo→1k Needles) was body-pulled and killed us twice.
            //    This is the SINGLE chokepoint: every pull path reads the post-weigh FirstUnit (grind commitment,
            //    TransitPeel on a vendor run, Roam approach), so dropping elites here stops us INITIATING from any
            //    surfacing path (LevelBot's faction filter AND IncludeNearbyHostiles). In-combat defense is
            //    unchanged — this hook early-returns above when Me.Combat, so if one's already on us the routine
            //    still fights back; we just never CHOOSE the fight. Spot selection already only grinds rank=0, so
            //    no intended target is removed. Backstop for the residual (pack roams onto us) is the existing
            //    death-cluster escalation. See CLAUDE.md "Roaming elite/rare patrols".
            VetoElites(units);

            // 1. Quality weighting decides which mob is the *cleanest* to open on (runs in transit too —
            //    TransitPeel relies on the resulting isolation ordering).
            ApplyCrowdAndNeutralWeighting(units, me);

            // 2. Commitment pins our chosen pull so we see it through instead of re-deciding every tick.
            //    TransitPeel (vendor-run pull) commits to an in-range mob (_peelGuid); pin THAT as FirstUnit so
            //    LevelBot's "POI is not the best pull target" can't override it with a far, UNPULLABLE FirstUnit
            //    and deadlock the Kill vs Repair POIs against each other (the doorstep thrash). Gate on the LIVE,
            //    in-range peel (a stale/out-of-range peel falls through to the normal grind commitment). OnVendorRun()
            //    is now a stable latch, but the peel pin still keys off the live _peelGuid — it's the precise signal.
            WoWUnit peel = _peelGuid != 0 ? FindCandidate(units, _peelGuid) : null;
            if (peel != null && !peel.Dead && peel.Distance <= Targeting.PullDistance * 1.5)
                PinGuid(units, _peelGuid);
            else if (!OnVendorRun())
                ApplyPullCommitment(units, me);
        }

        /// <summary>
        /// True for an elite / rare-elite / world-boss — a mob this bot must never proactively pull. CreatureRank
        /// is the authoritative DB rank (same creature_template.rank GrindMobs.db carries); .Elite (the PlusMob
        /// unit flag) is an OR'd backup in case the creature cache isn't populated yet. Plain Rare (non-elite,
        /// rank 4) is intentionally NOT included — those are usually soloable / RareKiller's job.
        /// </summary>
        private static bool IsUnkillableElite(WoWUnit u)
        {
            if (u == null) return false;
            WoWUnitClassificationType r = u.CreatureRank;
            return u.Elite
                || r == WoWUnitClassificationType.Elite
                || r == WoWUnitClassificationType.RareElite
                || r == WoWUnitClassificationType.WorldBoss;
        }

        // Drop every elite from the pre-combat candidate list so nothing downstream can commit/peel one.
        private void VetoElites(System.Collections.Generic.List<Targeting.TargetPriority> units)
        {
            int vetoed = 0;
            string sample = null;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                WoWUnit u = units[i].Object?.ToUnit();
                if (u == null || !IsUnkillableElite(u)) continue;
                if (sample == null) sample = u.Name;
                units.RemoveAt(i);
                vetoed++;
            }
            if (vetoed > 0 && (!_eliteVetoLogSw.IsRunning || _eliteVetoLogSw.Elapsed.TotalSeconds >= 3))
            {
                Logging.WriteDebug("[VibeGrinder/Hostiles] vetoed {0} elite(s) from pull candidates (e.g. {1}) — won't engage.",
                    vetoed, sample);
                _eliteVetoLogSw.Restart();
            }
        }

        // Pin one mob to the top of the score so it stays FirstUnit (PullCommitBoost). No-op if guid==0 or absent.
        private static void PinGuid(System.Collections.Generic.List<Targeting.TargetPriority> units, ulong guid)
        {
            if (guid == 0) return;
            for (int i = 0; i < units.Count; i++)
                if (units[i].Object != null && units[i].Object.Guid == guid)
                {
                    units[i].Score += VibeGrinderSettings.Instance.PullCommitBoost;
                    break;
                }
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
                if (h != null && h is not WoWPlayer && !h.Dead && !h.IsTotem && h.MyReaction <= WoWUnitReaction.Hostile)
                    hostiles.Add(h);   // totems aren't pack adds — don't let them inflate crowd/veto counts

            float crowdR2 = s.PullCrowdRadius * s.PullCrowdRadius;
            float neutR2 = s.NeutralHostileAvoidRadius * s.NeutralHostileAvoidRadius;
            // Iterate BACKWARD so the hard-veto can RemoveAt(i) safely.
            for (int i = units.Count - 1; i >= 0; i--)
            {
                WoWUnit a = units[i].Object.ToUnit();
                if (a == null) continue;
                // Enemy totem (own ones never surface): last-resort only. Bury it below the floor so any real mob
                // outranks it, but leave it in the list so the acquire fallback can still pick it when nothing
                // else is up — e.g. a snare/root totem holding us in place after the caster ran. (Worth destroying
                // to break free, never worth chasing over a real kill; the routine should hit it cheap, not nuke it.)
                if (a.IsTotem) { units[i].Score -= s.NeutralNearHostileVeto * 2f; continue; }
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
        /// True the instant we OPEN on the committed mob — the real "we've engaged, stop selecting" edge,
        /// which is NOT Me.Combat. The combat flag only flips when the opener LANDS: for a caster that's
        /// cast-time + projectile travel (1-3s) AFTER the bolt is away and the mob is already aggroed. The
        /// whole pre-pull selection layer (preempt, give-up) must stand down at the opener, not at that lagged
        /// flag, or it keeps "selecting" after we've physically pulled — the mid-cast target switch that
        /// opened a SECOND mob (2-mob pull → pack death), and the give-up clock ticking on a mob we're casting
        /// at. Signals, earliest first: mid-cast on it (caster opener in flight, the exact gap Me.Combat
        /// misses); it's now targeting us (instant-cast / landed backstop); Me.Combat (melee / post-land).
        /// A committed NEUTRAL we haven't provoked yet reads false on all three (no cast, not aggroed) — so
        /// the neutral-preempt below still fires during the walk-onto phase, as intended.
        /// </summary>
        private bool HasOpenedOnCommit(WoWUnit me)
        {
            if (_committedGuid == 0) return false;
            if (me.CurrentTargetGuid == _committedGuid && (me.IsCasting || me.IsChanneling)) return true;
            if (me.Combat) return true;
            WoWUnit cu = ObjectManager.GetObjectByGuid<WoWUnit>(_committedGuid);
            return cu != null && !cu.Dead && cu.IsTargetingMeOrPet;
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

            // Don't tread water after a target. If we've waded in swimming, just DROP the pin (no blacklist —
            // a stream crossing toward a fine land mob shouldn't poison it) and don't re-commit while swimming,
            // so Roam pulls us back to the on-land hotspot. A genuinely unreachable mob is still caught by the
            // normal no-progress give-up below once we're back on land.
            if (me.IsSwimming)
            {
                if (_committedGuid != 0)
                {
                    Logging.WriteDebug("[VibeGrinder/Commit] swimming — dropping pin on {0:X} (won't tread water after it).",
                        _committedGuid);
                    _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue;
                    // Definitive "the mobs here need swimming" signal — feeds the water-trap relocate (a kill
                    // clears it, so a workable shoreline camp is never abandoned). One count per commit→swim cycle.
                    _supervisor?.RecordSwimBlocked();
                }
                return;
            }

            // The engagement boundary for the whole pre-pull selection layer below. Gating on `opened` (the
            // opener edge) rather than Me.Combat is what stops the layer from re-selecting during the 1-3s the
            // combat flag lags a caster's opener — the window that let preempt switch away from a mob we'd
            // already bolted and pull a second one. See HasOpenedOnCommit.
            bool opened = HasOpenedOnCommit(me);

            // Path defense (never walk a far commitment past a mob that's about to body-pull us). While
            // approaching and BEFORE we've opened, if a level-safe, non-camp hostile is inside its OWN aggro
            // bubble + buffer AND nearer than what we're committed to, drop the pin (no blacklist — the far mob
            // is fine, just deferred) so Acquire below re-picks the nearest = the threat. We engage it
            // deliberately instead of riding into it blind (the "body-pulled as if he didn't see them" bug).
            // Camped hostiles were already removed from `units` by ApplyCrowdAndNeutralWeighting, so this never
            // opens on a pack. Narrow (only mobs in their aggro bubble) so it doesn't reopen the commitment
            // wobble it prevents. Gated on `!opened`: once the opener is away the pull is irrevocable — the mob
            // is aggroed, so switching would ADD it as a second mob, not replace it (the 2-mob pull death).
            if (_committedGuid != 0 && !opened)
            {
                WoWUnit held = FindCandidate(units, _committedGuid);
                double heldDist = held != null && !held.Dead ? held.Location.Distance(me.Location) : double.MaxValue;
                // A committed NEUTRAL won't aggro us, but OPENING on it (we have to provoke it) drags in any
                // hostile nearby — and we're usually standing ON the neutral by then, so nothing is "closer" and
                // the threatDist<heldDist guard below would suppress the preempt (the giraffe + thunder-lizard
                // double-pull). So for a neutral commit: use a WIDER trigger and defer it for ANY near hostile.
                bool heldNeutral = held != null && held.MyReaction > WoWUnitReaction.Hostile;
                WoWUnit threat = null;
                double threatDist = double.MaxValue;
                foreach (var c in units)
                {
                    WoWUnit cu = c.Object?.ToUnit();
                    if (cu == null || cu.Dead || cu.Guid == _committedGuid) continue;
                    if (cu.MyReaction > WoWUnitReaction.Hostile) continue;   // a neutral won't come to us — no path threat
                    double d = cu.Distance;
                    double trigger = cu.MyAggroRange + s.PreemptAggroBuffer;
                    if (heldNeutral) trigger = System.Math.Max(trigger, s.NeutralOpenAvoidRadius);
                    if (d <= trigger && d < threatDist) { threatDist = d; threat = cu; }
                }
                if (threat != null && (heldNeutral || threatDist < heldDist) && threat.Guid != _committedGuid)
                {
                    Logging.Write(System.Drawing.Color.Khaki,
                        "[VibeGrinder/Commit] PREEMPT to {0} (d={1:F1}, aggro={2:F0}{3}) — path hostile about to pull; deferring {4:X}",
                        threat.Name, threatDist, threat.MyAggroRange, heldNeutral ? ", neutral commit" : "", _committedGuid);
                    // Commit STRAIGHT to the threat — dropping to 0 and letting Acquire re-pick would re-grab the
                    // NEARER neutral (it's closer than the threat), re-fire the preempt, and oscillate 5x/sec.
                    _committedGuid = threat.Guid; _committedTimer.Restart(); _committedLastDist = double.MaxValue;
                }
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
                    // Progress = closing the gap OR actually ENGAGING (opened) — reset on those, NOT on bare
                    // proximity. An in-range mob we can't actually pull (a LoS/facing dead-end the InLineOfSpellSight
                    // flag misses, or an evade spot) used to reset the clock every tick via `d <= MaxPullDistance`,
                    // so it stood there forever casting into nothing (seen: ~6 min on one Hillsbrad Foreman —
                    // los=True, canPull=True, yet no damage and combat=False). Gating on `opened` instead: a real pull
                    // connects in ~3s (<< the 20s cap) so it's never blacklisted, and `opened` keeps the clock reset
                    // through a genuine fight (mob in range, no distance progress) so we don't drop the mob we're
                    // killing — and it resets from the opener's FIRST cast, not the lagged combat flag, so the clock
                    // can't blacklist a mob we're mid-cast on. A stuck pull that never opens expires → blacklist 2m.
                    if (opened || d < _committedLastDist - 0.5)
                        _committedTimer.Restart();
                    _committedLastDist = d;

                    // FAST give-up on client-reported pull failures (UI_ERROR_MESSAGE handler, see Start): two
                    // LoS/invalid-target errors within 6s mean this pull cannot work from here — resolve NOW
                    // with the reason verbatim instead of letting the 20s clock discover it (fluid doctrine).
                    if (!opened && _pullErrorCount >= 2 && (DateTime.Now - _pullErrorAt).TotalSeconds < 6)
                    {
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] GIVE UP FAST on {0} — client reported '{1}' ×{2} — blacklisting 2m.",
                            held.Name, _lastPullError, _pullErrorCount);
                        Blacklist.Add(_committedGuid, System.TimeSpan.FromMinutes(2));
                        _pullErrorCount = 0;
                        valid = false;
                    }

                    if (valid && _committedTimer.Elapsed.TotalSeconds > s.PullCommitMaxSeconds)
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

            // Acquire: commit to the NEAREST acceptable candidate, not the highest-scored one. Highest score =
            // most isolated, which biases FAR — and walking across the area to a far straggler drags us through
            // packs (the Sunscale/Kolkar pull that killed us) and blind-body-pulls things en route (the raptor
            // we "didn't see"). The crowd weighting already REMOVED hard packs and BURIED neutrals-near-hostiles
            // from the list, so nearest-of-what's-left is a close, single-pullable mob. Fall back to top score
            // only if everything is buried (so we still act rather than idle).
            // Rest BEFORE pulling: don't START a new pull while resting OR while we need to. Both are required:
            // RestNeeded catches the entry tick (before _resting latches), and _resting catches the RECOVERY band
            // — the rest latch is sticky with hysteresis (enters at MinHealth, exits at a higher done-band), so
            // HP can climb back above MinHealth (RestNeeded goes false) while we're STILL resting; committing
            // there is the commit-then-rest-decay again. An in-progress pull is unaffected (_committedGuid!=0
            // skips this whole block).
            if (_committedGuid == 0 && !_resting && !RestNeeded(me))
            {
                float buryFloor = -s.NeutralNearHostileVeto * 0.5f;   // below this = a buried neutral; skip it
                // Fluid doctrine (lookahead): validate the pick is PATHABLE before committing. Without this an
                // unreachable mob (ledge/roof/mesh hole) was only discovered by walking at it for
                // PullCommitMaxSeconds of no progress — the DOMINANT failure mode (the give-up diag counted
                // 87/run). One GeneratePath per acquire (~ms) turns that 20s inference into an instant fact:
                // reject → short blacklist (it may wander somewhere reachable) → try the next-nearest THIS
                // tick. Bounded at 3 pathfinds/tick; a mob already ON us is never rejected (it reached us —
                // rejecting it would leash us off a live attacker). The 20s give-up clock stays as backstop.
                var rejected = new System.Collections.Generic.HashSet<ulong>();
                Targeting.TargetPriority pick = null;
                for (int attempt = 0; attempt < 3 && pick == null; attempt++)
                {
                    double nearest = double.MaxValue;
                    for (int i = 0; i < units.Count; i++)
                    {
                        var c = units[i];
                        if (c.Object == null || c.Score < buryFloor || rejected.Contains(c.Object.Guid)) continue;
                        WoWUnit cu = c.Object.ToUnit();
                        if (cu == null || cu.Dead) continue;   // a just-killed corpse lingers a frame in the list — don't re-commit to it
                        double d = c.Object.Distance;
                        if (d < nearest) { nearest = d; pick = c; }
                    }
                    if (pick == null) break;

                    WoWUnit pu = pick.Object.ToUnit();
                    if (pu != null && !pu.IsTargetingMeOrPet)
                    {
                        WoWPoint[] path = Navigator.GeneratePath(me.Location, pu.Location);
                        if (path == null || path.Length == 0)
                        {
                            Logging.Write(System.Drawing.Color.Khaki,
                                "[VibeGrinder/Commit] REJECT {0} (d={1:F1}) — no path to it; trying next-nearest.",
                                pu.Name, pu.Distance);
                            Blacklist.Add(pu.Guid, System.TimeSpan.FromSeconds(45));
                            rejected.Add(pick.Object.Guid);
                            pick = null;
                        }
                    }
                }
                if (pick == null)
                    for (int i = 0; i < units.Count; i++)
                    {
                        WoWUnit cu = units[i].Object?.ToUnit();
                        if (cu == null || cu.Dead || rejected.Contains(cu.Guid)) continue;
                        if (pick == null || units[i].Score > pick.Score) pick = units[i];
                    }

                if (pick != null)
                {
                    _committedGuid = pick.Object.Guid;
                    _committedTimer.Restart();
                    _committedLastDist = double.MaxValue;
                    _pullErrorCount = 0;   // fresh commit → fresh error strikes
                    WoWUnit bu = pick.Object.ToUnit();
                    if (bu != null)
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] ACQUIRE {0} (reaction={1}, d={2:F1}, score={3:F0}) " +
                            "nearestHostile={4} candidates={5}",
                            bu.Name, bu.MyReaction, bu.Distance, pick.Score, NearestHostileDesc(me), units.Count);
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
        /// Drowning safety net (survival-critical Root branch, above grind/vendor/rest/combat). When the
        /// breath bar is draining and under BreathPanicSeconds remain, swim straight up (JumpAscend) and OWN
        /// the tick until we can breathe. "Draining" (ChangePerMillisecond &lt; 0) is the trigger and its own
        /// hysteresis: at the surface it flips to refilling / clears, so we fall through (Failure) immediately.
        /// Should rarely fire — VibeGrinder already avoids water (drops the pin while swimming, never rests
        /// swimming) — but it's the only guard against an actual drown if we get held under.
        /// </summary>
        private RunStatus SurfaceIfDrowning()
        {
            var me = StyxWoW.Me;
            bool safe = me == null || !me.IsSwimming;
            MirrorTimerInfo breath = default;
            if (!safe)
            {
                breath = me.GetMirrorTimerInfo(MirrorTimerType.Breath);
                safe = !breath.IsVisible || breath.ChangePerMillisecond >= 0
                       || breath.CurrentTime > VibeGrinderSettings.Instance.BreathPanicSeconds * 1000L;
            }
            if (safe) { _drowning = false; return RunStatus.Failure; }

            if (!_drowning)
            {
                _drowning = true;
                Logging.Write(System.Drawing.Color.OrangeRed,
                    "[VibeGrinder] Drowning — {0:F0}s of breath left, surfacing.", breath.CurrentTime / 1000f);
            }
            // Mirror Flightor's ascend-to-surface: a brief JumpAscend burst, re-fired each tick until we breathe.
            WoWMovement.Move(WoWMovement.MovementDirection.JumpAscend);
            StyxWoW.Sleep(100);
            WoWMovement.MoveStop();
            return RunStatus.Running; // own the tick — nothing below drags us back down
        }

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

            // One positioning decision per rest. Bug-B secondary fix: arrival used to clear _restSpot, so the
            // NEXT tick re-ran NearestHostileWithin and — in a dense camp where a different mob is always within
            // range — picked a NEW spot and walked off again, forever. That perpetual relocation cancelled the
            // eat/drink aura every few seconds (so Singular's rest never took hold → mana never recovered → 45s
            // cap loop) and ping-ponged against LootBehavior. Park once and stay put; combat preempts rest if a
            // mob wanders in, which re-arms positioning on the next rest cycle.
            if (_restParked) return RunStatus.Failure;

            // Pick a committed safe spot ONCE, only if a hostile is too close to rest where we stand.
            if (_restSpot == WoWPoint.Empty)
            {
                WoWUnit nearest = NearestHostileWithin(me, s.SafeRestDangerRange);
                if (nearest == null) return RunStatus.Failure;                  // already clear → rest in place (may back off once if one approaches)
                if (!TryPickSafeSpot(me, s.SafeRestDangerRange, nearest, out _restSpot))
                {
                    _restSpot = WoWPoint.Empty;
                    _restParked = true;                                         // boxed in → commit to resting here (don't re-path every tick)
                    return RunStatus.Failure;
                }
                Logging.Write(System.Drawing.Color.Khaki,
                    "[VibeGrinder/Rest] safe-spot pick: backing off {0:F0}yd from {1} (d={2:F0})",
                    me.Location.Distance(_restSpot), nearest.Name, nearest.Distance);
                _restMoveSw.Restart();
            }

            // Drive to the committed spot (exclusive — owns movement so nothing below interleaves).
            // Arrival is 2D: the spot is a navmesh path endpoint, but nav z vs live z can still disagree by a
            // couple of yards on slopes — a 3D check then never fires and we shove MoveTo forever (the
            // stuck-jig-on-open-ground bug). The 8s cap is the documented backstop: whatever goes wrong with
            // the walk, we stop repositioning and rest where we stand rather than fight the stuck handler.
            if (_restMoveSw.Elapsed.TotalSeconds > 8)
            {
                if (me.IsMoving) WoWMovement.MoveStop();
                _restSpot = WoWPoint.Empty;
                _restParked = true;
                Logging.Write(System.Drawing.Color.Khaki,
                    "[VibeGrinder/Rest] safe-spot walk capped at 8s — resting here instead.");
                return RunStatus.Failure;
            }
            if (me.Location.Distance2D(_restSpot) <= 3f)
            {
                if (me.IsMoving) WoWMovement.MoveStop();
                _restSpot = WoWPoint.Empty;
                _restParked = true;                                            // arrived → rest here, stop re-picking for this rest
                Logging.WriteDebug("[VibeGrinder/Rest] reached safe spot — resting in place");
                return RunStatus.Failure;
            }
            TreeRoot.StatusText = "VibeGrinder: moving to a safe spot to rest";
            Navigator.MoveTo(_restSpot);
            return RunStatus.Success;
        }

        /// <summary>
        /// The ONE "do we need to recover?" predicate — HP/mana below the live RestGovernor band (mana counts
        /// for any mana caster, drink or not — see UpdateRestingState). Both the rest gate (UpdateRestingState)
        /// AND the pull commitment (ApplyPullCommitment) read this, so "should I rest?" is decided BEFORE a pull
        /// is started and the two can't disagree — no commit-then-rest, no resting mid-flight-pull (that case is
        /// held off separately by the already-engaged check, since once committed we see the pull through).
        /// </summary>
        private bool RestNeeded(WoWUnit me)
        {
            if (me == null) return false;
            int minHp = _restGovernor?.MinHealth ?? 55;
            int minMana = _restGovernor?.MinMana ?? 45;
            bool manaGoverns = me.PowerType == WoWPowerType.Mana && minMana > 0;
            return me.HealthPercent <= minHp || (manaGoverns && me.ManaPercent <= minMana);
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
            // Mana governs rest for ANY mana caster, drink or not. (Earlier this required water in bags, resuming
            // on HP alone with no drink — but that sent a caster back into combat at ~0 mana: a lvl-23 Elemental
            // shaman whose every ability costs mana then just white-swings, drops to ~half HP, and re-rests, a
            // self-defeating loop. The "routine has instants/melee" assumption is false pre-30.) With no drink we
            // recover mana on natural out-of-combat regen instead — a deliberate, longer rest, bounded by the cap.
            // Safe because GoodVibes' rest does NOT sit-gate the pull on mana (it only heals HP when low, then
            // falls through), so nothing freezes us beyond RestMaxSeconds; combat preempts rest if a mob wanders
            // in. When DrinkAmount>0 the engine restocks water → the routine drinks → same path, fast recovery.
            bool manaGoverns = me.PowerType == WoWPowerType.Mana && minMana > 0;

            if (!_resting)
            {
                // Don't sit to rest while we're committed to ANOTHER activity — the tree re-evaluates every
                // pulse, so without this the bot "struggles with two decisions" and does both on one tick
                // (drink AND mount-and-run). Two committed activities pre-empt rest:
                //  - Kill POI = actively pulling/engaging. The pull cast drops mana below the band a beat
                //    BEFORE Me.Combat flips (~1s pull->combat-flag lag), so we'd sit to drink as the mob we
                //    just aggroed closes in, then stand straight back up (wasted food/drink, bot-like).
                //  - Vendor run (repair/sell/mail/buy/train/fly) doesn't need mana to TRAVEL, so resting just
                //    fights the errand — the mount+drink+"Moving to Repair" thrash. Commit to the errand and
                //    rest naturally afterwards, when grinding resumes and the POI clears.
                if (BotPoi.Current.Type == PoiType.Kill || OnVendorRun())
                    return;

                //  - Pending LOOT: corpses hit the loot list a beat AFTER combat ends, so the rest latch used
                //    to fire first, SafeRest walked 38yd AWAY from the corpses, the loot POI then dragged us
                //    straight back — a 76yd round-trip for nothing with Mana Spring churn in the middle (log
                //    2026-07-02_1346). Loot first (seconds, and corpses expire), THEN rest where the loot ends.
                if (LootTargeting.Instance.FirstObject != null || BotPoi.Current.Type == PoiType.Loot)
                    return;

                bool need = RestNeeded(me);
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

            // While actively eating/drinking, RIDE the consumable to ~full before standing up — a food/drink is
            // one item per use, so bailing at the done-band wastes the rest of its channel AND the item (you
            // drank a whole Melon Juice for +22% and threw the rest away). Gated on an actual Food/Drink aura, so
            // the no-water case is untouched (no aura → normal done-band, no freeze). This also matches the
            // routine's own stay-seated band so VibeGrinder and the routine don't fight over standing up.
            bool consuming = (me.HasAura("Drink") && me.ManaPercent < 95)
                             || (me.HasAura("Food") && me.HealthPercent < 95);

            // Resume at a modest hysteresis above the enter band — NOT the old flat 85% top-off (RestDonePct),
            // which froze a no-water caster for the full cap every rest (mana crawled 67->85 on regen while
            // Roam was blocked — the 11:13 stall). This low band is what makes the no-drink mana wait above
            // affordable: ~44% (not 85%) is "enough to fight," reached by natural regen well inside the cap.
            // Caps below keep a high-caution band from chasing near-full.
            int doneHp = System.Math.Min(95, minHp + RestHysteresisPct);
            int doneMana = System.Math.Min(90, minMana + RestHysteresisPct);
            bool recovered = !consuming && me.HealthPercent >= doneHp && (!manaGoverns || me.ManaPercent >= doneMana);
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

        /// <summary>
        /// ENGAGING — the activity commitment that stops the travel↔kill oscillation (see CLAUDE.md "Stateful
        /// inter-spot movement"). True while in combat, while a pull is committed, or within the EngageGrace
        /// hysteresis after either (refreshed in Pulse). The tree gates read this to (1) disarm relocation and
        /// (2) hold position during a transient no-target gap instead of bolting toward the far new spot.
        /// TRAVELING is simply the absence of this. Pure read — never mutate state here.
        /// </summary>
        private bool Engaging => StyxWoW.Me != null
            && (StyxWoW.Me.Combat || _committedGuid != 0 || DateTime.Now < _engageUntil);

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
            // Commit to the PATH'S endpoint, not the raw vector point: `away` carries OUR Z, and on a slope the
            // real ground 42yd out sits several yards higher/lower — the 3D arrival check then never fires and
            // Navigator.MoveTo re-issues into a stationary position until the stuck handler jigs on OPEN ground
            // (log 2026-07-02_1209, 9 stuck events on a Arathi hillside). The path end is on-mesh, correct-Z,
            // and reachable by construction. An endpoint that barely leaves our position = effectively boxed in.
            if (path != null && path.Length > 0)
            {
                WoWPoint reachable = path[path.Length - 1];
                if (me.Location.Distance2D(reachable) > 5f) { spot = reachable; return true; }
            }
            spot = WoWPoint.Empty;
            return false;
        }

        // Stable "we're on a vendor errand" state — the COMMITTED latch, not the live POI. Every vendor gate
        // (loot suppression, TransitPeel, grind-pull disable, rest suppression) reads this so they don't flicker
        // with the per-tick vendor POI. Set/held/cleared by UpdateVendorRun.
        private bool OnVendorRun() => _vendorRun;

        // Is the LIVE POI a vendor-errand type? (The raw, thrashing signal UpdateVendorRun turns into a latch.)
        private static bool PoiIsVendorType()
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
        /// Vendor-run commitment latch (the "do this errand, THEN go back to grinding" fix). The vendor POI is
        /// re-asserted/stolen every tick (combat, rest, roam all write the one POI slot), so deriving vendor-run
        /// state straight from it makes every vendor gate flicker — that's why the bot tried to drink AND run,
        /// and re-decided "repair" 60x/sec for 3 minutes before actually going. So: latch ON the first tick a
        /// vendor POI appears, HOLD through transient Kill POIs (peel fights) and brief POI gaps, and clear only
        /// when the errand truly finishes (no vendor POI / no combat for the hold window) or aborts (watchdog).
        /// Returns whether vendor mode is active — also used as the Root vendor-branch gate, so it ticks every frame.
        /// </summary>
        private bool UpdateVendorRun()
        {
            var me = StyxWoW.Me;
            if (me == null) return _vendorRun;

            bool poiIsVendor = PoiIsVendorType();
            var s = VibeGrinderSettings.Instance;

            // Safety-screen EVERY distinct vendor the resolver lands on — the initial pick AND any mid-errand
            // swap (GetClosestVendor re-runs as we move and drops an unreachable one, e.g. Razbo -> Zulrg). Without
            // re-checking on change, a switched-to vendor in enemy territory slipped through. VendorLocationSafe
            // blacklists an unsafe vendor + clears its POI; we then stop treating it as a vendor POI this tick so
            // the resolver re-picks. Entry 0 (mailbox/flight) has its own faction-safety upstream — skip it here.
            if (poiIsVendor && BotPoi.Current.Entry != 0 && (uint)BotPoi.Current.Entry != _vendorCheckedEntry)
            {
                if (VendorLocationSafe())
                    _vendorCheckedEntry = (uint)BotPoi.Current.Entry;
                else
                    poiIsVendor = false;   // unsafe → POI cleared inside VendorLocationSafe; resolver re-picks next tick
            }

            // Refresh the hold window while actively on the errand: a vendor POI is up, OR we're mid peel-fight
            // (combat transiently flips the POI to Kill — don't let that look like "errand done").
            if (poiIsVendor || (_vendorRun && me.Combat))
                _vendorHoldUntil = DateTime.Now.AddSeconds(s.VendorRunStickySeconds);

            if (!_vendorRun)
            {
                if (poiIsVendor)
                {
                    _vendorRun = true;
                    _vendorWatchdog.Restart();
                    Logging.Write(System.Drawing.Color.Khaki,
                        "[VibeGrinder/Vendor] COMMIT — {0} run; grind/rest/roam suspended until done.", BotPoi.Current.Type);
                    // Trek safety for the errand leg: bend the route around red/pack aggro bubbles.
                    Supervision.TrekSafety.MarkLeg(_factions, me.Location, BotPoi.Current.Location, me.Level, me.MapId, "vendor leg");
                }
                return _vendorRun;
            }

            // Committed. Hard backstop: an errand that can't complete (vendor unreachable / blacklisted / can't
            // afford repair) must not wedge us in vendor mode forever — abort and resume grinding.
            if (_vendorWatchdog.Elapsed.TotalSeconds > s.VendorRunAbortSeconds)
            {
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder/Vendor] ABORT — errand didn't complete in {0}s; resuming grind.", s.VendorRunAbortSeconds);
                EndVendorRun();
                return false;
            }

            // Done: the hold window lapsed — no vendor POI and no combat for VendorRunStickySeconds, i.e. the
            // transaction finished and nothing re-triggered it. Resume grinding (and rest then, if still low).
            if (DateTime.Now > _vendorHoldUntil)
            {
                Logging.Write(System.Drawing.Color.Khaki, "[VibeGrinder/Vendor] DONE — errand complete; resuming grind.");
                EndVendorRun();
                return false;
            }
            return true;
        }

        private void EndVendorRun()
        {
            _vendorRun = false;
            _vendorHoldUntil = DateTime.MinValue;
            _vendorWatchdog.Reset();
            _vendorCheckedEntry = 0;
            // Trek safety for the RETURN leg: re-mark hazards from here back to the grind spot (the vendor-leg
            // marks are for a route we're no longer on). Falls back to a plain clear when no spot is installed.
            var me = StyxWoW.Me;
            var area = StyxWoW.AreaManager?.CurrentGrindArea;
            if (me != null && area?.CurrentHotSpot != null)
                Supervision.TrekSafety.MarkLeg(_factions, me.Location, area.CurrentHotSpot.Position, me.Level, me.MapId, "return leg");
            else
                Supervision.TrekSafety.Clear();
        }

        /// <summary>
        /// Is the resolved vendor somewhere we can actually, safely shop? Vendor resolution is distance- +
        /// faction-reaction-based over the whole CONTINENT, so it sends us into trouble two ways — both visible
        /// at selection time (no wasted trek) from GrindMobs.db (every creature's faction + level + spawn pos):
        ///   (1) ENEMY TERRITORY — a knot of player-hostile spawns around the vendor (the DB flags sell/repair
        ///       NPCs inside opposing-faction camps, e.g. Prospector Khazgorm at the Alliance dig site Bael Modan).
        ///   (2) HIGHER-LEVEL ZONE — surrounding wild mobs average well above our level (the "nearest" vendor is
        ///       one zone over: a lvl-21 Barrens toon routed into Dustwallow Marsh, dead at the border).
        /// Either ⇒ blacklist the vendor (same list the "could not find vendor" path uses, honored by every later
        /// GetClosestVendor) + clear the POI, so the resolver re-picks the next-nearest. Distance is NOT a gate —
        /// a FAR same-level/safe vendor is fine; nearest-first among the OK ones keeps us in/near the current zone.
        /// </summary>
        private bool VendorLocationSafe()
        {
            var s = VibeGrinderSettings.Instance;
            var me = StyxWoW.Me;
            if (_factions == null || me == null) return true;   // no faction data yet → fail-safe (don't block)
            uint map = me.MapId;
            WoWPoint loc = BotPoi.Current.Location;

            if (s.VendorHostileThreshold > 0)
            {
                int hostiles = GrindMobsRepository.HostileSpawnCountNear(map, loc, s.VendorHostileRadius, _factions);
                if (hostiles >= s.VendorHostileThreshold)
                    return RejectVendor("{0} hostile spawns within {1:F0}yd (enemy territory)", hostiles, s.VendorHostileRadius);
            }

            if (s.VendorAreaLevelMargin > 0)
            {
                const long immune = 0x2L | 0x100L | 0x2000000L;   // mirrors SpotSelector.ImmuneUnitFlagMask
                float areaLevel = GrindMobsRepository.AverageAttackableLevelNear(map, loc, s.VendorAreaScanRadius, _factions, immune);
                if (areaLevel > me.Level + s.VendorAreaLevelMargin)
                    return RejectVendor("area avg level {0:F0} >> mine {1} (higher-level zone)", areaLevel, me.Level);
            }
            return true;
        }

        private bool RejectVendor(string reasonFmt, params object[] args)
        {
            Logging.Write(System.Drawing.Color.Orange,
                "[VibeGrinder/Vendor] SKIP {0} ({1}) — {2}; blacklisting, re-routing.",
                BotPoi.Current.Name, BotPoi.Current.Type, string.Format(reasonFmt, args));
            var vm = ProfileManager.CurrentProfile?.VendorManager;
            if (vm != null && BotPoi.Current.AsVendor != null)
                vm.Blacklist.Add(BotPoi.Current.AsVendor);
            BotPoi.Clear("vendor unsafe (enemy territory / higher-level zone)");
            return false;
        }

        /// <summary>
        /// Surface incidental hostiles as candidates. LevelBot's filter only adds grind-faction/in-band mobs,
        /// so a foreign hostile (a roaming raptor, a Kolkar off the grind list) is invisible to Targeting and
        /// the bot walks into it and body-pulls. Add a hostile when it is (a) attacking us/pet — DEFENSIVE,
        /// any level/distance, so an add is always a target and we never leash off something that's on us — or
        /// (b) within pull range AND level-safe — PATH-CLEAR, so we single-pull what's in our way instead of
        /// bodyblocking it. Distant side hostiles stay ignored. These flow through the same pack-fear weighting
        /// (ApplyCrowdAndNeutralWeighting vetoes any sitting in a camp) and the nearest-bias commitment, so we
        /// pull the nearest CLEAN hostile and refuse camped ones. WHO pulls is unchanged: TransitPeel on a
        /// vendor run, ApplyPullCommitment otherwise.
        /// </summary>
        private void IncludeNearbyHostiles(System.Collections.Generic.List<WoWObject> incoming,
                                           System.Collections.Generic.HashSet<WoWObject> outgoing)
        {
            var me = StyxWoW.Me;
            if (me == null) return;
            // Surface foreign hostiles WIDER than pull range so the commit/pull pipeline has lead time before
            // they aggro on the approach — a pull-range ring is too tight at walk speed (see IncidentalHostileRadius).
            double surfaceR = VibeGrinderSettings.Instance.IncidentalHostileRadius;
            if (surfaceR <= 0) surfaceR = Targeting.PullDistance > 0 ? Targeting.PullDistance : 28;
            int safeLevel = me.Level + VibeGrinderSettings.Instance.PathHostileLevelMargin;
            ulong petGuid = me.GotAlivePet && me.Pet != null ? me.Pet.Guid : 0;
            int surfaced = 0, defensive = 0;

            foreach (WoWObject obj in incoming)
            {
                if (obj is not WoWUnit u || obj is WoWPlayer) continue;
                if (u.Dead || u.MyReaction > WoWUnitReaction.Hostile) continue;   // hostiles only
                // OUR OWN totem reads faction-hostile (faction 58) — allegiance comes from the OWNER, not the
                // faction template — so it surfaced as "hostile" and the bot Lightning-Bolted its own Searing
                // Totem forever (can't damage your own totem). Never target it. Enemy totems still surface (a
                // snare/root totem whose caster fled may be the only thing left to break free on), but get
                // buried to last-resort in ApplyCrowdAndNeutralWeighting so a real mob always outranks them.
                if (u.IsTotem && u.CreatedByGuid == me.Guid) continue;
                if (outgoing.Contains(obj) || Blacklist.Contains(u.Guid)) continue;

                bool attackingUs = u.CurrentTargetGuid == me.Guid || (petGuid != 0 && u.CurrentTargetGuid == petGuid);
                // Path-clear is gated to level-safe in-range hostiles (level 0 = ?? → never proactively pull,
                // but still defend if it's on us). Defensive ignores both gates — fight what's hitting us.
                int ulevel = (int)u.Level;
                bool pathClear = u.Distance <= surfaceR && ulevel > 0 && ulevel <= safeLevel;
                if (!attackingUs && !pathClear) continue;

                outgoing.Add(obj);
                surfaced++;
                if (attackingUs) defensive++;
            }

            if (surfaced > 0 && (!_surfaceLogSw.IsRunning || _surfaceLogSw.Elapsed.TotalSeconds >= 3))
            {
                Logging.WriteDebug("[VibeGrinder/Hostiles] surfaced {0} incidental hostile(s) ({1} attacking us).",
                    surfaced, defensive);
                _surfaceLogSw.Restart();
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
                if (me.Level - u.Level >= TrivialLevelGap) continue;    // grey/trivial mob — no threat, don't detour to single-pull it on a vendor run
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
            Targeting.Instance.IncludeTargetsFilter -= IncludeNearbyHostiles;
            if (_onKill != null)
            {
                BotEvents.Player.OnMobKilled -= _onKill;
                _onKill = null;
            }
            if (_onUiError != null)
            {
                Lua.Events.DetachEvent("UI_ERROR_MESSAGE", _onUiError);
                _onUiError = null;
            }
            Vendors.OnVendorItems -= OnVendorSweep;
            Vendors.OnMailItems -= OnMailSweep;
            Supervision.TrekSafety.Clear();   // restore any hazard-marked navmesh polys
            _synth?.RestoreCharacterSettings();   // undo the global FoodAmount/DrinkAmount seeding
            GrindMobsRepository.Shutdown();   // release the DB handle so a later Start re-opens cleanly
            Bots.DungeonBuddy.Avoidance.WorldObstacleManager.Shutdown();
        }

        public override void Pulse()
        {
            var me = StyxWoW.Me;
            if (me == null) return;   // null on loading screens / zone transitions

            Navigator.PathPrecision = System.Math.Clamp(me.MovementInfo.CurrentSpeed * 0.15f, 1.5f, 10f);
            if (_restGovernor != null) _restGovernor.Suppressed = _vendorRun;   // no resting mid-errand (the routine's pull still runs)
            _restGovernor?.Pulse(me);
            if (_supervisor != null) _supervisor.RestingLatch = _resting;   // stall watchdog can't see a no-consumable rest otherwise
            _supervisor?.Pulse();

            // ENGAGING hysteresis (see CLAUDE.md "Stateful inter-spot movement"): while in combat or holding a
            // pull commit we're ENGAGING; keep that latched EngageGraceSeconds past the last such tick so a
            // one-tick commit flicker can't hand the wheel back to the relocate/travel goal (the travel↔kill
            // oscillation). Refreshed here every tick; the tree gates only READ Engaging.
            if (me.Combat || _committedGuid != 0)
                _engageUntil = DateTime.Now.AddSeconds(VibeGrinderSettings.Instance.EngageGraceSeconds);

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
