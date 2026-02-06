// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedQuestPickUp
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using CommonBehaviors.Actions;
using CommonBehaviors.Decorators;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Inventory.Frames;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using TreeSharp;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedQuestPickUp : ForcedBehavior
{
    private static readonly Frame QuestTitleButton = new Frame("QuestTitleButton1");
    private static readonly Frame QuestFrameCompleteQuestButton = new Frame("QuestFrameCompleteQuestButton");
    private int lastShownQuestId = -1;

    public ForcedQuestPickUp(
        uint questId,
        string questName,
        uint giverId,
        string giverName,
        WoWPoint giverLocation,
        QuestObjectType? giverType)
    {
        this.QuestId = questId;
        this.QuestName = questName;
        this.GiverId = giverId;
        this.GiverName = giverName;
        this.GiverLocation = giverLocation;
        this.GiverType = giverType;
    }

    public override bool IsDone
    {
        get
        {
            // PickUp is done when:
            // 1) Quest is in completed quests cache (already turned in)
            // 2) Quest is in the quest log (just accepted, ready for objectives)
            try
            {
                // If quest is in completed cache, skip pickup
                if (ObjectManager.Me.QuestLog.GetCompletedQuests().Contains(this.QuestId))
                    return true;

                // If quest is in log, the PickUp behavior is done
                // This is the key: we just need the quest IN the log, not completed
                if (ObjectManager.Me.QuestLog.ContainsQuest(this.QuestId))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public uint QuestId { get; private set; }

    public string QuestName { get; private set; }

    public uint GiverId { get; private set; }

    public string GiverName { get; private set; }

    public WoWPoint GiverLocation { get; private set; }

    public QuestObjectType? GiverType { get; private set; }

    public override void OnStart()
    {
        if (ObjectManager.Me.QuestLog.GetAllQuests().Count >= 25)
        {
            Logging.Write(Color.Red, "You do not have any space in your quest log.");
            Logging.Write(Color.Red, "CopilotBuddy stopped!");
            TreeRoot.Stop();
        }
        TreeRoot.GoalText = this.GetGoalText();
    }

    private string GetGoalText()
    {
        Styx.Logic.Questing.Quest quest = Styx.Logic.Questing.Quest.FromId(this.QuestId);
        QuestObjectType? giverType = this.GiverType;
        if ((giverType.GetValueOrDefault() != QuestObjectType.Item ? 0 : (giverType.HasValue ? 1 : 0)) != 0)
        {
            WoWItem woWitem = ObjectManager.Me.CarriedItems.FirstOrDefault<WoWItem>((Func<WoWItem, bool>)(woWItem_0 => (int)woWItem_0.Entry == (int)this.GiverId));
            if ((WoWObject)woWitem != (WoWObject)null && !string.IsNullOrEmpty(this.QuestName))
                return string.Format("Picking up quest {0} from item {1}", (object)this.QuestName, (object)woWitem.Name);
        }
        if (quest != null)
        {
            string str = string.Format("Picking up {0}", (object)quest.Name);
            Logging.WriteDebug("{0} : {1}", (object)str, (object)this.QuestId);
            return str;
        }
        if (string.IsNullOrEmpty(this.QuestName))
            return string.Format("Picking up quest with ID {0}", (object)this.QuestId);
        string questInfo = string.Format("Picking up {0}", (object)this.QuestName);
        Logging.WriteDebug("{0} : {1}", (object)questInfo, (object)this.QuestId);
        return questInfo;
    }

    private static LocalPlayer Me => ObjectManager.Me;

    protected override Composite CreateBehavior()
    {
        return (Composite)new DecoratorIsNotPoiType((IEnumerable<PoiType>)new PoiType[3]
        {
            PoiType.Harvest,
            PoiType.Skin,
            PoiType.Loot
        }, (Composite)new PrioritySelector(new Composite[4]
        {
            (Composite)new Decorator(new CanRunDecoratorDelegate(this.ShouldSetPoi), (Composite)new ActionSetPoi(true, (RetrieveBotPoiDelegate)(context => new BotPoi(new PickUpNode(this.GiverLocation, this.GiverId, this.GiverName, this.GiverType, this.QuestId, this.QuestName))))),
            (Composite)new Decorator((CanRunDecoratorDelegate)(context =>
            {
                QuestObjectType? giverType = this.GiverType;
                return giverType.GetValueOrDefault() == QuestObjectType.Item && giverType.HasValue;
            }), (Composite)new Sequence((ContextChangeHandler)(context => (object)ObjectManager.GetObjectsOfType<WoWItem>().FirstOrDefault<WoWItem>((Func<WoWItem, bool>)(woWItem_0 => (int)woWItem_0.Entry == (int)this.GiverId))), new Composite[1]
            {
                (Composite)new DecoratorContinue((CanRunDecoratorDelegate)(context => context != null && context is WoWItem), (Composite)new Sequence(new Composite[3]
                {
                    (Composite)new TreeSharp.Action((ActionSucceedDelegate)(context => ((WoWItem)context).UseContainerItem())),
                    (Composite)new WaitContinue(5, new CanRunDecoratorDelegate(this.IsQuestFrameVisible), (Composite)new TreeSharp.Action((ActionSucceedDelegate)(context => QuestFrame.Instance.AcceptQuest()))),
                    (Composite)new WaitContinue(2, (CanRunDecoratorDelegate)(context => false), (Composite)new ActionAlwaysSucceed())
                }))
            })),
            (Composite)new Decorator((CanRunDecoratorDelegate)(context => !(BotPoi.Current.AsObject != (WoWObject)null) ? (double)ForcedQuestPickUp.Me.Location.DistanceSqr(BotPoi.Current.Location) > 6.25 : !BotPoi.Current.AsObject.WithinInteractRange), (Composite)new ActionMoveToPoi()),
            (Composite)new Decorator((CanRunDecoratorDelegate)(context => BotPoi.Current.AsObject != (WoWObject)null && BotPoi.Current.AsObject.WithinInteractRange), (Composite)new Sequence((ContextChangeHandler)(context => (object)BotPoi.Current.AsObject), new Composite[10]
            {
                // HB 4.3.4: 10 elements in sequence
                (Composite)new ActionMoveStop(),
                (Composite)new TreeSharp.Action((ActionDelegate)(context => this.CloseFrames(context))),
                (Composite)new DecoratorContinue((CanRunDecoratorDelegate)(context => context is WoWUnit), (Composite)new TreeSharp.Action((ActionSucceedDelegate)(context => ((WoWUnit)context).Target()))),
                (Composite)new TreeSharp.Action((ActionSucceedDelegate)(context => ((WoWObject)context).Interact())),
                (Composite)new ActionSleep(1500),
                (Composite)new DecoratorContinue(new CanRunDecoratorDelegate(this.IsGossipOrQuestListVisible), (Composite)new Sequence(new Composite[2]
                {
                    (Composite)new TreeSharp.Action((ActionDelegate)(context => this.SelectAvailableQuest(context))),
                    (Composite)new ActionSleep(500)
                })),
                (Composite)new DecoratorContinue(new CanRunDecoratorDelegate(this.IsQuestFrameVisible), (Composite)new Sequence(new Composite[3]
                {
                    (Composite)new DecoratorContinue(new CanRunDecoratorDelegate(this.IsCompleteQuestButtonVisible), (Composite)new Sequence(new Composite[2]
                    {
                        (Composite)new TreeSharp.Action((ActionDelegate)(context => this.CompleteQuestBeforeAccept(context))),
                        (Composite)new TreeSharp.Action((ActionDelegate)(context => this.CloseFrames(context)))
                    })),
                    (Composite)new TreeSharp.Action((ActionSucceedDelegate)(context => QuestFrame.Instance.AcceptQuest())),
                    (Composite)new ActionSleep(500)
                })),
                (Composite)new TreeSharp.Action((ActionDelegate)(context => this.ClearTarget(context))),
                (Composite)new TreeSharp.Action((ActionDelegate)(context => this.CloseFrames(context))),
                (Composite)new ActionClearPoi("Quest Completed")
            }))
        }));
    }

    private bool ShouldSetPoi(object context)
    {
        BotPoi current = BotPoi.Current;
        return current.Type != PoiType.QuestPickUp || (int)current.Entry != (int)this.GiverId;
    }

    private RunStatus CloseFrames(object context)
    {
        if (!GossipFrame.Instance.IsVisible && !QuestFrame.Instance.IsVisible)
            return RunStatus.Success;
        GossipFrame.Instance.Close();
        QuestFrame.Instance.Close();
        Thread.Sleep(500);
        return RunStatus.Running;
    }

    private bool IsGossipOrQuestListVisible(object context)
    {
        return GossipFrame.Instance.IsVisible || ForcedQuestPickUp.QuestTitleButton.IsVisible;
    }

    private RunStatus SelectAvailableQuest(object context)
    {
        if (!GossipFrame.Instance.IsVisible && !ForcedQuestPickUp.QuestTitleButton.IsVisible)
            return RunStatus.Success;
        List<GossipQuestEntry> gossipAvailableQuests = GossipFrame.Instance.AvailableQuests;
        List<uint> questFrameAvailableQuests = QuestFrame.Instance.AvailableQuests;
        int questIndex = -1;
        if (gossipAvailableQuests.Count > 0)
        {
            for (int index2 = 0; index2 < gossipAvailableQuests.Count; ++index2)
            {
                if ((long)gossipAvailableQuests[index2].Id == (long)this.QuestId)
                {
                    questIndex = index2;
                    break;
                }
            }
        }
        else
        {
            if (questFrameAvailableQuests.Count <= 0)
                return RunStatus.Success;
            for (int index3 = 0; index3 < questFrameAvailableQuests.Count; ++index3)
            {
                if ((int)questFrameAvailableQuests[index3] == (int)this.QuestId)
                {
                    questIndex = index3;
                    break;
                }
            }
        }
        if (questIndex == -1)
            return RunStatus.Failure;
        GossipFrame.Instance.SelectAvailableQuest(questIndex);
        Thread.Sleep(500);
        return RunStatus.Running;
    }

    private bool IsQuestFrameVisible(object context)
    {
        return QuestFrame.Instance.IsVisible;
    }

    private bool IsCompleteQuestButtonVisible(object context)
    {
        return ForcedQuestPickUp.QuestFrameCompleteQuestButton.IsVisible;
    }

    private RunStatus CompleteQuestBeforeAccept(object context)
    {
        if (this.lastShownQuestId == -1)
            this.lastShownQuestId = (int)QuestFrame.Instance.CurrentShownQuestId;
        if (QuestFrame.Instance.IsVisible && (long)this.lastShownQuestId == (long)QuestFrame.Instance.CurrentShownQuestId)
        {
            QuestFrame.Instance.CompleteQuest();
            Thread.Sleep(500);
            return RunStatus.Running;
        }
        this.lastShownQuestId = -1;
        return RunStatus.Success;
    }

    private RunStatus ClearTarget(object context)
    {
        if (!ObjectManager.Me.GotTarget)
            return RunStatus.Success;
        ObjectManager.Me.ClearTarget();
        Thread.Sleep(300);
        return RunStatus.Running;
    }

    public override string ToString()
    {
        return string.Format("[ForcedQuestPickUp QuestId: {0}, QuestName: {1}]", (object)this.QuestId, (object)this.QuestName);
    }
}
