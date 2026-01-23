using System;
using System.Globalization;
using System.Xml.Linq;
using Styx.Logic.Pathing;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class MoveToNode : OrderNode
    {
        public MoveToNode(WoWPoint location, string locationName, float precision, uint questId)
            : base(OrderNodeType.MoveTo)
        {
            this.Location = location;
            this.LocationName = locationName ?? $"<{location.X}, {location.Y}, {location.Z}>";
            this.Precision = precision;
            this.QuestId = questId;
        }

        public WoWPoint Location { get; private set; }

        public string LocationName { get; private set; }

        public float Precision { get; private set; }

        public uint QuestId { get; private set; }

        public override string ToString() => $"[MoveToNode Location: {this.Location}]";

        public static MoveToNode FromXml(XElement element)
        {
            WoWPoint location;
            try
            {
                location = ParseLocation(element);
            }
            catch (ProfileException ex)
            {
                throw new ProfileException("Could not parse X, Y and Z in MoveToNode", ex);
            }

            string locationName = (element.Attribute("Name") ?? element.Attribute("DestName"))?.Value;

            var precAttr = element.Attribute("Precision");
            float precision = 1.5f;
            if (precAttr != null)
            {
                if (!float.TryParse(precAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out precision))
                    throw new ProfileAttributeExpectedException<float>(precAttr);
            }

            var questAttr = element.Attribute("QuestId");
            uint questId = 0;
            if (questAttr != null && !uint.TryParse(questAttr.Value, out questId))
                throw new ProfileAttributeExpectedException<int>(questAttr);

            return new MoveToNode(location, locationName, precision, questId);
        }

        private static WoWPoint ParseLocation(XElement element)
        {
            var xAttr = element.Attribute("X");
            var yAttr = element.Attribute("Y");
            var zAttr = element.Attribute("Z");

            if (xAttr == null || yAttr == null || zAttr == null)
                throw new ProfileException("Missing X, Y or Z attribute");

            if (!float.TryParse(xAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
                throw new ProfileAttributeExpectedException<float>(xAttr);
            if (!float.TryParse(yAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                throw new ProfileAttributeExpectedException<float>(yAttr);
            if (!float.TryParse(zAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                throw new ProfileAttributeExpectedException<float>(zAttr);

            return new WoWPoint(x, y, z);
        }
    }
}
