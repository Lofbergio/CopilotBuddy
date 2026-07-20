namespace Bots.Vibes.Shared
{
    /// <summary>
    /// The one place shared Vibes code reads tuning from. The owning botbase assigns
    /// <see cref="Current"/> in Start(); nothing in Shared/ may reach for a concrete bot's settings
    /// singleton instead — that is exactly what made "shared" code import VibeGrinder.
    ///
    /// A neutral holder rather than a property on EngagementGovernor, because the grind-data repository
    /// needs tuning too and a data layer reading its config out of the combat governor would be a worse
    /// dependency than the one being removed.
    ///
    /// Throws when unset: silently running on default thresholds is how a bot dies quietly.
    /// </summary>
    public static class VibeTuning
    {
        private static IVibeTuning _current;

        public static IVibeTuning Current
        {
            get => _current ?? throw new System.InvalidOperationException(
                "VibeTuning.Current is unset — the owning botbase must assign it in Start().");
            set => _current = value;
        }

        /// <summary>True when a bot has supplied tuning. For code that can legitimately no-op before Start.</summary>
        public static bool IsSet => _current != null;
    }
}
