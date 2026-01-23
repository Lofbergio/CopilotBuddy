using Styx.WoWInternals;

namespace Styx.Logic.Pathing
{
	/// <summary>
	/// Detects when the player is stuck and unable to move.
	/// </summary>
	public static class StuckDetector
	{
		/// <summary>
		/// Gets a value indicating whether the player is stuck (not moving when not swimming).
		/// </summary>
		public static bool IsStuck
		{
			get
			{
				if (ObjectManager.Me?.WoWMovementInfo.TimeMoved == 0)
					return !ObjectManager.Me.IsSwimming;
				return false;
			}
		}
	}
}
