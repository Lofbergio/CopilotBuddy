#nullable disable
using System;
using System.Threading;
using GreenMagic;

namespace Styx.WoWInternals
{
    public static class WoWChat
    {
        private static ChatMessageHandler chatMessageHandler_0;
        private static ChatMessageHandler chatMessageHandler_1;
        private static ChatMessageHandler chatMessageHandler_2;
        private static ChatMessageHandler chatMessageHandler_3;
        private static ChatMessageHandler chatMessageHandler_4;
        private static ChatMessageHandler chatMessageHandler_5;
        private static ChatMessageHandler chatMessageHandler_6;
        private static ChatMessageHandler chatMessageHandler_7;
        private static ChatMessageHandler chatMessageHandler_8;
        private static ChatMessageHandler chatMessageHandler_9;
        private static ChatMessageHandler chatMessageHandler_10;
        private static ChatMessageHandler chatMessageHandler_11;
        private static ChatMessageHandler chatMessageHandler_12;
        private static ChatMessageHandler chatMessageHandler_13;
        private static uint uint_0;
        private static bool bool_0;

        static WoWChat()
        {
            WoWChat.bool_0 = true;
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
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_0;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_0, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_0;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_0, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewSayMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_1;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_1, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_1;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_1, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewPartyMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_2;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_2, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_2;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_2, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewRaidMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_3;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_3, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_3;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_3, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewRaidLeaderMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_4;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_4, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_4;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_4, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewGuildMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_5;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_5, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_5;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_5, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewOfficerMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_6;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_6, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_6;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_6, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewYellMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_7;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_7, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_7;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_7, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewChannelMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_8;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_8, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_8;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_8, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewWhisperFromMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_9;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_9, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_9;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_9, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewWhisperToMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_10;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_10, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_10;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_10, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewEmoteMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_11;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_11, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_11;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_11, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewBattlegroundMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_12;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_12, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_12;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_12, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        public static event ChatMessageHandler NewBattlegroundLeaderMessage
        {
            add
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_13;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Combine(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_13, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
            remove
            {
                ChatMessageHandler chatMessageHandler = WoWChat.chatMessageHandler_13;
                ChatMessageHandler chatMessageHandler2;
                do
                {
                    chatMessageHandler2 = chatMessageHandler;
                    ChatMessageHandler chatMessageHandler3 = (ChatMessageHandler)Delegate.Remove(chatMessageHandler2, value);
                    chatMessageHandler = Interlocked.CompareExchange<ChatMessageHandler>(ref WoWChat.chatMessageHandler_13, chatMessageHandler3, chatMessageHandler2);
                }
                while (chatMessageHandler != chatMessageHandler2);
            }
        }

        private static void RaiseChatEvent(ChatMessageEventArgs e)
        {
            if (WoWChat.chatMessageHandler_0 != null)
            {
                WoWChat.chatMessageHandler_0(e);
            }
            ChatType chatType = e.Message.ChatType;
            switch (chatType)
            {
                case ChatType.Say:
                    if (WoWChat.chatMessageHandler_1 != null)
                    {
                        WoWChat.chatMessageHandler_1(e);
                    }
                    break;
                case ChatType.Party:
                    if (WoWChat.chatMessageHandler_2 != null)
                    {
                        WoWChat.chatMessageHandler_2(e);
                    }
                    break;
                case ChatType.Raid:
                    if (WoWChat.chatMessageHandler_3 != null)
                    {
                        WoWChat.chatMessageHandler_3(e);
                    }
                    break;
                case ChatType.Guild:
                    if (WoWChat.chatMessageHandler_5 != null)
                    {
                        WoWChat.chatMessageHandler_5(e);
                    }
                    break;
                case ChatType.Officer:
                    if (WoWChat.chatMessageHandler_6 != null)
                    {
                        WoWChat.chatMessageHandler_6(e);
                    }
                    break;
                case ChatType.Yell:
                    if (WoWChat.chatMessageHandler_7 != null)
                    {
                        WoWChat.chatMessageHandler_7(e);
                    }
                    break;
                case ChatType.WhisperInform:
                    if (WoWChat.chatMessageHandler_9 != null)
                    {
                        WoWChat.chatMessageHandler_9(e);
                    }
                    break;
                case ChatType.WhisperTo:
                    if (WoWChat.chatMessageHandler_10 != null)
                    {
                        WoWChat.chatMessageHandler_10(e);
                    }
                    break;
                case ChatType.Emote:
                    if (WoWChat.chatMessageHandler_11 != null)
                    {
                        WoWChat.chatMessageHandler_11(e);
                    }
                    break;
                case ChatType.Channel:
                    if (WoWChat.chatMessageHandler_8 != null)
                    {
                        WoWChat.chatMessageHandler_8(e);
                    }
                    break;
                case ChatType.RaidLeader:
                    if (WoWChat.chatMessageHandler_4 != null)
                    {
                        WoWChat.chatMessageHandler_4(e);
                    }
                    break;
                case ChatType.Battleground:
                    if (WoWChat.chatMessageHandler_12 != null)
                    {
                        WoWChat.chatMessageHandler_12(e);
                    }
                    break;
                case ChatType.BattlegroundLeader:
                    if (WoWChat.chatMessageHandler_13 != null)
                    {
                        WoWChat.chatMessageHandler_13(e);
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
            if (WoWChat.bool_0)
            {
                WoWChat.uint_0 = WoWChat.Position;
                WoWChat.bool_0 = false;
            }
            else
            {
                uint position = WoWChat.Position;
                if (position != WoWChat.uint_0)
                {
                    uint num;
                    if (position > WoWChat.uint_0)
                    {
                        num = position - WoWChat.uint_0;
                    }
                    else
                    {
                        int num2 = (int)(WoWChat.uint_0 - 60U);
                        num2 += (int)position;
                        num2 = Math.Abs(num2);
                        num = (uint)num2;
                    }
                    for (uint num3 = 0U; num3 < num; num3 += 1U)
                    {
                        WoWChatMessage woWChatMessage = new WoWChatMessage(WoWChat.GetChatMessagePtr(num3));
                        WoWChat.RaiseChatEvent(new ChatMessageEventArgs(woWChatMessage));
                    }
                    WoWChat.uint_0 = position;
                }
            }
        }
    }
}
