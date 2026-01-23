#nullable disable
using System;

namespace Styx.WoWInternals
{
    public enum ChatType : byte
    {
        Addon,
        Say,
        Party,
        Raid,
        Guild,
        Officer,
        Yell,
        WhisperInform,
        WhisperMob,
        WhisperTo,
        Emote,
        TextEmote,
        MonsterSay,
        MonsterParty,
        MonsterYell,
        MonsterWhisper,
        MonsterEmote,
        Channel,
        ChannelJoin,
        ChannelLeave,
        ChannelList,
        ChannelNotice,
        ChannelNoticeUser,
        Afk,
        Dnd,
        Ignored,
        Skill,
        Loot,
        System,
        BgEventNeutral = 35,
        BgEventAlliance,
        BgEventHorde,
        CombatFactionChange,
        RaidLeader,
        RaidWarning,
        RaidWarningWidescreen,
        Filtered = 43,
        Battleground,
        BattlegroundLeader,
        Restricted
    }
}
