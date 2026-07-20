namespace Bots.Vibes.Shared
{
    /// <summary>
    /// The engagement/survival knobs the shared governor reads. Exists to point the dependency arrow
    /// DOWNWARD: <see cref="EngagementGovernor"/> lives in Shared/ but took VibeGrinderSettings as a
    /// parameter type and read its singleton directly, so "shared" code imported a concrete botbase.
    /// That is why IEngagementHost had exactly one implementor for a year — the interface abstracted the
    /// host but never its config, so a second bot could not supply tuning without becoming VibeGrinder.
    ///
    /// VibeGrinderSettings implements this by name (no members were renamed), so every existing caller
    /// still compiles and every value still comes from the same panel. A second bot can now bring its
    /// own tuning without the governor knowing it exists.
    ///
    /// Read-only on purpose: the governor consumes tuning, it never writes it.
    /// </summary>
    public interface IVibeTuning
    {
        // ---- Pull selection / commitment ----
        int MaxPullDistance { get; }
        float PullCommitBoost { get; }
        int PullCommitMaxSeconds { get; }
        float PullCrowdRadius { get; }
        float PullCrowdPenalty { get; }
        float PullCrowdPenaltyCap { get; }
        int PullPackVetoCount { get; }

        // ---- Level bands / danger ----
        int LevelBandBelow { get; }
        int DangerLevelMargin { get; }
        int PathHostileLevelMargin { get; }

        // ---- Aggro geometry ----
        float PreemptAggroBuffer { get; }
        float AssistRadius { get; }
        int IncidentalHostileRadius { get; }
        float NeutralHostileAvoidRadius { get; }
        float NeutralNearHostileVeto { get; }
        float NeutralOpenAvoidRadius { get; }

        // ---- Exposure gate / bubble veto ----
        bool EnableExposureGate { get; }
        bool EnableBubbleVeto { get; }
        bool EnableCorridorCheck { get; }
        float ExposurePad { get; }
        int ExposureRejectSeconds { get; }

        // ---- Entry bans ----
        int EntryBanGiveUps { get; }
        int EntryBanMinutes { get; }
        bool EnableExperimentalDropBan { get; }
        int ExperimentalDropBanMinutes { get; }

        // ---- Spatial query shaping (grind-data repository) ----
        /// <summary>Vertical band a spawn query accepts around the query point — keeps a cave or an
        /// upper deck out of a ground-level result.</summary>
        float SpotQueryZBand { get; }

        // ---- Errands (see Shared/Errands) ----
        /// <summary>Abandon a trip that has not finished in this long. GeneratePath sees neither a
        /// world wedge nor a transaction the server refuses, so the trip needs a clock of its own.</summary>
        int VendorRunAbortSeconds { get; }
        /// <summary>Reject a stop with at least VendorHostileThreshold player-hostile spawns within
        /// this radius — it sits in enemy territory.</summary>
        float VendorHostileRadius { get; }
        int VendorHostileThreshold { get; }
        /// <summary>Reject a stop whose surrounding wild mobs average more than VendorAreaLevelMargin
        /// above our level — "nearest" is continent-wide and the next zone up is lethal.</summary>
        float VendorAreaScanRadius { get; }
        int VendorAreaLevelMargin { get; }
        /// <summary>Reject a stop whose WALK is this many times its straight-line distance (past the
        /// floor). Geometrically near is not reachably near.</summary>
        float VendorDetourFactor { get; }
        float VendorDetourMinYd { get; }

        // ---- Curves (methods, not knobs: the shape is the tuning) ----
        /// <summary>How many hostiles we'll accept in one fight at this caution level.</summary>
        int MaxFightCompany(double caution);

        /// <summary>Crowd-penalty scale for a mob of this level (squishy-low, relaxed-high).</summary>
        float CrowdLevelScale(int level);
    }
}
