using System;
using Styx;
using Styx.Helpers;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Bots.Vibes.Shared
{
    public enum QuestInteractOutcome
    {
        /// <summary>Verified by quest-log state (picked up = entered the log; turned in = left it).</summary>
        Success,
        /// <summary>Transient (frame didn't open, log lagging, wrong panel) — re-drive shortly.</summary>
        Retry,
        /// <summary>The server says no: the quest isn't in this NPC's gossip list here/now.</summary>
        NotOffered,
        /// <summary>The entity carries no questgiver npcflag at all — it can never serve this quest
        /// until a world action enables it. Callers decide how permanent that is.</summary>
        NoGiverFlag,
    }

    /// <summary>
    /// Id-driven quest pickup/turn-in against a LIVE entity — the one frame-driving implementation
    /// shared by VibeQuester2 and VibeParty (they were drifting copies before). Entity resolution and
    /// travel are the caller's job; these run once the character stands in interact range.
    /// Design rules (Grull Hawkwind incident + fluid doctrine):
    ///  - frames are STATE — bounded polls, no grace-window inference; completion is verified by
    ///    quest-LOG state, never frame flow;
    ///  - "not offered" is a TYPED outcome, never a silent re-loop;
    ///  - the reward choice is verified (ActionSelectReward reads back QuestInfoFrame.itemChoice) and
    ///    re-issued only while the client shows no registered choice — idempotent per pass;
    ///  - a server-pushed chained follow-up detail frame is screened through the caller's predicate.
    /// </summary>
    public static class QuestInteractionCore
    {
        public const int FrameOpenTimeoutMs = 3500;
        public const int LogUpdateTimeoutMs = 2500;
        // Travel stand-off for quest NPCs, deliberately INSIDE WoWUnit.InteractRange (CombatReach+4):
        // that property approximates the client's own right-click range, and a click fired from its
        // boundary can silently no-op — the client refuses, nothing is sent, no frame ever opens.
        // 4.5yd is VibeQuester2's live-proven value.
        public const float SafeInteractRange = 4.5f;
        // A chained follow-up is pushed into the SAME window synchronously with the turn-in — long
        // enough to ride a lag spike, short enough not to stall every non-chained turn-in.
        public const int ChainedOfferTimeoutMs = 1200;

        /// <summary>Accept one quest by id at a live giver. Caller guarantees interact range.</summary>
        public static QuestInteractOutcome PickUp(WoWObject giver, int questId, string questName, string logTag)
        {
            LocalPlayer me = StyxWoW.Me;
            if (me == null || giver == null) return QuestInteractOutcome.Retry;
            if (me.QuestLog.ContainsQuest((uint)questId))
                return QuestInteractOutcome.Success;   // already have it (share landed, chained accept)

            // The questgiver npcflag is the server's own "!" decider (memory read, no interact) —
            // captive/pre-trigger NPCs show no marker at all. Fail fast instead of interacting into a void.
            if (giver is WoWUnit gu && !gu.IsQuestGiver)
                return QuestInteractOutcome.NoGiverFlag;

            if (!OpenInteraction(giver, questId, logTag)) return QuestInteractOutcome.Retry;

            // Gossip list path: our quest must be among the AVAILABLE entries; a missing entry is the
            // server saying "not offerable here/now".
            if (GossipFrame.Instance.IsVisible && !QuestFrame.Instance.IsVisible)
            {
                int index = FindGossipIndex(actives: false, questId, questName, logTag);
                if (index <= 0)
                {
                    Logging.Write("{0} pickup q{1} '{2}': {3} does not OFFER it (gossip has no matching entry).",
                        logTag, questId, questName, giver.Name);
                    GossipFrame.Instance.Close();
                    return QuestInteractOutcome.NotOffered;
                }
                Lua.DoString("SelectGossipAvailableQuest({0})", index);
                if (!WaitState(() => QuestFrame.Instance.IsVisible, FrameOpenTimeoutMs))
                {
                    Logging.WriteDebug("{0} pickup q{1}: detail frame never opened after select — retry.", logTag, questId);
                    return QuestInteractOutcome.Retry;
                }
            }

            if (!QuestFrame.Instance.IsVisible)
            {
                Logging.WriteDebug("{0} pickup q{1}: no quest frame after interact — retry.", logTag, questId);
                return QuestInteractOutcome.Retry;
            }

            // Greeting panel (non-gossip multi-quest giver): select our quest by title (the greeting
            // API has no ids on 3.3.5a) — the detail panel's shown-id check below verifies the pick.
            if (GreetingShown())
            {
                int gi = FindGreetingIndex(actives: false, questName);
                if (gi <= 0)
                {
                    Logging.Write("{0} pickup q{1} '{2}': {3} does not OFFER it (greeting has no matching entry).",
                        logTag, questId, questName, giver.Name);
                    QuestFrame.Instance.Close();
                    return QuestInteractOutcome.NotOffered;
                }
                if (!SelectAndAwaitDetail("SelectAvailableQuest(" + gi + ")"))
                    return QuestInteractOutcome.Retry;
            }

            uint shown = ShownQuestId();
            if (shown != 0 && shown != (uint)questId)
            {
                // Direct-detail giver showing some OTHER quest — not ours to accept blind.
                Logging.WriteDebug("{0} pickup q{1}: frame shows q{2} instead — closing, retry.", logTag, questId, shown);
                QuestFrame.Instance.Close();
                return QuestInteractOutcome.Retry;
            }

            QuestFrame.Instance.AcceptQuest();
            // Events notify, the LOG is truth: accepted = it's in the log.
            if (WaitState(() => me.QuestLog.ContainsQuest((uint)questId), LogUpdateTimeoutMs))
            {
                Logging.Write("{0} PickUp q{1} '{2}' OK.", logTag, questId, questName);
                return QuestInteractOutcome.Success;
            }
            Logging.WriteDebug("{0} pickup q{1}: accept sent but quest not in log — retry.", logTag, questId);
            return QuestInteractOutcome.Retry;
        }

        /// <summary>
        /// Turn one quest in by id at a live ender. acceptChainedOffer screens a server-pushed
        /// follow-up detail frame (null = always decline).
        /// </summary>
        public static QuestInteractOutcome TurnIn(WoWObject ender, int questId, string questName, string logTag,
            Func<uint, bool> acceptChainedOffer)
        {
            LocalPlayer me = StyxWoW.Me;
            if (me == null || ender == null) return QuestInteractOutcome.Retry;
            if (!me.QuestLog.ContainsQuest((uint)questId))
                return QuestInteractOutcome.Success;   // already gone

            // A valid turn-in NPC always carries the questgiver npcflag (it drives the "?" marker).
            if (ender is WoWUnit eu && !eu.IsQuestGiver)
                return QuestInteractOutcome.NoGiverFlag;

            if (!OpenInteraction(ender, questId, logTag)) return QuestInteractOutcome.Retry;

            if (GossipFrame.Instance.IsVisible && !QuestFrame.Instance.IsVisible)
            {
                int index = FindGossipIndex(actives: true, questId, questName, logTag);
                if (index <= 0)
                {
                    Logging.Write("{0} turn-in q{1} '{2}': {3} does not TAKE it (gossip has no matching active entry).",
                        logTag, questId, questName, ender.Name);
                    GossipFrame.Instance.Close();
                    return QuestInteractOutcome.NotOffered;
                }
                Lua.DoString("SelectGossipActiveQuest({0})", index);
                if (!WaitState(() => QuestFrame.Instance.IsVisible, FrameOpenTimeoutMs))
                    return QuestInteractOutcome.Retry;
            }

            if (!QuestFrame.Instance.IsVisible)
                return QuestInteractOutcome.Retry;

            // Progress frame → Continue → (reward choice) → Complete. Bounded loop: each pass reads
            // the frame STATE fresh; completion is decided by the quest leaving the LOG, nothing else.
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (!me.QuestLog.ContainsQuest((uint)questId))
                    break;   // landed

                if (!QuestFrame.Instance.IsVisible)
                    return QuestInteractOutcome.Retry;   // frame died without completion — re-approach

                // Greeting panel (fresh window, or the window fell back to it after another quest):
                // select our active entry by title; absence is the server's "not turn-in-able here".
                if (GreetingShown() && ShownQuestId() == 0)
                {
                    int gi = FindGreetingIndex(actives: true, questName);
                    if (gi <= 0)
                    {
                        Logging.Write("{0} turn-in q{1} '{2}': {3} does not TAKE it (greeting has no matching active entry).",
                            logTag, questId, questName, ender.Name);
                        QuestFrame.Instance.Close();
                        return QuestInteractOutcome.NotOffered;
                    }
                    // Same edge-wait as the pickup path: the greeting hides before the QUEST_PROGRESS packet
                    // lands, so polling `ShownQuestId() != 0` here is satisfied INSTANTLY by the previous
                    // frame's id and the check below then rejects our own turn-in as "wrong quest".
                    SelectAndAwaitDetail("SelectActiveQuest(" + gi + ")");
                    continue;
                }

                uint shown = ShownQuestId();
                if (shown != 0 && shown != (uint)questId)
                {
                    // Wrong quest's frame while ours is still in the log (multi-quest NPC quirk) —
                    // close and let the retry re-select ours from the gossip list.
                    QuestFrame.Instance.Close();
                    StyxWoW.Sleep(300);
                    return QuestInteractOutcome.Retry;
                }

                // Progress panel is the server's own verdict: not completable = our state is wrong
                // (caller raced, objective lagging) — Continue-spamming it wedged the old bot 25s.
                if (Lua.GetReturnVal<int>(
                        "return ((QuestFrameProgressPanel and QuestFrameProgressPanel:IsShown()) and not IsQuestCompletable()) and 1 or 0", 0U) == 1)
                {
                    Logging.WriteDebug("{0} turn-in q{1}: server says NOT completable — closing, retry.", logTag, questId);
                    QuestFrame.Instance.Close();
                    return QuestInteractOutcome.Retry;
                }

                // Reward choice — ONLY while the offer-reward panel is shown: GetNumQuestChoices()
                // is panel-scoped and replays the last detail/offer block on every other panel
                // (a no-choice quest's progress panel reads the previous quest's choices).
                // Re-issue only while the client shows NO registered choice (QuestInfoFrame.itemChoice
                // is exactly what the complete button hands to GetQuestReward). ActionSelectReward
                // verifies its own click; a Failure means the frame is wedged — close and re-drive
                // rather than completing rewardless.
                if (Lua.GetReturnVal<int>(
                        "return ((QuestFrameRewardPanel and QuestFrameRewardPanel:IsShown())"
                        + " and GetNumQuestChoices() >= 1"
                        + " and ((QuestInfoFrame and QuestInfoFrame.itemChoice) or 0) == 0) and 1 or 0", 0U) == 1)
                {
                    var pick = new Bots.Quest.Actions.ActionSelectReward();
                    pick.Start(null);
                    RunStatus picked = pick.Tick(null);
                    pick.Stop(null);
                    if (picked == RunStatus.Failure)
                    {
                        Logging.Write("{0} turn-in q{1}: reward choice would not register — closing, retry.", logTag, questId);
                        QuestFrame.Instance.Close();
                        return QuestInteractOutcome.Retry;
                    }
                }

                QuestFrame.Instance.CompleteQuest();
                StyxWoW.Sleep(400);
            }

            if (me.QuestLog.ContainsQuest((uint)questId))
            {
                Logging.WriteDebug("{0} turn-in q{1}: still in log after the completion window — retry.", logTag, questId);
                return QuestInteractOutcome.Retry;
            }

            Logging.Write("{0} TurnIn q{1} '{2}' OK.", logTag, questId, questName);
            HandleChainedOffer((uint)questId, logTag, acceptChainedOffer, me);
            return QuestInteractOutcome.Success;
        }

        /// <summary>
        /// After a turn-in lands the server often pushes the follow-up quest's DETAIL frame into the
        /// same window. Screen it through the caller's predicate → accept on the spot (free pickup);
        /// otherwise decline cleanly so the frame can't confuse the next task.
        /// </summary>
        private static void HandleChainedOffer(uint completedId, string logTag, Func<uint, bool> accept, LocalPlayer me)
        {
            if (!WaitState(() => QuestFrame.Instance.IsVisible, ChainedOfferTimeoutMs))
                return;   // nothing chained
            uint shown = ShownQuestId();
            if (shown == 0 || shown == completedId)
                return;
            if (accept != null && accept(shown))
            {
                QuestFrame.Instance.AcceptQuest();
                bool accepted = WaitState(() => me.QuestLog.ContainsQuest(shown), LogUpdateTimeoutMs);
                Logging.Write("{0} chained offer q{1} after q{2}: {3}.",
                    logTag, shown, completedId, accepted ? "accepted (screened OK)" : "accept did not land");
            }
            else
            {
                Logging.Write("{0} chained offer q{1} after q{2}: declined (failed screen).", logTag, shown, completedId);
                QuestFrame.Instance.DeclineQuest();
            }
        }

        /// <summary>
        /// Fire a list-select and wait for the server's NEW panel to actually arrive.
        /// ⚠ Never poll `ShownQuestId() != 0` for this: `CurrentShownQuestId` is a memory read that keeps
        /// the PREVIOUS frame's id, so a stale non-zero satisfies the poll instantly and the caller then
        /// compares against last frame's quest. Live: selecting 'The Perfect Stout' (315) read 413
        /// ('Shimmer Stout', the chain's next quest) 131ms later and aborted as "wrong quest". The EDGE is
        /// the truth — QUEST_DETAIL/QUEST_PROGRESS fire when the panel genuinely changes (doctrine rule 2).
        /// </summary>
        private static bool SelectAndAwaitDetail(string selectLua)
        {
            using (var detail = new LuaEventWait("QUEST_DETAIL"))
            using (var progress = new LuaEventWait("QUEST_PROGRESS"))
            {
                Lua.DoString(selectLua);
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(FrameOpenTimeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    // Wait(0) is not a free poll — an unset event still pumps ProcessPendingEvents and
                    // sleeps ~30ms, so a loop pass costs ~80ms. That granularity is fine here.
                    if (detail.Wait(50) || progress.Wait(0)) return true;
                    // ⚠ Safe for the GREETING selects that call this: the QuestFrame container stays up
                    // across the panel swap. Do NOT reuse this bail for the GOSSIP selects — gossip→detail
                    // leaves BOTH frames closed for one round trip and this would abort a healthy select.
                    if (!QuestFrame.Instance.IsVisible && !GossipFrame.Instance.IsVisible) return false;
                }
                return false;
            }
        }

        private static bool GreetingShown()
            => Lua.GetReturnVal<int>("return (QuestFrameGreetingPanel and QuestFrameGreetingPanel:IsShown()) and 1 or 0", 0U) == 1;

        /// <summary>1-based greeting-panel select index by title match — the greeting API exposes no
        /// quest ids on 3.3.5a, so the caller must verify the resulting detail/progress panel's id.</summary>
        private static int FindGreetingIndex(bool actives, string questTitle)
        {
            string fn = actives ? "Active" : "Available";
            string list = Lua.GetReturnVal<string>(
                "local r = '' for i = 1, GetNum" + fn + "Quests() do r = r .. (Get" + fn + "Title(i) or '') .. '\\2' end return r", 0U) ?? "";
            string[] titles = list.Split(new[] { '\x02' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < titles.Length; i++)
                if (string.Equals(titles[i], questTitle, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            return 0;
        }

        private static bool OpenInteraction(WoWObject entity, int questId, string logTag)
        {
            if (GossipFrame.Instance.IsVisible || QuestFrame.Instance.IsVisible)
                return true;   // already open (retry pass)
            if (!entity.WithinInteractRange)
                return false;  // caller's travel isn't done — not ours to fix
            if (StyxWoW.Me.IsMoving)
                WoWMovement.MoveStop();
            (entity as WoWUnit)?.Target();
            // ignoreTimer: WoWObject.Interact's 2s anti-spam timer swallows the call SILENTLY (no log,
            // nothing sent) — a second per-quest transaction at the same NPC inside 2s is deliberate
            // here, and a swallowed click reads as "no frame" and charges a wrongful Retry.
            entity.Interact(ignoreTimer: true);
            bool opened = WaitState(() => GossipFrame.Instance.IsVisible || QuestFrame.Instance.IsVisible, FrameOpenTimeoutMs);
            if (!opened)
                Logging.WriteDebug("{0} q{1}: no frame within {2}ms of interacting with {3}.",
                    logTag, questId, FrameOpenTimeoutMs, entity.Name);
            return opened;
        }

        /// <summary>
        /// 1-based gossip select index for OUR quest, or 0 when it isn't listed. ⚠ LUA is the truth:
        /// the memory wrapper (GossipFrame.ActiveQuests/AvailableQuests) returned EMPTY lists while
        /// GetNumGossip*Quests() was non-zero live (docs/gotchas.md) — trusting it here turns a quest
        /// the server IS offering into a NotOffered strike and a wrongful abandon. Wrapper ids match
        /// first when they read; otherwise the Lua TITLES match against the DB quest name (same enUS
        /// strings the server sends).
        /// </summary>
        public static int FindGossipIndex(bool actives, int questId, string questTitle, string logTag)
        {
            var entries = actives ? GossipFrame.Instance.ActiveQuests : GossipFrame.Instance.AvailableQuests;
            if (entries != null)
                foreach (GossipQuestEntry e in entries)
                    if (e != null && e.Id == questId)
                        return e.Index + 1;   // wrapper index is 0-based; Lua selects are 1-based

            string fn = actives ? "Active" : "Available";
            string luaList = Lua.GetReturnVal<string>(
                "local r = '' local n = GetNumGossip" + fn + "Quests() local q = { GetGossip" + fn + "Quests() } " +
                "if n > 0 then local s = math.floor(#q / n) for i = 0, n - 1 do r = r .. tostring(q[i*s+1]) .. '\\2' end end return r", 0U) ?? "";
            string[] titles = luaList.Split(new[] { '\x02' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < titles.Length; i++)
                if (string.Equals(titles[i], questTitle, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            Logging.WriteDebug("{0} q{1} '{2}' not in the gossip {3} list ({4} entries: {5}).",
                logTag, questId, questTitle, fn.ToLowerInvariant(), titles.Length, string.Join(" | ", titles));
            return 0;
        }

        /// <summary>The id of the quest shown on a DETAIL/PROGRESS/REWARD panel (memory read).
        /// The greeting panel shows no single quest → force 0 so callers take the title-driven
        /// greeting path (a stale memory id must not masquerade as the shown quest). ⚠ 3.3.5a has
        /// NO GetQuestID() Lua (added in Cata 4.0.1) — the old fallback threw on every call
        /// (Lua status=2 spam on every greeting turn-in) and never returned a usable value.</summary>
        public static uint ShownQuestId()
        {
            if (GreetingShown()) return 0;
            return QuestFrame.Instance.CurrentShownQuestId;
        }

        /// <summary>Bounded STATE poll (frames/log are state — doctrine rule 1).</summary>
        public static bool WaitState(Func<bool> condition, int timeoutMs)
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
