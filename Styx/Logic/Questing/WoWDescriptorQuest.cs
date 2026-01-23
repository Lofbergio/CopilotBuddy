using System.Runtime.InteropServices;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Represents a quest descriptor from WoW memory.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct WoWDescriptorQuest
	{
		/// <summary>
		/// The quest ID.
		/// </summary>
		public uint Id;

		/// <summary>
		/// Quest flags.
		/// </summary>
		[MarshalAs(UnmanagedType.U4)]
		public WoWDescriptorQuestFlags Flags;

		/// <summary>
		/// Progress on each objective (up to 4).
		/// </summary>
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public ushort[] ObjectivesDone;

		/// <summary>
		/// Seconds before quest fails (for timed quests).
		/// </summary>
		public uint SecondsBeforeFailed;
	}
}
