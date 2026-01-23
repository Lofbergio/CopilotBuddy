using System;

namespace Styx
{
	/// <summary>
	/// Quest giver status for NPCs showing quest availability.
	/// Values match WoW 3.3.5a client.
	/// </summary>
	public enum QuestGiverStatus
	{
		None,
		Unavailable,
		LowLevelAvailable,
		LowLevelTurnInRepeatable,
		LowLevelAvailableRepeatable,
		Incomplete,
		TurnInRepeatable,
		AvailableRepeatable,
		Available,
		TurnInInvisible,
		TurnIn
	}
}
