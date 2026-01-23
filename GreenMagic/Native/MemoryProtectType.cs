using System;

namespace GreenMagic.Native
{
	public static class MemoryProtectType
	{
		public const uint PAGE_EXECUTE = 16U;

		public const uint PAGE_EXECUTE_READ = 32U;

		public const uint PAGE_EXECUTE_READWRITE = 64U;

		public const uint PAGE_EXECUTE_WRITECOPY = 128U;

		public const uint PAGE_NOACCESS = 1U;

		public const uint PAGE_READONLY = 2U;

		public const uint PAGE_READWRITE = 4U;

		public const uint PAGE_WRITECOPY = 8U;

		public const uint PAGE_GUARD = 256U;

		public const uint PAGE_NOCACHE = 512U;

		public const uint PAGE_WRITECOMBINE = 1024U;
	}
}
