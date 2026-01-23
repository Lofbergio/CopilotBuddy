using System;

namespace Styx.Offsets
{
	/// <summary>
	/// Corpse descriptor field indices for WoW 3.3.5a (Build 12340).
	/// These are byte offsets from the corpse descriptor base.
	/// Corpse descriptors start after OBJECT_END (0x6).
	/// </summary>
	public enum WoWCorpseFields : uint
	{
		CORPSE_FIELD_OWNER = 0x0,                // Size 2 (GUID)
		CORPSE_FIELD_PARTY = 0x2,                // Size 2 (GUID)
		CORPSE_FIELD_DISPLAY_ID = 0x4,
		CORPSE_FIELD_ITEM = 0x5,                 // 19 equipment slots
		CORPSE_FIELD_ITEM_01 = 0x6,
		CORPSE_FIELD_ITEM_02 = 0x7,
		CORPSE_FIELD_ITEM_03 = 0x8,
		CORPSE_FIELD_ITEM_04 = 0x9,
		CORPSE_FIELD_ITEM_05 = 0xA,
		CORPSE_FIELD_ITEM_06 = 0xB,
		CORPSE_FIELD_ITEM_07 = 0xC,
		CORPSE_FIELD_ITEM_08 = 0xD,
		CORPSE_FIELD_ITEM_09 = 0xE,
		CORPSE_FIELD_ITEM_10 = 0xF,
		CORPSE_FIELD_ITEM_11 = 0x10,
		CORPSE_FIELD_ITEM_12 = 0x11,
		CORPSE_FIELD_ITEM_13 = 0x12,
		CORPSE_FIELD_ITEM_14 = 0x13,
		CORPSE_FIELD_ITEM_15 = 0x14,
		CORPSE_FIELD_ITEM_16 = 0x15,
		CORPSE_FIELD_ITEM_17 = 0x16,
		CORPSE_FIELD_ITEM_18 = 0x17,
		CORPSE_FIELD_BYTES_1 = 0x18,             // Skin, Face, HairStyle, HairColor
		CORPSE_FIELD_BYTES_2 = 0x19,             // FacialHair, unused, unused, unused
		CORPSE_FIELD_GUILD = 0x1A,
		CORPSE_FIELD_FLAGS = 0x1B,
		CORPSE_FIELD_DYNAMIC_FLAGS = 0x1C,
		CORPSE_END = 0x1D
	}

	/// <summary>
	/// Corpse flags for WoW 3.3.5a (Build 12340).
	/// Read from CORPSE_FIELD_FLAGS descriptor.
	/// </summary>
	[Flags]
	public enum CorpseFlags : uint
	{
		None = 0x0,
		Bones = 0x1,
		Unk1 = 0x2,
		Unk2 = 0x4,
		HideHelm = 0x8,
		HideCloak = 0x10,
		Lootable = 0x20
	}

	/// <summary>
	/// Corpse dynamic flags for WoW 3.3.5a (Build 12340).
	/// Read from CORPSE_FIELD_DYNAMIC_FLAGS descriptor.
	/// </summary>
	[Flags]
	public enum CorpseDynamicFlags : uint
	{
		None = 0x0,
		Lootable = 0x1
	}
}
