// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedQuestTurnIn
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Bots.Quest.Actions;
using CommonBehaviors.Actions;
using CommonBehaviors.Decorators;
using Styx;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Inventory.Frames;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Threading;
using TreeSharp;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedQuestTurnIn : ForcedBehavior
{
    private readonly Frame frame_0 = new Frame("QuestTitleButton1");
    private static readonly Frame frame_1 = new Frame("QuestFrameCompleteButton");
    private int int_0;

    public ForcedQuestTurnIn(uint questId, string questName, uint npcId, WoWPoint location)
    {
        this.QuestId = questId;
        this.QuestName = questName;
        this.NpcId = npcId;
        this.Location = location;
    }

    public override bool IsDone => !ObjectManager.Me.QuestLog.ContainsQuest(this.QuestId);

    public uint QuestId { get; private set; }

    public string QuestName { get; private set; }

    public uint NpcId { get; private set; }

    public WoWPoint Location { get; private set; }

    public override void Dispose() => StyxWoW.Me.QuestLog.AbandonQuestById(this.QuestId);

    public override void OnStart() => TreeRoot.GoalText = this.method_0();

    private string method_0()
    {
        Styx.Logic.Questing.Quest quest = Styx.Logic.Questing.Quest.FromId(this.QuestId);
        if (quest != null)
            return string.Format("Turning in {0}", (object)quest.Name);
        return !string.IsNullOrEmpty(this.QuestName) ? string.Format("Turning in {0}", (object)this.QuestName) : string.Format("Turning in quest with ID {0}", (object)this.QuestId);
    }

    protected override Composite CreateBehavior()
    {
        return (Composite)new DecoratorIsNotPoiType((IEnumerable<PoiType>)new PoiType[3]
        {
            PoiType.Harvest,
            PoiType.Skin,
            PoiType.Loot
        }, (Composite)new PrioritySelector((ContextChangeHandler)(object_0 => (object)null), new Composite[3]
        {
            (Composite)new Decorator(new CanRunDecoratorDelegate(this.method_1), (Composite)new ActionSetPoi(true, (RetrieveBotPoiDelegate)(object_0 => new BotPoi(PoiType.QuestTurnIn)
            {
                Entry = this.NpcId,
                Location = this.Location
            }))),
            (Composite)new Decorator((CanRunDecoratorDelegate)(object_0 => !(BotPoi.Current.AsObject != (WoWObject)null) ? (double)ForcedQuestTurnIn.Me.Location.DistanceSqr(BotPoi.Current.Location) > 16.0 : !BotPoi.Current.AsObject.WithinInteractRange), (Composite)new ActionMoveToPoi()),
            (Composite)new Decorator((CanRunDecoratorDelegate)(object_0 => BotPoi.Current.AsObject != (WoWObject)null && BotPoi.Current.AsObject.WithinInteractRange), (Composite)new Sequence((ContextChangeHandler)(object_0 => (object)BotPoi.Current.AsObject), new Composite[9]
            {
                (Composite)new ActionMoveStop(),
                (Composite)new TreeSharp.Action((ActionDelegate)(object_0 => this.method_2(object_0))),
                (Composite)new TreeSharp.Action((ActionSucceedDelegate)(object_0 => this.method_3(object_0))),
                (Composite)new TreeSharp.Action((ActionDelegate)(object_0 => this.method_4(object_0))),
                (Composite)new ActionSleep(250),
                (Composite)new DecoratorContinue(new CanRunDecoratorDelegate(this.method_5), (Composite)new Sequence(new Composite[2]
                {
                    (Composite)new ActionSelectQuest((int)this.QuestId),
                    (Composite)new ActionSleep(500)
                })),
                (Composite)new DecoratorContinue(new CanRunDecoratorDelegate(this.method_6), (Composite)new Sequence(new Composite[4]
                {
                    (Composite)new DecoratorContinue(new CanRunDecoratorDelegate(this.method_7), (Composite)new TreeSharp.Action((ActionDelegate)(object_0 => this.method_8(object_0)))),
                    (Composite)new DecoratorContinue(new CanRunDecoratorDelegate(this.method_9), (Composite)new Sequence(new Composite[3]
                    {
                        (Composite)new ActionSleep(750),
                        (Composite)new ActionSelectReward(),
                        (Composite)new ActionSleep(350)
                    })),
                    (Composite)new TreeSharp.Action((ActionDelegate)(object_0 => this.method_10(object_0))),
                    (Composite)new TreeSharp.Action((ActionDelegate)(object_0 => this.method_2(object_0)))
                })),
                (Composite)new TreeSharp.Action((ActionDelegate)(object_0 => ForcedQuestTurnIn.smethod_0(object_0))),
                (Composite)new ActionClearPoi("Quest Completed #2")
            }))
        }));
    }

    private bool method_1(object object_0)
    {
        BotPoi current = BotPoi.Current;
        return current.Type != PoiType.QuestTurnIn || (int)current.Entry != (int)this.NpcId;
    }

    private RunStatus method_2(object object_0)
    {
        if (!GossipFrame.Instance.IsVisible && !QuestFrame.Instance.IsVisible)
            return RunStatus.Success;
        GossipFrame.Instance.Close();
        QuestFrame.Instance.Close();
        Thread.Sleep(300);
        return RunStatus.Running;
    }

    private void method_3(object object_0)
    {
        WoWUnit woWunit = object_0 as WoWUnit;
        if (!((WoWObject)woWunit != (WoWObject)null))
            return;
        woWunit.Target();
    }

    private RunStatus method_4(object object_0)
    {
        if (GossipFrame.Instance.IsVisible || QuestFrame.Instance.IsVisible)
            return RunStatus.Success;
        WoWObject woWobject = (WoWObject)object_0;
        if (!woWobject.WithinInteractRange)
            return RunStatus.Failure;
        woWobject.Interact();
        Thread.Sleep(300);
        return RunStatus.Running;
    }

    private static LocalPlayer Me => ObjectManager.Me;

    private bool method_5(object object_0)
    {
        return GossipFrame.Instance.IsVisible || this.frame_0.IsVisible;
    }

    private bool method_6(object object_0) => QuestFrame.Instance.IsVisible;

    private bool method_7(object object_0) => ForcedQuestTurnIn.frame_1.IsVisible;

    private RunStatus method_8(object object_0)
    {
        QuestFrame.Instance.ClickContinue();
        Thread.Sleep(1000);
        return RunStatus.Success;
    }

    private bool method_9(object object_0)
    {
        Styx.Logic.Questing.Quest quest = Styx.Logic.Questing.Quest.FromId(QuestFrame.Instance.CurrentShownQuestId);
        if (quest == null)
            return false;
        for (uint index = 0; (long)index < (long)quest.InternalInfo.RewardChoiceItem.Length; ++index)
        {
            if (quest.InternalInfo.RewardChoiceItem[(IntPtr)index] != 0)
                return true;
        }
        return Lua.GetReturnVal<int>("return GetNumQuestChoices()", 0U) >= 1;
    }

    private RunStatus method_10(object object_0)
    {
        if (QuestFrame.Instance.IsVisible && (int)QuestFrame.Instance.CurrentShownQuestId == (int)this.QuestId)
        {
            if (this.int_0++ == 5)
            {
                Logging.Write("Tried to complete quest 5 times with no success.");
                Logging.Write("Closing QuestFrame.");
                QuestFrame.Instance.Close();
                this.int_0 = 0;
                return RunStatus.Failure;
            }
            QuestFrame.Instance.CompleteQuest();
            Thread.Sleep(500);
            return RunStatus.Running;
        }
        this.int_0 = 0;
        return RunStatus.Success;
    }

    private static RunStatus smethod_0(object object_0)
    {
        if (!ForcedQuestTurnIn.Me.GotTarget)
            return RunStatus.Success;
        ForcedQuestTurnIn.Me.ClearTarget();
        Thread.Sleep(300);
        return RunStatus.Running;
    }

    public override string ToString()
    {
        return string.Format("[ForcedQuestTurnIn QuestId: {0}, QuestName: {1}]", (object)this.QuestId, (object)this.QuestName);
    }
}
