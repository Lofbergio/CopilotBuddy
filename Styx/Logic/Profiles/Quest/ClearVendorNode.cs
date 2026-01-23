using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class ClearVendorNode : OrderNode
    {
        public ClearVendorNode()
            : base(OrderNodeType.ClearVendor)
        {
        }

        public static ClearVendorNode FromXml(XElement element) => new ClearVendorNode();

        public override string ToString() => "[ClearVendorNode]";
    }
}
