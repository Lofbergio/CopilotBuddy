using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionAlwaysSucceed : TreeSharp.Action
	{
		protected override RunStatus Run(object context)
		{
			return RunStatus.Success;
		}
	}
}
