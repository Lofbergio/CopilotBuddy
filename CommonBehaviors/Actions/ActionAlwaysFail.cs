using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionAlwaysFail : TreeSharp.Action
	{
		protected override RunStatus Run(object context)
		{
			return RunStatus.Failure;
		}
	}
}
