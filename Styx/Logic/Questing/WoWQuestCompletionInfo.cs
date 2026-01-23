using System.Runtime.InteropServices;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Quest completion info structure (48 bytes).
	/// Read from address 12729088 (0xC24380) - array of 25 entries.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Size = 48)]
	public struct WoWQuestCompletionInfo
	{
		public int QuestID;
		public WoWQuestStepsCollection Steps;
		public QuestStepLocation CurrentStep;
		public WoWQuestState CurrentState;
	}
}
