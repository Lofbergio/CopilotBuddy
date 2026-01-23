using System;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Event args for chat messages
    /// </summary>
    public class ChatMessageEventArgs : EventArgs
    {
        public WoWChatMessage Message { get; private set; }

        public ChatMessageEventArgs(WoWChatMessage message)
        {
            Message = message;
        }
    }
}
