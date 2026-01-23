using System;

namespace Styx.Offsets
{
	/// <summary>
	/// GameObject descriptor field indices for WoW 3.3.5a (Build 12340).
	/// These are byte offsets from the gameobject descriptor base.
	/// GameObject descriptors start after OBJECT_END (0x6).
	/// </summary>
	public enum WoWGameObjectFields : uint
	{
		OBJECT_FIELD_CREATED_BY = 0x0,           // Size 2 (GUID)
		GAMEOBJECT_DISPLAYID = 0x2,
		GAMEOBJECT_FLAGS = 0x3,
		GAMEOBJECT_PARENTROTATION = 0x4,         // 4 floats (quaternion)
		GAMEOBJECT_PARENTROTATION_01 = 0x5,
		GAMEOBJECT_PARENTROTATION_02 = 0x6,
		GAMEOBJECT_PARENTROTATION_03 = 0x7,
		GAMEOBJECT_DYNAMIC = 0x8,                // Dynamic flags (packed)
		GAMEOBJECT_FACTION = 0x9,
		GAMEOBJECT_LEVEL = 0xA,
		GAMEOBJECT_BYTES_1 = 0xB,                // State, Type, ArtKit, AnimProgress
		GAMEOBJECT_END = 0xC
	}

	/// <summary>
	/// GameObject flags for WoW 3.3.5a (Build 12340).
	/// Read from GAMEOBJECT_FLAGS descriptor.
	/// </summary>
	[Flags]
	public enum GameObjectFlags : uint
	{
		None = 0x0,
		InUse = 0x1,
		Locked = 0x2,
		InteractionCondition = 0x4,
		Transport = 0x8,
		NotSelectable = 0x10,
		NoDespawn = 0x20,
		Triggered = 0x40,
		Damaged = 0x200,
		Destroyed = 0x400
	}

	/// <summary>
	/// GameObject type for WoW 3.3.5a (Build 12340).
	/// Read from GAMEOBJECT_BYTES_1 byte 1.
	/// </summary>
	public enum GameObjectType : byte
	{
		Door = 0,
		Button = 1,
		QuestGiver = 2,
		Chest = 3,
		Binder = 4,
		Generic = 5,
		Trap = 6,
		Chair = 7,
		SpellFocus = 8,
		Text = 9,
		Goober = 10,
		Transport = 11,
		AreaDamage = 12,
		Camera = 13,
		MapObject = 14,
		MoTransport = 15,
		DuelArbiter = 16,
		FishingNode = 17,
		Ritual = 18,
		Mailbox = 19,
		AuctionHouse = 20,
		SpellCaster = 22,
		MeetingStone = 23,
		FlagStand = 24,
		FishingHole = 25,
		FlagDrop = 26,
		MiniGame = 27,
		LotteryKiosk = 28,
		CapturePoint = 29,
		AuraGenerator = 30,
		DungeonDifficulty = 31,
		BarberChair = 32,
		DestructibleBuilding = 33,
		GuildBank = 34,
		Trapdoor = 35
	}
}
