using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class ClearGrindAreaNode : OrderNode
    {
        public ClearGrindAreaNode()
            : base(OrderNodeType.ClearGrindArea)
        {
        }

        public static ClearGrindAreaNode FromXml(XElement element) => new ClearGrindAreaNode();

        public override string ToString() => "[ClearGrindAreaNode]";
    }
}
