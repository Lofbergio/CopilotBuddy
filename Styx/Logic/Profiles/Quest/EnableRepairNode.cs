// Decompiled with JetBrains decompiler
// Type: Styx.Logic.Profiles.Quest.EnableRepairNode
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe


using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest;

public class EnableRepairNode : OrderNode
{
    public EnableRepairNode()
        : base(OrderNodeType.EnableRepair)
    {
    }

    public new static OrderNode FromXml(XElement element) => new EnableRepairNode();

    public override string ToString() => "[EnableRepairNode]";
}
