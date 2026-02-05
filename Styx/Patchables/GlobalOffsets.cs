using System;

namespace Styx.Patchables
{
    public enum GlobalOffsets
    {
        // Character/Player
        GetGUIDByKeyword = 6335472,             // 0x60A7F0
        ClntObjMgrGetActivePlayerObj = 4208880, // 0x4038F0
        ClntObjMgrObjectPtr = 5066160,          // 0x4D4DB0
        
        // UI/Targeting
        CGGameUI__Target = 5393392,             // 0x524DF0
        GetWorldState = 5541136,                // 0x548710
        
        // Looting
        LootFramePointer = 12560600,            // 0xBFA8D8
        
        // Quest
        CGQuestInfo_C__GetAvailableQuestInfoFromIndex = 5809760, // 0x58A2E0
        CGQuestInfo_C__GetActiveQuestFromIndex = 5810000,        // 0x58A3D0
        CGQuestLog__IsQuestCompleted_2 = 6164656,                // 0x5E1CB0
        CGQuestLog__GetLuaQuestIndexByID = 6155952,              // 0x5DFB30
        CGQuestLog__AbandonSelectedQuest__ = 6163648,            // 0x5E18C0
        CGQuestLog__IsQuestCompleted = 6164128,                  // 0x5E1AA0
        CGQuestLog__GetQuestIdByIndex = 6174784,                 // 0x5E4340
        CGPlayer_C__QuestLogRemoveQuest = 7163776,               // 0x6D4380
        
        // Input
        CGInputControl__UpdatePlayer = 6273984, // 0x5FB3C0
        CGInputControl__ToggleControlBit = 6274576, // 0x5FB610
        
        // Items
        CGItem_C__Use = 7375904,                // 0x709020
        CGItem_C__CreateItemLink = 6414992,     // 0x61E690
        CGItem_C__CreateItemLink2 = 6415264,    // 0x61E7A0
        CGItem_C__BuildItemName = 7368048,      // 0x707170
        CGItemStats_C = 6424480,                // 0x620BA0
        CGPlayer_C__CanUseItem = 7193584,       // 0x6DB5F0
        
        // Cache - Creature
        DbCreatureCache_GetInfoBlockById = 6796960, // 0x67B6A0
        
        // Cache - GameObject
        DbGameObjectCache_GetInfoBlockById = 6798656, // 0x67BD40
        
        // Cache - Arena Team
        DbArenaTeamCache_GetInfoBlockById = 6800352, // 0x67C3E0
        
        // Cache - Item
        DBItemCache_GetInfoBlockByID = 6801968,     // 0x67CA30
        
        // Cache - NPC
        DbNpcCache_GetInfoBlockById = 6803664,      // 0x67D0D0
        
        // Cache - Name
        DbNameCache_GetInfoBlockById = 6805360,     // 0x67D770
        
        // Cache - Guild
        DbGuildCache_GetInfoBlockById = 6805808,    // 0x67D930
        
        // Cache - Quest
        DbQuestCache_GetInfoBlockById = 6807184,    // 0x67DE90
        
        // Cache - ItemName
        DbItemNameCache_GetInfoBlockById = 6808544, // 0x67E3E0
        
        // Cache - PetName
        DbPetNameCache_GetInfoBlockById = 6810160,  // 0x67EA30
        
        // Cache - Petition
        DbPetitionCache_GetInfoBlockById = 6811504, // 0x67EF70
        
        // Cache - ItemText
        DbItemTextCache_GetInfoBlockById = 6812864, // 0x67F4C0
        
        // Cache - WoW
        DbWoWCache_GetInfoBlockById = 6814336,      // 0x67FAA0
        
        // Cache - PageText
        DbPageTextCache_GetInfoBlockById = 6816112, // 0x680170
        
        // Cache - Dance
        DbDanceCache_GetInfoBlockById = 6817488,    // 0x6806D0
        
        // Client Database
        ClientDb_RegisterBase = 6502352,            // 0x633DD0
        
        // Doors
        CGDoor_C__CanOpenNow = 7412176,             // 0x712AD0
        
        // Environment
        IsOutdoors = 7452656,                       // 0x71C7F0
        
        // Localization
        FrameScript__GetLocalizedText = 7480800,    // 0x7233E0
        
        // Units
        CGUnit_C__UnitReaction = 7492032,           // 0x725FC0
        CGUnit_C_CalculateThreat = 7566528,         // 0x737AC0
        
// Movement - FROM 335offsetsall.txt
		CGPlayer_C__ClickToMove = 7509504,          // 0x727400
		CGPlayer_C__ClickToMoveStop = 7517088,      // 0x72B3A0
        
        // Collision
        TraceLine = 8010608,                        // 0x7A3770
        
        // Spells
        Spell_C__GetSpellCooldown = 8419712,        // 0x806A80
        Spell_C__HandleTerrainClick = 8438592,      // 0x80B340
        Spell_C__CastSpell = 8444480,               // 0x80CA40
        
        // Lua
        LuaState = 0x00D3F78C,                      // Static pointer to Lua state
        FrameScript_Execute = 8491536,              // 0x819210
        FrameScript_GetTop = 8707024,               // 0x84DBD0
        FrameScript__SetTop = 8707056,              // 0x84DBF0
        FrameScript_ToLString = 8708320,            // 0x84E0E0
        FrameScript_PCall = 8711248,                // 0x84EC50
        FrameScript_Load = 8714336,                 // 0x84F860
        
        // Performance
        PerformanceCounter = 8826400                // 0x86C220
    }
}
