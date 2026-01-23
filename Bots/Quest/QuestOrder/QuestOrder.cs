// QuestOrder.cs - Quest order management system
// Ported from HB 4.3.4

using System;
using System.Collections.Generic;
using Styx;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;

namespace Bots.Quest.QuestOrder
{
    /// <summary>
    /// Manages the quest order - the list of ordered nodes to execute.
    /// </summary>
    public class QuestOrder
    {
        public QuestOrder()
        {
            Nodes = new OrderNodeCollection();
        }

        public QuestOrder(OrderNodeCollection order)
        {
            Nodes = order;
        }

        /// <summary>
        /// Event fired when there are no more nodes to process.
        /// </summary>
        public event EventHandler<EventArgs> OnNoMoreNodes;

        /// <summary>
        /// The collection of order nodes to execute.
        /// </summary>
        public OrderNodeCollection Nodes { get; set; }

        /// <summary>
        /// Whether to ignore checkpoints in the quest order.
        /// </summary>
        public bool IgnoreCheckpoints
        {
            get => Nodes?.IgnoreCheckpoints ?? false;
            set
            {
                if (Nodes != null)
                    Nodes.IgnoreCheckpoints = value;
            }
        }

        /// <summary>
        /// The currently executing forced behavior.
        /// </summary>
        public ForcedBehavior CurrentBehavior { get; set; }

        /// <summary>
        /// The current node being processed.
        /// </summary>
        public OrderNode CurrentNode
        {
            get => Nodes != null && Nodes.Count > 0 ? Nodes[0] : null;
        }

        /// <summary>
        /// Advance to the next node.
        /// </summary>
        public void Advance() => Advance(1);

        /// <summary>
        /// Advance by a number of nodes.
        /// </summary>
        public void Advance(int times)
        {
            if (Nodes == null || Nodes.Count <= 0 || times <= 0)
                return;

            Nodes.RemoveRange(0, Math.Min(Nodes.Count, times));

            if (Nodes.Count <= 0)
                OnNoMoreNodes?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Updates nodes based on current quest state.
        /// </summary>
        public void UpdateNodes()
        {
            if (Nodes == null)
                return;

            if (!IgnoreCheckpoints)
                SkipToCheckpoint(ObjectManager.Me?.LevelFraction ?? 0);

            var completedQuestsList = StyxWoW.Me?.QuestLog?.GetCompletedQuests();
            var completedQuests = completedQuestsList != null 
                ? new HashSet<uint>(completedQuestsList) 
                : new HashSet<uint>();
            RemoveCompletedNodes(completedQuests);
        }

        /// <summary>
        /// Skips to the checkpoint matching the player's level.
        /// </summary>
        public void SkipToCheckpoint(float level)
        {
            if (Nodes == null)
                return;

            int skipCount = 0;
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i] is CheckpointNode checkpoint)
                {
                    if (checkpoint.Level <= level)
                        skipCount = i + 1;
                    else
                        break;
                }
            }
            Advance(skipCount);
        }

        /// <summary>
        /// Removes nodes for quests that are already completed.
        /// </summary>
        public void RemoveCompletedNodes(HashSet<uint> completedQuests)
        {
            for (int i = Nodes.Count - 1; i >= 0; i--)
            {
                if (IsNodeCompleted(Nodes[i], completedQuests))
                    Nodes.RemoveAt(i);
            }
        }

        private bool IsNodeCompleted(OrderNode node, HashSet<uint> completedQuests)
        {
            switch (node.Type)
            {
                case OrderNodeType.PickUp:
                    return completedQuests.Contains(((PickUpNode)node).QuestId);

                case OrderNodeType.TurnIn:
                    return completedQuests.Contains(((TurnInNode)node).QuestId);

                case OrderNodeType.Objective:
                    return completedQuests.Contains(((ObjectiveNode)node).QuestId);

                case OrderNodeType.AbandonQuest:
                    return completedQuests.Contains(((AbandonQuestNode)node).QuestId);

                case OrderNodeType.MoveTo:
                    var moveNode = (MoveToNode)node;
                    return moveNode.QuestId != 0 && completedQuests.Contains(moveNode.QuestId);

                case OrderNodeType.UseItem:
                    return completedQuests.Contains(((UseItemNode)node).QuestId);

                default:
                    return false;
            }
        }
    }
}
