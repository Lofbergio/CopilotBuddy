using System;
using System.Runtime.InteropServices;

namespace Styx.WoWInternals.Misc
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct NetStats
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
		internal uint[] Latencies;

		internal uint LatencyIndex;

		internal uint LatencyCount;

		public uint BytesSent;

		public uint BytesReceived;

		public uint StartTime;
	}
}
