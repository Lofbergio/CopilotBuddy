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

        // Entry-level ban (see RecordEntryGiveUp / VetoBannedEntries): distinct same-entry give-ups → ban
        // the whole NAME, because a per-guid blacklist re-learns the same caged prisoner 11 times as its
        // neighbours rotate in. _dbImmuneCache: template unit_flags per entry (live flags can LIE — the
        // prisoners spawn 0x8000 live but carry IMMUNE_TO_PC in the template), one SQL per new entry.
        private readonly System.Collections.Generic.Dictionary<uint, System.Collections.Generic.HashSet<ulong>> _entryGiveUps
            = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.HashSet<ulong>>();
        private readonly System.Collections.Generic.Dictionary<uint, DateTime> _entryBanUntil
            = new System.Collections.Generic.Dictionary<uint, DateTime>();
        private readonly System.Collections.Generic.Dictionary<uint, bool> _dbImmuneCache
            = new System.Collections.Generic.Dictionary<uint, bool>();
        private readonly System.Diagnostics.Stopwatch _entryVetoLogSw = new System.Diagnostics.Stopwatch();   // throttle entry-veto log
        private bool _spotInstalled;
        private ulong _peelGuid;        // committed transit-peel target (see TransitPeel) — don't re-pick per tick
        private ulong _committedGuid;   // committed grind-pull target (see ApplyPullCommitment)
        private readonly System.Diagnostics.Stopwatch _committedTimer = new System.Diagnostics.Stopwatch();
        private double _committedLastDist = double.MaxValue;   // last distance to committed mob (progress watchdog)
        private bool _committedProgressed;      // made ANY approach progress / opened on the current commit? (experimental drop-ban gate)
        private string _committedName;          // last-committed mob name — held is null at the drop, so stash it for the ban log
        private readonly System.Diagnostics.Stopwatch _selectRetry = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _safeRest = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _restMoveSw = new System.Diagnostics.Stopwatch();   // safe-spot walk cap (see SafeRestReposition)
        private readonly System.Diagnostics.Stopwatch _surfaceLogSw = new System.Diagnostics.Stopwatch();   // throttle incidental-hostile log
        private readonly System.Diagnostics.Stopwatch _eliteVetoLogSw = new System.Diagnostics.Stopwatch();   // throttle elite-veto log
        private readonly System.Diagnostics.Stopwatch _playerVetoLogSw = new System.Diagnostics.Stopwatch();   // throttle player/pet-veto log
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
        // Pathability-REJECT strikes per guid (session-scoped, cleared in Start): 3 no-path rejects of the
        // same mob = a real mesh hole, escalate past the 45s cycle. A successful path resets the guid.
        private readonly System.Collections.Generic.Dictionary<ulong, int> _pathRejects = new();
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
            _committedGuid = 0;
            _committedTimer.Reset();
            _committedLastDist = double.MaxValue;
            _committedProgressed = false;
            _committedName = null;
            _entryGiveUps.Clear();
            _entryBanUntil.Clear();   // session-scoped learning; _dbImmuneCache survives (static DB truth)
            _pathRejects.Clear();
            // A residual strike + a fresh first error used to trip GIVE UP FAST on ONE real error this
            // session instead of the designed two-within-6s (audit 2026-07-05).
            _pullErrorCount = 0; _lastPullError = null; _pullErrorAt = DateTime.MinValue;
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
                _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue; _committedProgressed = false;
                _peelGuid = 0;
                _resting = false;
                ClearVendorRunState();   // ALL vendor state, not just the latch — see ClearVendorRunState
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

            _onKill = args =>
            {
                _supervisor.RecordKill();
                // A kill of an entry proves it's engageable — clear its give-up strikes (the SwimTrap
                // "one kill proves workable" rule), so real grind mobs can never accumulate to a ban.
                try { if (args?.KilledMob != null) _entryGiveUps.Remove(args.KilledMob.Entry); }
                catch { /* stale unit at the death tick — strikes just persist */ }
            };
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
                    _pullErrorAt = DateTime.UtcNow;
                    // Telemetry: the 2026-07-03 prisoner wedge burned 128 casts with ZERO strikes counted and
                    // the dead log couldn't say why. One line per counted strike makes any future deafness
                    // (handler gate vs event delivery) diagnosable from Logs\ alone.
                    Logging.WriteDebug("[VibeGrinder/Commit] pull-error strike {0} on {1:X}: '{2}'",
                        _pullErrorCount, _committedGuid, msg);
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

            // 0a. NEVER pre-combat commit/peel a PLAYER or a player-controlled unit (hunter pet / warlock minion /
            //     charmed mob). On a PvP server, damaging any of them flags PvP with a REAL player who will
            //     retaliate — an unattended bot must treat them as terrain, exactly like elites. Same single
            //     chokepoint (early-returns in combat, so this is INITIATION only — if one's already on us the
            //     routine still defends). Report: the bot committed to "Винус", an enemy hunter pet, and would have
            //     opened on it (log 2026-07-04_1707).
            VetoPlayerUnits(units);

            // 0b. Nor a mob whose NAME has proven unengageable: DB-flagged immune templates (the spot
            //     selector already excludes their spawns via the same mask — this extends that truth to
            //     LIVE surfacing, which is flag-blind) and session entry-bans earned via RecordEntryGiveUp
            //     (the caged Theramore Prisoners: client-LoS clear, server-LoS blocked, per-guid blacklists
            //     re-learned the same cage 11 mobs in a row). Same single-chokepoint coverage as VetoElites.
            VetoBannedEntries(units);

            // ONE hostile snapshot per pulse, shared by the crowd weighting and the commit-layer exposure
            // gates below (a second full OM sweep per pulse is the decision-time-cost smell the doctrine bans).
            var hostiles = SnapshotHostiles();

            // 1. Quality weighting decides which mob is the *cleanest* to open on (runs in transit too —
            //    TransitPeel relies on the resulting isolation ordering).
            ApplyCrowdAndNeutralWeighting(units, me, hostiles);

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
                ApplyPullCommitment(units, me, hostiles);
        }

        // Live hostiles from the object manager — NOT just the current target list. An un-aggroed camp
        // member isn't a candidate yet, but it WILL proximity-aggro/assist the instant we engage a mob
        // beside it; counting only list members is how we opened on a "lone" giraffe and ate the adds.
        private static System.Collections.Generic.List<WoWUnit> SnapshotHostiles()
        {
            var hostiles = new System.Collections.Generic.List<WoWUnit>();
            foreach (WoWUnit h in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
                if (h != null && h is not WoWPlayer && !h.Dead && !h.IsTotem && h.MyReaction <= WoWUnitReaction.Hostile)
                    hostiles.Add(h);   // totems aren't pack adds — don't let them inflate crowd/veto counts
            return hostiles;
        }

        /// <summary>
        /// An out-of-band hostile we're standing bubble-deep in — the "inevitable" (over-level gap-band,
        /// e.g. Kenata L+4) and "unavoidable" (below-band green) classes IncludeNearbyHostiles surfaces
        /// ONLY under this condition. The fight is coming regardless (a wall just postpones it via LoS),
        /// so these keep tier-0 pick priority and bypass the exposure gates: taking it 1v1 NOW at full
        /// resources beats eating it as a mid-fight add. In-band mobs deliberately do NOT qualify —
        /// walking away from an at-level camp mob that hasn't aggroed yet is allowed (the Lost Rigger fix).
        /// </summary>
        private static bool FightIsOurs(WoWUnit u, WoWUnit me, VibeGrinderSettings s)
        {
            int ulevel = (int)u.Level;
            bool outOfBand = ulevel > me.Level + s.PathHostileLevelMargin
                             || (ulevel > 0 && ulevel < me.Level - s.LevelBandBelow);
            return outOfBand
                   && u.Distance <= u.MyAggroRange + s.PreemptAggroBuffer
                   && System.Math.Abs(u.Location.Z - me.Location.Z) < 5f;
        }

        /// <summary>
        /// "If I fight x standing at point, how many OTHERS join?" — hostiles (≠x, not already fighting
        /// us) whose server aggro range + ExposurePad covers the point (Z≥5 excluded — cliffs protect),
        /// plus mobs within AssistRadius of x (coarse same-camp assist). A LOWER bound for a long fight:
        /// real assist is faction-gated but re-broadcasts ~2s centered on the ENGAGED, MOVING mob — which
        /// is why the mid-approach re-check exists. colliders = up to 3 names for the log (rule 5).
        /// </summary>
        private static int FightExposure(WoWUnit x, WoWPoint point,
                                         System.Collections.Generic.List<WoWUnit> hostiles,
                                         VibeGrinderSettings s, out string colliders)
        {
            int n = 0;
            System.Text.StringBuilder sb = null;
            float assistR2 = s.AssistRadius * s.AssistRadius;
            foreach (WoWUnit h in hostiles)
            {
                if (h.Guid == x.Guid || h.IsTargetingMeOrPet) continue;
                double bubble = h.MyAggroRange + s.ExposurePad;
                bool joins = (h.Location.DistanceSqr(point) <= bubble * bubble
                              && System.Math.Abs(h.Location.Z - point.Z) < 5f)
                             || h.Location.DistanceSqr(x.Location) <= assistR2;
                if (!joins) continue;
                n++;
                if (n <= 3)
                {
                    sb ??= new System.Text.StringBuilder();
                    if (sb.Length > 0) sb.Append(", ");
                    sb.AppendFormat("{0} d={1:F0}", h.Name, h.Location.Distance(point));
                }
            }
            colliders = sb?.ToString() ?? "";
            return n;
        }

        // Where WE stand when the opener goes out: on the approach line MaxPullDistance short of x (the
        // mob then runs to us, so the fight sits there); already in pull range → right where we are.
        private static WoWPoint OpenPoint(WoWUnit me, WoWUnit x, VibeGrinderSettings s)
        {
            WoWPoint mine = me.Location, theirs = x.Location;
            double d = mine.Distance(theirs);
            if (d <= s.MaxPullDistance || d < 0.01) return mine;
            double t = (d - s.MaxPullDistance) / d;
            return new WoWPoint((float)(mine.X + (theirs.X - mine.X) * t),
                                (float)(mine.Y + (theirs.Y - mine.Y) * t),
                                (float)(mine.Z + (theirs.Z - mine.Z) * t));
        }

        /// <summary>
        /// Distinct hostiles whose aggro bubble the PATH to x crosses. The open-point gate can't see a
        /// bubble web we'd walk THROUGH to a clean mob beyond it — that traversal (each web mob aggroing
        /// as we cross) is the incident's other death geometry. Densifies the already-generated path at
        /// ~8yd steps, bounded, so it costs distance checks, not pathfinds.
        /// </summary>
        private static int CorridorExposure(WoWPoint[] path, WoWUnit x,
                                            System.Collections.Generic.List<WoWUnit> hostiles,
                                            VibeGrinderSettings s, out string colliders)
        {
            int n = 0;
            System.Text.StringBuilder sb = null;
            foreach (WoWUnit h in hostiles)
            {
                if (h.Guid == x.Guid || h.IsTargetingMeOrPet) continue;
                double bubble = h.MyAggroRange + s.ExposurePad;
                double b2 = bubble * bubble;
                bool crossed = false;
                WoWPoint prev = path[0];
                for (int i = 0; i < path.Length && !crossed; i++)
                {
                    WoWPoint seg = path[i];
                    double segLen = prev.Distance(seg);
                    int steps = System.Math.Min(16, (int)(segLen / 8.0) + 1);
                    for (int k = 0; k <= steps; k++)
                    {
                        double t = steps == 0 ? 0 : (double)k / steps;
                        var p = new WoWPoint((float)(prev.X + (seg.X - prev.X) * t),
                                             (float)(prev.Y + (seg.Y - prev.Y) * t),
                                             (float)(prev.Z + (seg.Z - prev.Z) * t));
                        if (h.Location.DistanceSqr(p) <= b2 && System.Math.Abs(h.Location.Z - p.Z) < 5f)
                        {
                            crossed = true;
                            break;
                        }
                    }
                    prev = seg;
                }
                if (!crossed) continue;
                n++;
                if (n <= 3)
                {
                    sb ??= new System.Text.StringBuilder();
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(h.Name);
                }
            }
            colliders = sb?.ToString() ?? "";
            return n;
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
                // Never strip a mob that's ON us or the live commit: Me.Combat lags the aggro by 1-3s, and
                // removing an already-charging elite in that window hands FirstUnit to some other mob while
                // it closes — the leash-off class of bug (audit 2026-07-05). Losing to it deliberately beats
                // fighting it AND whatever we switch to; the death machinery backstops.
                if (u.IsTargetingMeOrPet || u.Guid == _committedGuid) continue;
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

        /// <summary>True if this unit is a player or is controlled by a player (pet / minion / charmed mob) —
        /// something we must never PROACTIVELY engage on a PvP server. Owner walk via the core WoWUnit chain
        /// (Charm/Summon/Created); a normal mob has none of those set → false, so real grind targets are safe.</summary>
        private static bool IsPlayerOrPlayerControlled(WoWObject o)
        {
            if (o is WoWPlayer) return true;
            WoWUnit u = o?.ToUnit();
            if (u == null) return false;
            try { return u.ControllingPlayer != null || u.OwnedByRoot is WoWPlayer; }
            catch { return false; }   // OM chain lookups can throw on a stale unit — treat as not-a-player
        }

        // Drop every player + player-controlled unit from the pre-combat candidate list so nothing downstream can
        // commit/peel/approach one (never initiate PvP). Same chokepoint + in-combat exemption as VetoElites.
        // DELIBERATELY no attacker/commit exemption (unlike VetoElites/VetoBannedEntries): damaging an attacking
        // player pet still flags PvP with a real player — worse than eating the pet's damage. Initiation: never.
        private void VetoPlayerUnits(System.Collections.Generic.List<Targeting.TargetPriority> units)
        {
            int vetoed = 0;
            string sample = null;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                if (units[i].Object == null || !IsPlayerOrPlayerControlled(units[i].Object)) continue;
                if (sample == null) sample = units[i].Object.Name;
                units.RemoveAt(i);
                vetoed++;
            }
            if (vetoed > 0 && (!_playerVetoLogSw.IsRunning || _playerVetoLogSw.Elapsed.TotalSeconds >= 3))
            {
                Logging.WriteDebug("[VibeGrinder/Hostiles] vetoed {0} player/pet candidate(s) (e.g. {1}) — never proactively PvP.",
                    vetoed, sample);
                _playerVetoLogSw.Restart();
            }
        }

        // Drop DB-flagged-immune and session-banned entries from pre-combat candidates. Initiation only —
        // the weigh hook early-returns in combat, so defense against one that somehow attacks is untouched.
        private void VetoBannedEntries(System.Collections.Generic.List<Targeting.TargetPriority> units)
        {
            int vetoed = 0;
            string sample = null;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                WoWUnit u = units[i].Object?.ToUnit();
                if (u == null || u.Entry == 0) continue;
                DateTime until;
                bool banned = _entryBanUntil.TryGetValue(u.Entry, out until) && DateTime.UtcNow < until;
                if (!banned && !IsDbImmune(u.Entry)) continue;
                // A banned-entry mob that's actually ON us (rare — bans exist because they can't engage) or
                // the live commit is never stripped mid-fight; same leash-off protection as VetoElites.
                if (u.IsTargetingMeOrPet || u.Guid == _committedGuid) continue;
                if (sample == null) sample = u.Name;
                units.RemoveAt(i);
                vetoed++;
            }
            if (vetoed > 0 && (!_entryVetoLogSw.IsRunning || _entryVetoLogSw.Elapsed.TotalSeconds >= 3))
            {
                Logging.WriteDebug("[VibeGrinder/Hostiles] vetoed {0} banned/immune-entry candidate(s) (e.g. {1}) — won't engage.",
                    vetoed, sample);
                _entryVetoLogSw.Restart();
            }
        }

        // Template unit_flags check (cached; one SQL per new entry). The DB carries the authored intent
        // (IMMUNE_TO_PC on the prisoner template) even when the live spawn drops the flag.
        private bool IsDbImmune(uint entry)
        {
            bool immune;
            if (_dbImmuneCache.TryGetValue(entry, out immune)) return immune;
            long flags = GrindMobsRepository.GetTemplateUnitFlags(entry);
            immune = flags > 0 && (flags & Selection.SpotSelector.ImmuneUnitFlagMask) != 0;
            _dbImmuneCache[entry] = immune;
            return immune;
        }

        /// <summary>
        /// "Mega sus" escalation (user 2026-07-03, the Theramore Prisoner cages): ONE give-up is an angle
        /// problem; EntryBanGiveUps DISTINCT mobs of the SAME entry all failing in place means the NAME is
        /// unengageable here → ban the entry (VetoBannedEntries consumes it). The fast path counts
        /// unconditionally — the client explicitly said the engage can't work; the slow 20s clock only
        /// counts with the in-place signature (in range + client-LoS true), because a give-up on a
        /// wanderer we couldn't catch says nothing about its entry. Kills reset strikes (see _onKill).
        /// </summary>
        private void RecordEntryGiveUp(WoWUnit held, double d, VibeGrinderSettings s, bool clientReported)
        {
            if (held == null || held.Entry == 0) return;
            if (!clientReported && (d > s.MaxPullDistance + 3 || !held.InLineOfSpellSight)) return;
            System.Collections.Generic.HashSet<ulong> guids;
            if (!_entryGiveUps.TryGetValue(held.Entry, out guids))
                _entryGiveUps[held.Entry] = guids = new System.Collections.Generic.HashSet<ulong>();
            guids.Add(held.Guid);
            if (guids.Count < s.EntryBanGiveUps) return;
            _entryBanUntil[held.Entry] = DateTime.UtcNow.AddMinutes(s.EntryBanMinutes);
            _entryGiveUps.Remove(held.Entry);
            Logging.Write(System.Drawing.Color.Khaki,
                "[VibeGrinder/Commit] ENTRY BAN {0} (entry {1}) — {2} distinct in-place give-ups; ignoring that name for {3}m.",
                held.Name, held.Entry, s.EntryBanGiveUps, s.EntryBanMinutes);
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
        private void ApplyCrowdAndNeutralWeighting(System.Collections.Generic.List<Targeting.TargetPriority> units,
                                                   WoWUnit me, System.Collections.Generic.List<WoWUnit> hostiles)
        {
            var s = VibeGrinderSettings.Instance;
            if (s.PullCrowdRadius <= 0f) return;   // AddAvoidance Off disables the whole layer

            // Squishy-lowbie COMFORT taper — applies only to the soft tiebreaker penalty below. The hard
            // vetoes are SURVIVAL and never taper: the old whole-method early-return at levelScale 0 turned
            // the pack veto off at 50+, and a L47 died to an 11-mob pack while the scale sat at 0.09.
            float levelScale = s.CrowdLevelScale(me.Level);
            // Adaptive: scale up the more we've been dying to packs (eased by progress).
            float caution = _supervisor != null ? (float)_supervisor.CrowdCautionFactor : 1f;
            float penalty = s.PullCrowdPenalty * levelScale * caution;

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

                // Never veto: a mob already ON us (rejecting it = leashing off a live attacker), the
                // COMMITTED mob (mid-flight removal reads as "no longer a candidate" → spurious drop + a
                // drop-ban false positive; its exposure is re-checked every pulse by the mid-approach abort
                // instead), or an out-of-band bubble mob (FightIsOurs: Kenata/below-band — it must stay
                // pickable so acquire takes it 1v1). NOTE the old blanket "inside its bubble → skip" is
                // GONE for in-band mobs: that exemption is what let the pirate camp through veto-free.
                if (!neutral && (a.IsTargetingMeOrPet || a.Guid == _committedGuid || FightIsOurs(a, me, s)))
                    continue;

                int addRisk = 0, bubbleRisk = 0;
                bool hostileInNeutralRange = false;
                foreach (WoWUnit b in hostiles)
                {
                    if (b.Guid == a.Guid) continue;
                    double d2 = a.Location.DistanceSqr(b.Location);
                    if (d2 <= crowdR2) addRisk++;
                    if (neutral && d2 <= neutR2) hostileInNeutralRange = true;
                    // Bubble overlap over the candidate's OWN body: a mob 3+ other aggro bubbles already
                    // cover is deep camp no matter how spread the bodies look — the fixed 12yd knot radius
                    // is blind to 17-18yd aggro ranges at 15-25yd spacing (Lost Rigger, 41 pirates, 0 vetoes).
                    if (s.EnableBubbleVeto && !neutral && !b.IsTargetingMeOrPet)
                    {
                        double br = b.MyAggroRange + s.ExposurePad;
                        if (d2 <= br * br && System.Math.Abs(b.Location.Z - a.Location.Z) < 5f) bubbleRisk++;
                    }
                }

                // HARD VETO a genuine camp — assist knot OR bubble web. The capped penalty below is only a
                // tiebreaker (so we don't walk past a near mob to a far one) — it CANNOT refuse a camp,
                // which is how a squishy caster fed itself into a 4-mob Kolkar bonfire camp and died. We
                // won't OPEN on a camp; if the area is all camps the empty list drives a relocate.
                if (!neutral && (addRisk >= s.PullPackVetoCount || bubbleRisk >= s.PullPackVetoCount))
                {
                    units.RemoveAt(i);
                    continue;
                }

                // CAP the crowd penalty so it stays a TIEBREAKER among similarly-close mobs and can never
                // outweigh proximity (base score loses only 2/yd). Without the cap a near mob with neighbours
                // loses to a far isolated one and the bot walks INTO/past the near pack to reach it.
                if (penalty > 0f && addRisk > 0)
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
        private void ApplyPullCommitment(System.Collections.Generic.List<Targeting.TargetPriority> units,
                                         WoWUnit me, System.Collections.Generic.List<WoWUnit> hostiles)
        {
            var s = VibeGrinderSettings.Instance;

            // Dead/ghost: the pre-pull selection layer has no business running. The incident log shows
            // PREEMPT firing 46ms after `I died.`, and the drop-ban falsely banning the mob we died
            // fighting when the candidate list vanished at the death tick. Own drop path — never the
            // held==null branch, so the drop-ban can't misread a death as a wedge.
            if (me.IsDead || me.IsGhost)
            {
                if (_committedGuid != 0)
                {
                    _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue;
                }
                return;
            }

            // Fear meter: max TOTAL mobs in a fight we CHOOSE, tightened by pack-death caution (see
            // MaxFightCompany). Shared by the acquire, preempt, and mid-approach exposure gates below.
            int maxCompany = s.MaxFightCompany(_supervisor?.CrowdCautionFactor ?? 1.0);

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
                    // Fear-meter gate on the switch: a threat we'd have to fight standing inside OTHER
                    // bubbles is not a fight to accept while we can still leave — the incident's death was
                    // this preempt chaining pirate-by-pirate into a 41-mob camp. Deny: shed the threat
                    // (45s, so it stops being FirstUnit and Roam doesn't approach it), defer the held
                    // commit (own path — no ban), and break the ENGAGING grace so TRAVELING re-arms NOW
                    // (EngageHold would otherwise hold us in the web ~3s, or approach the next camp mob).
                    // If it body-pulls anyway it becomes IsTargetingMeOrPet → tier 0 → we defend where we
                    // stand, moving away. Out-of-band bubble mobs bypass (FightIsOurs — that fight is ours).
                    if (s.EnableExposureGate && !threat.IsTargetingMeOrPet && !FightIsOurs(threat, me, s))
                    {
                        int exposure = FightExposure(threat, me.Location, hostiles, s, out string who);
                        if (exposure >= maxCompany)
                        {
                            Logging.Write(System.Drawing.Color.Khaki,
                                "[VibeGrinder/Commit] PREEMPT DENIED {0} (d={1:F1}) — fighting it here pulls {2} more ({3}); cap {4} — shedding it and leaving.",
                                threat.Name, threatDist, exposure, who, maxCompany);
                            Blacklist.Add(threat.Guid, System.TimeSpan.FromSeconds(s.ExposureRejectSeconds));
                            _supervisor?.RecordExposureReject(threat);
                            _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue;
                            _breakEngageGrace = true;
                            threat = null;
                        }
                    }
                    if (threat != null)
                    {
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] PREEMPT to {0} (d={1:F1}, aggro={2:F0}{3}) — path hostile about to pull; deferring {4:X}",
                            threat.Name, threatDist, threat.MyAggroRange, heldNeutral ? ", neutral commit" : "", _committedGuid);
                        // Commit STRAIGHT to the threat — dropping to 0 and letting Acquire re-pick would re-grab the
                        // NEARER neutral (it's closer than the threat), re-fire the preempt, and oscillate 5x/sec.
                        _committedGuid = threat.Guid; _committedTimer.Restart(); _committedLastDist = double.MaxValue;
                        _committedProgressed = false; _committedName = threat.Name;
                    }
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
                    {
                        _committedTimer.Restart();
                        // Real approach progress (closing) or engagement. Ignore the acquire-tick MaxValue→d
                        // seed — that's the first reading, not progress. Gates the experimental drop-ban.
                        if (opened || _committedLastDist != double.MaxValue) _committedProgressed = true;
                    }
                    _committedLastDist = d;

                    // Fight-duration lookahead: exposure re-checked where we STAND, every pulse until the
                    // opener is away — mobs drift in, our walk drifts into webs, and the commit-instant
                    // check sees neither (server assist re-broadcasts ~2s for the whole fight). Post-open
                    // is irrevocable (switching ADDS a mob) so this never fires then. Own drop path (valid
                    // = false → the !valid reset below), so the drop-ban can't misread it as a wedge.
                    // Near/bubble-inside commits open the same tree tick they're made — for those the
                    // acquire gate is the only guard, by design; this protects the far approach.
                    if (!opened && s.EnableExposureGate
                        && !held.IsTargetingMeOrPet && !FightIsOurs(held, me, s))
                    {
                        int exposure = FightExposure(held, me.Location, hostiles, s, out string who);
                        if (exposure >= maxCompany)
                        {
                            Logging.Write(System.Drawing.Color.Khaki,
                                "[VibeGrinder/Commit] ABORT approach to {0} (d={1:F1}) — standing inside {2} bubble(s) ({3}); cap {4} — backing off 45s.",
                                held.Name, d, exposure, who, maxCompany);
                            Blacklist.Add(_committedGuid, System.TimeSpan.FromSeconds(s.ExposureRejectSeconds));
                            _breakEngageGrace = true;
                            valid = false;
                        }
                    }

                    // FAST give-up on client-reported pull failures (UI_ERROR_MESSAGE handler, see Start): two
                    // LoS/invalid-target errors within 6s mean this pull cannot work from here — resolve NOW
                    // with the reason verbatim instead of letting the 20s clock discover it (fluid doctrine).
                    if (valid && !opened && _pullErrorCount >= 2 && (DateTime.UtcNow - _pullErrorAt).TotalSeconds < 6)
                    {
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] GIVE UP FAST on {0} — client reported '{1}' ×{2} — blacklisting 2m.",
                            held.Name, _lastPullError, _pullErrorCount);
                        Blacklist.Add(_committedGuid, System.TimeSpan.FromMinutes(2));
                        RecordEntryGiveUp(held, d, s, clientReported: true);
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
                            me.IsMoving, held.InLineOfSpellSight, NearestHostileDesc(hostiles), units.Count);
                        Blacklist.Add(_committedGuid, System.TimeSpan.FromMinutes(2));
                        RecordEntryGiveUp(held, d, s, clientReported: false);
                        valid = false;
                    }
                }
                else if (_committedGuid != 0)
                {
                    Logging.WriteDebug("[VibeGrinder/Commit] drop {0:X} — {1}", _committedGuid,
                        held == null ? "no longer a candidate" : "dead");
                    // EXPERIMENTAL (2026-07-04, Den of Flame timber-fence trap): the mob left the candidate
                    // list ("no longer a candidate") and we never closed distance or opened on it — almost
                    // always physically unreachable (mesh says reachable, a protruding timber wedges us short).
                    // The delist→recommit→delist flap resets the PullCommitMaxSeconds give-up clock so it never
                    // matures to a blacklist and the bot re-picks the trap forever; ban at the drop edge instead.
                    // Scoped hard: held==null (left the list, NOT a kill) AND no progress — so killed mobs (we
                    // opened → progressed) and deliberately-deferred preempts are spared. See EnableExperimentalDropBan.
                    if (held == null && !_committedProgressed && s.EnableExperimentalDropBan)
                    {
                        Blacklist.Add(_committedGuid, System.TimeSpan.FromMinutes(s.ExperimentalDropBanMinutes));
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] EXPERIMENTAL BAN {0} ({1:X}) — committed with zero approach progress before it left the list; likely a pathing wedge. Ignoring {2}min.",
                            _committedName ?? "?", _committedGuid, s.ExperimentalDropBanMinutes);
                    }
                }
                if (!valid) { _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue; }
            }

            // Acquire: commit to the NEAREST acceptable candidate (tiered — see below), not the highest-scored
            // one. Highest score = most isolated, which biases FAR — and walking across the area to a far
            // straggler drags us through packs (the Sunscale/Kolkar pull that killed us) and blind-body-pulls
            // things en route (the raptor we "didn't see"). The crowd weighting already REMOVED hard packs and
            // BURIED neutrals-near-hostiles from the list, so nearest-of-what's-left is a close, single-pullable
            // mob. Fall back to top score only if everything is buried (so we still act rather than idle).
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
                bool exposureRejected = false;
                Targeting.TargetPriority pick = null;
                for (int attempt = 0; attempt < 3 && pick == null; attempt++)
                {
                    double nearest = double.MaxValue;
                    int pickTier = int.MaxValue;
                    for (int i = 0; i < units.Count; i++)
                    {
                        var c = units[i];
                        if (c.Object == null || c.Score < buryFloor || rejected.Contains(c.Object.Guid)) continue;
                        WoWUnit cu = c.Object.ToUnit();
                        if (cu == null || cu.Dead) continue;   // a just-killed corpse lingers a frame in the list — don't re-commit to it
                        // Tiered nearest: already-ours (attacking us, or an OUT-OF-BAND mob we're bubble-deep
                        // in — Kenata/below-band, that fight is coming regardless) > visible > around-a-corner.
                        // A LoS-blocked pick means walking INTO its position to gain LoS, so the open happens
                        // at body-pull range (user report 2026-07-02); a corner mob also can't aggro through
                        // the wall while we fight the visible one. Last resort, not a veto: with nothing
                        // visible we still grind the corner mob, deliberately. NOTE: an IN-band mob whose
                        // bubble we're in is no longer tier 0 — the exposure gate below decides engage vs
                        // walk away (blanket "inevitable" ranking is what marched us into the pirate camp).
                        double d = c.Object.Distance;
                        int tier = cu.IsTargetingMeOrPet || FightIsOurs(cu, me, s) ? 0
                                 : cu.InLineOfSpellSight ? 1 : 2;
                        if (tier < pickTier || (tier == pickTier && d < nearest)) { nearest = d; pick = c; pickTier = tier; }
                    }
                    if (pick == null) break;

                    WoWUnit pu = pick.Object.ToUnit();
                    if (pu != null && !pu.IsTargetingMeOrPet)
                    {
                        WoWPoint[] path = Navigator.GeneratePath(me.Location, pu.Location);
                        if (path == null || path.Length == 0)
                        {
                            // Escalate a REPEAT offender: a genuine mesh hole re-rejects every 45s forever —
                            // a permanent per-pulse pathfind tax with no route to the long ban the analogous
                            // unengageable-entry case gets (audit 2026-07-05). 3 strikes → 45 min.
                            int strikes = _pathRejects.TryGetValue(pu.Guid, out int prev) ? prev + 1 : 1;
                            _pathRejects[pu.Guid] = strikes;
                            bool longBan = strikes >= 3;
                            Logging.Write(System.Drawing.Color.Khaki,
                                "[VibeGrinder/Commit] REJECT {0} (d={1:F1}) — no path to it ({2}); trying next-nearest.",
                                pu.Name, pu.Distance,
                                longBan ? "3rd no-path — banning " + s.EntryBanMinutes + "m" : "strike " + strikes);
                            Blacklist.Add(pu.Guid, longBan
                                ? System.TimeSpan.FromMinutes(s.EntryBanMinutes)
                                : System.TimeSpan.FromSeconds(45));
                            if (longBan) _pathRejects.Remove(pu.Guid);
                            rejected.Add(pick.Object.Guid);
                            pick = null;
                        }
                        else
                        {
                            _pathRejects.Remove(pu.Guid);   // pathable again (it wandered) — clean slate
                            // Fear-meter gate (lookahead, doctrine rule 3): who joins if we open on this pick
                            // from where we'd stand — and does the WALK there cross a bubble web (the open-point
                            // check can't see a web we'd traverse to a clean mob beyond it)? Out-of-band bubble
                            // mobs bypass (FightIsOurs) but still get the pathability REJECT above (an indoors
                            // Kenata must stay rejectable). Reject = 45s + next-nearest, same pattern as no-path.
                            if (s.EnableExposureGate && !FightIsOurs(pu, me, s))
                            {
                                int exposure = FightExposure(pu, OpenPoint(me, pu, s), hostiles, s, out string who);
                                int crossed = 0; string cwho = "";
                                if (exposure < maxCompany && s.EnableCorridorCheck)
                                    crossed = CorridorExposure(path, pu, hostiles, s, out cwho);
                                if (exposure >= maxCompany || crossed >= maxCompany)
                                {
                                    bool viaCorridor = exposure < maxCompany;
                                    Logging.Write(System.Drawing.Color.Khaki,
                                        "[VibeGrinder/Commit] REJECT {0} (d={1:F1}) — {2} pulls {3} more ({4}); cap {5} — 45s.",
                                        pu.Name, pu.Distance,
                                        viaCorridor ? "the walk to it" : "opening on it",
                                        viaCorridor ? crossed : exposure,
                                        viaCorridor ? cwho : who, maxCompany);
                                    Blacklist.Add(pu.Guid, System.TimeSpan.FromSeconds(s.ExposureRejectSeconds));
                                    _supervisor?.RecordExposureReject(pu);
                                    rejected.Add(pick.Object.Guid);
                                    exposureRejected = true;
                                    pick = null;
                                }
                            }
                        }
                    }
                }
                // Everything nearby was buried (neutrals) → act on the top score rather than idle. NOT after
                // an exposure-reject: recommitting the highest-score survivor UN-GATED re-opens the exact hole
                // the gate closed (an 11-mob camp sheds 3 rejects/tick and this fallback would open on the
                // 4th) — let the emptying list drive Depleted() → relocate instead.
                if (pick == null && !exposureRejected)
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
                    _committedProgressed = false;   // fresh commit — no approach progress yet
                    _pullErrorCount = 0;   // fresh commit → fresh error strikes
                    WoWUnit bu = pick.Object.ToUnit();
                    _committedName = bu?.Name;      // stash for the drop-ban log (held is null at the drop)
                    if (bu != null)
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] ACQUIRE {0} (reaction={1}, d={2:F1}, score={3:F0}) " +
                            "nearestHostile={4} candidates={5}",
                            bu.Name, bu.MyReaction, bu.Distance, pick.Score, NearestHostileDesc(hostiles), units.Count);
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
            && (StyxWoW.Me.Combat || _committedGuid != 0 || DateTime.UtcNow < _engageUntil);

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
        // Reuses the pulse's hostile snapshot — this used to do its own full OM sweep, the exact
        // decision-time-cost smell the shared snapshot exists to prevent.
        private static string NearestHostileDesc(System.Collections.Generic.List<WoWUnit> hostiles)
        {
            WoWUnit n = null;
            double best = double.MaxValue;
            foreach (WoWUnit u in hostiles)
            {
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
                    Supervision.TrekSafety.MarkLeg(_factions, me.Location, BotPoi.Current.Location, me.Level, me.MapId, "vendor leg");
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
            // Upper bound of the "inevitable fight" band (see below). Derived from DangerLevelMargin so the two
            // systems always meet: anything ABOVE it near kill positions already made the spot Dangerous at
            // selection (OverlevelHostileInAggro); anything at/below it that we're bubble-deep in is ours to fight.
            int inevitableLevel = me.Level + VibeGrinderSettings.Instance.DangerLevelMargin;
            ulong petGuid = me.GotAlivePet && me.Pet != null ? me.Pet.Guid : 0;
            int surfaced = 0, defensive = 0, inevitableN = 0, greenN = 0;

            foreach (WoWObject obj in incoming)
            {
                if (obj is not WoWUnit u || obj is WoWPlayer) continue;
                if (IsPlayerOrPlayerControlled(obj)) continue;                     // never surface an enemy player's pet/minion — no PvP
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
                // BAND-bounded below too (same TargetMinLevel band the mounted find-target checks): with no
                // floor, a below-band roadside mob (Witherbark Troll[31] vs a 36) got surfaced + committed,
                // and the mounted Kill-POI conversion then refused it on the level filter — the approach↔
                // hotspot oscillation (log 2026-07-03_1458 15:15+). Greens have no grind value anyway; if
                // one body-pulls, attackingUs surfaces it and we defend.
                int ulevel = (int)u.Level;
                int floorLevel = me.Level - VibeGrinderSettings.Instance.LevelBandBelow;
                bool pathClear = u.Distance <= surfaceR && ulevel >= floorLevel && ulevel <= safeLevel;
                // Inevitable fight: a hostile in the (L+PathHostileLevelMargin, L+DangerLevelMargin] band —
                // too high for pathClear, too low for OverlevelHostileInAggro to have rejected the spot — was
                // invisible to BOTH systems, so we camped inside its aggro bubble and it added onto a fight
                // (Kenata Dabyrie, L+4, in the farmhouse — log 2026-07-02_1513 15:22). When we're standing in
                // its bubble the fight is coming regardless (a wall only postpones it via LoS), so surface it
                // and let nearest-first acquire take it 1v1 at full resources instead. Z-separation ≥5yd is
                // real protection (server blocks proximity aggro past ~3yd of Z) — a cliff mob stays ignored.
                bool inevitable = ulevel > safeLevel && ulevel <= inevitableLevel
                                  && u.Distance <= u.MyAggroRange + VibeGrinderSettings.Instance.PreemptAggroBuffer
                                  && System.Math.Abs(u.Location.Z - me.Location.Z) < 5f;
                // Downward twin (user 2026-07-03: "either keep running or fight them when you get into
                // combat"): a BELOW-band hostile has no grind value, but ON FOOT inside its aggro bubble
                // the fight has already started — we just can't see it. Invisibility produced the
                // incoherent Sentry-camp loop (log 2026-07-03_1847 23:26): outran the aggro mid-transit,
                // then blindly body-pulled the same camp at the destination spot. Bubble-gated so distant
                // greens stay invisible (no Witherbark green-chasing regression), and NOT while mounted —
                // mounted = outrun it; surfacing mid-ride would steer the ride into the green, the exact
                // mounted-refusal oscillation the band floor was added to kill.
                bool unavoidable = !me.Mounted && ulevel > 0 && ulevel < floorLevel
                                   && u.Distance <= u.MyAggroRange + VibeGrinderSettings.Instance.PreemptAggroBuffer
                                   && System.Math.Abs(u.Location.Z - me.Location.Z) < 5f;
                // Commitment hysteresis: the COMMITTED mob stays surfaced regardless of the radius. A mob
                // wandering ON the 40yd boundary otherwise flaps candidate↔gone every pulse — ACQUIRE, "no
                // longer a candidate" 0.25s later, back to hotspot travel, re-ACQUIRE 4s later, ~7s/cycle
                // for a full minute (Highland Strider at 39.3-40.0yd, log 2026-07-02_1644 22:52). Real
                // cancellers stay real: death/blacklist still drop it (blacklist is checked above), and the
                // no-progress give-up clock still expires an unreachable one.
                bool committed = _committedGuid != 0 && u.Guid == _committedGuid;
                if (!attackingUs && !pathClear && !inevitable && !unavoidable && !committed) continue;

                outgoing.Add(obj);
                surfaced++;
                if (attackingUs) defensive++;
                else if (inevitable && !pathClear) inevitableN++;
                else if (unavoidable && !pathClear) greenN++;
            }

            if (surfaced > 0 && (!_surfaceLogSw.IsRunning || _surfaceLogSw.Elapsed.TotalSeconds >= 3))
            {
                Logging.WriteDebug("[VibeGrinder/Hostiles] surfaced {0} incidental hostile(s) ({1} attacking us{2}{3}).",
                    surfaced, defensive,
                    inevitableN > 0 ? ", " + inevitableN + " inevitable over-level" : "",
                    greenN > 0 ? ", " + greenN + " bubble-deep below-band" : "");
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
                    hostiles ??= SnapshotHostiles();
                    int exposure = FightExposure(u, OpenPoint(me, u, s), hostiles, s, out string who);
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
            _restGovernor?.ReleaseRestLatch();   // don't leave the routine trusting a latch nobody updates
            Supervision.TrekSafety.Clear();   // restore any hazard-marked navmesh polys
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
                if (!me.Combat && _committedGuid == 0) _engageUntil = DateTime.MinValue;
            }
            else if (me.Combat || _committedGuid != 0)
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
