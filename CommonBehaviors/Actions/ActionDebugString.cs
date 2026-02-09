using Styx.Helpers;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionDebugString : TreeSharp.Action
	{
		private string? _message;
		private readonly DebugStringDelegate? _stringDelegate;

		public ActionDebugString(DebugStringDelegate stringDelegate)
		{
			_stringDelegate = stringDelegate;
		}

		public ActionDebugString(string message)
		{
			_message = message;
		}

		protected override RunStatus Run(object context)
		{
			if (_stringDelegate != null)
			{
				_message = _stringDelegate(context);
			}

			if (!string.IsNullOrEmpty(_message))
			{
				Logging.WriteDiagnostic(_message);
			}

			return Parent != null && Parent is Selector ? RunStatus.Failure : RunStatus.Success;
		}
	}
}
