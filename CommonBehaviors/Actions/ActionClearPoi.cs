using Styx.Logic.POI;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionClearPoi : TreeSharp.Action
	{
		private readonly string _reason;

		public ActionClearPoi(string reason)
		{
			_reason = reason;
		}

		public ActionClearPoi()
			: this(string.Empty)
		{
		}

		protected override RunStatus Run(object context)
		{
			BotPoi.Clear(_reason);
			return RunStatus.Success;
		}
	}
}
