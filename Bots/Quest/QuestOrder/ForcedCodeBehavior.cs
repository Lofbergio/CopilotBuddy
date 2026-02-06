// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedCodeBehavior
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Styx.Helpers;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeSharp;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedCodeBehavior : ForcedBehavior
{
    private readonly CustomForcedBehavior customBehavior;

    public ForcedCodeBehavior(CodeNode codeNode)
    {
        if (codeNode == null)
            throw new ArgumentNullException(nameof(codeNode));
        this.customBehavior = ForcedCodeBehavior.CreateCustomBehaviorInstance(codeNode.AssemblyGetter(), codeNode.Arguments);
        if (this.customBehavior == null)
            throw new Exception("Unable to create instance of UserDefinedObjective");
        this.customBehavior.Element = codeNode.Element;
    }

    private static CustomForcedBehavior CreateCustomBehaviorInstance(
        Assembly assembly,
        Dictionary<string, string> arguments)
    {
        if (arguments == null)
        {
            arguments = new Dictionary<string, string>();
        }
        return ((IEnumerable<Type>)assembly.GetTypes())
            .Where<Type>((Func<Type, bool>)(behaviorType => behaviorType.IsSubclassOf(typeof(CustomForcedBehavior))))
            .Select<Type, CustomForcedBehavior>((Func<Type, CustomForcedBehavior>)(behaviorType =>
            {
                try
                {
                    return (CustomForcedBehavior)Activator.CreateInstance(behaviorType, new object[] { arguments });
                }
                catch
                {
                    return null;
                }
            }))
            .FirstOrDefault<CustomForcedBehavior>();
    }

    protected override Composite CreateBehavior() => this.customBehavior.Branch;

    public override bool IsDone => this.customBehavior.IsDone;

    public override void OnStart()
    {
        Logging.Write("[Code] Executing custom behavior: {0}", (object)this.customBehavior.GetType().Name);
        this.customBehavior.OnStart();
    }

    public override void OnTick() => this.customBehavior.OnTick();

    public override void Dispose() => this.customBehavior.Dispose();
}
