using Styx.Logic.Pathing;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class NavigationAction : TreeSharp.Action
	{
		private readonly GetPointDelegate? _getPointDelegate;

		public NavigationAction()
		{
		}

		public NavigationAction(WoWPoint point)
			: this(context => point)
		{
		}

		public NavigationAction(GetPointDelegate getPointDel)
		{
			_getPointDelegate = getPointDel;
		}

		protected override RunStatus Run(object context)
		{
			WoWPoint location;

			if (_getPointDelegate == null)
			{
				if (context == null || !(context is WoWPoint))
				{
					return RunStatus.Failure;
				}
				location = (WoWPoint)context;
			}
			else
			{
				location = _getPointDelegate(context);
			}

			return Navigator.GetRunStatusFromMoveResult(Navigator.MoveTo(location));
		}
	}
}
