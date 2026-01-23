using System;
using System.Xml.Linq;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Styx.Logic.AreaManagement
{
	public class Hotspot
	{
		public WoWPoint Position { get; set; }

		public Hotspot(XElement element)
		{
			try
			{
				float? x = null;
				float? y = null;
				float? z = null;

				foreach (XAttribute attr in element.Attributes())
				{
					string name = attr.Name.LocalName.ToLowerInvariant();
					if (name == "x")
					{
						float val;
						if (float.TryParse(attr.Value, out val))
							x = val;
					}
					else if (name == "y")
					{
						float val;
						if (float.TryParse(attr.Value, out val))
							y = val;
					}
					else if (name == "z")
					{
						float val;
						if (float.TryParse(attr.Value, out val))
							z = val;
					}
				}

				if (x.HasValue && y.HasValue && z.HasValue)
				{
					Position = new WoWPoint(x.Value, y.Value, z.Value);
				}
				else
				{
					Position = WoWPoint.Empty;
				}
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
				Position = WoWPoint.Empty;
			}
		}

		public Hotspot()
		{
			Position = WoWPoint.Empty;
		}

		public Hotspot(float x, float y, float z)
		{
			Position = new WoWPoint(x, y, z);
		}

		public static implicit operator WoWPoint(Hotspot hotspot)
		{
			return hotspot.Position;
		}

		public static implicit operator Hotspot(WoWPoint point)
		{
			return new Hotspot(point.X, point.Y, point.Z);
		}

		public WoWPoint ToWoWPoint()
		{
			return this;
		}

		public XElement ToXML()
		{
			return new XElement("Hotspot",
				new XAttribute("X", Position.X),
				new XAttribute("Y", Position.Y),
				new XAttribute("Z", Position.Z));
		}

		public override string ToString()
		{
			return Position.ToString();
		}
	}
}
