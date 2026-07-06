using Styx.WoWInternals.WoWObjects;

namespace Bots.Vibes.Shared
{
    /// <summary>
    /// What the shared pull pipeline (EngagementGovernor) needs from its hosting botbase.
    /// VibeGrinder and VibeQuester v2 each implement this over their own latches/supervision.
    /// </summary>
    public interface IEngagementHost
    {
        /// <summary>Live rest-governor band check — "should I recover before starting a pull?" (never a constant).</summary>
        bool RestNeeded(WoWUnit me);

        /// <summary>The sticky rest latch (recovery band — RestNeeded can read false while still resting).</summary>
        bool IsResting { get; }

        /// <summary>Vendor/mail errand latch — while true, TransitPeel owns pulls and grind commitment stands down.</summary>
        bool OnServiceRun { get; }

        /// <summary>The transit-peel commit (0 when none) — pinned as FirstUnit so LevelBot can't override the peel.</summary>
        ulong PeelGuid { get; }

        /// <summary>Pack-death fear factor (≥1) — tightens MaxFightCompany; eased only by real progress.</summary>
        double CrowdCautionFactor { get; }

        /// <summary>PREEMPT DENIED / ABORT wants ENGAGING grace broken NOW — consumed in the host's Pulse()
        /// (the _engageUntil field is written ONLY there, by invariant).</summary>
        void RequestBreakEngageGrace();

        /// <summary>An exposure-rejected mob (camp-wall accounting lives host-side).</summary>
        void RecordExposureReject(WoWUnit u);

        /// <summary>A commit was dropped because it needs swimming (water-trap accounting lives host-side).</summary>
        void RecordSwimBlocked();
    }
}
