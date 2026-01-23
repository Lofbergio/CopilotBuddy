// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedUseItem
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Styx;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using TreeSharp;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedUseItem : ForcedBehavior
{
    private bool bool_0;
    private Composite composite_1;

    public ForcedUseItem(
        Func<WoWItem> itemRetriever,
        Func<WoWObject> targetRetriever,
        bool forceUse,
        uint questId,
        WoWPoint location)
    {
        this.ItemRetriever = itemRetriever != null ? itemRetriever : throw new ArgumentNullException(nameof(itemRetriever));
        if (targetRetriever == null)
            targetRetriever = (Func<WoWObject>)(() => (WoWObject)null);
        this.TargetRetriever = targetRetriever;
        this.ForceUse = forceUse;
        this.QuestId = questId;
        this.Location = location;
    }

    public Func<WoWItem> ItemRetriever { get; private set; }

    public Func<WoWObject> TargetRetriever { get; private set; }

    public bool ForceUse { get; private set; }

    public uint QuestId { get; private set; }

    public WoWPoint Location { get; private set; }

    protected override Composite CreateBehavior()
    {
        Composite behavior = this.composite_1;
        if ((object)behavior == null)
            behavior = this.composite_1 = (Composite)new PrioritySelector((ContextChangeHandler)(object_0 => (object)this.ItemRetriever()), new Composite[2]
            {
                (Composite)new Decorator((CanRunDecoratorDelegate)(object_0 => (double)StyxWoW.Me.Location.Distance(this.Location) > (double)Navigator.PathPrecision), (Composite)new TreeSharp.Action((ActionSucceedDelegate)(object_0 =>
                {
                    Mount.StateMount((LocationRetriever)(() => this.Location));
                    int num = (int)Navigator.MoveTo(this.Location);
                }))),
                (Composite)new Decorator((CanRunDecoratorDelegate)(object_0 => object_0 != null), (Composite)new Sequence(new Composite[2]
                {
                    (Composite)new DecoratorContinue((CanRunDecoratorDelegate)(object_0 => StyxWoW.Me.IsMoving), (Composite)new TreeSharp.Action((ActionSucceedDelegate)(object_0 => WoWMovement.MoveStop()))),
                    (Composite)new WaitContinue(3, (CanRunDecoratorDelegate)(object_0 => !StyxWoW.Me.IsMoving), (Composite)new Sequence(new Composite[3]
                    {
                        (Composite)new DecoratorContinue((CanRunDecoratorDelegate)(object_0 => StyxWoW.Me.Mounted), (Composite)new TreeSharp.Action((ActionSucceedDelegate)(object_0 =>
                        {
                            Mount.Dismount("UseItem");
                            StyxWoW.SleepForLagDuration();
                        }))),
                        (Composite)new TreeSharp.Action((ActionSucceedDelegate)(object_0 =>
                        {
                            WoWItem woWitem = (WoWItem)object_0;
                            WoWObject woWobject = this.TargetRetriever();
                            woWitem.Use(woWobject != (WoWObject)null ? woWobject.Guid : 0UL, this.ForceUse);
                            StyxWoW.SleepForLagDuration();
                        })),
                        (Composite)new WaitContinue(30, (CanRunDecoratorDelegate)(object_0 => !StyxWoW.Me.IsCasting), (Composite)new TreeSharp.Action((ActionSucceedDelegate)(object_0 => this.bool_0 = true)))
                    }))
                }))
            });
        return behavior;
    }

    public override bool IsDone
    {
        get
        {
            PlayerQuest questById = StyxWoW.Me.QuestLog.GetQuestById(this.QuestId);
            if (this.bool_0)
                return true;
            return questById != null && questById.IsCompleted;
        }
    }

    public override void OnStart()
    {
        PlayerQuest questById = StyxWoW.Me.QuestLog.GetQuestById(this.QuestId);
        if (questById != null)
            TreeRoot.GoalText = string.Format("Using item for {0}", (object)questById.Name);
        else
            this.bool_0 = true;
    }

    public override string ToString()
    {
        return string.Format("[ForcedUseItem ForceUse: {0}]", (object)this.ForceUse);
    }
}
