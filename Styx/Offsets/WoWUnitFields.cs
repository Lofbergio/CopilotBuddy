using System;

namespace Styx.Offsets
{
	/// <summary>
	/// Unit descriptor field indices for WoW 3.3.5a (Build 12340).
	/// These are absolute indices into the descriptor array (OBJECT_END=0x6 already included).
	/// Multiply by 4 for byte offset from descriptor base.
	/// Source: 335offsetsall.txt (verified)
	/// </summary>
	public enum WoWUnitFields
	{
		// GUIDs (2 uint each = 8 bytes = 2 descriptor slots)
		Charm = 0x6,              // UNIT_FIELD_CHARM
		Summon = 0x8,             // UNIT_FIELD_SUMMON
		Critter = 0xA,            // UNIT_FIELD_CRITTER
		CharmedBy = 0xC,          // UNIT_FIELD_CHARMEDBY
		SummonedBy = 0xE,         // UNIT_FIELD_SUMMONEDBY
		CreatedBy = 0x10,         // UNIT_FIELD_CREATEDBY
		Target = 0x12,            // UNIT_FIELD_TARGET
		ChannelObject = 0x14,     // UNIT_FIELD_CHANNEL_OBJECT

		// Channel & Basic Info
		ChannelSpell = 0x16,      // UNIT_CHANNEL_SPELL
		Bytes0 = 0x17,            // UNIT_FIELD_BYTES_0 (Race, Class, Gender, PowerType)

		// Health & Power
		Health = 0x18,            // UNIT_FIELD_HEALTH
		Mana = 0x19,              // UNIT_FIELD_POWER1
		Rage = 0x1A,              // UNIT_FIELD_POWER2
		Focus = 0x1B,             // UNIT_FIELD_POWER3
		Energy = 0x1C,            // UNIT_FIELD_POWER4
		Happiness = 0x1D,         // UNIT_FIELD_POWER5
		Runes = 0x1E,             // UNIT_FIELD_POWER6
		RunicPower = 0x1F,        // UNIT_FIELD_POWER7

		MaxHealth = 0x20,         // UNIT_FIELD_MAXHEALTH
		MaxMana = 0x21,           // UNIT_FIELD_MAXPOWER1
		MaxRage = 0x22,           // UNIT_FIELD_MAXPOWER2
		MaxFocus = 0x23,          // UNIT_FIELD_MAXPOWER3
		MaxEnergy = 0x24,         // UNIT_FIELD_MAXPOWER4
		MaxHappiness = 0x25,      // UNIT_FIELD_MAXPOWER5
		MaxRunes = 0x26,          // UNIT_FIELD_MAXPOWER6
		MaxRunicPower = 0x27,     // UNIT_FIELD_MAXPOWER7

		// Regen (7 entries each)
		PowerRegenFlatModifier = 0x28,            // UNIT_FIELD_POWER_REGEN_FLAT_MODIFIER
		PowerRegenInterruptedFlatModifier = 0x2F, // UNIT_FIELD_POWER_REGEN_INTERRUPTED_FLAT_MODIFIER

		// Level & Faction
		Level = 0x36,             // UNIT_FIELD_LEVEL
		FactionTemplate = 0x37,   // UNIT_FIELD_FACTIONTEMPLATE

		// Virtual Items (weapon display, 3 entries)
		VirtualItemSlotId = 0x38, // UNIT_VIRTUAL_ITEM_SLOT_ID

		// Flags
		Flags = 0x3B,             // UNIT_FIELD_FLAGS
		Flags2 = 0x3C,            // UNIT_FIELD_FLAGS_2
		AuraState = 0x3D,         // UNIT_FIELD_AURASTATE

		// Attack Times (2 entries)
		BaseAttackTime = 0x3E,    // UNIT_FIELD_BASEATTACKTIME
		RangedAttackTime = 0x40,  // UNIT_FIELD_RANGEDATTACKTIME

		// Bounding
		BoundingRadius = 0x41,    // UNIT_FIELD_BOUNDINGRADIUS
		CombatReach = 0x42,       // UNIT_FIELD_COMBATREACH

		// Display
		DisplayId = 0x43,         // UNIT_FIELD_DISPLAYID
		NativeDisplayId = 0x44,   // UNIT_FIELD_NATIVEDISPLAYID
		MountDisplayId = 0x45,    // UNIT_FIELD_MOUNTDISPLAYID

		// Damage
		MinDamage = 0x46,         // UNIT_FIELD_MINDAMAGE
		MaxDamage = 0x47,         // UNIT_FIELD_MAXDAMAGE
		MinOffhandDamage = 0x48,  // UNIT_FIELD_MINOFFHANDDAMAGE
		MaxOffhandDamage = 0x49,  // UNIT_FIELD_MAXOFFHANDDAMAGE

		// Bytes 1 & Pet
		Bytes1 = 0x4A,            // UNIT_FIELD_BYTES_1 (StandState, PetTalentPoints, StandFlags, AnimTier)
		PetNumber = 0x4B,         // UNIT_FIELD_PETNUMBER
		PetNameTimestamp = 0x4C,  // UNIT_FIELD_PET_NAME_TIMESTAMP
		PetExperience = 0x4D,     // UNIT_FIELD_PETEXPERIENCE
		PetNextLevelExp = 0x4E,   // UNIT_FIELD_PETNEXTLEVELEXP

		// Dynamic & Cast
		DynamicFlags = 0x4F,      // UNIT_DYNAMIC_FLAGS
		ModCastSpeed = 0x50,      // UNIT_MOD_CAST_SPEED
		CreatedBySpell = 0x51,    // UNIT_CREATED_BY_SPELL

		// NPC
		NpcFlags = 0x52,          // UNIT_NPC_FLAGS
		NpcEmoteState = 0x53,     // UNIT_NPC_EMOTESTATE

		// Stats
		Stat0 = 0x54,             // UNIT_FIELD_STAT0 (Strength)
		Stat1 = 0x55,             // UNIT_FIELD_STAT1 (Agility)
		Stat2 = 0x56,             // UNIT_FIELD_STAT2 (Stamina)
		Stat3 = 0x57,             // UNIT_FIELD_STAT3 (Intellect)
		Stat4 = 0x58,             // UNIT_FIELD_STAT4 (Spirit)

		// Stat Modifiers
		PosStat0 = 0x59,          // UNIT_FIELD_POSSTAT0
		PosStat1 = 0x5A,          // UNIT_FIELD_POSSTAT1
		PosStat2 = 0x5B,          // UNIT_FIELD_POSSTAT2
		PosStat3 = 0x5C,          // UNIT_FIELD_POSSTAT3
		PosStat4 = 0x5D,          // UNIT_FIELD_POSSTAT4
		NegStat0 = 0x5E,          // UNIT_FIELD_NEGSTAT0
		NegStat1 = 0x5F,          // UNIT_FIELD_NEGSTAT1
		NegStat2 = 0x60,          // UNIT_FIELD_NEGSTAT2
		NegStat3 = 0x61,          // UNIT_FIELD_NEGSTAT3
		NegStat4 = 0x62,          // UNIT_FIELD_NEGSTAT4

		// Resistances (7 schools: armor, holy, fire, nature, frost, shadow, arcane)
		Resistances = 0x63,               // UNIT_FIELD_RESISTANCES (7 entries)
		ResistanceArmor = 0x63,           // Alias for Resistances[0]
		ResistanceHoly = 0x64,
		ResistanceFire = 0x65,
		ResistanceNature = 0x66,
		ResistanceFrost = 0x67,
		ResistanceShadow = 0x68,
		ResistanceArcane = 0x69,
		ResistanceBuffModsPositive = 0x6A,  // UNIT_FIELD_RESISTANCEBUFFMODSPOSITIVE (7 entries)
		ResistanceBuffModsNegative = 0x71,  // UNIT_FIELD_RESISTANCEBUFFMODSNEGATIVE (7 entries)

		// Base Stats
		BaseMana = 0x78,          // UNIT_FIELD_BASE_MANA
		BaseHealth = 0x79,        // UNIT_FIELD_BASE_HEALTH

		// Bytes 2
		Bytes2 = 0x7A,            // UNIT_FIELD_BYTES_2 (SheathState, PvPFlags, PetFlags, ShapeshiftForm)

		// Attack Power
		AttackPower = 0x7B,               // UNIT_FIELD_ATTACK_POWER
		AttackPowerMods = 0x7C,           // UNIT_FIELD_ATTACK_POWER_MODS
		AttackPowerMultiplier = 0x7D,     // UNIT_FIELD_ATTACK_POWER_MULTIPLIER
		RangedAttackPower = 0x7E,         // UNIT_FIELD_RANGED_ATTACK_POWER
		RangedAttackPowerMods = 0x7F,     // UNIT_FIELD_RANGED_ATTACK_POWER_MODS
		RangedAttackPowerMultiplier = 0x80, // UNIT_FIELD_RANGED_ATTACK_POWER_MULTIPLIER

		// Ranged Damage
		MinRangedDamage = 0x81,   // UNIT_FIELD_MINRANGEDDAMAGE
		MaxRangedDamage = 0x82,   // UNIT_FIELD_MAXRANGEDDAMAGE

		// Power Cost (7 entries each)
		PowerCostModifier = 0x83,     // UNIT_FIELD_POWER_COST_MODIFIER
		PowerCostMultiplier = 0x8A,   // UNIT_FIELD_POWER_COST_MULTIPLIER

		// Final Fields
		MaxHealthModifier = 0x91,     // UNIT_FIELD_MAXHEALTHMODIFIER
		HoverHeight = 0x92,           // UNIT_FIELD_HOVERHEIGHT
		Padding = 0x93,               // UNIT_FIELD_PADDING

		End = 0x94                    // Start of PLAYER_FIELDS
	}
}

// Alias for backward compatibility - same values, different namespace
namespace Styx.WoWInternals
{
	/// <summary>
	/// Alias for WoWUnitFields - used by WoWUnit.cs for HB API compatibility.
	/// Identical to Styx.Offsets.WoWUnitFields with corrected 3.3.5a offsets.
	/// </summary>
	public enum UnitFields
	{
		// GUIDs (2 uint each)
		Charm = 0x6,
		Summon = 0x8,
		Critter = 0xA,
		CharmedBy = 0xC,
		SummonedBy = 0xE,
		CreatedBy = 0x10,
		Target = 0x12,
		ChannelObject = 0x14,
		ChannelSpell = 0x16,
		Bytes0 = 0x17,
		
		// Health & Power
		Health = 0x18,
		Mana = 0x19,
		Rage = 0x1A,
		Focus = 0x1B,
		Energy = 0x1C,
		Happiness = 0x1D,
		Runes = 0x1E,
		RunicPower = 0x1F,
		MaxHealth = 0x20,
		MaxMana = 0x21,
		MaxRage = 0x22,
		MaxFocus = 0x23,
		MaxEnergy = 0x24,
		MaxHappiness = 0x25,
		MaxRunes = 0x26,
		MaxRunicPower = 0x27,
		
		// Regen
		PowerRegenFlatModifier = 0x28,
		PowerRegenInterruptedFlatModifier = 0x2F,
		
		// Level & Faction
		Level = 0x36,
		FactionTemplate = 0x37,
		VirtualItemSlotId = 0x38,
		
		// Flags
		Flags = 0x3B,
		Flags2 = 0x3C,
		AuraState = 0x3D,
		
		// Attack Times
		BaseAttackTime = 0x3E,
		RangedAttackTime = 0x40,
		
		// Bounding
		BoundingRadius = 0x41,
		CombatReach = 0x42,
		
		// Display
		DisplayId = 0x43,
		NativeDisplayId = 0x44,
		MountDisplayId = 0x45,
		
		// Damage
		MinDamage = 0x46,
		MaxDamage = 0x47,
		MinOffhandDamage = 0x48,
		MaxOffhandDamage = 0x49,
		
		// Bytes 1 & Pet
		Bytes1 = 0x4A,
		PetNumber = 0x4B,
		PetNameTimestamp = 0x4C,
		PetExperience = 0x4D,
		PetNextLevelExp = 0x4E,
		
		// Dynamic & Cast
		DynamicFlags = 0x4F,
		ModCastSpeed = 0x50,
		CreatedBySpell = 0x51,
		
		// NPC
		NpcFlags = 0x52,
		NpcEmoteState = 0x53,
		
		// Stats
		Stat0 = 0x54,
		Stat1 = 0x55,
		Stat2 = 0x56,
		Stat3 = 0x57,
		Stat4 = 0x58,
		
		// Stat Modifiers
		PosStat0 = 0x59,
		PosStat1 = 0x5A,
		PosStat2 = 0x5B,
		PosStat3 = 0x5C,
		PosStat4 = 0x5D,
		NegStat0 = 0x5E,
		NegStat1 = 0x5F,
		NegStat2 = 0x60,
		NegStat3 = 0x61,
		NegStat4 = 0x62,
		
		// Resistances
		Resistances = 0x63,
		ResistanceArmor = 0x63,
		ResistanceHoly = 0x64,
		ResistanceFire = 0x65,
		ResistanceNature = 0x66,
		ResistanceFrost = 0x67,
		ResistanceShadow = 0x68,
		ResistanceArcane = 0x69,
		ResistanceBuffModsPositive = 0x6A,
		ResistanceBuffModsNegative = 0x71,
		
		// Base Stats
		BaseMana = 0x78,
		BaseHealth = 0x79,
		
		// Bytes 2
		Bytes2 = 0x7A,
		
		// Attack Power
		AttackPower = 0x7B,
		AttackPowerMods = 0x7C,
		AttackPowerMultiplier = 0x7D,
		RangedAttackPower = 0x7E,
		RangedAttackPowerMods = 0x7F,
		RangedAttackPowerMultiplier = 0x80,
		
		// Ranged Damage
		MinRangedDamage = 0x81,
		MaxRangedDamage = 0x82,
		
		// Power Cost
		PowerCostModifier = 0x83,
		PowerCostMultiplier = 0x8A,
		
		// Final Fields
		MaxHealthModifier = 0x91,
		HoverHeight = 0x92,
		Padding = 0x93,
		End = 0x94
	}
}
