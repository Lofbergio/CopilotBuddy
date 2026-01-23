using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;


namespace Styx.Logic.Profiles.Quest
{
    /// <summary>
    /// Node for grinding to a specific level.
    /// </summary>
    public class GrindToNode : OrderNode
    {
        public GrindToNode(float level, Func<bool> condition)
            : base(OrderNodeType.GrindTo)
        {
            Level = level;
            Condition = condition;
        }

        public GrindToNode(float level)
            : this(level, null)
        {
        }

        /// <summary>
        /// Target level to grind to.
        /// </summary>
        public float Level { get; private set; }

        /// <summary>
        /// Condition function for grinding.
        /// </summary>
        public Func<bool> Condition { get; private set; }

        /// <summary>
        /// Goal text to display while grinding.
        /// </summary>
        public string GoalText { get; private set; }

        public override string ToString()
        {
            return $"[GrindToNode Level: {Level} GoalText: {GoalText}]";
        }

        public new static GrindToNode FromXml(XElement element)
        {
            // Get goal text
            var goalTextAttr = element.Attributes()
                .FirstOrDefault(a => a.Name.LocalName.Equals("goaltext", StringComparison.OrdinalIgnoreCase));
            string goalText = goalTextAttr?.Value ?? "";

            // Check for condition attribute
            var conditionAttr = element.Attributes()
                .FirstOrDefault(a => a.Name.LocalName.Equals("condition", StringComparison.OrdinalIgnoreCase));
            if (conditionAttr != null)
            {
                var condition = ConditionHelper.ParseConditionString(conditionAttr.Value);
                if (condition == null)
                    throw new ProfileException($"Could not parse GrindTo Condition code: {conditionAttr.Value}");
                return new GrindToNode(-1f, condition) { GoalText = goalText };
            }

            // Check for level attribute
            var levelAttr = element.Attributes()
                .FirstOrDefault(a => a.Name.LocalName.Equals("level", StringComparison.OrdinalIgnoreCase));
            if (levelAttr != null)
            {
                if (!float.TryParse(levelAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var level))
                    throw new ProfileAttributeExpectedException<float>(levelAttr);
                return new GrindToNode(level) { GoalText = goalText };
            }

            throw new ProfileException("You need at least one level or condition attribute in GrindToNode!");
        }
    }
}
