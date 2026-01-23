using System;

namespace Styx.Helpers
{
	[AttributeUsage(AttributeTargets.Property)]
	public class SettingAttribute : Attribute
	{
		public SettingAttribute()
		{
		}

		public SettingAttribute(string elementName)
			: this(elementName, null)
		{
		}

		public SettingAttribute(string elementName, string explanation)
		{
			this.ElementName = elementName;
			this.Explanation = explanation;
		}

		public string ElementName;

		public string Explanation;
	}
}
