using Bots.Vibes.Shared;
using Bots.Vibes.VibeQuester2.Planning;
using Styx;
using Styx.Helpers;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.Vibes.VibeQuester2.Execution
{
    public enum InteractionResult
    {
        /// <summary>The task's goal is verified by quest-log state.</summary>
        Success,
        /// <summary>Transient (no entity loaded, frame didn't open, log lagging) — try again shortly.</summary>
        Retry,
        /// <summary>The server says no (quest not offered / not turn-in-able here) — strike toward abandon.</summary>
        NotOffered,
    }

    /// <summary>
    /// At-the-NPC pickup/turn-in for VibeQuester v2 — VQ2-specific policy (entry+coordinate entity
    /// resolution, planner blacklisting, chained-offer screening) over the shared frame driver in
    /// Bots.Vibes.Shared.QuestInteractionCore (one implementation for VQ2 + VibeParty). Travel is the
    /// executor's job — these run once the character stands in interact range of the task position.
    /// </summary>
    public static class QuestInteraction
    {
        private const float EntityResolveRadius = 15f;   // OM entity must sit near the DB spawn coord

        public static InteractionResult TryPickUp(QuestTask task, QuestPlan plan, QuestPlanner planner)
        {
            var me = StyxWoW.Me;
            if (me == null) return InteractionResult.Retry;
            if (me.QuestLog.ContainsQuest((uint)task.QuestId))
                return InteractionResult.Success;   // already have it (chained accept, manual play)

            WoWObject giver = ResolveEntity(task);
            if (giver == null)
            {
                Logging.WriteDebug("[VQ2-Task] pickup q{0}: giver {1} ({2}) not loaded near {3} — retry.",
                    task.QuestId, task.EntityName, task.EntityId, task.Position);
                return InteractionResult.Retry;
            }

            QuestInteractOutcome outcome = QuestInteractionCore.PickUp(giver, task.QuestId, task.QuestName, "[VQ2-Task]");
            if (outcome == QuestInteractOutcome.NoGiverFlag)
            {
                // Captive/pre-trigger NPCs (Bristlelimb younglings) never offer this until a world
                // action VQ2 can't trigger enables it — permanent, not a strike.
                Logging.Write("[VQ2-Task] pickup q{0} '{1}': {2} carries no questgiver flag — offers nothing here (blacklisting so we never walk back).",
                    task.QuestId, task.QuestName, task.EntityName);
                planner.RecordPermanentBlacklist(task.QuestId, "no-questgiver-flag", "free-from-cage",
                    "DB giver has no questgiver npcflag (captive/pre-trigger NPC — no '!' at all); it never offers this until a world action enables it, which VQ2 can't trigger.",
                    string.Format("giver {0} ({1}) IsQuestGiver=false at its spawn", task.EntityName, task.EntityId),
                    blockClass: "conditional");
                return InteractionResult.NotOffered;
            }
            return Map(outcome);
        }

        public static InteractionResult TryTurnIn(QuestTask task, QuestPlan plan, QuestPlanner planner)
        {
            var me = StyxWoW.Me;
            if (me == null) return InteractionResult.Retry;
            if (!me.QuestLog.ContainsQuest((uint)task.QuestId))
            {
                planner.NotifyTurnedIn((uint)task.QuestId);   // already gone — count it done NOW (cache window)
                return InteractionResult.Success;
            }

            WoWObject ender = ResolveEntity(task);
            if (ender == null)
            {
                Logging.WriteDebug("[VQ2-Task] turn-in q{0}: ender {1} ({2}) not loaded near {3} — retry.",
                    task.QuestId, task.EntityName, task.EntityId, task.Position);
                return InteractionResult.Retry;
            }

            QuestInteractOutcome outcome = QuestInteractionCore.TurnIn(ender, task.QuestId, task.QuestName, "[VQ2-Task]",
                chained => planner.ScreenChainedOffer((int)chained, me));
            if (outcome == QuestInteractOutcome.NoGiverFlag)
            {
                Logging.Write("[VQ2-Task] turn-in q{0} '{1}': {2} carries no questgiver flag — takes nothing here (blacklisting so we never walk back).",
                    task.QuestId, task.QuestName, task.EntityName);
                planner.RecordPermanentBlacklist(task.QuestId, "no-questgiver-flag", "free-from-cage",
                    "DB ender has no questgiver npcflag (captive/pre-trigger NPC); it never takes this until a world action enables it, which VQ2 can't trigger.",
                    string.Format("ender {0} ({1}) IsQuestGiver=false at its spawn", task.EntityName, task.EntityId),
                    blockClass: "conditional");
                return InteractionResult.NotOffered;
            }
            if (outcome == QuestInteractOutcome.Success)
                planner.NotifyTurnedIn((uint)task.QuestId);
            return Map(outcome);
        }

        private static InteractionResult Map(QuestInteractOutcome outcome)
        {
            switch (outcome)
            {
                case QuestInteractOutcome.Success: return InteractionResult.Success;
                case QuestInteractOutcome.NotOffered:
                case QuestInteractOutcome.NoGiverFlag: return InteractionResult.NotOffered;
                default: return InteractionResult.Retry;
            }
        }

        /// <summary>Entry + proximity resolution — the id-collision-safe lookup (creature/GO id
        /// spaces overlap, so never resolve by bare entry).</summary>
        private static WoWObject ResolveEntity(QuestTask task)
        {
            WoWObject best = null;
            double bestDist = double.MaxValue;
            if (task.IsGameObject)
            {
                foreach (WoWGameObject go in ObjectManager.GetObjectsOfType<WoWGameObject>(false, false))
                {
                    if (go == null || go.Entry != (uint)task.EntityId) continue;
                    double d = go.Location.Distance(task.Position);
                    if (d <= EntityResolveRadius && d < bestDist) { bestDist = d; best = go; }
                }
            }
            else
            {
                foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
                {
                    if (u == null || u.Entry != (uint)task.EntityId || u.Dead) continue;
                    double d = u.Location.Distance(task.Position);
                    // NPCs pace around their spawn — resolve generously but never cross-position.
                    if (d <= EntityResolveRadius * 3 && d < bestDist) { bestDist = d; best = u; }
                }
            }
            return best;
        }
    }
}
