#nullable disable
using System.Runtime.InteropServices;
using Styx.WoWInternals;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Collection of quest steps (12 bytes).
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Size = 12)]
	public struct WoWQuestStepsCollection
	{
		private uint uint_0;
		public uint StepsCount;
		private uint uint_1;

		public WoWQuestStep[] Steps
		{
			get
			{
				if (uint_1 != 0U)
					return ObjectManager.Wow.ReadStructArray<WoWQuestStep>(uint_1, (int)StepsCount);
				return new WoWQuestStep[StepsCount];
			}
		}
	}
}
