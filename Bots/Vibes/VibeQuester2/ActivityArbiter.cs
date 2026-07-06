using Styx.Helpers;

namespace Bots.Vibes.VibeQuester2
{
    public enum Activity
    {
        Quest,
        Grind,
    }

    /// <summary>
    /// Quest↔grind arbitration. Signal = count of DOABLE quests (eligible ∧ safe ∧ within travel
    /// range) from the planner; grinding is the universal filler when supply dries up. Hysteresis on
    /// both edges (LowWater/HighWater) and evaluation only at replan boundaries, so it cannot flap
    /// and never yanks an in-flight task (abandon paths are their own machinery).
    /// </summary>
    public class ActivityArbiter
    {
        private Activity _current = Activity.Grind;   // conservative start: grind until a scan proves supply

        public Activity Current => _current;

        /// <summary>Re-evaluate at a replan boundary.</summary>
        public void Update(int doableSupply, int lowWater, int highWater)
        {
            Activity prev = _current;
            if (_current == Activity.Quest && doableSupply < lowWater)
                _current = Activity.Grind;
            else if (_current == Activity.Grind && doableSupply >= highWater)
                _current = Activity.Quest;

            if (prev != _current)
                Logging.Write(System.Drawing.Color.MediumPurple,
                    "[VQ2-Arbiter] {0}→{1} (supply={2}, low={3}, high={4}).",
                    prev.ToString().ToUpperInvariant(), _current.ToString().ToUpperInvariant(),
                    doableSupply, lowWater, highWater);
        }

        public void Reset()
        {
            _current = Activity.Grind;
        }
    }
}
