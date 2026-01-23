namespace Styx.Logic.Pathing
{
	/// <summary>
	/// Indicates the type of location where a point is situated.
	/// </summary>
	public enum PointLocationType
	{
		/// <summary>Unknown location type.</summary>
		Unknown,

		/// <summary>Point is under liquid (water).</summary>
		UnderLiquid,

		/// <summary>Point is underground.</summary>
		UnderGround,

		/// <summary>Point is inside a structure or building.</summary>
		InsideStructure,

		/// <summary>Point is in free air (flying).</summary>
		InFreeAir
	}
}
