using System;
using System.Collections.Generic;

namespace TreeSharp
{
    /// <summary>
    /// DecoratorContinue - Always continues after child execution
    /// </summary>
    public class DecoratorContinue : Decorator
    {
        public DecoratorContinue(Composite decorated, CanRunDecoratorDelegate func) : base(decorated, func) { }
        public DecoratorContinue(CanRunDecoratorDelegate func, Composite decorated) : base(func, decorated) { }
        public DecoratorContinue(Composite child) : base(child) { }
        public DecoratorContinue() { }

        protected override IEnumerable<RunStatus> Execute(object context)
        {
            if (!this.CanRun(context))
            {
                yield return RunStatus.Success;
            }
            else
            {
                this.DecoratedChild.Start(context);
                while (this.DecoratedChild.Tick(context) == RunStatus.Running)
                    yield return RunStatus.Running;
                this.DecoratedChild.Stop(context);
                RunStatus? lastStatus = this.DecoratedChild.LastStatus;
                if ((lastStatus.GetValueOrDefault() != RunStatus.Failure ? 0 : (lastStatus.HasValue ? 1 : 0)) != 0)
                    yield return RunStatus.Failure;
                else
                    yield return RunStatus.Success;
            }
        }
    }
}
