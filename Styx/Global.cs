using System;
using System.Runtime.CompilerServices;

namespace Styx
{
	public static class Global
	{
		public static bool ShouldMail
		{
			get
			{
				return _shouldMail;
			}
			set
			{
				_shouldMail = value;
			}
		}

		private static bool _shouldMail;
	}
}
