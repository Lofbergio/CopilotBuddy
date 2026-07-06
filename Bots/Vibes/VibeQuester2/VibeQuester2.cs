using System;
using Bots.VibeGrinder.Selection;
using Bots.Vibes.VibeQuester2.Planning;
using Styx;
using Styx.Helpers;
using Styx.Logic.Pathing;
using VibeQuester;

namespace Bots.Vibes.VibeQuester2
{
    /// <summary>
    /// VibeQuester v2 — procedural quester rebuilt on the VibeGrinder chassis (design of record:
    /// docs/superpowers/specs/2026-07-06-vibequester-v2-design.md). Subclassing IS the architecture:
    /// the survival shell (Root cascade order, vendor/rest/engage latches, watchdogs, EngagementGovernor,
    /// TrekSafety, seed/restore) is inherited, never duplicated — v2 adds the quest layer (planner,
    /// task executor, arbiter) and swaps the activity slot. While the arbiter is pinned to Grind
    /// (chassis phase), this botbase grinds exactly like VibeGrinder and only SCANS quests (the
    /// [VQ2-Plan] lines are live grading of the planner before any execution exists).
    /// </summary>
    public class VibeQuester2 : Bots.VibeGrinder.VibeGrinder
    {
        private readonly ActivityArbiter _arbiter = new ActivityArbiter();
        private DataLoader _questData;
        private QuestPlanner _planner;
        private FactionResolver _planFactions;   // own instance — the base's is private, and plan scans
                                                 // may run for maps the grind side hasn't built yet
        private uint _planFactionsMap = uint.MaxValue;
        private DateTime _nextScanAt = DateTime.MinValue;
        private QuestPlan _lastPlan;

        public override string Name => "VibeQuester v2";

        /// <summary>The current plan (scan-only while the arbiter is pinned; the executor consumes it later).</summary>
        public QuestPlan CurrentPlan => _lastPlan;

        public override void Start()
        {
            _arbiter.Reset();
            _arbiter.Update(0, VibeQuester2Settings.Instance.SupplyLowWater, VibeQuester2Settings.Instance.SupplyHighWater);

            _questData = new DataLoader();
            bool dataReady = _questData.Load() != null;
            Logging.Write("[VQ2] quest data {0}.", dataReady ? "loaded" : "NOT AVAILABLE — quest layer idle, grinding only");
            _planner = dataReady ? new QuestPlanner(_questData) : null;
            _planFactions = new FactionResolver();
            _planFactionsMap = uint.MaxValue;
            _nextScanAt = DateTime.MinValue;
            _lastPlan = null;

            base.Start();
        }

        public override void Pulse()
        {
            base.Pulse();
            MaybeScan();
        }

        /// <summary>
        /// Throttled planner scan on the worker thread (never a UI timer — the 2026-07-06 deadlock rule).
        /// Calm-window only: not in combat, alive, navmesh up. Scan-only while pinned to grind; the
        /// executor phase will consume the plan and add event-driven replan triggers.
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
            }
            catch (Exception ex)
            {
                Logging.Write(System.Drawing.Color.Orange, "[VQ2-Plan] scan failed: {0}", ex.Message);
                Logging.WriteDebug(ex.ToString());
            }
        }
    }
}
