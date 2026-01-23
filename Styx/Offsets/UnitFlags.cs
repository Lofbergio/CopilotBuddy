using System;

namespace Styx
{
	/// <summary>
	/// Unit flags for WoW 3.3.5a (Build 12340).
	/// Read from UNIT_FIELD_FLAGS descriptor.
	/// </summary>
	[Flags]
	public enum UnitFlags : uint
	{
		None = 0x0,
		ServerControlled = 0x1,
		NotAttackable = 0x2,
		RemoveClientControl = 0x4,
		PlayerControlled = 0x8,
		Rename = 0x10,
		Preparation = 0x20,
		Unk6 = 0x40,
		NotAttackable1 = 0x80,
		ImmuneToPc = 0x100,
		ImmuneToNpc = 0x200,
		Looting = 0x400,
		PetInCombat = 0x800,
		PvpEnabling = 0x1000,
		Silenced = 0x2000,
		CannotSwim = 0x4000,
		Unk15 = 0x8000,
		Unk16 = 0x10000,
		Pacified = 0x20000,
		Stunned = 0x40000,
		InCombat = 0x80000,
		OnTaxi = 0x100000,
		Disarmed = 0x200000,
		Confused = 0x400000,
		Fleeing = 0x800000,
		Possessed = 0x1000000,
		NotSelectable = 0x2000000,
		Skinnable = 0x4000000,
		Mount = 0x8000000,
		Unk28 = 0x10000000,
		Unk29 = 0x20000000,
		Sheathe = 0x40000000,
		Unk31 = 0x80000000,

		// Aliases
		Combat = InCombat,
		Dazed = Unk31,
		PlusMob = Unk6,
		Rooted = Unk15
	}

	/// <summary>
	/// Unit flags 2 for WoW 3.3.5a (Build 12340).
	/// Read from UNIT_FIELD_FLAGS_2 descriptor.
	/// </summary>
	[Flags]
	public enum UnitFlags2 : uint
	{
		None = 0x0,
		FeignDeath = 0x1,
		Unk1 = 0x2,
		IgnoreReputation = 0x4,
		ComprehendLang = 0x8,
		MirrorImage = 0x10,
		ForceMove = 0x40,
		DisarmOffhand = 0x80,
		DisablePredStats = 0x100,
		DisarmRanged = 0x400,
		RegeneratePower = 0x800,
		RestrictPartyInteraction = 0x1000,
		PreventSpellClick = 0x2000,
		AllowEnemyInteract = 0x4000,
		DisableTurn = 0x8000,
		Unk2 = 0x10000,
		PlayDeathAnim = 0x20000,
		AllowCheatSpells = 0x40000
	}

	/// <summary>
	/// Unit dynamic flags for WoW 3.3.5a (Build 12340).
	/// Read from UNIT_DYNAMIC_FLAGS descriptor.
	/// </summary>
	[Flags]
	public enum UnitDynamicFlags : uint
	{
		None = 0x0,
		Lootable = 0x1,
		TrackUnit = 0x2,
		TaggedByOther = 0x4,
		TaggedByMe = 0x8,
		SpecialInfo = 0x10,
		Dead = 0x20,
		ReferAFriendLinked = 0x40,
		TappedByAllThreatLists = 0x80,
		Invisible = 0x100,

		// Aliases
		CanSkin = Lootable
	}
}
