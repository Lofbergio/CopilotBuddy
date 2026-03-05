// ForcedSingleton.cs - A behavior that executes a single action
// Ported from HB 4.3.4

using CommonBehaviors.Actions;
using TreeSharp;

namespace Bots.Quest.QuestOrder
{
    /// <summary>
    /// A forced behavior that executes a single action and completes immediately.
    /// Used for simple one-shot actions like setting grind area, vendors, etc.
    /// HB 4.3.4: Action runs in OnStart(), IsDone is always true, Branch is ActionAlwaysSucceed.
    /// This lets the while(IsDone) loop in ForcedBehaviorExecutor process singletons
    /// instantly without consuming a full tick.
    /// </summary>
    public class ForcedSingleton : ForcedBehavior
    {
        private readonly System.Action _action;

        public ForcedSingleton(System.Action action)
        {
            _action = action ?? throw new System.ArgumentNullException(nameof(action));
        }

        public override bool IsDone => true;

        public override void OnStart()
        {
            _action();
        }

        protected override Composite CreateBehavior()
        {
            return new ActionAlwaysSucceed();
        }

        public override string ToString()
        {
            return "[ForcedSingleton]";
        }
    }
}
