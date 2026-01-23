using Styx.WoWInternals;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionMoveStop : TreeSharp.Action
	{
		protected override RunStatus Run(object context)
		{
			WoWMovement.MoveStop();
			return RunStatus.Success;
		}
	}
}
