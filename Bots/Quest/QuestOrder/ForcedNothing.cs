// ForcedNothing.cs - A behavior that does nothing (for checkpoints, etc.)
// Ported from HB 4.3.4

using CommonBehaviors.Actions;
using TreeSharp;

namespace Bots.Quest.QuestOrder
{
    /// <summary>
    /// A forced behavior that does nothing and immediately completes.
    /// Used for checkpoint nodes and other pass-through nodes.
    /// </summary>
    public class ForcedNothing : ForcedBehavior
    {
        public override bool IsDone => true;

        protected override Composite CreateBehavior()
        {
            return new ActionAlwaysSucceed();
        }
    }
}
