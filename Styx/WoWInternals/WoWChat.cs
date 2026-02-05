#nullable disable
using System;
using System.Threading;
using GreenMagic;

namespace Styx.WoWInternals
{
    public static class WoWChat
    {
        private static ChatMessageHandler _onChatMessage;
        private static ChatMessageHandler _onSayMessage;
        private static ChatMessageHandler _onPartyMessage;
        private static ChatMessageHandler _onRaidMessage;
        private static ChatMessageHandler _onRaidLeaderMessage;
        private static ChatMessageHandler _onGuildMessage;
        private static ChatMessageHandler _onOfficerMessage;
        private static ChatMessageHandler _onYellMessage;
        private static ChatMessageHandler _onChannelMessage;
        private static ChatMessageHandler _onWhisperFromMessage;
        private static ChatMessageHandler _onWhisperToMessage;
        private static ChatMessageHandler _onEmoteMessage;
        private static ChatMessageHandler _onBattlegroundMessage;
        private static ChatMessageHandler _onBattlegroundLeaderMessage;
        private static uint _lastReadPosition;
        private static bool _isFirstRead;

        static WoWChat()
        {
            WoWChat._isFirstRead = true;
        }

        public static void SendChatMessage(string content, ChatType chatType, string channel)
        {
            string text = "NIL";
            switch (chatType)
            {
                case ChatType.Say:
                    text = "SAY";
                    break;
                case ChatType.Party:
                    text = "PARTY";
                    break;
                case ChatType.Raid:
                    text = "RAID";
                    break;
                case ChatType.Guild:
                    text = "GUILD";
                    break;
                case ChatType.Officer:
                    text = "OFFICER";
                    break;
                case ChatType.Yell:
                    text = "YELL";
                    break;
                case ChatType.WhisperTo:
                    text = "WHISPER";
                    break;
                case ChatType.Emote:
                    text = "EMOTE";
                    break;
                case ChatType.Channel:
                    text = "CHANNEL";
                    break;
                case ChatType.RaidWarning:
                    text = "RAID_WARNING";
                    break;
                case ChatType.Battleground:
                    text = "BATTLEGROUND";
                    break;
            }
            if (chatType != ChatType.Channel)
            {
                string text2 = (chatType == ChatType.WhisperTo) ? string.Format("SendChatMessage('{0}','{1}',GetDefaultLanguage('player'), '{2}')", content, text, channel) : string.Format("SendChatMessage('{0}','{1}',GetDefaultLanguage('player'))", content, text);
                Lua.DoString(text2);
            }
            else
            {
                string[] array = new string[]
                {
                    "SendChatMessage(\"",
                    content,
                    "\", \"",
                    text,
                    "\", GetDefaultLanguage(\"player\"), GetChannelName(\"",
                    channel,
                    "\"));"
                };
                string text3 = string.Concat(array);
                Lua.DoString(text3);
            }
        }

        public static event ChatMessageHandler NewChatMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onChatMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onChatMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onChatMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onChatMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewSayMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onSayMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onSayMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onSayMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onSayMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewPartyMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onPartyMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onPartyMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onPartyMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onPartyMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewRaidMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onRaidMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onRaidMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onRaidMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onRaidMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewRaidLeaderMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onRaidLeaderMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onRaidLeaderMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onRaidLeaderMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onRaidLeaderMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewGuildMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onGuildMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onGuildMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onGuildMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onGuildMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewOfficerMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onOfficerMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onOfficerMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onOfficerMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onOfficerMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewYellMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onYellMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onYellMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onYellMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onYellMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewChannelMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onChannelMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onChannelMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onChannelMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onChannelMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewWhisperFromMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onWhisperFromMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onWhisperFromMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onWhisperFromMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onWhisperFromMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewWhisperToMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onWhisperToMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onWhisperToMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onWhisperToMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onWhisperToMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewEmoteMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onEmoteMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onEmoteMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onEmoteMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onEmoteMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewBattlegroundMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onBattlegroundMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onBattlegroundMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onBattlegroundMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onBattlegroundMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewBattlegroundLeaderMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onBattlegroundLeaderMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onBattlegroundLeaderMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat._onBattlegroundLeaderMessage;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat._onBattlegroundLeaderMessage, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        private static void RaiseChatEvent(ChatMessageEventArgs e)
        {
            if (WoWChat._onChatMessage != null)
            {
                WoWChat._onChatMessage(e);
            }
            ChatType chatType = e.Message.ChatType;
            switch (chatType)
            {
                case ChatType.Say:
                    if (WoWChat._onSayMessage != null)
                    {
                        WoWChat._onSayMessage(e);
                    }
                    break;
                case ChatType.Party:
                    if (WoWChat._onPartyMessage != null)
                    {
                        WoWChat._onPartyMessage(e);
                    }
                    break;
                case ChatType.Raid:
                    if (WoWChat._onRaidMessage != null)
                    {
                        WoWChat._onRaidMessage(e);
                    }
                    break;
                case ChatType.Guild:
                    if (WoWChat._onGuildMessage != null)
                    {
                        WoWChat._onGuildMessage(e);
                    }
                    break;
                case ChatType.Officer:
                    if (WoWChat._onOfficerMessage != null)
                    {
                        WoWChat._onOfficerMessage(e);
                    }
                    break;
                case ChatType.Yell:
                    if (WoWChat._onYellMessage != null)
                    {
                        WoWChat._onYellMessage(e);
                    }
                    break;
                case ChatType.WhisperInform:
                    if (WoWChat._onWhisperFromMessage != null)
                    {
                        WoWChat._onWhisperFromMessage(e);
                    }
                    break;
                case ChatType.WhisperTo:
                    if (WoWChat._onWhisperToMessage != null)
                    {
                        WoWChat._onWhisperToMessage(e);
                    }
                    break;
                case ChatType.Emote:
                    if (WoWChat._onEmoteMessage != null)
                    {
                        WoWChat._onEmoteMessage(e);
                    }
                    break;
                case ChatType.Channel:
                    if (WoWChat._onChannelMessage != null)
                    {
                        WoWChat._onChannelMessage(e);
                    }
                    break;
                case ChatType.RaidLeader:
                    if (WoWChat._onRaidLeaderMessage != null)
                    {
                        WoWChat._onRaidLeaderMessage(e);
                    }
                    break;
                case ChatType.Battleground:
                    if (WoWChat._onBattlegroundMessage != null)
                    {
                        WoWChat._onBattlegroundMessage(e);
                    }
                    break;
                case ChatType.BattlegroundLeader:
                    if (WoWChat._onBattlegroundLeaderMessage != null)
                    {
                        WoWChat._onBattlegroundLeaderMessage(e);
                    }
                    break;
            }
        }

        private static Memory Memory
        {
            get { return ObjectManager.Wow; }
        }

        private static uint Position
        {
            get { return Memory.Read<uint>(12382196U); }
        }

        private static uint GetChatMessagePtr(uint index)
        {
            uint num = WoWChat.Position - 1U;
            uint num2 = (num + index) % 60U;
            uint num3 = 12016224U + num2 * 6080U;
            return num3;
        }

        internal static void Update()
        {
            if (WoWChat._isFirstRead)
            {
                WoWChat._lastReadPosition = WoWChat.Position;
                WoWChat._isFirstRead = false;
            }
            else
            {
                uint position = WoWChat.Position;
                if (position != WoWChat._lastReadPosition)
                {
                    uint num;
                    if (position > WoWChat._lastReadPosition)
                    {
                        num = position - WoWChat._lastReadPosition;
                    }
                    else
                    {
                        int num2 = (int)(WoWChat._lastReadPosition - 60U);
                        num2 += (int)position;
                        num2 = Math.Abs(num2);
                        num = (uint)num2;
                    }
                    for (uint num3 = 0U; num3 < num; num3 += 1U)
                    {
                        WoWChatMessage woWChatMessage = new WoWChatMessage(WoWChat.GetChatMessagePtr(num3));
                        WoWChat.RaiseChatEvent(new ChatMessageEventArgs(woWChatMessage));
                    }
                    WoWChat._lastReadPosition = position;
                }
            }
        }
    }
}
