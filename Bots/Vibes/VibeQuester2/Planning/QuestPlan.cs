using System.Collections.Generic;
using Styx;
using Styx.Logic.Pathing;
using VibeQuester;

namespace Bots.Vibes.VibeQuester2.Planning
{
    public enum QuestTaskKind
    {
        PickUp,
        DoObjective,
        TurnIn,
    }

    /// <summary>One executable unit of quest work. The executor runs the plan head-to-tail;
    /// replans rebuild the whole list (tasks are cheap, state lives in the quest log).</summary>
    public class QuestTask
    {
        public QuestTaskKind Kind;
        public int QuestId;
        public string QuestName;

        // PickUp/TurnIn: the NPC/GO to interact with — resolved by TYPE + COORDS, never bare entry
        // (creature and GO id spaces overlap — the 3076 Bloodhoof collision).
        public int EntityId;
        public string EntityName;
        public bool IsGameObject;
        public WoWPoint Position;

        // DoObjective payload (Kind == DoObjective).
        public QuestObjective Objective;
        public int ObjectiveIndex;

        public override string ToString() =>
            Kind == QuestTaskKind.DoObjective
                ? string.Format("[{0} q{1} obj{2} {3}]", Kind, QuestId, ObjectiveIndex, Objective?.Type)
                : string.Format("[{0} q{1} @{2} ({3})]", Kind, QuestId, EntityName, EntityId);
    }

    public class QuestPlan
    {
        public readonly List<QuestTask> Tasks = new List<QuestTask>();

        /// <summary>Arbiter signal: quests that are eligible ∧ safe ∧ in range this scan.</summary>
        public int DoableSupply;

        /// <summary>Nearest giver of a quest failing ONLY the level gate — grind toward tomorrow's quests.</summary>
        public WoWPoint NextFutureHub = WoWPoint.Empty;
        public bool HasFutureHub => NextFutureHub != WoWPoint.Empty;

        /// <summary>Quest id set — reload/replan only when this changes (the churn rule).</summary>
        public readonly HashSet<int> QuestIds = new HashSet<int>();
    }
}
