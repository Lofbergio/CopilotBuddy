using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionMoveToPoi : NavigationAction
	{
		protected override RunStatus Run(object context)
		{
			if (Mount.ShouldMount(BotPoi.Current.Location))
			{
				Mount.StateMount(() => BotPoi.Current.Location);
			}

			if (BotPoi.Current.Location == WoWPoint.Zero)
			{
				Logging.Write("ActionMoveToPoi: I don't want to move to (0,0,0)");
				return RunStatus.Failure;
			}

			Logging.Write("ActionMoveToPoi: Moving to {0}", BotPoi.Current);
			return base.Run(BotPoi.Current.Location);
		}
	}
}
