using System;
using System.Xml.Linq;

namespace Styx.Logic.Profiles
{
	/// <summary>
	/// Exception thrown when a required profile attribute is missing (generic version with type info).
	/// </summary>
	public class ProfileMissingAttributeException<T> : ProfileException
	{
		public ProfileMissingAttributeException(string attributeName, XElement tag)
			: base(string.Format("Missing attribute {0} of type {1} in \"{2}\".",
				attributeName,
				typeof(T).Name,
				tag.Name))
		{
		}
	}
}
