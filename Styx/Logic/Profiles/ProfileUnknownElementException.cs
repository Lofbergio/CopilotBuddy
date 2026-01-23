using System;
using System.Xml.Linq;

namespace Styx.Logic.Profiles
{
	/// <summary>
	/// Exception thrown when a profile contains an unknown element/tag.
	/// </summary>
	public class ProfileUnknownElementException : ProfileException
	{
		public ProfileUnknownElementException(XElement element)
			: this(element, null)
		{
		}

		public ProfileUnknownElementException(XElement element, params string[]? validTags)
			: base(string.Format("Unknown tag \"{0}\" (Input: \"{1}\") in \"{2}\"!{3}",
				element.Name,
				element,
				(element.Parent == null || element.Parent.Name == null) ? "unknown" : element.Parent.Name.ToString(),
				(validTags == null || validTags.Length <= 0) ? "" : (Environment.NewLine + "Valid tags are: " + string.Join(", ", validTags))))
		{
		}
	}
}
