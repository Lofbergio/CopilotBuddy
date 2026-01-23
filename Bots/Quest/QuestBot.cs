// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestBot
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Bots.Grind;
using Bots.Quest.Actions;
using Bots.Quest.QuestOrder;
using CommonBehaviors.Actions;
using CommonBehaviors.Decorators;
using Levelbot;
using Levelbot.Actions.Combat;
using Levelbot.Decorators.Combat;
using Styx;
using Styx.Common;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using TreeSharp;

#nullable disable
namespace Bots.Quest;

public class QuestBot : BotBase
{
    private static Composite composite_0;

    public override string Name => "Questing";

    public static Composite CreateRoot()
    {
        return (Composite)new PrioritySelector(new Composite[7]
        {
            (Composite)LevelBot.CreateDeathBehavior(),
            LevelBot.CreateCombatBehavior(),
            LevelBot.CreateLootBehavior(),
            QuestBot.CreateTargetingBehavior(),
            (Composite)LevelBot.CreateVendorBehavior(),
            (Composite)CreateQuestOrderBehavior(),
            (Composite)LevelBot.CreateRoamBehavior()
        });
    }

    public override Composite Root
    {
        get
        {
            Composite root = QuestBot.composite_0;
            if ((object)root == null)
                root = QuestBot.composite_0 = QuestBot.CreateRoot();
            return root;
        }
    }

    public override Form ConfigurationForm => (Form)new FormLevelbotSettings();

    public override PulseFlags PulseFlags => PulseFlags.All;

    // QuestBot requires a profile to function (HB 6.2.3 pattern)
    public override bool RequiresProfile => true;

    public override void Start()
    {
        if (ProfileManager.CurrentOuterProfile == (Profile)null)
            throw new HonorbuddyUnableToStartException("You haven't loaded a profile.");
        if (ProfileManager.CurrentProfile.QuestOrder.Count <= 0)
            throw new HonorbuddyUnableToStartException("Can not start quest bot - this profile does not contain a quest order.");
        QuestState.Instance.InitializeFromProfile(ProfileManager.CurrentProfile);
        LootTargeting.Instance.IncludeTargetsFilter += new IncludeTargetsFilterDelegate(LevelBot.LevelbotIncludeLootsFilter);
        Targeting.Instance.IncludeTargetsFilter += new IncludeTargetsFilterDelegate(QuestBot.QuestIncludeTargetsFilter);
        QuestState.Instance.Order.OnNoMoreNodes += new EventHandler<EventArgs>(OnNoMoreNodes);
        if (StyxSettings.Instance.ProfileDebuggingMode && !CheckQuestBehaviors(ProfileManager.CurrentOuterProfile))
            throw new HonorbuddyUnableToStartException("Could not construct all quest behaviors.");
    }

    private static bool CheckQuestBehaviors(Profile profile_0)
    {
        bool flag = true;
        if (profile_0.QuestOrder != null && profile_0.QuestOrder.Count > 0 && !CheckNodeContainer((INodeContainer)profile_0.QuestOrder))
            flag = false;
        foreach (Profile subProfile in profile_0.SubProfiles)
        {
            if (subProfile != (Profile)null && subProfile.QuestOrder != null && subProfile.QuestOrder.Count > 0 && !CheckNodeContainer((INodeContainer)subProfile.QuestOrder))
                flag = false;
        }
        return flag;
    }

    private static bool CheckNodeContainer(INodeContainer inodeContainer_0)
    {
        bool flag = true;
        foreach (OrderNode node in inodeContainer_0.GetNodes())
        {
            if (node is INodeContainer inodeContainer_0_1 && !CheckNodeContainer(inodeContainer_0_1))
                flag = false;
            if (node is CodeNode codeNode)
            {
                try
                {
                    new ForcedCodeBehavior(codeNode).Dispose();
                }
                catch (TargetInvocationException ex)
                {
                    Logging.Write("Exception thrown while trying to construct quest behavior. Tag:{0}{1}{0}{2}", (object)Environment.NewLine, (object)codeNode.Element.ToString(), (object)ex.InnerException.ToString());
                    Logging.Write("----------------------");
                }
                catch (Exception ex)
                {
                    Logging.Write("Exception thrown while trying to construct quest behavior. Tag:{0}{1}{0}{2}", (object)Environment.NewLine, (object)codeNode.Element.ToString(), (object)ex.ToString());
                    Logging.Write("----------------------");
                }
            }
        }
        return flag;
    }

    private static void OnNoMoreNodes(object sender, EventArgs e)
    {
        Logging.Write(Color.Red, "Nothing more to do. Stopping bot.");
        TreeRoot.Stop();
    }

    public override void Stop()
    {
        if (QuestState.Instance.Order.CurrentBehavior != null)
        {
            QuestState.Instance.Order.CurrentBehavior.Dispose();
            QuestState.Instance.Order.CurrentBehavior = (ForcedBehavior)null;
        }
        LootTargeting.Instance.IncludeTargetsFilter -= new IncludeTargetsFilterDelegate(LevelBot.LevelbotIncludeLootsFilter);
        Targeting.Instance.IncludeTargetsFilter -= new IncludeTargetsFilterDelegate(QuestBot.QuestIncludeTargetsFilter);
    }

    private float GetPathPrecision() => MathEx.Clamp(StyxWoW.Me.MovementInfo.CurrentSpeed * 0.15f, 1.5f, 10f);

    public override void Pulse() => Navigator.PathPrecision = this.GetPathPrecision();

    private static Composite CreateTargetingBehavior()
    {
        return (Composite)new Decorator((CanRunDecoratorDelegate)(object_0 => StyxWoW.Me.IsMoving), (Composite)new DecoratorIsNotPoiType(PoiType.Kill, (Composite)new Decorator((CanRunDecoratorDelegate)(object_0 => !StyxWoW.Me.Combat), (Composite)new DecoratorNeedToFindTarget((Composite)new Sequence(new Composite[2]
        {
            (Composite)new ActionSetTarget(),
            (Composite)new Wait(5, (CanRunDecoratorDelegate)(object_0 => (WoWObject)StyxWoW.Me.CurrentTarget != (WoWObject)null), (Composite)new ActionSetPoi((RetrieveBotPoiDelegate)(object_0 => new BotPoi((WoWObject)StyxWoW.Me.CurrentTarget, PoiType.Kill))))
        })))));
    }

    private static PrioritySelector CreateQuestOrderBehavior()
    {
        return new PrioritySelector(new Composite[2]
        {
            (Composite)new ForcedBehaviorExecutor(QuestState.Instance.Order),
            (Composite)new ActionAlwaysSucceed()
        });
    }

    public static void QuestIncludeTargetsFilter(
        List<WoWObject> incomingUnits,
        HashSet<WoWObject> outgoingUnits)
    {
        WoWPoint location = StyxWoW.Me.Location;
        float rotation = StyxWoW.Me.Rotation;
        bool isMoving = StyxWoW.Me.IsMoving;
        if (StyxWoW.Me.Combat)
            return;
        List<WoWUnit> woWunitList = new List<WoWUnit>();
        for (int index = 0; index < incomingUnits.Count; ++index)
        {
            if (incomingUnits[index] is WoWUnit && !(incomingUnits[index] is WoWPlayer))
            {
                WoWUnit unit = incomingUnits[index].ToUnit();
                if (unit.IsPet)
                {
                    WoWUnit ownedByUnit = unit.OwnedByUnit;
                    if ((WoWObject)ownedByUnit != (WoWObject)null && ownedByUnit.IsPlayer)
                        continue;
                }
                if (!LevelBot.IsTooNearBlackspot((IEnumerable<Blackspot>)ProfileManager.CurrentProfile.Blackspots, unit.Location) && (!StyxWoW.Me.Mounted || Targeting.Instance.KillBetweenHotspots) && (isMoving && WoWMathHelper.IsInPath(unit, location, location.RayCast(rotation, System.Math.Max((float)Targeting.PullDistance, 30f))) || !isMoving && WoWMathHelper.IsInPath(unit, location, location.RayCast(rotation, 10f))) && unit.MyReaction < WoWUnitReaction.Neutral)
                    woWunitList.Add(unit);
            }
        }
        WoWPoint traceLinePos = StyxWoW.Me.GetTraceLinePos();
        WorldLine[] lines = new WorldLine[woWunitList.Count];
        for (int index = 0; index < woWunitList.Count; ++index)
            lines[index] = new WorldLine(traceLinePos, woWunitList[index].GetTraceLinePos());
        bool[] hitResults;
        GameWorld.MassTraceLine(lines, GameWorld.CGWorldFrameHitFlags.HitTestLOS, out hitResults);
        for (int index = 0; index < woWunitList.Count; ++index)
        {
            if (!hitResults[index])
                outgoingUnits.Add((WoWObject)woWunitList[index]);
        }
    }
}
