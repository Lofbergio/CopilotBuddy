using System;

namespace Styx.WoWInternals
{
	/// <summary>
	/// PvP state flags for WoW 3.3.5a (Build 12340).
	/// Read from Bytes2[1] in UNIT_FIELD_BYTES_2.
	/// </summary>
	[Flags]
	public enum UnitPvPStateFlags : byte
	{
		None = 0x0,
		PvP = 0x1,              // flag_1 in HB
		ContestedPvP = 0x4,     // flag_2 in HB - Used for contested PvP zone flagging
		FreeForAllPvP = 0x4,    // Same as ContestedPvP (alias)
		Sanctuary = 0x8,        // flag_3 in HB
		PvPDesired = 0x10
	}
}
