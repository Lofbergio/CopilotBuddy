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
    public class VibeGrinder : BotBase, IEngagementHost
    {
        private FactionResolver _factions;
        private MailboxService _mailboxes;
        private SpotSelector _selector;
        private GrindAreaSynthesizer _synth;
        private GrindSupervisor _supervisor;
        private RestGovernor _restGovernor;
        private PrioritySelector _root;
        private BotEvents.Player.MobKilledDelegate _onKill;

        // The shared pull pipeline (vetoes, pack lookahead, commitment) — extracted to Vibes/Shared so
        // VibeQuester v2 runs the identical machinery. This class is its IEngagementHost.
        private EngagementGovernor _governor;

        // Pull-failure events (fluid doctrine: the client TELLS us why a pull isn't working). The handler
        // shell lives here (attach/detach lifecycle); the strike counting is _governor.OnClientPullError.
        private LuaEventHandlerDelegate _onUiError;
        private bool _spotInstalled;
        private ulong _peelGuid;        // committed transit-peel target (see TransitPeel) — don't re-pick per tick
        private readonly System.Diagnostics.Stopwatch _selectRetry = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _safeRest = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _restMoveSw = new System.Diagnostics.Stopwatch();   // safe-spot walk cap (see SafeRestReposition)

        private bool _resting;                  // committed rest state (sticky: enter at Min*, exit at RestDonePct/cap)
        private WoWPoint _restSpot = WoWPoint.Empty;   // committed safe-rest destination (picked once per rest)
        private bool _restParked;               // positioning decision made for this rest — stop re-picking (see SafeRestReposition)
        private bool _drowning;                  // surfacing latch — log once per drowning episode (see SurfaceIfDrowning)
        private bool _vendorRun;                // committed vendor-errand latch (see UpdateVendorRun) — stable across the thrashing vendor POI
        private DateTime _vendorHoldUntil = DateTime.MinValue;   // hold the latch this long past the last vendor-POI/combat tick (rides out peel fights + brief POI gaps)
        private readonly System.Diagnostics.Stopwatch _vendorWatchdog = new System.Diagnostics.Stopwatch();   // abort a stuck errand (unreachable vendor / broke)
        private DateTime _engageUntil = DateTime.MinValue;   // ENGAGING activity latch held until here (see Engaging + CLAUDE.md "Stateful inter-spot movement")
        // Set by PREEMPT DENIED / ABORT approach (Targeting pulse); consumed in Pulse() — _engageUntil
        // itself is written ONLY in Pulse() (invariant). Without this, EngageHold's 3s grace holds us
        // standing in the bubble web we just decided to leave.
        private bool _breakEngageGrace;
        private DateTime _restSkipLogAt = DateTime.MinValue;   // throttles the rest-SKIP diagnostic (condition holds many ticks)

        private uint _vendorCheckedEntry;       // last vendor entry that passed the enemy-territory safety check (re-check when it changes)
        // Abort-streak escalation (2026-07-06: 61 COMMITs / 30 ABORTs against one wedged repair run all
        // night — an errand that keeps aborting must change TARGET, not just retry). Deliberately NOT
        // reset by ClearVendorRunState: the streak must survive the abort's own state reset to count it.
        private uint _vendorAbortEntry;         // vendor entry of the last abort (streak is per-entry)
        private int _vendorAbortStreak;         // consecutive aborts on that entry (or on Mail when entry==0)
        private PoiType _vendorRunType;         // errand type captured at COMMIT (POI may be anything by abort time)
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
                        // SURVIVAL FIRST, before the spot-install bootstrap: death handling and drowning need no
                        // spot, and a character that STARTS dead (or in draining water) with no eligible spot was
                        // otherwise a permanent ghost — the bootstrap gate owned every tick (audit 2026-07-05).
                        LevelBot.CreateDeathBehavior(),
                        // Not drowning outranks everything below (grind/vendor/rest/combat). Surfaces
                        // if the breath bar is draining and nearly out; falls through the instant we can breathe.
                        new Action(ctx => SurfaceIfDrowning()),
                        // Defer spot selection to the first tick: the navmesh isn't loaded until
                        // RaiseBotStart fires (after BotBase.Start). Holds the tree until a spot
                        // is installed; once installed this gate falls through.
                        new Decorator(ctx => !_spotInstalled, new Action(ctx => EnsureSpotSelected())),
                        // FLIGHT TRAVEL — while a taxi hop is in progress this OWNS the tick once airborne (so the
                        // vendor-run servicing below doesn't try to walk us back to the start-node POI) and detects
                        // the landing. Pre-takeoff it returns Failure so the vendor-run branch does the walk-to-
                        // master + taxi-open. Above vendor on purpose; below drowning/death (can't happen mid-air).
                        _supervisor != null ? _supervisor.FlightTravelCheck() : new Action(ctx => RunStatus.Failure),
                        // SURVIVAL-CRITICAL flee (pack death / death loop / camp wall) — ABOVE vendor mode and rest
                        // on purpose (audit 2026-07-05: a post-death durability errand latched vendor mode, and the
                        // post-res low-HP rest sat down AT the death camp — both starved this branch for minutes
                        // while it sat lower in the tree; rest ENTER 2s after corpse grab with 43 hostiles, log
                        // 2026-07-05_1402 14:18). Airborne ticks stay owned by FlightTravelCheck above (flying IS
                        // leaving); a hung pre-takeoff hop returns Failure there and lands here. Gate requires
                        // _current != null, so it's inert until the bootstrap below has installed a spot.
                        _supervisor != null ? _supervisor.EmergencyRelocationCheck() : new Action(ctx => RunStatus.Failure),
                        // TERMINAL-UNSTUCK HOLD — own the tick while the wedge-escape Hearthstone cast (10s) is
                        // in flight, so roam/vendor movement (and the stuck-handler jitter it causes) can't
                        // interrupt it. Set only by GrindSupervisor.InvokeUnstuck after a CONFIRMED geometry
                        // wedge (movement probe failed). Below the survival flee — a wedged char can't move
                        // anyway, and the flee must never sit behind anything; above everything that moves.
                        new Decorator(ctx => _supervisor != null && _supervisor.UnstuckHoldActive,
                            new Action(ctx => RunStatus.Success)),
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
                        // Never loot while a fight is still LIVE: a mob that FLED keeps us in combat, but
                        // CombatBehavior returns Failure for the tick it's out of cast range — without this gate a
                        // pending loot of a PREVIOUS kill's corpse slips through underneath and drags us off the
                        // runner before we finish it (the "looted the dead mob while the 2nd fled" bug). But the
                        // raw Me.Combat flag LINGERS ~5.5s after the last kill (server leave-combat timer), and for
                        // that window loot was blocked, CombatBehavior idled, and Roam sprinted toward the hotspot
                        // — after a "spot depleted" pre-relocate that hotspot is 100yd+ out, so the bot visibly ran
                        // AWAY from its unlooted corpses and doubled back (log 2026-07-02_1601 16:06:35→:41). Fight
                        // is over = nothing alive has us targeted (a fled runner still targets us → still blocked).
                        new Decorator(ctx => !OnVendorRun() && (!StyxWoW.Me.Combat || NoLiveAttackers()),
                            LevelBot.CreateLootBehavior()),
                        // NOT dead code: the one-tick "decide to vendor" bootstrap. LevelBot detects the sell/
                        // repair/mail need and sets the first vendor POI HERE; UpdateVendorRun() latches on it
                        // next tick and the vendor-mode branch (top of this selector) owns the rest of the
                        // errand. (A TransitPeel Decorator that used to sit above this line was unreachable —
                        // the latched vendor-mode branch already owns every OnVendorRun tick — and was removed;
                        // peeling happens inside that branch.)
                        LevelBot.CreateVendorBehavior(),
                        // Rest commitment: while we've decided to rest and aren't topped off yet, OWN the tick so
                        // Relocate/Roam below cannot pull us off the rest spot to grind — the rest↔roam oscillation.
                        // The actual eat/drink happens above in CombatBehavior's not-in-combat RestBehavior.
                        // (The survival flee now sits ABOVE vendor/rest — see the EmergencyRelocationCheck near the
                        // top of this selector; rest-ENTRY is additionally gated in UpdateRestingState.)
                        new Action(ctx => RestRoamBlock()),
                        // ENGAGING commitment — owns the wheel over travel so the DISCRETIONARY relocate and the
                        // kill-commit can't fight each other (see CLAUDE.md "Stateful inter-spot movement"):
                        //  (1) Don't relocate for a nearer/denser/contested spot while committed/fighting — finish
                        //      the kill, THEN re-evaluate. (The survival flee above is exempt.) Re-arms on disengage.
                        new Decorator(ctx => !Engaging,
                            _supervisor != null ? _supervisor.RelocationCheck() : new Action(ctx => RunStatus.Failure)),
                        // Flight learning: opportunistically detour to a nearby unlearned flight master. Gated to
                        // free travel — drowning/emergency-flee/combat/vendor/rest above all preempt it — and it
                        // owns the tick only while walking up, then hands the interact/record to the vendor-run
                        // servicing (it sets a Fly POI). Falls through to Roam when there's nothing to learn.
                        new Decorator(ctx => !Engaging && !OnVendorRun() && !StyxWoW.Me.Combat,
                            _supervisor != null ? _supervisor.FlightLearnCheck() : new Action(ctx => RunStatus.Failure)),
                        //  (2) EngageHold: during a TRANSIENT no-target gap mid-engage (commit dropped this tick,
                        //      not yet in combat — OR the post-kill combat-flag linger, when nothing above owns the
                        //      tick), OWN the tick so Roam's hotspot-move can't bolt toward the far new spot — the
                        //      travel↔kill ping-pong, and the linger-window sprint-and-mount-attempt (16:06:35, log
                        //      2026-07-02_1601). FirstUnit present → fall through so Roam approaches it normally
                        //      (route-killing untouched). Not engaging → fall through to travel.
                        new Decorator(ctx => Engaging && Targeting.Instance.FirstUnit == null,
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
            // Flight module follows the two STANDARD checkboxes (Settings → Use/Learn Flight Paths) — the
            // old AllowTaxiTravel master switch force-seeded both and was redundant (removed 2026-07-06).
            Logging.Write(System.Drawing.Color.SkyBlue,
                "[VibeGrinder/Flight] taxi travel {0}, path learning {1} (Settings → 'Use Flight Paths' / 'Learn Flight Paths'). {2} flight node(s) known.",
                CharacterSettings.Instance.UseFlightPaths ? "ON" : "off",
                CharacterSettings.Instance.UseFlightPaths && CharacterSettings.Instance.LearnFlightPaths ? "ON" : "off",
                Styx.Logic.FlightPaths.XmlNodes?.Count ?? 0);
            // A typo'd recipient silently voids mailed items for 30 days on an unattended run — make the
            // configured name visible in every session log.
            if (VibeGrinderSettings.Instance.EnableMailing)
                Logging.Write("[VibeGrinder] Mailing ENABLED → recipient '{0}' (verify the spelling — returned mail takes 30 days).",
                    CharacterSettings.Instance.MailRecipient ?? "(not set)");
            LevelBot.ResetState();
            BotPoi.Clear("VibeGrinder start");
            _root = null;
            _spotInstalled = false;
            _peelGuid = 0;
            _resting = false;
            _restSpot = WoWPoint.Empty;
            _restParked = false;
            _vendorRun = false;
            _vendorHoldUntil = DateTime.MinValue;
            _vendorWatchdog.Reset();
            _vendorCheckedEntry = 0;
            _vendorAbortEntry = 0;
            _vendorAbortStreak = 0;
            _vendorRunType = PoiType.None;
            _engageUntil = DateTime.MinValue;
            _breakEngageGrace = false;
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
                _governor?.DropCommit();
                _peelGuid = 0;
                _resting = false;
                ClearVendorRunState();   // ALL vendor state, not just the latch — see ClearVendorRunState
            };
            _restGovernor = new RestGovernor();   // dynamic rest thresholds; SafeRest reads these
            _restGovernor.SuppressedFloorHealth = VibeGrinderSettings.Instance.EmergencyMinHealth;   // vendor-run survival floor
            _restGovernor.SuppressedFloorMana = VibeGrinderSettings.Instance.EmergencyMinMana;

            // Shared pull pipeline — fresh per Start (session-scoped strikes/bans reset by construction).
            _governor = new EngagementGovernor(this);

            // Reuse LevelBot's target/loot filters (faction + blackspot + loot rules).
            Targeting.Instance.IncludeTargetsFilter += LevelBot.LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter += LevelBot.LevelbotIncludeLootsFilter;
            // Pull discipline: bias FirstUnit toward isolated mobs so we don't open on a pack.
            Targeting.Instance.WeighTargetsFilter += _governor.WeighTargets;
            // Surface incidental hostiles (attackers + nearby level-safe hostiles) so the bot SEES the mobs
            // off its grind list — otherwise it body-pulls them blind.
            Targeting.Instance.IncludeTargetsFilter += _governor.IncludeTargets;

            _onKill = args =>
            {
                _supervisor.RecordKill();
                // A kill of an entry proves it's engageable — clear its give-up strikes (the SwimTrap
                // "one kill proves workable" rule), so real grind mobs can never accumulate to a ban.
                try { if (args?.KilledMob != null) _governor.NotifyKillEntry(args.KilledMob.Entry); }
                catch { /* stale unit at the death tick — strikes just persist */ }
            };
            BotEvents.Player.OnMobKilled += _onKill;

            _onUiError = (sender, e) =>
            {
                string msg = e.Args.Length > 0 ? e.Args[0] as string : null;
                _governor.OnClientPullError(msg);   // gates on committed + pre-combat internally
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
                // Never START a rest while a survival flee is PENDING — the post-res low-HP tick is exactly
                // when it is (rest ENTER 2s after corpse grab with 43 hostiles, log 2026-07-05_1402 14:18).
                // The pending flag ONLY, deliberately: an earlier version also refused rest anywhere inside
                // the 45-min death-blacklist ring, which produced a chronic no-rest grind loop — the bot
                // fights on-route mobs and respawns inside the (up to 700yd) ring for the ring's whole
                // lifetime, and pulling at ~60% hp forever is deadlier than sitting down inside it (live
                // regression 2026-07-06 00:2x: 76 rest-skips, chronic 57-70% hp pulls, 6 deaths).
                // SafeRestReposition still backs the rest spot off from visible hostiles.
                if (need && _supervisor != null && _supervisor.HasPendingEmergencyRelocate)
                {
                    if (DateTime.UtcNow >= _restSkipLogAt)   // once per 10s — the condition holds for many ticks
                    {
                        _restSkipLogAt = DateTime.UtcNow.AddSeconds(10);
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Rest] SKIP enter (hp={0:F0}%) — survival flee pending; leaving first.",
                            me.HealthPercent);
                    }
                    return;
                }
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
            && (StyxWoW.Me.Combat || (_governor?.CommittedGuid ?? 0) != 0 || DateTime.UtcNow < _engageUntil);

        // --- IEngagementHost: the shared pull pipeline's view of this botbase ---
        bool IEngagementHost.RestNeeded(WoWUnit me) => RestNeeded(me);
        bool IEngagementHost.IsResting => _resting;
        bool IEngagementHost.OnServiceRun => _vendorRun;
        ulong IEngagementHost.PeelGuid => _peelGuid;
        double IEngagementHost.CrowdCautionFactor => _supervisor?.CrowdCautionFactor ?? 1.0;
        void IEngagementHost.RequestBreakEngageGrace() => _breakEngageGrace = true;
        void IEngagementHost.RecordExposureReject(WoWUnit u) => _supervisor?.RecordExposureReject(u);
        void IEngagementHost.RecordSwimBlocked() => _supervisor?.RecordSwimBlocked();

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
                _vendorHoldUntil = DateTime.UtcNow.AddSeconds(s.VendorRunStickySeconds);

            if (!_vendorRun)
            {
                if (poiIsVendor)
                {
                    _vendorRun = true;
                    _vendorRunType = BotPoi.Current.Type;   // for the abort escalation — the POI thrashes by abort time
                    _vendorWatchdog.Restart();
                    Logging.Write(System.Drawing.Color.Khaki,
                        "[VibeGrinder/Vendor] COMMIT — {0} run; grind/rest/roam suspended until done.", BotPoi.Current.Type);
                    // Trek safety for the errand leg: bend the route around red/pack aggro bubbles.
                    TrekSafety.MarkLeg(_factions, me.Location, BotPoi.Current.Location, me.Level, me.MapId, "vendor leg");
                }
                return _vendorRun;
            }

            // Committed. Hard backstop: an errand that can't complete (vendor unreachable / blacklisted / can't
            // afford repair) must not wedge us in vendor mode forever — abort and resume grinding.
            // Don't abort while airborne on a taxi — a long flight is legit and FlightTravelCheck owns it.
            if (_vendorWatchdog.Elapsed.TotalSeconds > s.VendorRunAbortSeconds && !me.OnTaxi)
            {
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder/Vendor] ABORT — errand didn't complete in {0}s; resuming grind.", s.VendorRunAbortSeconds);
                EscalateVendorAbort();
                EndVendorRun();
                return false;
            }

            // Done: the hold window lapsed — no vendor POI and no combat for VendorRunStickySeconds, i.e. the
            // transaction finished and nothing re-triggered it. Resume grinding (and rest then, if still low).
            if (DateTime.UtcNow > _vendorHoldUntil)
            {
                Logging.Write(System.Drawing.Color.Khaki, "[VibeGrinder/Vendor] DONE — errand complete; resuming grind.");
                _vendorAbortStreak = 0;   // a completed errand proves the target works
                _vendorAbortEntry = 0;
                EndVendorRun();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Two consecutive aborts on the same errand target = the TARGET is the problem (unreachable,
        /// unaffordable, wedged approach) — retrying it forever is the 2026-07-06 all-night loop. Blacklist
        /// it (timed for vendors, session for mailboxes) so the resolver picks a different one; a completed
        /// errand (DONE) resets the streak. Bounded and self-healing — never suppresses the NEED itself.
        /// </summary>
        private void EscalateVendorAbort()
        {
            uint failed = _vendorCheckedEntry;
            bool sameTarget = failed != 0 ? failed == _vendorAbortEntry : _vendorRunType == PoiType.Mail && _vendorAbortEntry == 0 && _vendorAbortStreak > 0;
            _vendorAbortStreak = sameTarget ? _vendorAbortStreak + 1 : 1;
            _vendorAbortEntry = failed;

            if (_vendorAbortStreak < 2)
                return;
            _vendorAbortStreak = 0;

            var profile = Styx.Logic.Profiles.ProfileManager.CurrentProfile;
            if (failed != 0 && profile?.VendorManager != null)
            {
                profile.VendorManager.Blacklist.Add(
                    new Styx.Logic.Profiles.Vendor((int)failed, "abort-streak", Styx.Logic.Profiles.Vendor.VendorType.Repair, WoWPoint.Empty));
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder/Vendor] ABORT ×2 on vendor entry {0} — blacklisting it (timed); the resolver picks another.", failed);
            }
            else if (_vendorRunType == PoiType.Mail && profile?.MailboxManager != null)
            {
                var mb = profile.MailboxManager.GetClosestMailbox();
                if (mb != null)
                {
                    profile.MailboxManager.Blacklist.Add(mb);
                    Logging.Write(System.Drawing.Color.Orange,
                        "[VibeGrinder/Vendor] ABORT ×2 on the mail errand — blacklisting the mailbox at {0} for this session; the resolver picks another.", mb.Location);
                }
            }
        }

        // One reset for ALL vendor-run state — shared by EndVendorRun and OnForceEscape so the two can't
        // drift as fields get added. OnForceEscape used to reset only _vendorRun; the stale
        // _vendorCheckedEntry then silently skipped VendorLocationSafe for a later same-entry vendor
        // (audit 2026-07-05). Deliberately does NOT touch _vendorAbortStreak/_vendorAbortEntry — the
        // abort escalation must survive its own errand's reset to count consecutive failures.
        private void ClearVendorRunState()
        {
            _vendorRun = false;
            _vendorHoldUntil = DateTime.MinValue;
            _vendorWatchdog.Reset();
            _vendorCheckedEntry = 0;
        }

        private void EndVendorRun()
        {
            ClearVendorRunState();
            // Trek safety for the RETURN leg: re-mark hazards from here back to the grind spot (the vendor-leg
            // marks are for a route we're no longer on). Falls back to a plain clear when no spot is installed.
            var me = StyxWoW.Me;
            var area = StyxWoW.AreaManager?.CurrentGrindArea;
            if (me != null && area?.CurrentHotSpot != null)
                TrekSafety.MarkLeg(_factions, me.Location, area.CurrentHotSpot.Position, me.Level, me.MapId, "return leg");
            else
                TrekSafety.Clear();
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
                float areaLevel = GrindMobsRepository.AverageAttackableLevelNear(map, loc, s.VendorAreaScanRadius, _factions, Selection.SpotSelector.ImmuneUnitFlagMask);
                if (areaLevel > me.Level + s.VendorAreaLevelMargin)
                    return RejectVendor("area avg level {0:F0} >> mine {1} (higher-level zone)", areaLevel, me.Level);
            }

            // TOPOLOGY: geometrically near ≠ reachably near. The core resolver picks by straight-line 3D
            // distance over the whole continent, so a vendor 200yd straight DOWN (Yarley inside the
            // Caverns of Time canyon, Z=-205, 2026-07-05) "beats" Gadgetzan while the real walk is the
            // entire spiral descent. One pathfind per newly-resolved vendor (this method is keyed by
            // _vendorCheckedEntry): no/partial path, or walk length far beyond the straight line ⇒ reject;
            // the TTL'd blacklist makes the resolver re-pick the next-nearest.
            if (s.VendorDetourFactor > 0)
            {
                float straight = (float)me.Location.Distance(loc);
                if (straight > 50f)
                {
                    WoWPoint[] path = Navigator.GeneratePath(me.Location, loc);
                    if (path == null || path.Length == 0)
                        return RejectVendor("no nav path to it");
                    float shortfall = (float)path[path.Length - 1].Distance(loc);
                    if (shortfall > 25f)
                        return RejectVendor("partial path (ends {0:F0}yd short)", shortfall);
                    float walk = 0f;
                    for (int i = 1; i < path.Length; i++)
                        walk += (float)path[i - 1].Distance(path[i]);
                    if (walk > System.Math.Max(s.VendorDetourMinYd, straight * s.VendorDetourFactor))
                        return RejectVendor("walk {0:F0}yd vs {1:F0}yd straight (canyon/cave detour)", walk, straight);
                }
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


        // "The fight is actually over" while Me.Combat still lingers (server leave-combat timer holds the
        // flag ~5.5s past the last kill): nothing alive has us or the pet targeted. A fled runner still
        // targets us, so the fled-mob loot protection this refines stays intact. Only scanned while the
        // combat flag is up (short-circuited by the caller), so the OM sweep costs nothing in normal ticks.
        private static bool NoLiveAttackers()
        {
            ulong meGuid = StyxWoW.Me.Guid;
            ulong petGuid = StyxWoW.Me.GotAlivePet && StyxWoW.Me.Pet != null ? StyxWoW.Me.Pet.Guid : 0;
            foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
                if (u != null && u.IsAlive
                    && (u.CurrentTargetGuid == meGuid || (petGuid != 0 && u.CurrentTargetGuid == petGuid)))
                    return false;
            return true;
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

            var s = VibeGrinderSettings.Instance;
            // Fear-meter gate for the peel too (audit 2026-07-05): the weighting's body-veto passes a mob
            // with 2 bubbles on it, but peeling it can still stand US in 3+ others' bubbles. Snapshot is
            // taken lazily — only when a peelable candidate actually exists this tick.
            System.Collections.Generic.List<WoWUnit> hostiles = null;
            int maxCompany = s.MaxFightCompany(_supervisor?.CrowdCautionFactor ?? 1.0);
            foreach (WoWUnit u in Targeting.Instance.TargetList)   // caution-ordered: most isolated first
            {
                if (u == null || u.Dead) continue;
                if (u.MyReaction > WoWUnitReaction.Hostile) continue;   // hostiles only — neutrals won't body-pull
                if (me.Level - u.Level >= TrivialLevelGap) continue;    // grey/trivial mob — no threat, don't detour to single-pull it on a vendor run
                if (u.Distance > pull || !u.InLineOfSpellSight) continue;
                if (s.EnableExposureGate && !u.IsTargetingMeOrPet)
                {
                    hostiles ??= EngagementGovernor.SnapshotHostiles();
                    int exposure = EngagementGovernor.FightExposure(u, EngagementGovernor.OpenPoint(me, u, s), hostiles, s, out string who);
                    if (exposure >= maxCompany)
                    {
                        Logging.WriteDebug("[VibeGrinder/Peel] skip {0} — peeling it pulls {1} more ({2}); cap {3}.",
                            u.Name, exposure, who, maxCompany);
                        continue;   // try the next-most-isolated; none clean → Failure, keep travelling
                    }
                }
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
            if (_governor != null)
            {
                Targeting.Instance.WeighTargetsFilter -= _governor.WeighTargets;
                Targeting.Instance.IncludeTargetsFilter -= _governor.IncludeTargets;
            }
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
            _restGovernor?.ReleaseRestLatch();   // don't leave the routine trusting a latch nobody updates
            TrekSafety.Clear();   // restore any hazard-marked navmesh polys
            _supervisor?.ClearWedgeBlackspots();   // remove session wedge blackspots so they don't bleed into the next run/toon
            _synth?.RestoreCharacterSettings();   // undo the global FoodAmount/DrinkAmount seeding
            GrindMobsRepository.Shutdown();   // release the DB handle so a later Start re-opens cleanly
            Bots.DungeonBuddy.Avoidance.WorldObstacleManager.Shutdown();
        }

        public override void Pulse()
        {
            var me = StyxWoW.Me;
            if (me == null) return;   // null on loading screens / zone transitions

            Navigator.PathPrecision = System.Math.Clamp(me.MovementInfo.CurrentSpeed * 0.15f, 1.5f, 10f);
            if (_restGovernor != null)
            {
                _restGovernor.Suppressed = _vendorRun;        // no resting mid-errand (the routine's pull still runs)
                _restGovernor.RoutineRestLatch = _resting;    // the routine's rest totem drops only inside a REAL rest
            }
            _restGovernor?.Pulse(me);
            if (_supervisor != null) _supervisor.RestingLatch = _resting;   // stall watchdog can't see a no-consumable rest otherwise
            _supervisor?.Pulse();

            // ENGAGING hysteresis (see CLAUDE.md "Stateful inter-spot movement"): while in combat or holding a
            // pull commit we're ENGAGING; keep that latched EngageGraceSeconds past the last such tick so a
            // one-tick commit flicker can't hand the wheel back to the relocate/travel goal (the travel↔kill
            // oscillation). Refreshed here every tick; the tree gates only READ Engaging. A PREEMPT DENIED /
            // ABORT drops the grace deliberately (leave the bubble web NOW, not in 3s) — via the flag, so
            // _engageUntil stays written only here.
            if (_breakEngageGrace)
            {
                _breakEngageGrace = false;
                if (!me.Combat && (_governor?.CommittedGuid ?? 0) == 0) _engageUntil = DateTime.MinValue;
            }
            else if (me.Combat || (_governor?.CommittedGuid ?? 0) != 0)
                _engageUntil = DateTime.UtcNow.AddSeconds(VibeGrinderSettings.Instance.EngageGraceSeconds);

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
            // BagItems, NOT CarriedItems: the latter includes EQUIPPED gear, which was classifying worn
            // items for vendor/mail (bag-only sell/mail mechanics + soulbound only accidentally saved us).
            foreach (WoWItem item in me.BagItems)
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
            // BagItems, NOT CarriedItems - never queue equipped gear for mailing (see OnVendorSweep).
            foreach (WoWItem item in me.BagItems)
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
