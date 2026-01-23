// Decompiled with JetBrains decompiler
// Type: Bots.Quest.Objectives.ActionMoveToGrindArea
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using CommonBehaviors.Actions;
using Styx.Logic;
using Styx.Logic.AreaManagement;
using TreeSharp;

#nullable disable
namespace Bots.Quest.Objectives;

public class ActionMoveToGrindArea : NavigationAction
{
    private readonly GrindArea grindArea_0;

    public ActionMoveToGrindArea(GrindArea area) => this.grindArea_0 = area;

    protected override RunStatus Run(object context)
    {
        Mount.StateMount((LocationRetriever)(() => this.grindArea_0.CurrentHotSpot.Position));
        return base.Run((object)this.grindArea_0.CurrentHotSpot.Position);
    }
}
