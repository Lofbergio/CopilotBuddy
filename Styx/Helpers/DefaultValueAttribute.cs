using System;

namespace Styx.Helpers
{
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public class DefaultValueAttribute : Attribute
	{
		public DefaultValueAttribute(object val)
		{
			this.Value = val;
		}

		public readonly object Value;
	}
}
