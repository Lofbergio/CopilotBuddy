using System;
using System.Globalization;
using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class CheckpointNode : OrderNode
    {
        public CheckpointNode(float level)
            : base(OrderNodeType.Checkpoint)
        {
            this.Level = level;
        }

        public CheckpointNode(float level, XElement xml)
            : base(OrderNodeType.Checkpoint, xml)
        {
            this.Level = level;
        }

        public float Level { get; private set; }

        public static CheckpointNode FromXml(XElement element)
        {
            var attribute = element.Attribute("Level") ?? element.Attribute("level");
            if (attribute == null)
                return null;
            
            float result;
            if (!float.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                throw new ProfileAttributeExpectedException<float>(attribute);
            
            return new CheckpointNode(result, element);
        }

        public override string ToString() => $"[CheckpointNode Level: {this.Level}]";
    }
}
