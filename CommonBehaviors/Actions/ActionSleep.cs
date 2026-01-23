using System.Threading;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionSleep : TreeSharp.Action
	{
		public int SleepTime;

		public ActionSleep(int ms)
		{
			SleepTime = ms;
		}

		protected override RunStatus Run(object context)
		{
			if (SleepTime > 0)
			{
				Thread.Sleep(SleepTime);
			}

			return RunStatus.Success;
		}
	}
}
