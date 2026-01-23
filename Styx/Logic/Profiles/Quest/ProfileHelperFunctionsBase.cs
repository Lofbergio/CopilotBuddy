// Decompiled with JetBrains decompiler
// Type: Styx.Logic.Profiles.Quest.ProfileHelperFunctionsBase
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest;

public class ProfileHelperFunctionsBase
{
    private static readonly HashSet<QuestGiverStatus> hashSet_0 = new HashSet<QuestGiverStatus>()
    {
        QuestGiverStatus.Available,
        QuestGiverStatus.AvailableRepeatable,
        QuestGiverStatus.LowLevelAvailable,
        QuestGiverStatus.LowLevelAvailableRepeatable
    };
    private static readonly Random random_0 = new Random(Environment.TickCount);

    protected LocalPlayer Me => ObjectManager.Me;

    protected WoWSkill SkinningSkill => this.Me.GetSkill(SkillLine.Skinning);

    protected WoWSkill MiningSkill => this.Me.GetSkill(SkillLine.Mining);

    protected WoWSkill HerbalismSkill => this.Me.GetSkill(SkillLine.Herbalism);

    protected bool OnTransport => this.Me.IsOnTransport;

    protected int GetItemCount(int itemId)
    {
        return this.Me.CarriedItems
            .Where(item => (int)item.Entry == itemId)
            .Sum(item => (int)item.StackCount);
    }

    protected bool IsObjectiveComplete(int objectiveId, uint questId)
    {
        if (this.Me.QuestLog.GetQuestById(questId) == null)
            return false;
        int returnVal = Lua.GetReturnVal<int>($"return GetQuestLogIndexByID({questId})", 0U);
        return Lua.GetReturnVal<bool>($"return GetQuestLogLeaderBoard({objectiveId},{returnVal})", 2U);
    }

    protected bool HasMininion(uint entry)
    {
        return (WoWObject)this.Me != (WoWObject)null && 
               this.Me.Minions != null && 
               this.Me.Minions.Any(unit => unit.Entry == entry);
    }

    protected bool HasQuestAvailable(int objectId)
    {
        return this.HasQuestAvailable(objectId, hashSet_0);
    }

    protected bool HasQuestAvailable(int objectId, string type)
    {
        HashSet<QuestGiverStatus> statuses = new HashSet<QuestGiverStatus>();
        foreach (string str in type.Split(','))
        {
            string trimmed = str.Trim();
            try
            {
                statuses.Add((QuestGiverStatus)Enum.Parse(typeof(QuestGiverStatus), trimmed, true));
            }
            catch (ArgumentException)
            {
                Logging.WriteDebug("HasQuestAvailable called with invalid type: {0}", trimmed);
            }
        }
        return this.HasQuestAvailable(objectId, statuses);
    }

    protected bool HasQuestAvailable(int objectId, params QuestGiverStatus[] statuses)
    {
        return this.HasQuestAvailable(objectId, new HashSet<QuestGiverStatus>(statuses));
    }

    protected bool HasQuestAvailable(int objectId, HashSet<QuestGiverStatus> statuses)
    {
        return statuses.Count > 0 && 
               ObjectManager.ObjectList
                   .Where(obj => (int)obj.Entry == objectId)
                   .Any(obj => statuses.Contains(obj.QuestGiverStatus));
    }

    protected bool IsAchievementCompleted(int id)
    {
        List<string> returnValues = Lua.GetReturnValues($"local _,_,_,completed = GetAchievementInfo({id}) if completed then return '1' else return '0' end");
        return returnValues != null && returnValues.Count > 0 && returnValues[0] == "1";
    }

    protected bool IsAchievementCompleted(int achievementId, int index)
    {
        List<string> returnValues = Lua.GetReturnValues($"local _,_,completed = GetAchievementCriteriaInfo({achievementId},{index}) if completed then return '1' else return '0' end");
        return returnValues != null && returnValues.Count > 0 && returnValues[0] == "1";
    }

    protected bool HasItem(int itemId)
    {
        return this.Me.CarriedItems.Any(item => (int)item.Entry == itemId);
    }

    protected bool HasSpell(string spellName) => SpellManager.HasSpell(spellName);

    protected bool HasSpell(int spellId) => SpellManager.HasSpell(spellId);

    protected bool HasQuest(uint questId) => this.Me.QuestLog.ContainsQuest(questId);

    protected bool IsQuestCompleted(uint id)
    {
        PlayerQuest questById = this.Me.QuestLog.GetQuestById(id);
        return questById != null ? questById.IsCompleted : StyxWoW.Me.QuestLog.GetCompletedQuests().Contains(id);
    }

    protected bool HasFaction(int factionId)
    {
        return this.Me.GetFactionStanding((uint)factionId, out FactionStanding _);
    }

    protected int GetFactionReputation(int factionId)
    {
        FactionStanding standing;
        return !this.Me.GetFactionStanding((uint)factionId, out standing) ? 0 : standing.TotalReputation;
    }

    protected FactionStanding GetFactionStanding(int factionId)
    {
        FactionStanding standing;
        this.Me.GetFactionStanding((uint)factionId, out standing);
        return standing;
    }

    protected uint GetCurrencyAmount(uint currencyId)
    {
        WoWCurrency currencyById = WoWCurrency.GetCurrencyById(currencyId);
        return currencyById != null && currencyById.IsValid ? currencyById.Amount : 0U;
    }

    protected bool CanFly()
    {
        WoWSkill skill = this.Me.GetSkill(SkillLine.Riding);
        if (skill == null || skill.CurrentValue < 225)
            return false;
        switch (this.Me.MapId)
        {
            case 0:
            case 1:
            case 646:
                if (!SpellManager.HasSpell("Flight Master's License"))
                    return false;
                break;
            case 571:
                if (!SpellManager.HasSpell("Cold Weather Flying"))
                    return false;
                break;
        }
        return MountHelper.FlyingMounts.Any<MountHelper.MountWrapper>();
    }

    protected bool Chance(double val)
    {
        bool flag;
        lock (random_0)
            flag = random_0.NextDouble() * 100.0 < val;
        return flag;
    }

    protected int Random(int max) => this.Random(0, max);

    protected int Random(int min, int max)
    {
        int num;
        lock (random_0)
            num = random_0.Next(min, max);
        return num;
    }
}
