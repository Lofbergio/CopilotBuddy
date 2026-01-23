using System;

namespace GreenMagic.Native
{
	public static class ThreadFlags
	{
		public const uint THREAD_EXECUTE_IMMEDIATELY = 0U;

		public const uint CREATE_SUSPENDED = 4U;

		public const uint STACK_SIZE_PARAM_IS_A_RESERVATION = 65536U;

		public const uint STILL_ACTIVE = 259U;
	}
}
