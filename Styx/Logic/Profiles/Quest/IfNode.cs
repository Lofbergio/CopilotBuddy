using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Styx.Helpers;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class IfNode : OrderNode, INodeContainer
    {
        public IfNode(Func<bool> condition, IEnumerable<OrderNode> body)
            : base(OrderNodeType.If)
        {
            this.Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            this.Body = body != null ? new OrderNodeCollection(body) : new OrderNodeCollection();
            this.ElseIfs = new List<ElseIf>();
        }

        public IfNode(Func<bool> condition, IEnumerable<OrderNode> body, IEnumerable<ElseIf> elseIfs)
            : this(condition, body)
        {
            if (elseIfs != null)
                this.ElseIfs.AddRange(elseIfs);
        }

        public IfNode(Func<bool> condition, IEnumerable<OrderNode> body, IEnumerable<ElseIf> elseIfs, Else @else)
            : this(condition, body, elseIfs)
        {
            this.Else = @else;
        }

        public IfNode(Func<bool> condition, IEnumerable<OrderNode> body, Else @else)
            : this(condition, body, null, @else)
        {
        }

        public Func<bool> Condition { get; private set; }

        public OrderNodeCollection Body { get; private set; }

        public List<ElseIf> ElseIfs { get; private set; }

        public Else Else { get; private set; }

        public static IfNode FromXml(XElement element)
        {
            var condAttr = element.Attribute("Condition") ?? element.Attribute("condition");
            if (condAttr == null)
                throw new ProfileMissingAttributeException("condition", element);

            Func<bool> condition = ConditionHelper.ParseConditionString(condAttr.Value);
            if (condition == null)
                throw new ProfileException($"Could not parse If Condition code: {condAttr.Value}");

            // Parse ElseIf nodes
            List<ElseIf> elseIfList = new List<ElseIf>();
            foreach (XElement elseIfElem in element.Elements().Where(e => 
                e.Name.ToString().Equals("elseif", StringComparison.OrdinalIgnoreCase)))
            {
                elseIfList.Add(ElseIf.FromXml(elseIfElem));
            }

            // Parse Else node
            Else @else = null;
            XElement elseElem = element.Elements().FirstOrDefault(e => 
                e.Name.ToString().Equals("else", StringComparison.OrdinalIgnoreCase));
            if (elseElem != null)
                @else = Else.FromXml(elseElem);

            // Parse body nodes (excluding else/elseif)
            List<OrderNode> body = new List<OrderNode>();
            foreach (XElement child in element.Elements().Where(e => e.NodeType != XmlNodeType.Comment))
            {
                string name = child.Name.ToString().ToLowerInvariant();
                if (name == "else" || name == "elseif")
                    continue;

                try
                {
                    body.Add(OrderNode.FromXml(child));
                }
                catch (ProfileException ex)
                {
                    if (StyxSettings.Instance.ProfileDebuggingMode)
                        Logging.WriteException(ex);
                    throw new ProfileException("Could not parse If body node", ex);
                }
            }

            return new IfNode(condition, body, elseIfList, @else);
        }

        public IEnumerable<OrderNode> GetNodes()
        {
            if (this.Body != null)
            {
                foreach (var node in this.Body)
                    yield return node;
            }

            if (this.ElseIfs != null)
            {
                foreach (var elseIf in this.ElseIfs)
                {
                    if (elseIf.Body != null)
                    {
                        foreach (var node in elseIf.Body)
                            yield return node;
                    }
                }
            }

            if (this.Else?.Body != null)
            {
                foreach (var node in this.Else.Body)
                    yield return node;
            }
        }
    }
}
