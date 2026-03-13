using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Styx.Helpers
{
    public static class XmlUtils
    {
        public static string? FindAttributeValue(XElement element, string name, bool caseSensitive)
        {
            IEnumerable<XAttribute> attributes = element.Attributes();
            XAttribute? attr = caseSensitive
                ? attributes.FirstOrDefault(a => a.Name == name)
                : attributes.FirstOrDefault(a => a.Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase));

            return attr?.Value;
        }

        public static bool GetInt32Attribute(XElement element, string name, int minValue, int maxValue, out int value)
        {
            value = 0;
            string? text = FindAttributeValue(element, name, false);
            if (string.IsNullOrEmpty(text))
                return false;
            if (!int.TryParse(text, out int parsed))
                return false;
            if (parsed < minValue || parsed > maxValue)
                return false;

            value = parsed;
            return true;
        }
    }
}
