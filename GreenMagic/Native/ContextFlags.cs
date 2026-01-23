using System;

namespace GreenMagic.Native
{
	public static class ContextFlags
	{
		private const uint CONTEXT_i386 = 65536U;

		private const uint CONTEXT_i486 = 65536U;

		public const uint CONTEXT_CONTROL = 65537U;

		public const uint CONTEXT_INTEGER = 65538U;

		public const uint CONTEXT_SEGMENTS = 65540U;

		public const uint CONTEXT_FLOATING_POINT = 65544U;

		public const uint CONTEXT_DEBUG_REGISTERS = 65552U;

		public const uint CONTEXT_EXTENDED_REGISTERS = 65568U;

		public const uint CONTEXT_FULL = 65543U;

		public const uint CONTEXT_ALL = 65599U;
	}
}
