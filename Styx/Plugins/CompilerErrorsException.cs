using System;

namespace Styx.Plugins
{
	public class CompilerErrorsException : Exception
	{
		public CompilerErrorsException(string message)
			: base(message)
		{
		}
	}
}
