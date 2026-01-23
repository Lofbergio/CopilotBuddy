using System;

namespace Styx.Offsets
{
	/// <summary>
	/// Object descriptor field offsets for WoW 3.3.5a (Build 12340).
	/// These are indices into the descriptor array, multiply by 4 for byte offset.
	/// </summary>
	public enum WoWObjectFields
	{
		OBJECT_FIELD_GUID = 0x0,
		OBJECT_FIELD_TYPE = 0x2,
		OBJECT_FIELD_ENTRY = 0x3,
		OBJECT_FIELD_SCALE_X = 0x4,
		OBJECT_FIELD_PADDING = 0x5,
		OBJECT_END = 0x6
	}
}
