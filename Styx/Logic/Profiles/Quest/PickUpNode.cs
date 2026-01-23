using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Styx.Logic.Pathing;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class PickUpNode : OrderNode
    {
        public PickUpNode(
            WoWPoint giverLocation,
            uint giverId,
            string giverName,
            QuestObjectType? giverType,
            uint questId,
            string questName)
            : base(OrderNodeType.PickUp)
        {
            this.GiverLocation = giverLocation;
            this.GiverId = giverId;
            this.GiverName = giverName;
            this.GiverType = giverType;
            this.QuestId = questId;
            this.QuestName = questName;
        }

        public WoWPoint GiverLocation { get; private set; }

        public uint GiverId { get; private set; }

        public string GiverName { get; private set; }

        public QuestObjectType? GiverType { get; private set; }

        public uint QuestId { get; private set; }

        public string QuestName { get; private set; }

        public override string ToString()
        {
            return $"[PickUpNode GiverLocation: {GiverLocation}, GiverId: {GiverId}, GiverName: {GiverName ?? "(null)"}, GiverType: {GiverType?.ToString() ?? "(null)"}, QuestId: {QuestId}, QuestName: {QuestName ?? "(null)"}]";
        }

        public static PickUpNode FromXml(XElement element)
        {
            WoWPoint giverLocation = WoWPoint.Zero;
            try
            {
                giverLocation = ParseLocation(element);
            }
            catch { }

            var giverIdAttr = element.Attribute("GiverId");
            int giverId = 0;
            if (giverIdAttr != null && !int.TryParse(giverIdAttr.Value, out giverId))
                throw new ProfileAttributeExpectedException<int>(giverIdAttr);

            string giverName = element.Attribute("GiverName")?.Value;

            if (giverId == 0 && giverName == null)
                throw new ProfileMissingAttributeException<int>("GiverId", element);

            QuestObjectType? giverType = null;
            var giverTypeAttr = element.Attribute("GiverType");
            if (giverTypeAttr != null)
            {
                giverType = ParseQuestObjectType(giverTypeAttr.Value);
                if (!giverType.HasValue)
                    throw new ProfileAttributeExpectedException(giverTypeAttr, "Object", "Npc", "Item");
            }

            var questIdAttr = element.Attribute("QuestId");
            if (questIdAttr == null)
                throw new ProfileMissingAttributeException<int>("QuestId", element);
            if (!int.TryParse(questIdAttr.Value, out int questId))
                throw new ProfileAttributeExpectedException<int>(questIdAttr);

            string questName = element.Attribute("QuestName")?.Value;

            return new PickUpNode(giverLocation, (uint)giverId, giverName, giverType, (uint)questId, questName);
        }

        internal static QuestObjectType? ParseQuestObjectType(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "obj":
                case "object":
                case "gameobj":
                case "gameobject":
                    return QuestObjectType.GameObject;
                case "npc":
                case "unit":
                    return QuestObjectType.Npc;
                case "item":
                    return QuestObjectType.Item;
                default:
                    return null;
            }
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
