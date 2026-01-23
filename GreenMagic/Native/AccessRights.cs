using System;

namespace GreenMagic.Native
{
	public static class AccessRights
	{
		public const uint STANDARD_RIGHTS_REQUIRED = 983040U;

		public const uint SYNCHRONIZE = 1048576U;

		public const uint PROCESS_TERMINATE = 1U;

		public const uint PROCESS_CREATE_THREAD = 2U;

		public const uint PROCESS_VM_OPERATION = 8U;

		public const uint PROCESS_VM_READ = 16U;

		public const uint PROCESS_VM_WRITE = 32U;

		public const uint PROCESS_DUP_HANDLE = 64U;

		public const uint PROCESS_CREATE_PROCESS = 128U;

		public const uint PROCESS_SET_QUOTA = 256U;

		public const uint PROCESS_SET_INFORMATION = 512U;

		public const uint PROCESS_QUERY_INFORMATION = 1024U;

		public const uint PROCESS_SUSPEND_RESUME = 2048U;

		public const uint PROCESS_QUERY_LIMITED_INFORMATION = 4096U;

		public const uint PROCESS_ALL_ACCESS = 2035711U;

		public const uint THREAD_TERMINATE = 1U;

		public const uint THREAD_SUSPEND_RESUME = 2U;

		public const uint THREAD_GET_CONTEXT = 8U;

		public const uint THREAD_SET_CONTEXT = 16U;

		public const uint THREAD_QUERY_INFORMATION = 64U;

		public const uint THREAD_SET_INFORMATION = 32U;

		public const uint THREAD_SET_THREAD_TOKEN = 128U;

		public const uint THREAD_IMPERSONATE = 256U;

		public const uint THREAD_DIRECT_IMPERSONATION = 512U;

		public const uint THREAD_ALL_ACCESS = 2032639U;
	}
}
