#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Text;
using GreenMagic;

namespace Styx.WoWInternals
{
    public class WoWChatMessage
    {
        private readonly uint uint_0;
        private ChatMessageData chatMessageData_0;

        public WoWChatMessage(uint ptr)
        {
            this.uint_0 = ptr;
            if (this.uint_0 != 0U)
            {
                this.chatMessageData_0 = ObjectManager.Wow.ReadStruct<ChatMessageData>(ptr);
            }
        }

        public bool IsValid
        {
            get { return (this.uint_0 != 0U); }
        }

        public ulong SenderGuid
        {
            get { return this.chatMessageData_0.SenderGuid; }
        }

        public string Sender
        {
            get
            {
                if (this.chatMessageData_0.SenderName == null)
                    return string.Empty;
                int nullIndex = Array.IndexOf(this.chatMessageData_0.SenderName, (byte)0);
                if (nullIndex < 0) nullIndex = this.chatMessageData_0.SenderName.Length;
                return Encoding.UTF8.GetString(this.chatMessageData_0.SenderName, 0, nullIndex);
            }
        }

        public string Content
        {
            get
            {
                if (this.chatMessageData_0.Content == null)
                    return string.Empty;
                int nullIndex = Array.IndexOf(this.chatMessageData_0.Content, (byte)0);
                if (nullIndex < 0) nullIndex = this.chatMessageData_0.Content.Length;
                return Encoding.UTF8.GetString(this.chatMessageData_0.Content, 0, nullIndex);
            }
        }

        public string Channel
        {
            get { return this.chatMessageData_0.ChannelId.ToString(); }
        }

        public ChatType ChatType
        {
            get { return this.chatMessageData_0.ChatType; }
        }

        public string Text
        {
            get { return this.Content; }
        }

        public string FormattedMessage
        {
            get
            {
                if (this.chatMessageData_0.FormattedMessage == null)
                    return string.Empty;
                int nullIndex = Array.IndexOf(this.chatMessageData_0.FormattedMessage, (byte)0);
                if (nullIndex < 0) nullIndex = this.chatMessageData_0.FormattedMessage.Length;
                return Encoding.UTF8.GetString(this.chatMessageData_0.FormattedMessage, 0, nullIndex);
            }
        }

        public override string ToString()
        {
            return string.Format("[ChatMessage][{0}][{1}]: {2}", this.ChatType, this.Sender, this.Content);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ChatMessageData
        {
            public ulong SenderGuid;
            private uint Unused;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
            public byte[] SenderName;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3000)]
            public byte[] FormattedMessage;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3000)]
            public byte[] Content;

            public ChatType ChatType;
            public uint ChannelId;
            public uint Unused2;
        }
    }
}
