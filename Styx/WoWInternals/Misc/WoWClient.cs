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
				// Connection object at 0x00C79CF4 (13081844) — the client's own netstats provider
				// (sub_6B0970) reads exactly this dword. The old 0x00C7B1F4 was dead: it read 0, so
				// every field came back zero. Sub-offset 11860 (0x2E54) is confirmed by sub_6320D0,
				// which reads the counters at +0x2E9C/0x2EA0/0x2EA4 — i.e. 0x2E54 + the struct's
				// 0x48-byte latency block lands BytesSent on 0x2E9C.
				return ObjectManager.Wow.Read<NetStats>(new uint[] { 0x00C79CF4, 11860 });
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
