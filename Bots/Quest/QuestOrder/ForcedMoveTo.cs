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
    private bool hasReachedLocation;
    private readonly NavType? _navType;

    public ForcedMoveTo(WoWPoint location, uint questId)
        : this(location, null, 1.5f, questId, null)
    {
    }

    public ForcedMoveTo(WoWPoint location, string locationName, float precision, uint questId, NavType? navType = null)
    {
        this.Location = location;
        this.LocationName = locationName ?? $"<{location.X.ToStringInvariant()}, {location.Y.ToStringInvariant()}, {location.Z.ToStringInvariant()}>";
        this.Precision = precision;
        this.QuestId = questId;
        _navType = navType;
    }

    public WoWPoint Location { get; private set; }

    public string LocationName { get; private set; }

    public float Precision { get; private set; }

    public uint QuestId { get; private set; }

    // Legion: ForcedBehavior.NavType override — null means auto-detect.
    public override NavType? NavType => _navType;

    protected override Composite CreateBehavior()
    {
        return new Action((ActionSucceedDelegate)(context =>
        {
            if (ObjectManager.Me.Location.DistanceSqr(this.Location) <= this.Precision * this.Precision)
            {
                hasReachedLocation = true;
                return;
            }

            // Resolve effective NavType: node override → QuestOrder auto-detect (Flightor.CanFly).
            // QuestOrder.NavType is non-nullable and always returns a value — no further fallback needed.
            // Legion: ForcedMoveTo.method_0 line 70 used QuestOrder.Instance.NavType directly.
            NavType effective = QuestOrder.Instance?.NavType ?? (Flightor.CanFly ? Styx.NavType.Fly : Styx.NavType.Run);

            if (effective == Styx.NavType.Fly)
            {
                if (this.Location.DistanceSqr(ObjectManager.Me.Location) < 100f)
                {
                    // Within 10y: land and finish on foot.
                    Mount.Dismount("ForcedMoveTo: reached destination");
                    hasReachedLocation = true;
                    return;
                }
                Flightor.MoveTo(this.Location, 40f);
            }
            else
            {
                if (Mount.ShouldMount(this.Location))
                    Mount.StateMount((LocationRetriever)(() => this.Location));
                Navigator.MoveTo(this.Location);
            }
        }));
    }

    public override bool IsDone
    {
        get
        {
            if (this.QuestId == 0U)
                return this.hasReachedLocation;
            PlayerQuest questById = StyxWoW.Me.QuestLog.GetQuestById(this.QuestId);
            if (this.hasReachedLocation)
                return true;
            if (questById != null)
                return questById.IsCompleted;
            return false;
        }
    }

    public override void OnStart()
    {
        string goalText = string.Format("Moving to {0}", (object)this.LocationName);
        Logging.Write("[MoveTo] {0}", (object)goalText);
        TreeRoot.GoalText = goalText;
    }
}
