using Styx.Helpers;

namespace Bots.Vibes.VibeQuester2
{
    public enum Activity
    {
        Quest,
        Grind,
    }

    /// <summary>
    /// Quest↔grind arbitration for VibeQuester v2. Signal = count of DOABLE quests (eligible ∧ safe ∧
    /// within travel range) from the planner; grinding is the universal filler when supply dries up.
    /// Hysteresis on both edges (LowWater/HighWater), and flips happen only at replan boundaries so an
    /// in-flight task is never abandoned by a flip (abandon paths are their own thing).
    ///
    /// Chassis phase (Task 6): pinned to Grind — the quest side doesn't exist yet. The pin is removed
    /// in Task 10 when the planner supplies a real signal.
    /// </summary>
    public class ActivityArbiter
    {
        private Activity _current = Activity.Grind;
        private bool _announced;

        /// <summary>Chassis pin — true until the quest pipeline lands (Task 10 unpins).</summary>
        public bool PinnedToGrind => true;

        public Activity Current => _current;

        /// <summary>Re-evaluate at a replan boundary. No-op while pinned.</summary>
        public void Update(int doableQuestSupply, int lowWater, int highWater)
        {
            if (PinnedToGrind)
            {
                if (!_announced)
                {
                    _announced = true;
                    Logging.Write("[VQ2-Arbiter] pinned GRIND (chassis phase — quest pipeline not wired yet).");
                }
                _current = Activity.Grind;
                return;
            }

            // Real hysteresis logic lands in Task 10:
            // supply < lowWater  → Grind; supply >= highWater → Quest; in between → keep current.
        }

        public void Reset()
        {
            _current = Activity.Grind;
            _announced = false;
        }
    }
}
