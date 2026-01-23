using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Styx.Logic.Profiles.Quest
{
	/// <summary>
	/// Objective info for using objects.
	/// </summary>
	public class UseObjectObjectiveInfo : ObjectiveInfo
	{
		public uint ObjectID { get; private set; }
		public uint Count { get; private set; }
		public HotspotCollection? OverridedHotspots { get; private set; }

		public UseObjectObjectiveInfo() : base(ObjectiveType.UseObject)
		{
		}

		public new static UseObjectObjectiveInfo FromXML(XElement element)
		{
			UseObjectObjectiveInfo useObjectObjectiveInfo = new UseObjectObjectiveInfo();
			foreach (XAttribute xattribute in element.Attributes())
			{
				try
				{
					string? text;
					if ((text = xattribute.Name.ToString().ToLower()) != null)
					{
						if (text == "id" || text == "objectid" || text == "entry")
						{
							if (uint.TryParse(xattribute.Value, out uint id))
							{
								useObjectObjectiveInfo.ObjectID = id;
							}
							else
							{
								throw new ProfileAttributeExpectedException<uint>(xattribute);
							}
						}
						else if (text == "count" || text == "usecount")
						{
							if (uint.TryParse(xattribute.Value, out uint count))
							{
								useObjectObjectiveInfo.Count = count;
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
			useObjectObjectiveInfo.OverridedHotspots = HotspotCollection.FromXElement(element, "Hotspot");
			return useObjectObjectiveInfo;
		}
	}
}
