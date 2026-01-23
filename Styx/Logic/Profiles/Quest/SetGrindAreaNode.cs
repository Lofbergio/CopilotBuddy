// Decompiled with JetBrains decompiler
// Type: Styx.Logic.Profiles.Quest.SetGrindAreaNode
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Styx.Logic.AreaManagement;

using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest;

public class SetGrindAreaNode : OrderNode
{
    public SetGrindAreaNode(GrindArea area)
        : base(OrderNodeType.SetGrindArea)
    {
        this.Area = area;
    }

    public GrindArea Area { get; private set; }

    public GrindArea GetArea() => this.Area;

    public new static OrderNode FromXml(XElement element)
    {
        var grindAreaElement = element.Element("GrindArea") ?? element.Element("grindarea");
        if (grindAreaElement == null)
            throw new ProfileMissingElementException("GrindArea", element);
        return new SetGrindAreaNode(GrindArea.FromXML(grindAreaElement));
    }

    public override string ToString() => $"[SetGrindAreaNode Area: {this.Area}]";
}
