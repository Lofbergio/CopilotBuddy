#nullable disable
using System.Linq;
using System.Runtime.InteropServices;
using Styx.WoWInternals;
using Tripper.XNAMath;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Quest step structure (52 bytes).
	/// Contains POI and area information for quest objectives.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Size = 52)]
	public struct WoWQuestStep
	{
		private uint reserved0;
		public uint AreaPointsCount;
		private uint areaPointsPointer;
		private uint reserved1;
		public Vector2i StepPosition;
		public uint PoiID;
		public int PoiObjectiveIndex;
		public uint PoiMapID;
		public uint PoiMapAreaID;
		public uint PoiFloorID;
		private uint reserved2;
		private uint reserved3;

		/// <summary>
		/// Gets the area boundary points for this quest step.
		/// </summary>
		public Vector2[] AreaPoints
		{
			get
			{
				Vector2i[] array;
				if (areaPointsPointer != 0U)
					array = ObjectManager.Wow.ReadStructArray<Vector2i>(areaPointsPointer, (int)AreaPointsCount);
				else
					array = new Vector2i[AreaPointsCount];

				return array.Select(v => new Vector2((float)v.X, (float)v.Y)).ToArray();
			}
		}
	}
}
