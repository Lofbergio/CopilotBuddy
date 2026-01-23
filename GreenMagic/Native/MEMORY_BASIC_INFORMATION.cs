using System;
using System.Runtime.InteropServices;

namespace GreenMagic.Native
{
	[StructLayout(LayoutKind.Sequential)]
	public struct MEMORY_BASIC_INFORMATION
	{
		public int BaseAddress;

		public int AllocationBase;

		public int AllocationProtect;

		public int RegionSize;

		public int State;

		public int Protect;

		public int lType;
	}
}
