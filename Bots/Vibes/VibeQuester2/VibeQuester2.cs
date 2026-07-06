using System;
using Bots.VibeGrinder.Selection;
using Bots.Vibes.VibeQuester2.Execution;
using Bots.Vibes.VibeQuester2.Planning;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using TreeSharp;
using VibeQuester;
using Action = TreeSharp.Action;

namespace Bots.Vibes.VibeQuester2
{
    /// <summary>
    /// VibeQuester v2 — procedural quester rebuilt on the VibeGrinder chassis (design of record:
    /// docs/superpowers/specs/2026-07-06-vibequester-v2-design.md). Subclassing IS the architecture:
    /// the survival shell (Root cascade order, vendor/rest/engage latches, watchdogs, the shared
    /// EngagementGovernor, TrekSafety, seed/restore) is inherited, never duplicated. v2 adds the quest
    /// layer — QuestPlanner (danger-gated selection), QuestActivity (task executor in the activity
    /// slot), ActivityArbiter (quest↔grind on the supply signal; grinding is the universal filler).
    /// </summary>
    public class VibeQuester2 : Bots.VibeGrinder.VibeGrinder
    {
        private readonly ActivityArbiter _arbiter = new ActivityArbiter();
        private DataLoader _questData;
        private QuestPlanner _planner;
        private QuestActivity _activity;
        private FactionResolver _planFactions;   // own instance — plan scans may run before/without a grind build
        private uint _planFactionsMap = uint.MaxValue;
        private DateTime _nextScanAt = DateTime.MinValue;
        private QuestPlan _lastPlan;
        private BotEvents.Player.PlayerDiedDelegate _onDied;

        public override string Name => "VibeQuester v2";

        /// <summary>Grind-only branches (spot bootstrap, discretionary relocate) run only in grind-mode.</summary>
        protected override bool GrindEnabled => _arbiter.Current == Activity.Grind;

        protected override Composite CreateActivityBranch() =>
            new Decorator(ctx => _arbiter.Current == Activity.Quest && _activity != null,
                new Action(ctx => _activity.Tick()));

        public override void Start()
        {
            _arbiter.Reset();
            _questData = new DataLoader();
            bool dataReady = _questData.Load() != null;
            Logging.Write("[VQ2] quest data {0}.", dataReady ? "loaded" : "NOT AVAILABLE — quest layer idle, grinding only");
            _planner = dataReady ? new QuestPlanner(_questData) : null;
            _planFactions = new FactionResolver();
            _planFactionsMap = uint.MaxValue;
            _nextScanAt = DateTime.MinValue;
            _lastPlan = null;
            _activity = _planner == null ? null : new QuestActivity(
                _planner,
                () => Synth,
                () => _planFactions,
                () => _questData.Database,
                onAreaReplaced: InvalidateGrindSpot,
                requestReplan: () => _nextScanAt = DateTime.MinValue);

            _onDied = delegate { try { _activity?.NotifyDeath(); } catch { } };
            BotEvents.Player.OnPlayerDied += _onDied;

            base.Start();
        }

        public override void Stop()
        {
            if (_onDied != null)
            {
                BotEvents.Player.OnPlayerDied -= _onDied;
                _onDied = null;
            }
            _activity?.Reset();
            base.Stop();
        }

        public override void Pulse()
        {
            base.Pulse();
            MaybeScan();
        }

        /// <summary>
        /// Throttled planner scan on the worker thread (never a UI timer — the 2026-07-06 deadlock rule).
        /// Calm-window only (alive, out of combat, navmesh up). New plans are adopted at task
        /// boundaries only; the arbiter re-evaluates on every scan (replan boundary by construction).
        /// Turn-in edges and abandons request an immediate rescan via the executor's callback.
        /// </summary>
        private void MaybeScan()
        {
            if (_planner == null || DateTime.UtcNow < _nextScanAt) return;
            var me = StyxWoW.Me;
            if (me == null || !StyxWoW.IsInGame || me.IsDead || me.IsGhost || me.Combat) return;
            if (!Navigator.IsNavigatorLoaded) return;

            _nextScanAt = DateTime.UtcNow.AddSeconds(60);
            try
            {
                if (_planFactionsMap != me.MapId)
                {
                    _planFactions.Build(me.MapId);
                    _planFactionsMap = me.MapId;
                }
                _lastPlan = _planner.BuildPlan(me, _planFactions);
                _arbiter.Update(_lastPlan.DoableSupply,
                    VibeQuester2Settings.Instance.SupplyLowWater,
                    VibeQuester2Settings.Instance.SupplyHighWater);

                // Adopt at boundaries only — never yank an in-flight task; the executor asks for a
                // rescan the moment it finishes/abandons, so adoption lag is one edge, not a minute.
                if (_activity != null && _arbiter.Current == Activity.Quest && _activity.AtBoundary)
                    _activity.AdoptPlan(_lastPlan);
            }
            catch (Exception ex)
            {
                Logging.Write(System.Drawing.Color.Orange, "[VQ2-Plan] scan failed: {0}", ex.Message);
                Logging.WriteDebug(ex.ToString());
            }
        }
    }
}
