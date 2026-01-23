using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Styx.Logic.Profiles.Quest
{
	/// <summary>
	/// Objective info for killing mobs.
	/// </summary>
	public class KillMobObjectiveInfo : ObjectiveInfo
	{
		public HotspotCollection? OverridedHotspots { get; private set; }
		public uint MobID { get; private set; }
		public uint Count { get; private set; }
		public int TargetMaxLevel { get; private set; }
		public int TargetMinLevel { get; private set; }

		public KillMobObjectiveInfo() : base(ObjectiveType.KillMob)
		{
		}

		internal static KillMobObjectiveInfo FromXMLInternal(XElement element)
		{
			KillMobObjectiveInfo killMobObjectiveInfo = new KillMobObjectiveInfo();
			foreach (XAttribute xattribute in element.Attributes())
			{
				try
				{
					string? text;
					if ((text = xattribute.Name.ToString().ToLower()) != null)
					{
						if (text == "id" || text == "mobid" || text == "entry")
						{
							if (uint.TryParse(xattribute.Value, out uint id))
							{
								killMobObjectiveInfo.MobID = id;
							}
							else
							{
								throw new ProfileAttributeExpectedException<uint>(xattribute);
							}
						}
						else if (text == "count" || text == "killcount")
						{
							if (uint.TryParse(xattribute.Value, out uint count))
							{
								killMobObjectiveInfo.Count = count;
							}
							else
							{
								throw new ProfileAttributeExpectedException<uint>(xattribute);
							}
						}
					}
				}
				catch (ProfileException ex)
				{
					Logging.WriteDebug(ex.Message);
				}
			}

			// Parse child elements
			foreach (XElement childElement in element.Elements())
			{
				try
				{
					string? text;
					if ((text = childElement.Name.ToString().ToLower()) != null)
					{
						if (text == "targetmaxlevel" || text == "mobmaxlevel")
						{
							if (int.TryParse(childElement.Value, out int maxLevel))
							{
								killMobObjectiveInfo.TargetMaxLevel = maxLevel;
							}
						}
						else if (text == "targetminlevel" || text == "mobminlevel")
						{
							if (int.TryParse(childElement.Value, out int minLevel))
							{
								killMobObjectiveInfo.TargetMinLevel = minLevel;
							}
						}
						else if (text == "hotspots" || text == "spots" || text == "locations")
						{
							if (killMobObjectiveInfo.OverridedHotspots == null)
							{
								killMobObjectiveInfo.OverridedHotspots = HotspotCollection.FromXElement(childElement, "hotspot", "spot", "location");
							}
							else
							{
								killMobObjectiveInfo.OverridedHotspots.AddRange(HotspotCollection.FromXElement(childElement, "hotspot", "spot", "location"));
							}
						}
					}
				}
				catch (ProfileException ex)
				{
					Logging.WriteDebug(ex.Message);
				}
			}

			if (killMobObjectiveInfo.OverridedHotspots == null)
			{
				killMobObjectiveInfo.OverridedHotspots = HotspotCollection.FromXElement(element, "Hotspot");
			}

			return killMobObjectiveInfo;
		}
	}
}
