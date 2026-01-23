using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Styx.Helpers;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class ElseIf
    {
        public ElseIf(Func<bool> condition, IEnumerable<OrderNode> body)
        {
            this.Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            this.Body = body != null ? new OrderNodeCollection(body) : new OrderNodeCollection();
        }

        public Func<bool> Condition { get; private set; }

        public OrderNodeCollection Body { get; private set; }

        public static ElseIf FromXml(XElement element)
        {
            var condAttr = element.Attribute("Condition") ?? element.Attribute("condition");
            if (condAttr == null)
                throw new ProfileMissingAttributeException("Condition", element);

            Func<bool> condition = ConditionHelper.ParseConditionString(condAttr.Value);
            if (condition == null)
                throw new ProfileException($"Could not parse ElseIf Condition code: {condAttr.Value}");

            List<OrderNode> body = new List<OrderNode>();
            foreach (XElement child in element.Elements().Where(e => e.NodeType != XmlNodeType.Comment))
            {
                try
                {
                    body.Add(OrderNode.FromXml(child));
                }
                catch (ProfileException ex)
                {
                    if (StyxSettings.Instance.ProfileDebuggingMode)
                        Logging.WriteException(ex);
                    throw new ProfileException($"Could not parse ElseIf body node: {ex.Message}", ex);
                }
            }
            return new ElseIf(condition, body);
        }
    }
}
