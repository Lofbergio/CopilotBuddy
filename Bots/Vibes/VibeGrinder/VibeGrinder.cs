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
        private PrioritySelector _root;
        private BotEvents.Player.MobKilledDelegate _onKill;
        private bool _spotInstalled;
        private readonly System.Diagnostics.Stopwatch _selectRetry = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _safeRest = new System.Diagnostics.Stopwatch();

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
                        // ...and deliberately single-pull the most isolated hostile in pull range instead of
                        // body-pulling blind into a pack. TransitPeel must TARGET the mob (CanPull keys off
                        // CurrentTarget, and CombatBehavior only targets on a *switch* — our POI already ==
                        // FirstUnit, so without an explicit target it never pulls and the POI thrashes).
                        new Decorator(ctx => OnVendorRun() && !StyxWoW.Me.Combat, new Action(ctx => TransitPeel())),
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
        private RunStatus SafeRestReposition()
        {
            var me = StyxWoW.Me;
            bool eligible = me != null && !me.Combat && !me.IsDead && !me.IsGhost && !me.Mounted
                            && !me.HasAura("Food") && !me.HasAura("Drink");
            if (eligible)
            {
                var s = VibeGrinderSettings.Instance;
                bool usesMana = me.PowerType == WoWPowerType.Mana;
                bool resting = me.HealthPercent <= s.SafeRestHealthPct || (usesMana && me.ManaPercent <= s.SafeRestManaPct);
                if (resting && _safeRest.Elapsed.TotalSeconds <= 8)
                {
                    float danger = s.SafeRestDangerRange;
                    WoWUnit nearest = null;
                    double nearestDist = double.MaxValue;
                    foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(true, false))
                    {
                        if (u == null || u.Dead || !u.IsHostile) continue;
                        double d = u.Distance;
                        if (d <= danger && d < nearestDist) { nearestDist = d; nearest = u; }
                    }
                    if (nearest != null)
                    {
                        // Back off directly away from the nearest hostile to just past the danger range.
                        float dx = me.Location.X - nearest.Location.X;
                        float dy = me.Location.Y - nearest.Location.Y;
                        float len = (float)System.Math.Sqrt(dx * dx + dy * dy);
                        if (len < 0.1f) { dx = 1f; dy = 0f; len = 1f; }   // degenerate: any direction
                        float scale = (danger + 12f) / len;
                        WoWPoint away = new WoWPoint(me.Location.X + dx * scale, me.Location.Y + dy * scale, me.Location.Z);

                        WoWPoint[] path = Navigator.GeneratePath(me.Location, away);
                        if (path != null && path.Length > 0)
                        {
                            if (!_safeRest.IsRunning) _safeRest.Restart();
                            TreeRoot.StatusText = "VibeGrinder: backing off to rest safely";
                            Navigator.MoveTo(away);
                            return RunStatus.Success;
                        }
                    }
                }
            }
            if (_safeRest.IsRunning) _safeRest.Reset();
            return RunStatus.Failure;
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
        /// Transit targeting: LevelBotIncludeTargetsFilter adds NO faction targets during a vendor run
        /// (HB 6.2.3: "only aggro mobs"), which is exactly why the bot body-pulls — it can't see a pack
        /// until it's already standing in it. Re-add nearby *hostiles* within pull range (neutrals don't
        /// proximity-aggro, so they never add on a trek) so the caution weighting (WeighTargetsAvoidPacks)
        /// orders them and TransitPeel can single them off. Pre-combat, on-trek only; nothing else acts on
        /// these (Roam excludes vendor POIs, the pull branch is gated on Kill) — only TransitPeel consumes them.
        /// </summary>
        private void TransitIncludeHostiles(System.Collections.Generic.List<WoWObject> incoming,
                                            System.Collections.Generic.HashSet<WoWObject> outgoing)
        {
            if (!OnVendorRun() || StyxWoW.Me.Combat) return;
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
        /// Transit peel: on a vendor run, before anything body-pulls us, deliberately single-pull the most
        /// isolated hostile in pull range (TargetList is caution-ordered: FirstUnit == [0]) by handing it to
        /// the existing CombatBehavior as a Kill POI. Returns Success once a POI is set (next tick pulls);
        /// Failure (→ VendorBehavior resumes travel) when there's nothing to peel. Skips when essentially at
        /// the vendor so it interacts instead of starting a fight on the doorstep.
        /// </summary>
        private RunStatus TransitPeel()
        {
            var me = StyxWoW.Me;
            // At the destination — let VendorBehavior interact; don't peel on the doorstep.
            if (BotPoi.Current.Location.Distance(me.Location) <= 12f) return RunStatus.Failure;

            double pull = Targeting.PullDistance;
            if (pull <= 0) return RunStatus.Failure;

            foreach (WoWUnit u in Targeting.Instance.TargetList)   // caution-ordered: most isolated first
            {
                if (u == null || u.Dead) continue;
                if (u.MyReaction > WoWUnitReaction.Hostile) continue;   // hostiles only — neutrals won't body-pull
                if (u.Distance > pull || !u.InLineOfSpellSight) continue;
                // Target it: CanPull()/Singular key off CurrentTarget, and CombatBehavior only calls
                // Target() when switching off a different POI — ours already is FirstUnit, so without this
                // the mob is never targeted, the pull never fires, and the Kill/vendor POI thrashes.
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
