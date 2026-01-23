using System;
using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class ObjectiveNode : OrderNode
    {
        public ObjectiveNode(
            uint questId,
            QuestObjectType objectiveType,
            uint objectiveId,
            string objectiveName,
            int objectiveCount,
            int objectiveIndex)
            : base(OrderNodeType.Objective)
        {
            this.QuestId = questId;
            this.ObjectiveType = objectiveType;
            this.ObjectiveId = objectiveId;
            this.ObjectiveName = objectiveName;
            this.ObjectiveCount = objectiveCount;
            this.ObjectiveIndex = objectiveIndex;
        }

        public uint QuestId { get; private set; }

        public QuestObjectType ObjectiveType { get; private set; }

        public uint ObjectiveId { get; private set; }

        public string ObjectiveName { get; private set; }

        public int ObjectiveCount { get; private set; }

        public int ObjectiveIndex { get; private set; }

        public override string ToString()
        {
            return $"[ObjectiveNode QuestId: {QuestId}, Type: {ObjectiveType}, Id: {ObjectiveId}, Name: {ObjectiveName ?? "(null)"}, Count: {ObjectiveCount}, Index: {ObjectiveIndex}]";
        }

        public static ObjectiveNode FromXml(XElement element)
        {
            var questIdAttr = element.Attribute("QuestId");
            uint questId = 0;
            if (questIdAttr != null)
                uint.TryParse(questIdAttr.Value, out questId);

            QuestObjectType objectiveType = QuestObjectType.Npc;
            var typeAttr = element.Attribute("Type");
            if (typeAttr != null)
            {
                var parsedType = PickUpNode.ParseQuestObjectType(typeAttr.Value);
                if (parsedType.HasValue)
                    objectiveType = parsedType.Value;
            }

            var objectiveIdAttr = element.Attribute("Id");
            uint objectiveId = 0;
            if (objectiveIdAttr != null)
                uint.TryParse(objectiveIdAttr.Value, out objectiveId);

            string objectiveName = element.Attribute("Name")?.Value;

            var countAttr = element.Attribute("Count");
            int objectiveCount = 1;
            if (countAttr != null)
                int.TryParse(countAttr.Value, out objectiveCount);

            var indexAttr = element.Attribute("Index");
            int objectiveIndex = 0;
            if (indexAttr != null)
                int.TryParse(indexAttr.Value, out objectiveIndex);

            return new ObjectiveNode(questId, objectiveType, objectiveId, objectiveName, objectiveCount, objectiveIndex);
        }
    }
}
