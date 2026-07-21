using System;
using System.Runtime.InteropServices;

namespace Styx.WoWInternals.Misc
{
	public class WoWClient
	{
		[DllImport("kernel32.dll", EntryPoint = "QueryPerformanceCounter", SetLastError = true)]
		private static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

		[DllImport("kernel32.dll", EntryPoint = "GetTickCount")]
		private static extern uint GetTickCount();

		internal WoWClient()
		{
		}

		public uint Latency
		{
			get
			{
				float downKBs;
				float upKBs;
				uint latency;
				GetNetStats(out downKBs, out upKBs, out latency);
				return latency;
			}
		}

		public NetStats NetStats
		{
			get
			{
				// 3.3.5a offset: 0x00C7B1F4 = 13087220, sub offset 11860
				return ObjectManager.Wow.Read<NetStats>(new uint[] { 0x00C7B1F4, 11860 });
			}
		}

		public ulong PerformanceCounter()
		{
			// IDA-verified 3.3.5a 12340: PerformanceCounter() calls sub_86ADC0((double*)dword_D4159C)
			// dword_D4159C stores the pointer to the timer struct.
			// sub_86ADC0 struct layout (double* this):
			//   +0  : double multiplier  (*this)
			//   +8  : uint   type        (*((_DWORD*)this + 2)) — 2=QPC, else GetTickCount
			//   +24 : double base        (*(this + 3))
			uint structPtr = ObjectManager.Wow.Read<uint>(0xD4159C);
			if (structPtr == 0)
				return GetTickCount(); // fallback: WoW not initialized yet

			double multiplier = ObjectManager.Wow.Read<double>(structPtr + 0);
			uint timerType    = ObjectManager.Wow.Read<uint>(structPtr + 8);
			double baseVal    = ObjectManager.Wow.Read<double>(structPtr + 24);

			if (timerType != 2)
				return (ulong)(GetTickCount() * multiplier + baseVal);

			long perfCount;
			QueryPerformanceCounter(out perfCount);
			return (ulong)((double)perfCount * multiplier + baseVal);
		}

		public void GetNetStats(out float downKBs, out float upKBs, out uint latency)
		{
			NetStats netStats = NetStats;
			double elapsedSeconds = (PerformanceCounter() - (ulong)netStats.StartTime) * 0.001;
			downKBs = (float)(netStats.BytesReceived * 0.001 / elapsedSeconds);
			upKBs = (float)(netStats.BytesSent * 0.001 / elapsedSeconds);

			uint latencyIndex = netStats.LatencyIndex;
			uint latencyCount = netStats.LatencyCount;
			uint totalLatency = 0;
			uint count = 0;

			if (latencyIndex == latencyCount)
			{
				latency = 0;
			}
			else
			{
				do
				{
					if (latencyIndex >= 16)
					{
						latencyIndex = 0;
						if (latencyCount == 0)
						{
							break;
						}
					}
					totalLatency += netStats.Latencies[latencyIndex++];
					count++;
				}
				while (latencyIndex != latencyCount);

				if (count != 0)
				{
					latency = totalLatency / count;
				}
				else
				{
					latency = 0;
				}
			}
		}
	}
}
