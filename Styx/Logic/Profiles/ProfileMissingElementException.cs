using System;
using System.Xml.Linq;

namespace Styx.Logic.Profiles
{
	/// <summary>
	/// Exception thrown when a required subelement is missing from a profile.
	/// </summary>
	public class ProfileMissingElementException : ProfileException
	{
		public ProfileMissingElementException(string elementName, XElement parent)
			: base(string.Format("Missing subelement {0} in \"{1}\".", elementName, parent.Name))
		{
		}
	}
}
