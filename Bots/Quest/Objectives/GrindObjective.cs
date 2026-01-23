// Decompiled with JetBrains decompiler
// Type: Bots.Quest.Objectives.GrindObjective
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Bots.Grind;
using CommonBehaviors.Decorators;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.AreaManagement;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TreeSharp;

#nullable disable
namespace Bots.Quest.Objectives;

public class GrindObjective : QuestObjective
{
    private readonly KillMobObjectiveInfo killMobObjectiveInfo_0;
    private WoWPoint? nullable_0;
    private Composite composite_0;

    public GrindObjective(
        PlayerQuest quest,
        List<WoWQuestStep> questSteps,
        Styx.Logic.Questing.Quest.QuestObjective questObjective,
        List<QuestObjective> prerequisites)
        : base(quest, questSteps, prerequisites)
    {
        this.Objective = questObjective;
        if (this.OverridedQuestInfo != null)
        {
            this.killMobObjectiveInfo_0 = this.OverridedQuestInfo.FindKillMob((uint)this.Objective.ID);
            if (this.killMobObjectiveInfo_0 == null)
            {
                foreach (KillMobObjectiveInfo mobObjectiveInfo in this.OverridedQuestInfo.Objectives.OfType<KillMobObjectiveInfo>().Where<KillMobObjectiveInfo>((Func<KillMobObjectiveInfo, bool>)(killMobObjectiveInfo_1 => killMobObjectiveInfo_1.Type == ObjectiveType.KillMob)))
                {
                    WoWCache.InfoBlock infoBlockById = StyxWoW.Cache[CacheDb.Creature].GetInfoBlockById(mobObjectiveInfo.MobID);
                    if (infoBlockById != null)
                    {
                        WoWCache.CreatureCacheEntry creature = infoBlockById.Creature;
                        if ((long)creature.GroupID == (long)this.Objective.ID || (long)creature.GroupID2 == (long)this.Objective.ID)
                        {
                            this.killMobObjectiveInfo_0 = mobObjectiveInfo;
                            break;
                        }
                    }
                }
            }
        }
        Targeting.Instance.IncludeTargetsFilter += new IncludeTargetsFilterDelegate(this.method_0);
    }

    public Styx.Logic.Questing.Quest.QuestObjective Objective { get; private set; }

    public override void Dispose()
    {
        Targeting.Instance.IncludeTargetsFilter -= new IncludeTargetsFilterDelegate(this.method_0);
    }

    private void method_0(List<WoWObject> list_1, HashSet<WoWObject> hashSet_0)
    {
        if (this.IsCompleted || StyxWoW.Me.IsActuallyInCombat)
            return;
        foreach (WoWObject woWobject in list_1)
        {
            if (woWobject is WoWUnit && this.method_2(woWobject.ToUnit()))
                hashSet_0.Add(woWobject);
        }
    }

    public override bool IsCompleted
    {
        get
        {
            WoWDescriptorQuest data;
            return this.Quest.GetData(out data) && (int)data.ObjectivesDone[this.Objective.Index] >= this.Objective.Count;
        }
    }

    public override bool CanComplete => this.DonePrerequisites && this.method_1();

    public override Composite CreateBranch()
    {
        int level = StyxWoW.Me.Level;
        if (this.composite_0 == (Composite)null)
        {
            GrindArea area;
            if (this.killMobObjectiveInfo_0 != null && this.killMobObjectiveInfo_0.OverridedHotspots != null && this.killMobObjectiveInfo_0.OverridedHotspots.Count > 0)
            {
                area = new GrindArea(new HotspotManager((IEnumerable<WoWPoint>)this.killMobObjectiveInfo_0.OverridedHotspots))
                {
                    TargetMaxLevel = this.killMobObjectiveInfo_0.TargetMaxLevel > 0 ? this.killMobObjectiveInfo_0.TargetMaxLevel : level + 5,
                    TargetMinLevel = this.killMobObjectiveInfo_0.TargetMinLevel > 0 ? this.killMobObjectiveInfo_0.TargetMinLevel : this.Quest.Level - 5
                };
            }
            else
            {
                this.QuestArea.CreateHotspots();
                this.QuestArea.TargetMaxLevel = level + 5;
                this.QuestArea.TargetMinLevel = this.Quest.Level - 5;
                area = (GrindArea)this.QuestArea;
            }
            StyxWoW.AreaManager.SetArea(area);
            this.composite_0 = (Composite)new DecoratorIsNotPoiType((IEnumerable<PoiType>)new PoiType[2]
            {
                PoiType.Loot,
                PoiType.Skin
            }, (Composite)LevelBot.CreateRoamBehavior());
        }
        return this.composite_0;
    }

    public override WoWPoint GetObjectiveLocation()
    {
        List<WoWUnit> list = ObjectManager.GetObjectsOfType<WoWUnit>().Where<WoWUnit>((Func<WoWUnit, bool>)(woWUnit_0 => this.method_2(woWUnit_0))).ToList<WoWUnit>();
        if (list.Count <= 0)
        {
            if (!this.nullable_0.HasValue)
            {
                if (this.killMobObjectiveInfo_0 != null && this.killMobObjectiveInfo_0.OverridedHotspots != null && this.killMobObjectiveInfo_0.OverridedHotspots.Count > 0)
                {
                    this.nullable_0 = new WoWPoint?(this.killMobObjectiveInfo_0.OverridedHotspots.FindClosestTo(ObjectManager.Me.Location));
                }
                else
                {
                    WoWQuestStep closestQuestStep = this.GetClosestQuestStep();
                    var xnaVec = new Tripper.XNAMath.Vector3((float)closestQuestStep.StepPosition.X, (float)closestQuestStep.StepPosition.Y, 0.0f);
                    if (!Navigator.FindHeight(ref xnaVec))
                    {
                        Logging.Write("GrindObjective: Could not find mesh height for quest {0} on step {1}", (object)this.Quest.Name, (object)closestQuestStep.PoiID);
                        this.nullable_0 = new WoWPoint?(WoWPoint.Zero);
                    }
                    else
                        this.nullable_0 = new WoWPoint?(new WoWPoint(xnaVec.X, xnaVec.Y, xnaVec.Z));
                }
            }
            return this.nullable_0.Value;
        }
        WoWPoint location1 = ObjectManager.Me.Location;
        WoWPoint objectiveLocation = list[0].Location;
        float num1 = objectiveLocation.DistanceSqr(location1);
        for (int index = 1; index < list.Count; ++index)
        {
            WoWPoint location2 = list[index].Location;
            float num2 = location2.DistanceSqr(location1);
            if ((double)num2 < (double)num1)
            {
                num1 = num2;
                objectiveLocation = location2;
            }
        }
        return objectiveLocation;
    }

    private bool method_1()
    {
        if (this.killMobObjectiveInfo_0 != null && this.killMobObjectiveInfo_0.OverridedHotspots != null && this.killMobObjectiveInfo_0.OverridedHotspots.Count > 0)
            return true;
        if (!this.QuestArea.HotspotsCreated)
            this.QuestArea.CreateHotspots();
        return this.QuestArea.Hotspots.Count > 0;
    }

    private bool method_2(WoWUnit woWUnit_0)
    {
        if (woWUnit_0 is WoWPlayer)
            return false;
        if ((long)woWUnit_0.Entry == (long)this.Objective.ID)
            return true;
        WoWCache.CreatureCacheEntry info;
        if (!woWUnit_0.GetCachedInfo(out info))
            return false;
        return (long)info.GroupID == (long)this.Objective.ID || (long)info.GroupID2 == (long)this.Objective.ID;
    }

    public override string ToString()
    {
        return string.Format("[GrindObjective MobID: {0}, Count: {1}]", (object)this.Objective.ID, (object)this.Objective.Count);
    }
}
