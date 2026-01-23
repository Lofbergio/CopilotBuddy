using System;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Quest flags stored in player descriptor.
	/// </summary>
	[Flags]
	public enum WoWDescriptorQuestFlags : uint
	{
		None = 0,
		Completed = 1,
		Failed = 2
	}
}
