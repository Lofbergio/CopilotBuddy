using System;
using TreeSharp;

namespace Styx.Combat.CombatRoutine
{
	public interface IBehaviors
	{
		Composite RestBehavior { get; }

		Composite PreCombatBuffBehavior { get; }

		Composite PullBuffBehavior { get; }

		Composite PullBehavior { get; }

		Composite CombatBuffBehavior { get; }

		Composite CombatBehavior { get; }

		Composite HealBehavior { get; }

		Composite MoveToTargetBehavior { get; }
	}
}
