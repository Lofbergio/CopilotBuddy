// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestManager
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Bots.Quest.Objectives;
using Styx;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Questing;
using System;
using System.Collections.Generic;
using System.Text;

#nullable disable
namespace Bots.Quest;

public static class QuestManager
{
    public static readonly GossipFrame GossipFrame = new GossipFrame();
    public static readonly QuestFrame QuestFrame = new QuestFrame();
    public static readonly List<QuestObjective> Objectives = new List<QuestObjective>();
    public static readonly List<PlayerQuest> Quests = new List<PlayerQuest>();
    private static readonly Random random_0 = new Random();
    private static readonly char[] char_0 = "ABCDEFGHIJKLMNOPQRSTUVXYZabcdefghijklmnopqrstuvxyz".ToCharArray();

    [Obsolete("Use ObjectManager.Me.QuestLog.GetCompletedQuests() instead.")]
    public static List<uint> GetCompletedQuests()
    {
        return new List<uint>((IEnumerable<uint>)StyxWoW.Me.QuestLog.GetCompletedQuests());
    }

    private static string smethod_0(int int_0, int int_1)
    {
        int capacity = random_0.Next(int_0, int_1 + 1);
        StringBuilder stringBuilder = new StringBuilder(capacity);
        for (int index = 0; index < capacity; ++index)
            stringBuilder.Append(char_0[random_0.Next(0, char_0.Length)]);
        return stringBuilder.ToString();
    }

    internal static QuestObjective smethod_1(
        Styx.Logic.Questing.Quest.QuestObjective questObjective_0,
        PlayerQuest playerQuest_0,
        List<WoWQuestStep> list_0,
        List<QuestObjective> list_1)
    {
        switch (questObjective_0.Type)
        {
            case Styx.Logic.Questing.Quest.QuestObjectiveType.CollectIntermediateItem:
            case Styx.Logic.Questing.Quest.QuestObjectiveType.CollectItem:
                return new CollectItemObjective(playerQuest_0, list_0, questObjective_0, list_1);
            case Styx.Logic.Questing.Quest.QuestObjectiveType.KillMob:
                return new GrindObjective(playerQuest_0, list_0, questObjective_0, list_1);
            case Styx.Logic.Questing.Quest.QuestObjectiveType.UseGameObject:
                return new UseGameObjectObjective(playerQuest_0, list_0, questObjective_0, list_1);
            default:
                return null;
        }
    }

    public static QuestObjective CreateQuestObjective(
        Styx.Logic.Questing.Quest.QuestObjective questObjective,
        PlayerQuest quest,
        List<WoWQuestStep> poiSteps,
        List<QuestObjective> objectivePool)
    {
        return smethod_1(questObjective, quest, poiSteps, objectivePool);
    }

    internal delegate void Delegate9(QuestObjective objective);

    internal delegate void Delegate10(List<PlayerQuest> quests);
}
