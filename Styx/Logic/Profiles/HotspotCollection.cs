using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Styx.Logic.Profiles
{
	public class HotspotCollection : List<WoWPoint>
	{
		public HotspotCollection()
		{
		}

		public HotspotCollection(int capacity)
			: base(capacity)
		{
		}

		public HotspotCollection(IEnumerable<WoWPoint> collection)
			: base(collection)
		{
		}

		public WoWPoint FindClosestTo(WoWPoint loc)
		{
			if (Count <= 0)
			{
				throw new Exception("Hotspot collection is empty.");
			}

			WoWPoint closest = WoWPoint.Zero;
			float minDistSqr = float.MaxValue;

			for (int i = 0; i < Count; i++)
			{
				WoWPoint point = this[i];
				float distSqr = point.DistanceSqr(loc);
				if (distSqr < minDistSqr)
				{
					minDistSqr = distSqr;
					closest = this[i];
				}
			}

			return closest;
		}

		internal static HotspotCollection FromXElement(XElement element, params string[] elementNames)
		{
			HotspotCollection hotspotCollection = new HotspotCollection();
			foreach (XElement xelement in element.Elements())
			{
				try
				{
					if (elementNames.Contains(xelement.Name.ToString(), StringComparer.InvariantCultureIgnoreCase))
					{
						float? x = null;
						float? y = null;
						float? z = null;
						foreach (XAttribute xattribute in xelement.Attributes())
						{
							string text = xattribute.Name.ToString().ToUpper();
							if (text == "X")
							{
								if (float.TryParse(xattribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
								{
									x = val;
								}
							}
							else if (text == "Y")
							{
								if (float.TryParse(xattribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
								{
									y = val;
								}
							}
							else if (text == "Z")
							{
								if (float.TryParse(xattribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
								{
									z = val;
								}
							}
						}
						if (x.HasValue && y.HasValue && z.HasValue)
						{
							hotspotCollection.Add(new WoWPoint(x.Value, y.Value, z.Value));
						}
					}
				}
				catch (ProfileException ex)
				{
					Logging.WriteDebug(ex.Message);
				}
			}
			return hotspotCollection;
		}
	}
}
