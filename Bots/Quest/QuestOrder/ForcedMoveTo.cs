// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedMoveTo
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using TreeSharp;
using Action = TreeSharp.Action;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedMoveTo : ForcedBehavior
{
    private bool bool_0;

    public ForcedMoveTo(WoWPoint location, uint questId)
        : this(location, (string)null, 1.5f, questId)
    {
    }

    public ForcedMoveTo(WoWPoint location, string locationName, float precision, uint questId)
    {
        this.Location = location;
        this.LocationName = locationName ?? $"<{location.X.ToStringInvariant()}, {location.Y.ToStringInvariant()}, {location.Z.ToStringInvariant()}>";
        this.Precision = precision;
        this.QuestId = questId;
    }

    public WoWPoint Location { get; private set; }

    public string LocationName { get; private set; }

    public float Precision { get; private set; }

    public uint QuestId { get; private set; }

    protected override Composite CreateBehavior()
    {
        return (Composite)new Action((ActionSucceedDelegate)(object_0 =>
        {
            if ((double)ObjectManager.Me.Location.DistanceSqr(this.Location) <= (double)this.Precision * (double)this.Precision)
                this.bool_0 = true;
            if (Mount.ShouldMount(this.Location))
                Mount.StateMount((LocationRetriever)(() => this.Location));
            int num = (int)Navigator.MoveTo(this.Location);
        }));
    }

    public override bool IsDone
    {
        get
        {
            if (this.QuestId == 0U)
                return this.bool_0;
            PlayerQuest questById = StyxWoW.Me.QuestLog.GetQuestById(this.QuestId);
            if (this.bool_0)
                return true;
            return questById != null && questById.IsCompleted;
        }
    }

    public override void OnStart()
    {
        TreeRoot.GoalText = string.Format("Moving to {0}", (object)this.LocationName);
    }
}
