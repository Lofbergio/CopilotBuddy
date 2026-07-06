using Styx.Helpers;

namespace Bots.Vibes.VibeQuester2
{
    /// <summary>
    /// VibeQuester v2 — procedural quester rebuilt on the VibeGrinder chassis (design of record:
    /// docs/superpowers/specs/2026-07-06-vibequester-v2-design.md). Subclassing IS the architecture:
    /// the survival shell (Root cascade order, vendor/rest/engage latches, watchdogs, EngagementGovernor,
    /// TrekSafety, seed/restore) is inherited, never duplicated — v2 adds the quest layer (planner,
    /// task executor, arbiter) and swaps the activity slot. While the arbiter is pinned to Grind
    /// (chassis phase), this botbase is deliberately indistinguishable from VibeGrinder in a log.
    /// </summary>
    public class VibeQuester2 : Bots.VibeGrinder.VibeGrinder
    {
        private readonly ActivityArbiter _arbiter = new ActivityArbiter();

        public override string Name => "VibeQuester v2";

        public override void Start()
        {
            _arbiter.Reset();
            _arbiter.Update(0, 0, 0);   // logs the pinned-GRIND announcement
            base.Start();
        }
    }
}
