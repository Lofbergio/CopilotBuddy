using Styx.Helpers;
using Styx.Logic.POI;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionInteract : TreeSharp.Action
	{
		private readonly bool _ignoreInteractTimer;

		public ActionInteract()
		{
		}

		public ActionInteract(bool ignoreInteractTimer)
		{
			_ignoreInteractTimer = ignoreInteractTimer;
		}

		protected override RunStatus Run(object context)
		{
			WoWObject? targetObject = null;

			if (context is WoWObject woWObject)
			{
				targetObject = woWObject;
			}
			else if (context is BotPoi poi)
			{
				targetObject = poi.AsObject;
			}

			if (targetObject == null)
			{
				Logging.WriteDebug("ActionInteract: Trying to interact with a null object");
				return RunStatus.Failure;
			}

			Logging.WriteDebug("ActionInteract: Interacting with {0}", targetObject.Name);
			targetObject.Interact(_ignoreInteractTimer);
			return RunStatus.Success;
		}
	}
}
