// Decompiled with JetBrains decompiler
// Type: Bots.Quest.Actions.ActionSelectQuest
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using System.Collections.Generic;
using TreeSharp;
using Action = TreeSharp.Action;

#nullable disable
namespace Bots.Quest.Actions;

public class ActionSelectQuest : Action
{
    private readonly int int_0;

    public ActionSelectQuest() => this.int_0 = -1;

    public ActionSelectQuest(int id) => this.int_0 = id;

    protected override RunStatus Run(object context)
    {
        if (QuestManager.QuestFrame.IsVisible && QuestManager.QuestFrame.CurrentShownQuestId != 0U)
            return RunStatus.Success;
        if (QuestManager.GossipFrame.IsVisible)
        {
            List<GossipQuestEntry> activeQuests = QuestManager.GossipFrame.ActiveQuests;
            for (int index = 0; index < activeQuests.Count; ++index)
            {
                if (this.int_0 == -1 || activeQuests[index].Id == this.int_0)
                {
                    PlayerQuest questById = ObjectManager.Me.QuestLog.GetQuestById((uint)activeQuests[index].Id);
                    if (this.int_0 != -1 || questById.IsCompleted)
                    {
                        QuestManager.GossipFrame.SelectActiveQuest(activeQuests[index].Index);
                        return RunStatus.Success;
                    }
                }
            }
        }
        else if (QuestManager.QuestFrame.IsVisible)
        {
            List<uint> quests = QuestManager.QuestFrame.Quests;
            if (this.int_0 != -1 && !quests.Contains((uint)this.int_0))
            {
                QuestManager.QuestFrame.Close();
                return RunStatus.Failure;
            }
            for (int index = 0; index < quests.Count; ++index)
            {
                if (this.int_0 == -1 || (long)quests[index] == (long)this.int_0)
                {
                    PlayerQuest questById = ObjectManager.Me.QuestLog.GetQuestById(quests[index]);
                    if (this.int_0 != -1 || questById.IsCompleted)
                    {
                        QuestManager.GossipFrame.SelectActiveQuest(index);
                        return RunStatus.Success;
                    }
                }
            }
        }
        return RunStatus.Failure;
    }
}
