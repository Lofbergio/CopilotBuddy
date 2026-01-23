// Decompiled with JetBrains decompiler
// Type: Bots.Quest.Objectives.QuestObjective
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Styx;
using Styx.Logic.AreaManagement;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TreeSharp;

#nullable disable
namespace Bots.Quest.Objectives;

public abstract class QuestObjective : IDisposable, IEquatable<QuestObjective>
{
    private Guid guid_0;
    public readonly List<QuestObjective> Prerequisites;

    protected QuestObjective(
        PlayerQuest quest,
        List<WoWQuestStep> questSteps,
        List<QuestObjective> prerequisites)
    {
        if (quest == null)
            throw new ArgumentNullException(nameof(quest));
        if (quest.Id == 0U)
            throw new ArgumentException("Quest passed has an invalid quest ID.", nameof(quest));
        if (!ObjectManager.Me.QuestLog.ContainsQuest(quest.Id))
            throw new ArgumentException("Quest passed is not contained in the quest log.", nameof(quest));
        this.Quest = quest;
        this.QuestSteps = questSteps ?? new List<WoWQuestStep>();
        this.QuestArea = new QuestArea(quest, (IList<WoWQuestStep>)this.QuestSteps);
        StyxWoW.AreaManager.Add((Area)this.QuestArea);
        this.Prerequisites = prerequisites ?? new List<QuestObjective>();
        Profile currentProfile = ProfileManager.CurrentProfile;
        QuestInfo quest1;
        this.OverridedQuestInfo = !(currentProfile != (Profile)null) || (quest1 = currentProfile.FindQuest(quest.Id)) == null ? (QuestInfo)null : quest1;
        this.guid_0 = Guid.NewGuid();
    }

    public QuestArea QuestArea { get; private set; }

    public List<WoWQuestStep> QuestSteps { get; private set; }

    public PlayerQuest Quest { get; private set; }

    public QuestInfo OverridedQuestInfo { get; private set; }

    public abstract bool IsCompleted { get; }

    public abstract bool CanComplete { get; }

    public bool DonePrerequisites
    {
        get
        {
            return this.Prerequisites.All<QuestObjective>((Func<QuestObjective, bool>)(questObjective_0 => questObjective_0.IsCompleted));
        }
    }

    public abstract Composite CreateBranch();

    public abstract WoWPoint GetObjectiveLocation();

    protected List<WoWQuestStep> GetDistanceSortedQuestSteps()
    {
        List<WoWQuestStep> sortedQuestSteps = new List<WoWQuestStep>();
        sortedQuestSteps.AddRange((IEnumerable<WoWQuestStep>)this.QuestSteps);
        WoWPoint location = ObjectManager.Me.Location;
        sortedQuestSteps.Sort((Comparison<WoWQuestStep>)((a, b) => CompareQuestSteps(location, a, b)));
        return sortedQuestSteps;
    }

    protected WoWQuestStep GetClosestQuestStep()
    {
        if (this.QuestSteps == null || this.QuestSteps.Count <= 0)
            return new WoWQuestStep();
        WoWPoint location = ObjectManager.Me.Location;
        WoWQuestStep closestQuestStep = this.QuestSteps[0];
        float num1 = location.Distance2DSqr(new WoWPoint((float)closestQuestStep.StepPosition.X, (float)closestQuestStep.StepPosition.Y, 0.0f));
        for (int index = 1; index < this.QuestSteps.Count; ++index)
        {
            WoWQuestStep questStep = this.QuestSteps[index];
            float num2 = location.Distance2DSqr(new WoWPoint((float)questStep.StepPosition.X, (float)questStep.StepPosition.Y, 0.0f));
            if ((double)num2 < (double)num1)
            {
                closestQuestStep = questStep;
                num1 = num2;
            }
        }
        return closestQuestStep;
    }

    private static int CompareQuestSteps(
        WoWPoint woWPoint_0,
        WoWQuestStep woWQuestStep_0,
        WoWQuestStep woWQuestStep_1)
    {
        WoWPoint other1 = new WoWPoint((float)woWQuestStep_0.StepPosition.X, (float)woWQuestStep_0.StepPosition.Y, 0.0f);
        WoWPoint other2 = new WoWPoint((float)woWQuestStep_1.StepPosition.X, (float)woWQuestStep_1.StepPosition.Y, 0.0f);
        return woWPoint_0.Distance2DSqr(other1).CompareTo(woWPoint_0.Distance2DSqr(other2));
    }

    public bool IsPointInArea(Vector2 pnt)
    {
        Vector3 pnt1 = new Vector3(pnt, 0.0f);
        return this.IsPointInArea(pnt1);
    }

    public bool IsPointInArea(Vector3 pnt)
    {
        return this.QuestArea.AreaDefinitions.Any(area => IsPointInPolygon(area, ref pnt));
    }

    private static bool IsPointInPolygon(IList<Vector3> ilist_0, ref Vector3 vector3_0)
    {
        bool flag = false;
        int index1 = 0;
        int index2 = ilist_0.Count - 1;
        for (; index1 < ilist_0.Count - 1; index2 = index1++)
        {
            if ((double)ilist_0[index1].Y > (double)vector3_0.Y != (double)ilist_0[index2].Y > (double)vector3_0.Y && (double)vector3_0.X < ((double)ilist_0[index2].X - (double)ilist_0[index1].X) * ((double)vector3_0.Y - (double)ilist_0[index1].Y) / ((double)ilist_0[index2].Y - (double)ilist_0[index1].Y) + (double)ilist_0[index1].X)
                flag = !flag;
        }
        return flag;
    }

    public virtual void Dispose()
    {
    }

    public bool Equals(QuestObjective other)
    {
        if (object.ReferenceEquals((object)null, (object)other))
            return false;
        return object.ReferenceEquals((object)this, (object)other) || other.guid_0.Equals(this.guid_0);
    }

    public override bool Equals(object obj)
    {
        return !object.ReferenceEquals((object)null, obj) && this.Equals(obj as QuestObjective);
    }

    public override int GetHashCode() => this.guid_0.GetHashCode();

    public static bool operator ==(QuestObjective left, QuestObjective right)
    {
        return object.Equals((object)left, (object)right);
    }

    public static bool operator !=(QuestObjective left, QuestObjective right)
    {
        return !object.Equals((object)left, (object)right);
    }
}
