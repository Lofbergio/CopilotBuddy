using System;
using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class ObjectiveNode : OrderNode
    {
        public ObjectiveNode(
            uint questId,
            ObjectiveType objectiveType,
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

        public ObjectiveType ObjectiveType { get; private set; }

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

            ObjectiveType objectiveType = ObjectiveType.KillMob;
            var typeAttr = element.Attribute("Type");
            if (typeAttr != null)
            {
                if (!Enum.TryParse<ObjectiveType>(typeAttr.Value, true, out objectiveType))
                {
                    objectiveType = ObjectiveType.KillMob;  // Fallback
                }
            }

            // Get objective ID using type-specific aliases (HB 4.3.4: smethod_2)
            string[] idAliases = GetIdAliases(objectiveType);
            var objectiveIdAttr = GetAttributeByAliases(element, idAliases);
            uint objectiveId = 0;
            if (objectiveIdAttr != null)
                uint.TryParse(objectiveIdAttr.Value, out objectiveId);

            string objectiveName = element.Attribute("Name")?.Value;

            // Get count using type-specific aliases (HB 4.3.4: smethod_4)
            string[] countAliases = GetCountAliases(objectiveType);
            var countAttr = GetAttributeByAliases(element, countAliases);
            int objectiveCount = 1;
            if (countAttr != null)
                int.TryParse(countAttr.Value, out objectiveCount);

            var indexAttr = element.Attribute("Index");
            int objectiveIndex = -1;  // -1 = not specified, will search by ID instead
            if (indexAttr != null)
                int.TryParse(indexAttr.Value, out objectiveIndex);

            return new ObjectiveNode(questId, objectiveType, objectiveId, objectiveName, objectiveCount, objectiveIndex);
        }

        /// <summary>
        /// Gets ID attribute aliases based on objective type.
        /// HB 4.3.4: private static string[] smethod_2(ObjectiveType)
        /// </summary>
        private static string[] GetIdAliases(ObjectiveType objectiveType)
        {
            switch (objectiveType)
            {
                case ObjectiveType.KillMob:
                    return new[] { "Id", "Entry", "MobId", "MobEntry" };
                case ObjectiveType.CollectItem:
                    return new[] { "Id", "Entry", "ItemId", "ItemEntry" };
                case ObjectiveType.UseObject:
                    return new[] { "Id", "Entry", "GameObjectId", "ObjectId", "GameObjectEntry", "ObjectEntry", "UseGameObject", "UseObject" };
                default:
                    return new[] { "Id" };
            }
        }

        /// <summary>
        /// Gets count attribute aliases based on objective type.
        /// HB 4.3.4: private static string[] smethod_4(ObjectiveType)
        /// </summary>
        private static string[] GetCountAliases(ObjectiveType objectiveType)
        {
            switch (objectiveType)
            {
                case ObjectiveType.KillMob:
                    return new[] { "Count", "KillCount", "SlayCount" };
                case ObjectiveType.CollectItem:
                    return new[] { "Count", "CollectCount" };
                case ObjectiveType.UseObject:
                    return new[] { "Count", "GameObjectCount", "ObjectCount", "UseCount" };
                default:
                    return new[] { "Count" };
            }
        }

        /// <summary>
        /// Helper to get first matching attribute from array of aliases.
        /// HB 4.3.4: Class570.smethod_3() - part of obfuscated utilities
        /// </summary>
        private static XAttribute GetAttributeByAliases(XElement element, string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var attr = element.Attribute(alias);
                if (attr != null)
                    return attr;
            }
            return null;
        }
    }
}
