using System;
using System.Xml.Linq;

namespace Styx.Logic.Profiles
{
	/// <summary>
	/// Exception thrown when a required profile attribute is missing.
	/// </summary>
	public class ProfileMissingAttributeException : ProfileException
	{
		public ProfileMissingAttributeException(string attributeName, XElement tag, params string[] validValues)
			: base(string.Format("Missing attribute {0} in \"{1}\".{2}",
				attributeName,
				tag.Name,
				(validValues == null || validValues.Length <= 0) ? "" : (" Valid values are: " + string.Join(", ", validValues))))
		{
		}
	}
}
