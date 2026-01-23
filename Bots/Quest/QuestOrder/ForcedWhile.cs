// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedWhile
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
using System.Threading;
using TreeSharp;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedWhile : ForcedBehavior
{
    private Class104 class104_0;

    public ForcedWhile(WhileNode node)
    {
        this.WhileNode = node != null ? node : throw new ArgumentNullException(nameof(node));
    }

    public WhileNode WhileNode { get; private set; }

    protected override Composite CreateBehavior()
    {
        return (Composite)(this.class104_0 ?? (this.class104_0 = new Class104(this.WhileNode)));
    }

    public override bool IsDone
    {
        get => (Composite)this.class104_0 != (Composite)null && this.class104_0.IsDone;
    }

    private class Class104 : Composite
    {
        private readonly WhileNode whileNode_0;
        private ForcedBehaviorExecutor forcedBehaviorExecutor_0;
        private bool bool_0;

        public Class104(WhileNode node) => this.whileNode_0 = node;

        public bool IsDone { get; private set; }

        protected override IEnumerable<RunStatus> Execute(object context)
        {
            if (!this.bool_0)
            {
                bool flag;
                try
                {
                    flag = this.whileNode_0.Condition();
                }
                catch (Exception ex)
                {
                    if (ex is ThreadAbortException)
                        throw;
                    Logging.Write(Color.Red, "Unable to evaluate compile condition in If tag. Please check your profile.");
                    Logging.Write(Color.Red, "CopilotBuddy stopped!");
                    Logging.WriteException(ex);
                    TreeRoot.Stop();
                    yield break;
                }
                if (!flag)
                {
                    this.IsDone = true;
                    yield return RunStatus.Success;
                    yield break;
                }
                this.bool_0 = true;
                QuestOrder order = new QuestOrder(new OrderNodeCollection((IEnumerable<OrderNode>)this.whileNode_0.Body))
                {
                    IgnoreCheckpoints = QuestState.Instance.Order.IgnoreCheckpoints
                };
                order.UpdateNodes();
                this.forcedBehaviorExecutor_0 = new ForcedBehaviorExecutor(order);
            }
            if (this.forcedBehaviorExecutor_0.Order.Nodes.Count <= 0)
            {
                this.bool_0 = false;
                yield return RunStatus.Success;
            }
            else
            {
                this.forcedBehaviorExecutor_0.Start(context);
                while (this.forcedBehaviorExecutor_0.Tick(context) == RunStatus.Running)
                    yield return RunStatus.Running;
                this.forcedBehaviorExecutor_0.Stop(context);
                yield return this.forcedBehaviorExecutor_0.LastStatus ?? RunStatus.Failure;
            }
        }
    }
}
