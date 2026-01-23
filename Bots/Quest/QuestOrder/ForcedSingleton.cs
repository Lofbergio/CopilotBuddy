// ForcedSingleton.cs - A behavior that executes a single action
// Ported from HB 4.3.4

using TreeSharp;

namespace Bots.Quest.QuestOrder
{
    /// <summary>
    /// A forced behavior that executes a single action and completes.
    /// Used for simple one-shot actions like setting grind area, vendors, etc.
    /// </summary>
    public class ForcedSingleton : ForcedBehavior
    {
        private readonly System.Action _action;
        private bool _done;

        public ForcedSingleton(System.Action action)
        {
            _action = action ?? throw new System.ArgumentNullException(nameof(action));
        }

        public override bool IsDone => _done;

        protected override Composite CreateBehavior()
        {
            return new TreeSharp.Action(ctx =>
            {
                _action();
                _done = true;
                return RunStatus.Success;
            });
        }
    }
}
