using System.Collections.Generic;
using System.Xml.Linq;
using Styx.Helpers;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class OrderNodeCollection : List<OrderNode>, INodeContainer
    {
        public OrderNodeCollection()
        {
        }

        public OrderNodeCollection(int capacity)
            : base(capacity)
        {
        }

        public OrderNodeCollection(IEnumerable<OrderNode> collection)
            : base(collection)
        {
        }

        public bool IgnoreCheckpoints { get; set; }

        public static OrderNodeCollection FromXml(XElement element)
        {
            OrderNodeCollection collection = new OrderNodeCollection();
            
            var ignoreAttr = element.Attribute("IgnoreCheckpoints");
            if (ignoreAttr != null)
            {
                bool.TryParse(ignoreAttr.Value, out bool result);
                collection.IgnoreCheckpoints = result;
            }

            foreach (XElement child in element.Elements())
            {
                try
                {
                    collection.Add(OrderNode.FromXml(child));
                }
                catch (ProfileException ex)
                {
                    if (StyxSettings.Instance.ProfileDebuggingMode)
                        Logging.WriteException(ex);
                    if (!string.IsNullOrEmpty(ex.Message))
                        Logging.Write($"Warning: {ex.Message}");
                }
            }

            return collection;
        }

        public IEnumerable<OrderNode> GetNodes() => this;
    }
}
