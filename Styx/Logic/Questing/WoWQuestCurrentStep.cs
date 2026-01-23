namespace Styx.Logic.Questing
{
	/// <summary>
	/// Represents the current step of a quest objective.
	/// </summary>
	public struct WoWQuestCurrentStep
	{
		/// <summary>
		/// Internal field.
		/// </summary>
		private uint _reserved;

		/// <summary>
		/// The map ID where this step takes place.
		/// </summary>
		public uint MapID;

		/// <summary>
		/// The objective index this step relates to.
		/// </summary>
		public uint ObjectiveIndex;

		/// <summary>
		/// The floor ID (for multi-floor areas).
		/// </summary>
		public uint FloorID;

		/// <summary>
		/// The position on the map.
		/// </summary>
		public Vector2i Pos;
	}
}
