using System.Xml.Linq;
using Styx.Logic.Pathing;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class TurnInNode : OrderNode
    {
        public TurnInNode(
            WoWPoint turnInLocation,
            uint turnInId,
            string turnInName,
            QuestObjectType? turnInType,
            uint questId,
            string questName)
            : base(OrderNodeType.TurnIn)
        {
            this.TurnInId = turnInId;
            this.TurnInName = turnInName;
            this.TurnInType = turnInType;
            this.QuestId = questId;
            this.QuestName = questName;
            this.TurnInLocation = turnInLocation;
        }

        public WoWPoint TurnInLocation { get; private set; }

        public uint TurnInId { get; private set; }

        public string TurnInName { get; private set; }

        public QuestObjectType? TurnInType { get; private set; }

        public uint QuestId { get; private set; }

        public string QuestName { get; private set; }

        public override string ToString()
        {
            return $"[TurnInNode TurnInId: {TurnInId}, TurnInName: {TurnInName ?? "(null)"}, QuestId: {QuestId}, QuestName: {QuestName ?? "(null)"}]";
        }

        public static TurnInNode FromXml(XElement element)
        {
            WoWPoint turnInLocation = WoWPoint.Zero;
            try
            {
                turnInLocation = ParseLocation(element);
            }
            catch { }

            var turnInIdAttr = element.Attribute("TurnInId");
            int turnInId = 0;
            if (turnInIdAttr != null && !int.TryParse(turnInIdAttr.Value, out turnInId))
                throw new ProfileAttributeExpectedException<int>(turnInIdAttr);

            string turnInName = element.Attribute("TurnInName")?.Value;

            if (turnInId == 0 && turnInName == null)
                throw new ProfileMissingAttributeException<int>("TurnInId", element);

            QuestObjectType? turnInType = null;
            var turnInTypeAttr = element.Attribute("TurnInType");
            if (turnInTypeAttr != null)
            {
                turnInType = PickUpNode.ParseQuestObjectType(turnInTypeAttr.Value);
                if (turnInType.HasValue && turnInType.Value == QuestObjectType.Item)
                    throw new ProfileAttributeExpectedException(turnInTypeAttr, "Object", "Npc");
            }

            var questIdAttr = element.Attribute("QuestId");
            int questId = 0;
            if (questIdAttr != null)
                int.TryParse(questIdAttr.Value, out questId);

            string questName = element.Attribute("QuestName")?.Value;

            return new TurnInNode(turnInLocation, (uint)turnInId, turnInName, turnInType, (uint)questId, questName);
        }

        private static WoWPoint ParseLocation(XElement element)
        {
            var xAttr = element.Attribute("X");
            var yAttr = element.Attribute("Y");
            var zAttr = element.Attribute("Z");

            if (xAttr == null || yAttr == null || zAttr == null)
                throw new ProfileException("Missing X, Y or Z attribute");

            float.TryParse(xAttr.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float x);
            float.TryParse(yAttr.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float y);
            float.TryParse(zAttr.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float z);

            return new WoWPoint(x, y, z);
        }
    }
}
