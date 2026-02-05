#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Text;
using GreenMagic;

namespace Styx.WoWInternals
{
    public class WoWChatMessage
    {
        private readonly uint _messagePtr;
        private ChatMessageData _data;

        public WoWChatMessage(uint ptr)
        {
            this._messagePtr = ptr;
            if (this._messagePtr != 0U)
            {
                this._data = ObjectManager.Wow.ReadStruct<ChatMessageData>(ptr);
            }
        }

        public bool IsValid
        {
            get { return (this._messagePtr != 0U); }
        }

        public ulong SenderGuid
        {
            get { return this._data.SenderGuid; }
        }

        public string Sender
        {
            get
            {
                if (this._data.SenderName == null)
                    return string.Empty;
                int nullIndex = Array.IndexOf(this._data.SenderName, (byte)0);
                if (nullIndex < 0) nullIndex = this._data.SenderName.Length;
                return Encoding.UTF8.GetString(this._data.SenderName, 0, nullIndex);
            }
        }

        public string Content
        {
            get
            {
                if (this._data.Content == null)
                    return string.Empty;
                int nullIndex = Array.IndexOf(this._data.Content, (byte)0);
                if (nullIndex < 0) nullIndex = this._data.Content.Length;
                return Encoding.UTF8.GetString(this._data.Content, 0, nullIndex);
            }
        }

        public string Channel
        {
            get { return this._data.ChannelId.ToString(); }
        }

        public ChatType ChatType
        {
            get { return this._data.ChatType; }
        }

        public string Text
        {
            get { return this.Content; }
        }

        public string FormattedMessage
        {
            get
            {
                if (this._data.FormattedMessage == null)
                    return string.Empty;
                int nullIndex = Array.IndexOf(this._data.FormattedMessage, (byte)0);
                if (nullIndex < 0) nullIndex = this._data.FormattedMessage.Length;
                return Encoding.UTF8.GetString(this._data.FormattedMessage, 0, nullIndex);
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
