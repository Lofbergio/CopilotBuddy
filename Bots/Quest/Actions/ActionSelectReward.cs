// Decompiled with JetBrains decompiler
// Type: Bots.Quest.Actions.ActionSelectReward
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// Based on HB 4.3.4 ActionSelectReward

using System;
using Styx;
using Styx.Helpers;
using Styx.Logic.Inventory;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

#nullable disable
namespace Bots.Quest.Actions;

/// <summary>
/// Picks the best choice reward on the open reward panel and VERIFIES the client registered it.
/// First pass: WeightSetEx.EvaluateItem on equippable choices. Second pass: vendor sell-price.
/// Returns Failure only when no click could be verified — callers should re-drive the interaction.
/// </summary>
public class ActionSelectReward : Action
{
    /// <summary>1-based index of the verified selection; 0 when nothing was selected.</summary>
    public int SelectedChoice { get; private set; }

    protected override RunStatus Run(object context)
    {
        int numChoices = Lua.GetReturnVal<int>("return GetNumQuestChoices()", 0U);
        if (numChoices <= 0)
            return RunStatus.Success;

        // The quest cache and item cache fill on their own server round-trips after the reward
        // panel opens — wait for them (bounded) instead of scoring a half-loaded list.
        Styx.Logic.Questing.Quest shown = null;
        DateTime cacheDeadline = DateTime.UtcNow.AddMilliseconds(2000);
        while (DateTime.UtcNow < cacheDeadline)
        {
            shown = QuestManager.QuestFrame.CurrentShownQuest;
            if (shown != null && ChoiceItemsCached(shown, numChoices))
                break;
            StyxWoW.Sleep(50);
        }

        int bestIndex = -1;
        string bestName = "";

        if (shown != null)
        {
            Styx.WoWInternals.WoWCache.WoWCache.QuestCacheEntry internalInfo = shown.InternalInfo;

            // Resolved here, not at construction: CurrentWeightSet is null when the weight-set
            // files are missing, and a ctor snapshot turned that into an NRE mid-turn-in.
            WeightSetEx weightSet = WeightSetEx.CurrentWeightSet;
            if (weightSet != null)
            {
                float bestScore = float.MinValue;
                for (int i = 0; i < internalInfo.RewardChoiceItem.Length; i++)
                {
                    int itemId = internalInfo.RewardChoiceItem[i];
                    int itemCount = internalInfo.RewardChoiceItemCount[i];
                    if (itemId == 0 || itemCount == 0)
                        continue;

                    ItemInfo itemInfo = ItemInfo.FromId((uint)itemId);
                    if (itemInfo == null || !ObjectManager.Me.CanEquipItem(itemInfo))
                        continue;

                    string itemLink = Lua.GetReturnVal<string>(
                        string.Format("return GetQuestItemLink('choice', {0})", i + 1), 0U);
                    if (string.IsNullOrEmpty(itemLink))
                        continue;

                    ItemStats itemStats = new ItemStats(itemLink);
                    float score = weightSet.EvaluateItem(itemInfo, itemStats);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = i;
                        bestName = itemInfo.Name;
                    }
                }
            }

            // Second pass: nothing equippable (or no weight set) — take the most vendor value.
            if (bestIndex == -1)
            {
                float bestValue = float.MinValue;
                for (int j = 0; j < internalInfo.RewardChoiceItem.Length; j++)
                {
                    int itemId = internalInfo.RewardChoiceItem[j];
                    int itemCount = internalInfo.RewardChoiceItemCount[j];
                    if (itemId == 0 || itemCount <= 0)
                        continue;

                    ItemInfo itemInfo = ItemInfo.FromId((uint)itemId);
                    if (itemInfo == null)
                        continue;

                    float sellValue = (float)(itemInfo.SellPrice * itemCount);
                    Logging.WriteDebug("[SelectReward] {0}{1} sells for {2}",
                        itemInfo.Name,
                        itemCount > 1 ? ("x" + itemCount) : "",
                        sellValue);

                    if (sellValue > bestValue)
                    {
                        bestName = itemInfo.Name;
                        bestValue = sellValue;
                        bestIndex = j;
                    }
                }
            }
        }

        if (bestIndex == -1)
        {
            // Quest/item cache never filled within the wait — the only blind path left. Choice 1,
            // named from the Lua link (the client truth), so the log shows what was actually taken.
            bestIndex = 0;
            bestName = Lua.GetReturnVal<string>(
                "local l = GetQuestItemLink('choice', 1) return (l and l:match('%[(.-)%]')) or 'choice 1'", 0U);
            Logging.Write("[SelectReward] quest/item cache unavailable — falling back to {0}.", bestName);
        }

        // The cache row can list more choices than the server offers — the Lua count bounds clicks.
        if (bestIndex >= numChoices)
            bestIndex = 0;

        // Click and VERIFY it registered: QuestInfoFrame.itemChoice is exactly what the complete
        // button hands to GetQuestReward(). Two attempts on the pick, then choice 1 as last resort.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int target = attempt < 2 ? bestIndex : 0;
            QuestFrame.Instance.SelectQuestReward(target);
            DateTime clickDeadline = DateTime.UtcNow.AddMilliseconds(1000);
            while (DateTime.UtcNow < clickDeadline)
            {
                if (Lua.GetReturnVal<int>("return (QuestInfoFrame and QuestInfoFrame.itemChoice) or 0", 0U) == target + 1)
                {
                    SelectedChoice = target + 1;
                    Logging.Write("[SelectReward] chose {0} (choice {1} of {2}).",
                        target == bestIndex ? bestName : "choice 1 (last resort)", target + 1, numChoices);
                    return RunStatus.Success;
                }
                StyxWoW.Sleep(50);
            }
            Logging.Write("[SelectReward] choice {0} did not register (attempt {1}).", target + 1, attempt + 1);
        }

        Logging.Write("[SelectReward] FAILED to register any reward choice for quest {0} — caller should re-drive the turn-in.",
            shown != null ? shown.Id.ToString() : "?");
        return RunStatus.Failure;
    }

    // The cache row may lag the Lua choice count — all listed items resolvable AND at least as
    // many as the server reports means scoring sees the full list.
    private static bool ChoiceItemsCached(Styx.Logic.Questing.Quest quest, int numChoices)
    {
        Styx.WoWInternals.WoWCache.WoWCache.QuestCacheEntry info = quest.InternalInfo;
        int found = 0;
        for (int i = 0; i < info.RewardChoiceItem.Length; i++)
        {
            int id = info.RewardChoiceItem[i];
            if (id == 0)
                continue;
            if (ItemInfo.FromId((uint)id) == null)
                return false;
            found++;
        }
        return found >= numChoices;
    }
}
