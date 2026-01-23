using System.Runtime.InteropServices;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Quest log entry structure (16 bytes).
	/// Read from address 12728252 (0xC23F3C).
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct QuestLogEntry
	{
		public uint Id;
		public uint State;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public ushort[] ObjectiveRequiredCounts;
		public uint Time;
	}
}
