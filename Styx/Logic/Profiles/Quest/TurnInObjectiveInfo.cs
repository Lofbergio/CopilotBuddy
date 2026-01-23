using System;
using System.Xml.Linq;
using Styx.Logic.Pathing;

namespace Styx.Logic.Profiles.Quest
{
	/// <summary>
	/// Objective info for turning in a quest.
	/// </summary>
	public class TurnInObjectiveInfo : ObjectiveInfo
	{
		public WoWPoint Location { get; private set; }

		public TurnInObjectiveInfo(WoWPoint npcLocation) : base(ObjectiveType.TurnIn)
		{
			Location = npcLocation;
		}

		internal static TurnInObjectiveInfo FromXMLInternal(XElement element)
		{
			float? x = null;
			float? y = null;
			float? z = null;
			foreach (XAttribute xattribute in element.Attributes())
			{
				string text = xattribute.Name.ToString().ToUpper();
				if (text == "X")
				{
					if (float.TryParse(xattribute.Value, out float val))
					{
						x = val;
					}
				}
				else if (text == "Y")
				{
					if (float.TryParse(xattribute.Value, out float val))
					{
						y = val;
					}
				}
				else if (text == "Z")
				{
					if (float.TryParse(xattribute.Value, out float val))
					{
						z = val;
					}
				}
			}
			WoWPoint woWPoint = WoWPoint.Zero;
			if (x.HasValue && y.HasValue && z.HasValue)
			{
				woWPoint = new WoWPoint(x.Value, y.Value, z.Value);
			}
			return new TurnInObjectiveInfo(woWPoint);
		}
	}
}
