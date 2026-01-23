using System;

namespace Styx.Logic.Profiles
{
	public class ProfileException : Exception
	{
		public ProfileException()
		{
		}

		public ProfileException(string message)
			: base(message)
		{
		}

		public ProfileException(string message, Exception innerException)
			: base(message, innerException)
		{
		}
	}
}
