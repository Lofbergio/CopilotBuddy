using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class SetMailboxNode : OrderNode
    {
        public SetMailboxNode(List<Mailbox> mailboxes)
            : base(OrderNodeType.SetMailbox)
        {
            this.Mailboxes = mailboxes;
        }

        public SetMailboxNode(Mailbox mailbox)
            : this(new List<Mailbox>() { mailbox })
        {
        }

        public List<Mailbox> Mailboxes { get; private set; }

        public static SetMailboxNode FromXml(XElement element)
        {
            // Look for either <Mailbox> or <Mailboxes> child element
            var mailboxElement = element.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals("mailbox", StringComparison.OrdinalIgnoreCase));
            var mailboxesElement = element.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals("mailboxes", StringComparison.OrdinalIgnoreCase));

            XElement targetElement = mailboxElement ?? mailboxesElement;
            if (targetElement == null)
                throw new ProfileMissingElementException("Mailbox", element);

            if (targetElement.Name.LocalName.Equals("mailbox", StringComparison.OrdinalIgnoreCase))
            {
                // Single mailbox
                Mailbox mailbox;
                try
                {
                    mailbox = new Mailbox(targetElement);
                }
                catch (ProfileException ex)
                {
                    throw new ProfileException("Could not parse SetMailbox node", ex);
                }
                return new SetMailboxNode(mailbox);
            }

            // Multiple mailboxes
            var mailboxes = new List<Mailbox>();
            foreach (var childElement in targetElement.Elements()
                .Where(e => e.Name.LocalName.Equals("mailbox", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    mailboxes.Add(new Mailbox(childElement));
                }
                catch (ProfileException ex)
                {
                    throw new ProfileException("Could not parse SetMailbox node", ex);
                }
            }
            return new SetMailboxNode(mailboxes);
        }

        public override string ToString()
        {
            return $"[SetMailboxNode Mailboxes: {string.Join(", ", Mailboxes.ConvertAll(m => m.ToString()).ToArray())}]";
        }
    }
}
