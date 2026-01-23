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
				// 3.3.5a offset: 0x00C7B1F4 = 13081844, sub offset 11860
				return ObjectManager.Wow.Read<NetStats>(new uint[] { 0x00C7B1F4, 11860 });
			}
		}

		public ulong PerformanceCounter()
		{
			// 3.3.5a offset: 0x00D417AC = 13899164
			uint timerType = ObjectManager.Wow.Read<uint>(0x00D417AC);
			double multiplier = ObjectManager.Wow.Read<double>(0x00D417AC + 4);
			double offset = ObjectManager.Wow.Read<double>(0x00D417AC + 12);

			ulong result;
			if (timerType == 1 || timerType != 2)
			{
				result = (ulong)(GetTickCount() * multiplier + offset);
			}
			else
			{
				long perfCount;
				QueryPerformanceCounter(out perfCount);
				result = (ulong)((double)perfCount * multiplier + offset);
			}
			return result;
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
