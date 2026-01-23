using Styx.Logic;
using Styx.Logic.Pathing;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionMoveToPoint : NavigationAction
	{
		public WoWPoint Point;

		public ActionMoveToPoint(WoWPoint point)
		{
			Point = point;
		}

		protected override RunStatus Run(object context)
		{
			Mount.StateMount(() => Point);
			return base.Run(Point);
		}
	}
}
