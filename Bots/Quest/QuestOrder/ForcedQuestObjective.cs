// ForcedQuestObjective.cs
// Ported from HB 4.3.4

using Bots.Quest.Objectives;
using Styx;
using Styx.Logic.BehaviorTree;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;
using System;
using TreeSharp;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedQuestObjective : ForcedBehavior
{
    public ForcedQuestObjective(QuestObjective objective)
    {
        this.Objective = !(objective == (QuestObjective)null) ? objective : throw new ArgumentNullException(nameof(objective));
    }

    ~ForcedQuestObjective()
    {
        if (!(this.Objective != (QuestObjective)null))
            return;
        this.Objective.Dispose();
        this.Objective = (QuestObjective)null;
    }

    public QuestObjective Objective { get; private set; }

    protected override Composite CreateBehavior() => this.Objective.CreateBranch();

    public override void Dispose()
    {
        if (!(this.Objective != (QuestObjective)null))
            return;
        this.Objective.Dispose();
        this.Objective = (QuestObjective)null;
    }

    public override bool IsDone => this.Objective.IsCompleted;

    public override void OnStart()
    {
        TreeRoot.GoalText = ForcedQuestObjective.GetGoalText(this.Objective);
    }

    public override string ToString()
    {
        return string.Format("[ForcedQuestObjective Objective: {0}]", (object)this.Objective);
    }

    private static string GetGoalText(QuestObjective objective)
    {
        switch (objective)
        {
            case CollectItemObjective _:
                CollectItemObjective collectItemObjective = (CollectItemObjective)objective;
                Styx.WoWInternals.WoWObjects.ItemInfo itemInfo = Styx.WoWInternals.WoWObjects.ItemInfo.FromId((uint)collectItemObjective.Objective.ID);
                return itemInfo != null ? string.Format("Goal: Collect {0} x {1}", (object)itemInfo.Name, (object)collectItemObjective.Objective.Count) : string.Format("Goal: Collect {0} of item with ID {1}", (object)collectItemObjective.Objective.Count, (object)collectItemObjective.Objective.ID);
            case GrindObjective _:
                GrindObjective grindObjective = (GrindObjective)objective;
                WoWCache.InfoBlock infoBlockById1 = StyxWoW.Cache[CacheDb.Creature].GetInfoBlockById((uint)grindObjective.Objective.ID);
                if (infoBlockById1 == null)
                    return string.Format("Goal: Kill mob with ID {0} {1} times", (object)grindObjective.Objective.ID, (object)grindObjective.Objective.Count);
                return string.Format("Goal: Kill {0} x {1}", (object)ObjectManager.Wow.Read<string>(infoBlockById1.Creature.NamePtrs[0]), (object)grindObjective.Objective.Count);
            case UseGameObjectObjective _:
                UseGameObjectObjective gameObjectObjective = (UseGameObjectObjective)objective;
                WoWCache.InfoBlock infoBlockById2 = StyxWoW.Cache[CacheDb.GameObject].GetInfoBlockById((uint)gameObjectObjective.Objective.ID);
                if (infoBlockById2 == null)
                    return string.Format("Goal: Use object with ID {0} {1} times", (object)gameObjectObjective.Objective.ID, (object)gameObjectObjective.Objective.Count);
                return string.Format("Goal: Use {0} x {1}", (object)ObjectManager.Wow.Read<string>(infoBlockById2.GameObject.NamePtrs[0]), (object)gameObjectObjective.Objective.Count);
            default:
                return "";
        }
    }
}
