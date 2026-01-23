using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Styx.Helpers;

namespace Styx.Logic.Profiles.Quest
{
	/// <summary>
	/// Represents a source for collecting items (mob, gameobject, or vendor).
	/// </summary>
	public class CollectFrom
	{
		public uint ID { get; private set; }
		public string? Name { get; private set; }
		public CollectFromType Type { get; private set; }

		public CollectFrom(uint id, string? name, CollectFromType type)
		{
			ID = id;
			Name = name;
			Type = type;
		}

		public static CollectFrom FromXML(XElement element)
		{
			uint num = 0U;
			string? text = null;
			CollectFromType? collectFromType = null;
			foreach (XAttribute xattribute in element.Attributes())
			{
				try
				{
					string? text2;
					if ((text2 = xattribute.Name.ToString().ToLower()) != null)
					{
						if (text2 == "id" || text2 == "entry")
						{
							if (!uint.TryParse(xattribute.Value, out num))
							{
								throw new ProfileAttributeExpectedException<uint>(xattribute);
							}
						}
						else if (text2 == "name")
						{
							text = xattribute.Value;
						}
						else if (text2 == "type")
						{
							collectFromType = ParseType(xattribute.Value);
						}
					}
				}
				catch (ProfileException ex)
				{
					Logging.WriteDebug(ex.Message);
				}
			}
			if (num == 0U && text == null)
			{
				throw new ProfileMissingAttributeException<uint>("Id", element);
			}
			if (collectFromType == null)
			{
				collectFromType = ParseType(element.Name.ToString());
				if (collectFromType == null)
				{
					throw new ProfileException("Expected object type; Mob or GameObject.");
				}
			}
			return new CollectFrom(num, text, collectFromType.Value);
		}

		private static CollectFromType? ParseType(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return null;
			}
			string lower = value.ToLower();
			if (lower == "mob" || lower == "npc")
			{
				return CollectFromType.Mob;
			}
			if (lower == "gameobject" || lower == "object")
			{
				return CollectFromType.GameObject;
			}
			if (lower == "vendor")
			{
				return CollectFromType.Vendor;
			}
			return null;
		}
	}
}
