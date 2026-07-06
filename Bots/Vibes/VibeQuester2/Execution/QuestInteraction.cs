using System;
using System.Collections.Generic;
using Bots.Vibes.VibeQuester2.Planning;
using Styx;
using Styx.Helpers;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Pathing;
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
    /// At-the-NPC pickup/turn-in primitives for VibeQuester v2. Travel is the executor's job — these
    /// run once the character stands in interact range of the task position. Design rules (from the
    /// 2026-07-06 Grull Hawkwind incident + the fluid doctrine):
    ///  - the entity is resolved by entry AND proximity to the DB coordinate, never a bare entry
    ///    (creature/GO id spaces overlap);
    ///  - frames are STATE — bounded polls, no grace-window inference; completion is verified by
    ///    quest-LOG state, never by frame flow;
    ///  - "the NPC offers no such quest" is a TYPED outcome (NotOffered) the executor strike-counts,
    ///    never a silent re-loop;
    ///  - a server-pushed chained follow-up detail frame is handled explicitly: accepted when it's in
    ///    the current plan (free pickup), declined otherwise.
    /// </summary>
    public static class QuestInteraction
    {
        private const int FrameOpenTimeoutMs = 3500;
        private const int LogUpdateTimeoutMs = 2500;
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

            if (!OpenInteraction(giver, task)) return InteractionResult.Retry;

            // Gossip list path: our quest must be among the AVAILABLE entries; a missing entry is the
            // server saying "not offerable here/now" — the exact state the old bot looped on for 23 min.
            if (GossipFrame.Instance.IsVisible && !QuestFrame.Instance.IsVisible)
            {
                int index = FindQuestIndex(GossipFrame.Instance.AvailableQuests, task.QuestId);
                if (index < 0)
                {
                    Logging.Write("[VQ2-Task] pickup q{0} '{1}': {2} does not OFFER it (gossip has no matching entry).",
                        task.QuestId, task.QuestName, task.EntityName);
                    GossipFrame.Instance.Close();
                    return InteractionResult.NotOffered;
                }
                GossipFrame.Instance.SelectAvailableQuest(index);
                if (!WaitState(() => QuestFrame.Instance.IsVisible, FrameOpenTimeoutMs))
                {
                    Logging.WriteDebug("[VQ2-Task] pickup q{0}: detail frame never opened after select — retry.", task.QuestId);
                    return InteractionResult.Retry;
                }
            }

            if (!QuestFrame.Instance.IsVisible)
            {
                Logging.WriteDebug("[VQ2-Task] pickup q{0}: no quest frame after interact — retry.", task.QuestId);
                return InteractionResult.Retry;
            }

            uint shown = QuestFrame.Instance.CurrentShownQuestId;
            if (shown != 0 && shown != (uint)task.QuestId)
            {
                // Direct-detail giver showing some OTHER quest — not ours to accept blind.
                Logging.WriteDebug("[VQ2-Task] pickup q{0}: frame shows q{1} instead — closing, retry.", task.QuestId, shown);
                QuestFrame.Instance.Close();
                return InteractionResult.Retry;
            }

            QuestFrame.Instance.AcceptQuest();
            // Events notify, the LOG is truth: accepted = it's in the log.
            if (WaitState(() => me.QuestLog.ContainsQuest((uint)task.QuestId), LogUpdateTimeoutMs))
            {
                Logging.Write("[VQ2-Task] PickUp q{0} '{1}' OK.", task.QuestId, task.QuestName);
                return InteractionResult.Success;
            }
            Logging.WriteDebug("[VQ2-Task] pickup q{0}: accept sent but quest not in log — retry.", task.QuestId);
            return InteractionResult.Retry;
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

            if (!OpenInteraction(ender, task)) return InteractionResult.Retry;

            if (GossipFrame.Instance.IsVisible && !QuestFrame.Instance.IsVisible)
            {
                int index = FindQuestIndex(GossipFrame.Instance.ActiveQuests, task.QuestId);
                if (index < 0)
                {
                    Logging.Write("[VQ2-Task] turn-in q{0} '{1}': {2} does not TAKE it (gossip has no matching active entry).",
                        task.QuestId, task.QuestName, task.EntityName);
                    GossipFrame.Instance.Close();
                    return InteractionResult.NotOffered;
                }
                GossipFrame.Instance.SelectActiveQuest(index);
                if (!WaitState(() => QuestFrame.Instance.IsVisible, FrameOpenTimeoutMs))
                    return InteractionResult.Retry;
            }

            if (!QuestFrame.Instance.IsVisible)
                return InteractionResult.Retry;

            // Progress frame → Continue → (reward choice) → Complete. Bounded loop: each pass reads the
            // frame STATE fresh; completion is decided by the quest leaving the LOG, nothing else.
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (!me.QuestLog.ContainsQuest((uint)task.QuestId))
                    break;   // landed

                if (!QuestFrame.Instance.IsVisible)
                    return InteractionResult.Retry;   // frame died without completion — re-approach

                uint shown = QuestFrame.Instance.CurrentShownQuestId;
                if (shown != 0 && shown != (uint)task.QuestId)
                {
                    // Wrong quest's frame while ours is still in the log (multi-quest NPC quirk) —
                    // close and let the retry re-select ours from the gossip list.
                    QuestFrame.Instance.Close();
                    StyxWoW.Sleep(300);
                    return InteractionResult.Retry;
                }

                if (Lua.GetReturnVal<int>("return GetNumQuestChoices()", 0U) >= 1)
                    SelectBestReward();

                QuestFrame.Instance.CompleteQuest();
                StyxWoW.Sleep(400);
            }

            if (me.QuestLog.ContainsQuest((uint)task.QuestId))
            {
                Logging.WriteDebug("[VQ2-Task] turn-in q{0}: still in log after the completion window — retry.", task.QuestId);
                return InteractionResult.Retry;
            }

            planner.NotifyTurnedIn((uint)task.QuestId);
            Logging.Write("[VQ2-Task] TurnIn q{0} '{1}' OK.", task.QuestId, task.QuestName);
            HandleChainedOffer(task, plan, me);
            return InteractionResult.Success;
        }

        /// <summary>
        /// After a turn-in lands the server often pushes the follow-up quest's DETAIL frame into the
        /// same window (the flow that wedged ForcedQuestTurnIn on 2026-07-06). In our plan → accept it
        /// on the spot (free pickup, no extra walk); otherwise decline cleanly so the frame can't
        /// confuse the next task.
        /// </summary>
        private static void HandleChainedOffer(QuestTask completed, QuestPlan plan, LocalPlayer me)
        {
            if (!WaitState(() => QuestFrame.Instance.IsVisible, 800))
                return;   // nothing chained
            uint shown = QuestFrame.Instance.CurrentShownQuestId;
            if (shown == 0 || shown == (uint)completed.QuestId)
                return;
            if (plan != null && plan.QuestIds.Contains((int)shown))
            {
                QuestFrame.Instance.AcceptQuest();
                bool accepted = WaitState(() => me.QuestLog.ContainsQuest(shown), LogUpdateTimeoutMs);
                Logging.Write("[VQ2-Task] chained offer q{0} after q{1}: {2}.",
                    shown, completed.QuestId, accepted ? "accepted (in plan)" : "accept did not land");
            }
            else
            {
                Logging.Write("[VQ2-Task] chained offer q{0} after q{1}: declined (not in plan).", shown, completed.QuestId);
                QuestFrame.Instance.DeclineQuest();
            }
        }

        /// <summary>Best reward via the engine's own scorer (WeightSetEx + sell-price fallback) —
        /// reuse ActionSelectReward's exact semantics by ticking it once.</summary>
        private static void SelectBestReward()
        {
            try
            {
                var action = new Bots.Quest.Actions.ActionSelectReward();
                action.Start(null);
                action.Tick(null);
                action.Stop(null);
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[VQ2-Task] reward selection failed ({0}) — taking slot 1.", ex.Message);
                QuestFrame.Instance.SelectQuestReward(0);
            }
            StyxWoW.Sleep(300);
        }

        /// <summary>Entry + proximity resolution — the id-collision-safe lookup.</summary>
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

        private static bool OpenInteraction(WoWObject entity, QuestTask task)
        {
            if (GossipFrame.Instance.IsVisible || QuestFrame.Instance.IsVisible)
                return true;   // already open (retry pass)
            if (!entity.WithinInteractRange)
                return false;  // executor's travel isn't done — not ours to fix
            if (StyxWoW.Me.IsMoving)
                WoWMovement.MoveStop();
            (entity as WoWUnit)?.Target();
            entity.Interact();
            bool opened = WaitState(() => GossipFrame.Instance.IsVisible || QuestFrame.Instance.IsVisible, FrameOpenTimeoutMs);
            if (!opened)
                Logging.WriteDebug("[VQ2-Task] q{0}: no frame within {1}ms of interacting with {2}.",
                    task.QuestId, FrameOpenTimeoutMs, task.EntityName);
            return opened;
        }

        private static int FindQuestIndex(List<GossipQuestEntry> entries, int questId)
        {
            if (entries == null) return -1;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].Id == questId)
                    return entries[i].Index;
            return -1;
        }

        /// <summary>Bounded STATE poll (frames/log are state — doctrine rule 1; no event mirror to desync).</summary>
        private static bool WaitState(Func<bool> condition, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                StyxWoW.Sleep(50);
            }
            return condition();
        }
    }
}
