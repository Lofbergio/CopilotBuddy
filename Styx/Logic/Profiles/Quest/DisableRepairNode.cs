// Decompiled with JetBrains decompiler
// Type: Styx.Logic.Profiles.Quest.DisableRepairNode
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe


using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest;

public class DisableRepairNode : OrderNode
{
    public DisableRepairNode()
        : base(OrderNodeType.DisableRepair)
    {
    }

    public new static OrderNode FromXml(XElement element) => new DisableRepairNode();

    public override string ToString() => "[DisableRepairNode]";
}
