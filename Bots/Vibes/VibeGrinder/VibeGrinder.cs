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
        private PrioritySelector _root;
        private BotEvents.Player.MobKilledDelegate _onKill;
        private bool _spotInstalled;
        private readonly System.Diagnostics.Stopwatch _selectRetry = new System.Diagnostics.Stopwatch();

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
                        LevelBot.CreateCombatBehavior(),
                        LevelBot.CreateLootBehavior(),
                        LevelBot.CreateVendorBehavior(),
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
            _selectRetry.Reset();

            _factions = new FactionResolver();
            _mailboxes = new MailboxService();
            _synth = new GrindAreaSynthesizer(_mailboxes);
            _selector = new SpotSelector(_factions);
            _supervisor = new GrindSupervisor(_selector, _synth, _factions);

            // Reuse LevelBot's target/loot filters (faction + blackspot + loot rules).
            Targeting.Instance.IncludeTargetsFilter += LevelBot.LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter += LevelBot.LevelbotIncludeLootsFilter;
            // Pull discipline: bias FirstUnit toward isolated mobs so we don't open on a pack.
            Targeting.Instance.WeighTargetsFilter += WeighTargetsAvoidPacks;

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
            if (StyxWoW.Me.Combat) return;
            var s = VibeGrinderSettings.Instance;
            if (s.PullCrowdPenalty <= 0f || s.PullCrowdRadius <= 0f) return;

            // Squishy-lowbie concern: full strength at/below PullCrowdFullLevel, tapering off by
            // PullCrowdLevelCeiling (you have AoE/cooldowns; density is upside).
            float levelScale = s.CrowdLevelScale(StyxWoW.Me.Level);
            if (levelScale <= 0f) return;
            // Adaptive: scale up the more we've been dying to packs (eased by progress).
            float caution = _supervisor != null ? (float)_supervisor.CrowdCautionFactor : 1f;
            float penalty = s.PullCrowdPenalty * levelScale * caution;

            float r2 = s.PullCrowdRadius * s.PullCrowdRadius;
            for (int i = 0; i < units.Count; i++)
            {
                WoWUnit a = units[i].Object.ToUnit();
                if (a == null) continue;

                int addRisk = 0;
                for (int j = 0; j < units.Count; j++)
                {
                    if (j == i) continue;
                    WoWUnit b = units[j].Object.ToUnit();
                    // Only mobs that aggro on proximity drag in as adds; neutrals stay put.
                    if (b != null && b.MyReaction <= WoWUnitReaction.Hostile
                        && a.Location.DistanceSqr(b.Location) <= r2)
                        addRisk++;
                }
                if (addRisk > 0)
                    units[i].Score -= addRisk * penalty;
            }
        }

        public override void Stop()
        {
            Targeting.Instance.IncludeTargetsFilter -= LevelBot.LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter -= LevelBot.LevelbotIncludeLootsFilter;
            Targeting.Instance.WeighTargetsFilter -= WeighTargetsAvoidPacks;
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
