using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Styx.Helpers;

namespace Styx.Logic.Profiles.Quest
{
	/// <summary>
	/// Collection of CollectFrom sources.
	/// </summary>
	public class CollectFromCollection : List<CollectFrom>
	{
		public CollectFromCollection()
		{
		}

		public CollectFromCollection(int capacity) : base(capacity)
		{
		}

		public CollectFromCollection(IEnumerable<CollectFrom> collection) : base(collection)
		{
		}

		public bool ContainsMob(uint mobID)
		{
			for (int i = 0; i < Count; i++)
			{
				if (this[i].Type == CollectFromType.Mob && this[i].ID == mobID)
				{
					return true;
				}
			}
			return false;
		}

		public bool ContainsGameObject(uint gameObjectID)
		{
			for (int i = 0; i < Count; i++)
			{
				if (this[i].Type == CollectFromType.GameObject && this[i].ID == gameObjectID)
				{
					return true;
				}
			}
			return false;
		}

		public bool ContainsVendor(uint vendorID)
		{
			for (int i = 0; i < Count; i++)
			{
				if (this[i].Type == CollectFromType.Vendor && this[i].ID == vendorID)
				{
					return true;
				}
			}
			return false;
		}

		internal static CollectFromCollection FromXElement(XElement element, params string[] elementNames)
		{
			CollectFromCollection collectFromCollection = new CollectFromCollection();
			foreach (XElement xelement in element.Elements())
			{
				try
				{
					if (elementNames.Contains(xelement.Name.ToString(), StringComparer.InvariantCultureIgnoreCase))
					{
						collectFromCollection.Add(CollectFrom.FromXML(xelement));
					}
				}
				catch (ProfileException ex)
				{
					Logging.WriteDebug(ex.Message);
				}
			}
			return collectFromCollection;
		}
	}
}
