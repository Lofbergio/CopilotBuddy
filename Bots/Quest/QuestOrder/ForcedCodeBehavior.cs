// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedCodeBehavior
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

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
    private readonly CustomForcedBehavior customForcedBehavior_0;

    public ForcedCodeBehavior(CodeNode codeNode)
    {
        if (codeNode == null)
            throw new ArgumentNullException(nameof(codeNode));
        this.customForcedBehavior_0 = ForcedCodeBehavior.smethod_0(codeNode.AssemblyGetter(), codeNode.Arguments);
        if (this.customForcedBehavior_0 == null)
            throw new Exception("Unable to create instance of UserDefinedObjective");
        this.customForcedBehavior_0.Element = codeNode.Element;
    }

    private static CustomForcedBehavior smethod_0(
        Assembly assembly_0,
        Dictionary<string, string> dictionary_0)
    {
        if (dictionary_0 == null)
        {
            dictionary_0 = new Dictionary<string, string>();
        }
        return ((IEnumerable<Type>)assembly_0.GetTypes())
            .Where<Type>((Func<Type, bool>)(type_0 => type_0.IsSubclassOf(typeof(CustomForcedBehavior))))
            .Select<Type, CustomForcedBehavior>((Func<Type, CustomForcedBehavior>)(type_0 =>
            {
                try
                {
                    return (CustomForcedBehavior)Activator.CreateInstance(type_0, new object[] { dictionary_0 });
                }
                catch
                {
                    return null;
                }
            }))
            .FirstOrDefault<CustomForcedBehavior>();
    }

    protected override Composite CreateBehavior() => this.customForcedBehavior_0.Branch;

    public override bool IsDone => this.customForcedBehavior_0.IsDone;

    public override void OnStart() => this.customForcedBehavior_0.OnStart();

    public override void OnTick() => this.customForcedBehavior_0.OnTick();

    public override void Dispose() => this.customForcedBehavior_0.Dispose();
}
