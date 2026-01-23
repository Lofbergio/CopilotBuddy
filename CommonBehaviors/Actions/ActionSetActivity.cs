using Styx.Logic.BehaviorTree;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionSetActivity : TreeSharp.Action
	{
		private readonly GetStatusTextDelegate _getStatusTextDelegate;

		public ActionSetActivity(string statusText)
		{
			_getStatusTextDelegate = context => statusText;
		}

		public ActionSetActivity(string format, params object[] args)
			: this(string.Format(format, args))
		{
		}

		public ActionSetActivity(GetStatusTextDelegate getStatusText)
		{
			_getStatusTextDelegate = getStatusText;
		}

		protected override RunStatus Run(object context)
		{
			TreeRoot.StatusText = _getStatusTextDelegate(context);
			return Parent is Selector ? RunStatus.Failure : RunStatus.Success;
		}

		public delegate string GetStatusTextDelegate(object context);
	}
}
