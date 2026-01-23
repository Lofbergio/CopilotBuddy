using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class ClearMailboxNode : OrderNode
    {
        public ClearMailboxNode()
            : base(OrderNodeType.ClearMailbox)
        {
        }

        public static ClearMailboxNode FromXml(XElement element) => new ClearMailboxNode();

        public override string ToString() => "[ClearMailboxNode]";
    }
}
