using Styx;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class SequenceOpenGossip : TreeSharp.Action
	{
		private WoWObject? _targetObject;

		protected override RunStatus Run(object context)
		{
			if (context is WoWUnit || context is WoWGameObject)
			{
				_targetObject = (WoWObject)context;

				if (GossipFrame.Instance.IsVisible || QuestFrame.Instance.IsVisible)
				{
					return RunStatus.Success;
				}

				if (_targetObject.Type != WoWObjectType.Unit && _targetObject.Type != WoWObjectType.GameObject)
				{
					if (_targetObject.Type == WoWObjectType.Item)
					{
						WoWItem item = _targetObject.ToItem();
						if (ObjectManager.Me.QuestLog.ContainsQuest((uint)item.ItemInfo.InternalInfo.BeginQuestId))
						{
							return RunStatus.Success;
						}
					}
				}
				else
				{
					if (_targetObject.DistanceSqr >= 25.0)
					{
						return RunStatus.Failure;
					}

					if (_targetObject.Type == WoWObjectType.Unit)
					{
						WoWUnit unit = _targetObject.ToUnit();
						if (unit != null && ObjectManager.Me.CurrentTarget != unit)
						{
							unit.Target();
						}
					}
				}

				_targetObject.Interact();
				return RunStatus.Running;
			}

			return RunStatus.Failure;
		}
	}
}
