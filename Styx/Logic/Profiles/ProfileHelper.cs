#nullable disable
using System;
using System.Globalization;
using System.Xml.Linq;
using Styx.Logic.Pathing;

namespace Styx.Logic.Profiles
{
    /// <summary>
    /// Helper methods for profile parsing.
    /// </summary>
    internal static class ProfileHelper
    {
        /// <summary>
        /// Parses a WoWPoint from an XML element with X, Y, Z attributes.
        /// </summary>
        public static WoWPoint ParseLocation(XElement element)
        {
            float x = 0, y = 0, z = 0;

            foreach (var attr in element.Attributes())
            {
                switch (attr.Name.LocalName.ToLowerInvariant())
                {
                    case "x":
                        float.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
                        break;
                    case "y":
                        float.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
                        break;
                    case "z":
                        float.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out z);
                        break;
                }
            }

            return new WoWPoint(x, y, z);
        }

        /// <summary>
        /// Parses a boolean attribute value.
        /// </summary>
        public static bool ParseBool(XAttribute attr, bool defaultValue = false)
        {
            if (attr == null) return defaultValue;
            
            string value = attr.Value.ToLowerInvariant();
            return value == "true" || value == "1" || value == "yes";
        }

        /// <summary>
        /// Parses an integer attribute value.
        /// </summary>
        public static int ParseInt(XAttribute attr, int defaultValue = 0)
        {
            if (attr == null) return defaultValue;
            return int.TryParse(attr.Value, out int result) ? result : defaultValue;
        }

        /// <summary>
        /// Parses a float attribute value.
        /// </summary>
        public static float ParseFloat(XAttribute attr, float defaultValue = 0f)
        {
            if (attr == null) return defaultValue;
            return float.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) 
                ? result 
                : defaultValue;
        }
    }
}
