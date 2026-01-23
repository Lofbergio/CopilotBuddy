using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Styx.Logic.Profiles.Quest
{
	/// <summary>
	/// Objective info for collecting items.
	/// </summary>
	public class CollectItemObjectiveInfo : ObjectiveInfo
	{
		public CollectFromCollection? OverridedCollectFrom { get; private set; }
		public HotspotCollection? OverridedHotspots { get; private set; }
		public uint ItemID { get; private set; }
		public uint Count { get; private set; }
		public int TargetMinLevel { get; private set; }
		public int TargetMaxLevel { get; private set; }

		public CollectItemObjectiveInfo() : base(ObjectiveType.CollectItem)
		{
		}

		internal static CollectItemObjectiveInfo FromXMLInternal(XElement element)
		{
			CollectItemObjectiveInfo collectItemObjectiveInfo = new CollectItemObjectiveInfo();
			foreach (XAttribute xattribute in element.Attributes())
			{
				try
				{
					string? text;
					if ((text = xattribute.Name.ToString().ToLowerInvariant()) != null)
					{
						switch (text)
						{
							case "id":
							case "entry":
							case "itemid":
							case "itementry":
								if (uint.TryParse(xattribute.Value, out uint id))
								{
									collectItemObjectiveInfo.ItemID = id;
								}
								else
								{
									throw new ProfileAttributeExpectedException<uint>(xattribute);
								}
								break;
							case "collectcount":
							case "count":
								if (uint.TryParse(xattribute.Value, out uint count))
								{
									collectItemObjectiveInfo.Count = count;
								}
								else
								{
									throw new ProfileAttributeExpectedException<uint>(xattribute);
								}
								break;
						}
					}
				}
				catch (ProfileException ex)
				{
					Logging.WriteDebug(ex.Message);
				}
			}
			foreach (XElement childElement in element.Elements())
			{
				try
				{
					switch (childElement.Name.ToString().ToLowerInvariant())
					{
						case "targetmaxlevel":
						case "mobmaxlevel":
							if (int.TryParse(childElement.Value, out int maxLevel))
							{
								collectItemObjectiveInfo.TargetMaxLevel = maxLevel;
							}
							break;
						case "targetminlevel":
						case "mobminlevel":
							if (int.TryParse(childElement.Value, out int minLevel))
							{
								collectItemObjectiveInfo.TargetMinLevel = minLevel;
							}
							break;
						case "collectfrom":
							var collection = CollectFromCollection.FromXElement(childElement, "GameObject", "Mob", "Object", "Vendor");
							if (collection != null && collection.Count > 0)
							{
								collectItemObjectiveInfo.OverridedCollectFrom = collection;
							}
							break;
						case "hotspots":
						case "spots":
						case "locations":
							var hotspots = HotspotCollection.FromXElement(childElement, "hotspot", "spot", "location");
							if (collectItemObjectiveInfo.OverridedHotspots == null)
							{
								collectItemObjectiveInfo.OverridedHotspots = hotspots;
							}
							else if (hotspots != null)
							{
								collectItemObjectiveInfo.OverridedHotspots.AddRange(hotspots);
							}
							break;
					}
				}
				catch (ProfileException ex)
				{
					Logging.WriteDebug(ex.Message);
				}
			}
			return collectItemObjectiveInfo;
		}
	}
}
