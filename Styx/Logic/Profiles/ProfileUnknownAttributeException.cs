using System;
using System.Xml.Linq;

namespace Styx.Logic.Profiles
{
	/// <summary>
	/// Exception thrown when a profile contains an unknown attribute.
	/// </summary>
	public class ProfileUnknownAttributeException : ProfileException
	{
		public ProfileUnknownAttributeException(XAttribute attribute)
			: this(attribute, null)
		{
		}

		public ProfileUnknownAttributeException(XAttribute attribute, params string[]? validAttributes)
			: base(string.Format("Unknown attribute \"{0}\" (Input: \"{1}\") in \"{2}\"!{3}",
				attribute.Name,
				attribute,
				(attribute.Parent == null || attribute.Parent.Name == null) ? "unknown" : attribute.Parent.Name.ToString(),
				(validAttributes == null || validAttributes.Length <= 0) ? "" : (Environment.NewLine + "Valid attributes are: " + string.Join(", ", validAttributes))))
		{
		}
	}
}
