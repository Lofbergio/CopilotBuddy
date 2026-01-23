using System;
using System.Xml.Linq;

namespace Styx.Logic.Profiles
{
	/// <summary>
	/// Exception thrown when a profile tag has an unexpected value (generic version with type info).
	/// </summary>
	public class ProfileTagExpectedException<T> : ProfileException
	{
		public ProfileTagExpectedException(XElement tag)
			: this(tag, null)
		{
		}

		public ProfileTagExpectedException(XElement tag, params string[]? validValues)
			: base(string.Format("Value \"{0}\" in \"{1}\" is not a valid {2}!{3}",
				tag.Value,
				tag.Name,
				typeof(T).Name,
				(validValues == null || validValues.Length <= 0) ? "" : (Environment.NewLine + "Valid values are: " + string.Join(", ", validValues))))
		{
		}
	}
}
