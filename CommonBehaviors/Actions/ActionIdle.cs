using Styx;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionIdle : ActionAlwaysSucceed
	{
		protected override RunStatus Run(object context)
		{
			StyxWoW.ResetAfk();
			return base.Run(context);
		}
	}
}
