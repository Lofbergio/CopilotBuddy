using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class AbandonQuestNode : OrderNode
    {
        public AbandonQuestNode(uint questId)
            : base(OrderNodeType.AbandonQuest)
        {
            this.QuestId = questId;
        }

        public uint QuestId { get; private set; }

        public static AbandonQuestNode FromXml(XElement element)
        {
            var attribute = element.Attribute("QuestId") ?? element.Attribute("Id");
            if (attribute == null)
                throw new ProfileMissingAttributeException<int>("QuestId", element);
            
            int result;
            if (!int.TryParse(attribute.Value, out result))
                throw new ProfileAttributeExpectedException<int>(attribute);
            
            return new AbandonQuestNode((uint)result);
        }

        public override string ToString() => $"[AbandonQuestNode QuestId: {this.QuestId}]";
    }
}
