// ForcedBehavior.cs - Base class for forced quest behaviors
// Ported from HB 4.3.4

using System;
using TreeSharp;

namespace Bots.Quest.QuestOrder
{
    /// <summary>
    /// Base class for all forced quest behaviors.
    /// Forced behaviors are executed in order from the quest profile.
    /// </summary>
    public abstract class ForcedBehavior : IDisposable
    {
        private Composite _branch;

        /// <summary>
        /// The behavior tree branch for this forced behavior.
        /// Created lazily on first access.
        /// </summary>
        public Composite Branch
        {
            get
            {
                if (_branch == null)
                    _branch = CreateBehavior();
                return _branch;
            }
        }

        /// <summary>
        /// Creates the behavior tree for this forced behavior.
        /// </summary>
        protected abstract Composite CreateBehavior();

        /// <summary>
        /// Returns true when this behavior has completed its task.
        /// </summary>
        public abstract bool IsDone { get; }

        /// <summary>
        /// Called when this behavior starts executing.
        /// </summary>
        public virtual void OnStart()
        {
        }

        /// <summary>
        /// Called each tick while this behavior is active.
        /// </summary>
        public virtual void OnTick()
        {
        }

        /// <summary>
        /// Disposes resources used by this behavior.
        /// </summary>
        public virtual void Dispose()
        {
        }
    }
}
