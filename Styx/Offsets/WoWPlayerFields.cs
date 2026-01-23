using System;

namespace Styx.Offsets
{
	/// <summary>
	/// Player descriptor field indices for WoW 3.3.5a (Build 12340).
	/// These are byte offsets from the player descriptor base.
	/// Player descriptors start after UNIT_END (0x8E).
	/// </summary>
	public enum WoWPlayerFields : uint
	{
		PLAYER_DUEL_ARBITER = 0x0,               // 0x8E from object base, size 2 (GUID)
		PLAYER_FLAGS = 0x2,
		PLAYER_GUILDRANK = 0x3,
		PLAYER_GUILDDELETE_DATE = 0x4,
		PLAYER_GUILDLEVEL = 0x5,
		PLAYER_BYTES = 0x6,
		PLAYER_BYTES_2 = 0x7,
		PLAYER_BYTES_3 = 0x8,
		PLAYER_DUEL_TEAM = 0x9,
		PLAYER_GUILD_TIMESTAMP = 0xA,
		PLAYER_QUEST_LOG_1_1 = 0xB,              // Quest log starts here (25 quests * 5 fields = 125 entries)
		// ... quest log entries ...
		PLAYER_VISIBLE_ITEM_1_ENTRYID = 0x84,   // Visible items (19 slots * 2 = 38 entries)
		PLAYER_VISIBLE_ITEM_1_ENCHANTMENT = 0x85,
		// ... visible items ...
		PLAYER_CHOSEN_TITLE = 0xAA,
		PLAYER_FAKE_INEBRIATION = 0xAB,
		PLAYER_PAD_0 = 0xAC,
		PLAYER_FIELD_INV_SLOT_HEAD = 0xAD,       // Inventory slots (23 bag slots * 2 = 46 entries)
		// ... inventory slots ...
		PLAYER_FIELD_PACK_SLOT_1 = 0xDB,         // Pack slots (16 * 2 = 32 entries)
		// ... pack slots ...
		PLAYER_FIELD_BANK_SLOT_1 = 0xFB,         // Bank slots (28 * 2 = 56 entries)
		// ... bank slots ...
		PLAYER_FIELD_BANKBAG_SLOT_1 = 0x133,    // Bank bag slots (7 * 2 = 14 entries)
		// ... bank bag slots ...
		PLAYER_FIELD_VENDORBUYBACK_SLOT_1 = 0x141, // Vendor buyback (12 * 2 = 24)
		// ... vendor buyback ...
		PLAYER_FIELD_KEYRING_SLOT_1 = 0x159,    // Keyring (32 * 2 = 64 entries)
		// ... keyring slots ...
		PLAYER_FIELD_CURRENCYTOKEN_SLOT_1 = 0x199, // Currency (32 * 2 = 64)
		// ... currency slots ...
		PLAYER_FARSIGHT = 0x1D9,                 // Size 2 (GUID)
		PLAYER__FIELD_KNOWN_TITLES = 0x1DB,      // Size 2
		PLAYER__FIELD_KNOWN_TITLES1 = 0x1DD,     // Size 2
		PLAYER__FIELD_KNOWN_TITLES2 = 0x1DF,     // Size 2
		PLAYER_FIELD_KNOWN_CURRENCIES = 0x1E1,  // Size 2
		PLAYER_XP = 0x1E3,
		PLAYER_NEXT_LEVEL_XP = 0x1E4,
		PLAYER_SKILL_INFO_1_1 = 0x1E5,          // Skill info (384 entries)
		// ... skill info ...
		PLAYER_CHARACTER_POINTS = 0x365,
		PLAYER_CHARACTER_POINTS2 = 0x366,
		PLAYER_TRACK_CREATURES = 0x367,
		PLAYER_TRACK_RESOURCES = 0x368,
		PLAYER_BLOCK_PERCENTAGE = 0x369,        // Float
		PLAYER_DODGE_PERCENTAGE = 0x36A,        // Float
		PLAYER_PARRY_PERCENTAGE = 0x36B,        // Float
		PLAYER_EXPERTISE = 0x36C,
		PLAYER_OFFHAND_EXPERTISE = 0x36D,
		PLAYER_CRIT_PERCENTAGE = 0x36E,         // Float
		PLAYER_RANGED_CRIT_PERCENTAGE = 0x36F,  // Float
		PLAYER_OFFHAND_CRIT_PERCENTAGE = 0x370, // Float
		PLAYER_SPELL_CRIT_PERCENTAGE1 = 0x371,  // 7 schools
		// ... spell crit ...
		PLAYER_SHIELD_BLOCK = 0x378,
		PLAYER_SHIELD_BLOCK_CRIT_PERCENTAGE = 0x379, // Float
		PLAYER_EXPLORED_ZONES_1 = 0x37A,        // 128 entries
		// ... explored zones ...
		PLAYER_REST_STATE_EXPERIENCE = 0x3FA,
		PLAYER_FIELD_COINAGE = 0x3FB,
		PLAYER_FIELD_MOD_DAMAGE_DONE_POS = 0x3FC, // 7 schools
		// ... mod damage done pos ...
		PLAYER_FIELD_MOD_DAMAGE_DONE_NEG = 0x403, // 7 schools
		// ... mod damage done neg ...
		PLAYER_FIELD_MOD_DAMAGE_DONE_PCT = 0x40A, // 7 schools
		// ... mod damage done pct ...
		PLAYER_FIELD_MOD_HEALING_DONE_POS = 0x411,
		PLAYER_FIELD_MOD_HEALING_PCT = 0x412,
		PLAYER_FIELD_MOD_HEALING_DONE_PCT = 0x413,
		PLAYER_FIELD_MOD_TARGET_RESISTANCE = 0x414,
		PLAYER_FIELD_MOD_TARGET_PHYSICAL_RESISTANCE = 0x415,
		PLAYER_FIELD_BYTES = 0x416,
		PLAYER_AMMO_ID = 0x417,
		PLAYER_SELF_RES_SPELL = 0x418,
		PLAYER_FIELD_PVP_MEDALS = 0x419,
		PLAYER_FIELD_BUYBACK_PRICE_1 = 0x41A,   // 12 entries
		// ... buyback prices ...
		PLAYER_FIELD_BUYBACK_TIMESTAMP_1 = 0x426, // 12 entries
		// ... buyback timestamps ...
		PLAYER_FIELD_KILLS = 0x432,
		PLAYER_FIELD_TODAY_CONTRIBUTION = 0x433,
		PLAYER_FIELD_YESTERDAY_CONTRIBUTION = 0x434,
		PLAYER_FIELD_LIFETIME_HONORABLE_KILLS = 0x435,
		PLAYER_FIELD_BYTES2 = 0x436,
		PLAYER_FIELD_WATCHED_FACTION_INDEX = 0x437,
		PLAYER_FIELD_COMBAT_RATING_1 = 0x438,   // 25 entries
		// ... combat ratings ...
		PLAYER_FIELD_ARENA_TEAM_INFO_1_1 = 0x451, // 21 entries
		// ... arena team info ...
		PLAYER_FIELD_HONOR_CURRENCY = 0x466,
		PLAYER_FIELD_ARENA_CURRENCY = 0x467,
		PLAYER_FIELD_MAX_LEVEL = 0x468,
		PLAYER_FIELD_DAILY_QUESTS_1 = 0x469,    // 25 entries
		// ... daily quests ...
		PLAYER_RUNE_REGEN_1 = 0x482,            // 4 floats
		// ... rune regen ...
		PLAYER_NO_REAGENT_COST_1 = 0x486,       // 3 entries
		// ... no reagent cost ...
		PLAYER_FIELD_GLYPH_SLOTS_1 = 0x489,     // 6 entries
		// ... glyph slots ...
		PLAYER_FIELD_GLYPHS_1 = 0x48F,          // 6 entries
		// ... glyphs ...
		PLAYER_GLYPHS_ENABLED = 0x495,
		PLAYER_PET_SPELL_POWER = 0x496,
		PLAYER_END = 0x497
	}
}
