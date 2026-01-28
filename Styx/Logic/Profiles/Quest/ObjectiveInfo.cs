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
		private static readonly Dictionary<ObjectiveType, Func<XElement, ObjectiveInfo>> _objectiveFactories;

		public ObjectiveType Type { get; private set; }

		public ObjectiveInfo(ObjectiveType type)
		{
			Type = type;
		}

		static ObjectiveInfo()
		{
			_objectiveFactories = new Dictionary<ObjectiveType, Func<XElement, ObjectiveInfo>>();
			_objectiveFactories.Add(ObjectiveType.TurnIn, TurnInObjectiveInfo.FromXMLInternal);
			_objectiveFactories.Add(ObjectiveType.KillMob, KillMobObjectiveInfo.FromXMLInternal);
			_objectiveFactories.Add(ObjectiveType.CollectItem, CollectItemObjectiveInfo.FromXMLInternal);
			_objectiveFactories.Add(ObjectiveType.UseObject, UseObjectObjectiveInfo.FromXML);
		}

		public static ObjectiveInfo FromXML(XElement element)
		{
			string? text;
			if ((text = element.Name.ToString().ToLower()) != null)
			{
				if (text == "turnin" || text == "handin")
				{
					return _objectiveFactories[ObjectiveType.TurnIn](element);
				}
				if (text == "objective")
				{
					XAttribute? typeAttr = element.Attribute("Type");
					if (typeAttr != null)
					{
						if (Enum.TryParse<ObjectiveType>(typeAttr.Value, true, out ObjectiveType objType))
						{
							if (_objectiveFactories.TryGetValue(objType, out var factory))
							{
								return factory(element);
							}
						}
					}
				}
				if (text == "killmob")
				{
					return _objectiveFactories[ObjectiveType.KillMob](element);
				}
				if (text == "collectitem")
				{
					return _objectiveFactories[ObjectiveType.CollectItem](element);
				}
				if (text == "useobject")
				{
					return _objectiveFactories[ObjectiveType.UseObject](element);
				}
			}
			throw new ProfileUnknownElementException(element, "TurnIn", "HandIn", "Objective", "KillMob", "CollectItem", "UseObject");
		}
	}
}
