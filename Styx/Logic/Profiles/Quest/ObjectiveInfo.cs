using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Styx.Logic.Profiles.Quest
{
	/// <summary>
	/// Base class for quest objective information.
	/// </summary>
	public class ObjectiveInfo
	{
		private static readonly Dictionary<ObjectiveType, Func<XElement, ObjectiveInfo>> dictionary_0;

		public ObjectiveType Type { get; private set; }

		public ObjectiveInfo(ObjectiveType type)
		{
			Type = type;
		}

		static ObjectiveInfo()
		{
			dictionary_0 = new Dictionary<ObjectiveType, Func<XElement, ObjectiveInfo>>();
			dictionary_0.Add(ObjectiveType.TurnIn, TurnInObjectiveInfo.FromXMLInternal);
			dictionary_0.Add(ObjectiveType.KillMob, KillMobObjectiveInfo.FromXMLInternal);
			dictionary_0.Add(ObjectiveType.CollectItem, CollectItemObjectiveInfo.FromXMLInternal);
			dictionary_0.Add(ObjectiveType.UseObject, UseObjectObjectiveInfo.FromXML);
		}

		public static ObjectiveInfo FromXML(XElement element)
		{
			string? text;
			if ((text = element.Name.ToString().ToLower()) != null)
			{
				if (text == "turnin" || text == "handin")
				{
					return dictionary_0[ObjectiveType.TurnIn](element);
				}
				if (text == "objective")
				{
					XAttribute? typeAttr = element.Attribute("Type");
					if (typeAttr != null)
					{
						if (Enum.TryParse<ObjectiveType>(typeAttr.Value, true, out ObjectiveType objType))
						{
							if (dictionary_0.TryGetValue(objType, out var factory))
							{
								return factory(element);
							}
						}
					}
				}
				if (text == "killmob")
				{
					return dictionary_0[ObjectiveType.KillMob](element);
				}
				if (text == "collectitem")
				{
					return dictionary_0[ObjectiveType.CollectItem](element);
				}
				if (text == "useobject")
				{
					return dictionary_0[ObjectiveType.UseObject](element);
				}
			}
			throw new ProfileUnknownElementException(element, "TurnIn", "HandIn", "Objective", "KillMob", "CollectItem", "UseObject");
		}
	}
}
