// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedIf
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Bots.Quest.Actions;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Profiles.Quest;
using System;
using System.Collections.Generic;
using System.Drawing;
using TreeSharp;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedIf : ForcedBehavior
{
    private QuestOrder questOrder_0;
    private Composite composite_1;

    public ForcedIf(IfNode node)
    {
        this.IfNode = node != null ? node : throw new ArgumentNullException(nameof(node));
    }

    public IfNode IfNode { get; private set; }

    protected override Composite CreateBehavior()
    {
        Composite behavior = this.composite_1;
        if ((object)behavior == null)
            behavior = this.composite_1 = (Composite)new ForcedBehaviorExecutor(this.questOrder_0);
        return behavior;
    }

    public override void OnStart()
    {
        try
        {
            if (this.IfNode.Condition())
            {
                this.questOrder_0 = new QuestOrder(new OrderNodeCollection((IEnumerable<OrderNode>)this.IfNode.Body));
            }
            else
            {
                OrderNodeCollection orderNodeCollection_0;
                if (this.method_0(out orderNodeCollection_0))
                    this.questOrder_0 = new QuestOrder(new OrderNodeCollection((IEnumerable<OrderNode>)orderNodeCollection_0));
                else if (this.IfNode.Else != null)
                    this.questOrder_0 = new QuestOrder(new OrderNodeCollection((IEnumerable<OrderNode>)this.IfNode.Else.Body));
            }
        }
        catch (Exception ex)
        {
            Logging.Write(Color.Red, "Unable to evaluate compile condition in If tag. Please check your profile.");
            Logging.Write(Color.Red, "CopilotBuddy stopped!");
            Logging.WriteException(ex);
            TreeRoot.Stop();
        }
        if (this.questOrder_0 == null)
            return;
        this.questOrder_0.IgnoreCheckpoints = QuestState.Instance.Order.IgnoreCheckpoints;
        this.questOrder_0.UpdateNodes();
    }

    private bool method_0(out OrderNodeCollection orderNodeCollection_0)
    {
        bool flag;
        using (List<ElseIf>.Enumerator enumerator = this.IfNode.ElseIfs.GetEnumerator())
        {
            ElseIf current;
            do
            {
                if (enumerator.MoveNext())
                    current = enumerator.Current;
                else
                    goto label_6;
            }
            while (!current.Condition());
            orderNodeCollection_0 = current.Body;
            flag = true;
            goto label_7;
        }
    label_6:
        orderNodeCollection_0 = (OrderNodeCollection)null;
        return false;
    label_7:
        return flag;
    }

    public override bool IsDone
    {
        get
        {
            return this.questOrder_0 == null || this.questOrder_0.Nodes == null || this.questOrder_0.Nodes.Count <= 0;
        }
    }
}
