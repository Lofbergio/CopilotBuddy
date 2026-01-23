using System;

namespace Styx.Offsets
{
	/// <summary>
	/// DynamicObject descriptor field indices for WoW 3.3.5a (Build 12340).
	/// These are byte offsets from the dynamicobject descriptor base.
	/// DynamicObject descriptors start after OBJECT_END (0x6).
	/// </summary>
	public enum WoWDynamicObjectFields : uint
	{
		DYNAMICOBJECT_CASTER = 0x0,              // Size 2 (GUID)
		DYNAMICOBJECT_BYTES = 0x2,               // Type, SpellVisualId
		DYNAMICOBJECT_SPELLID = 0x3,
		DYNAMICOBJECT_RADIUS = 0x4,              // Float
		DYNAMICOBJECT_CASTTIME = 0x5,
		DYNAMICOBJECT_END = 0x6
	}
}
