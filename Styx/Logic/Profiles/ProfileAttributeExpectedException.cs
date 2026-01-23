using System;
using System.Xml.Linq;

namespace Styx.Logic.Profiles
{
	/// <summary>
	/// Exception thrown when a profile attribute has an unexpected value.
	/// </summary>
	public class ProfileAttributeExpectedException : ProfileException
	{
		public ProfileAttributeExpectedException(XAttribute attribute, params string[] validValues)
			: base(string.Format("Value \"{0}\" in \"{1}\" is not supported!{2}",
				attribute.Value,
				attribute.Name,
				(validValues == null || validValues.Length <= 0) ? "" : (" Valid values are: " + string.Join(", ", validValues))))
		{
		}
	}

	/// <summary>
	/// Exception thrown when a profile attribute has an unexpected value with expected type.
	/// </summary>
	public class ProfileAttributeExpectedException<T> : ProfileException
	{
		public ProfileAttributeExpectedException(XAttribute attribute, params string[] validValues)
			: base(string.Format("Value \"{0}\" in \"{1}\" attribute is not supported! Expected {2} type.{3}",
				attribute.Value,
				attribute.Name,
				typeof(T).Name,
				(validValues == null || validValues.Length <= 0) ? "" : (Environment.NewLine + "Valid values are: " + string.Join(", ", validValues))))
		{
		}
	}
}
