#nullable disable

using System.Runtime.InteropServices;
using GreenMagic;
using Styx.WoWInternals;

namespace Styx.Logic.Inventory.Frames.Gossip
{
    /// <summary>
    /// Represents a quest entry in the gossip menu.
    /// </summary>
    public class GossipQuestEntry
    {
        private readonly QuestInfoStruct _data;
        private readonly uint _pointer;

        public int Index;

        internal GossipQuestEntry(uint ptr, int index)
        {
            Index = index;
            _pointer = ptr;

            if (ptr != 0U)
            {
                Memory wow = ObjectManager.Wow;
                if (wow != null)
                    _data = wow.Read<QuestInfoStruct>(ptr);
            }
        }

        /// <summary>
        /// Whether this entry is valid.
        /// </summary>
        public bool IsValid => _pointer != 0U;

        /// <summary>
        /// Quest ID.
        /// </summary>
        public int Id => _data.QuestId;

        /// <summary>
        /// Quest name.
        /// </summary>
        public unsafe string Name
        {
            get
            {
                if (_data.Name == null || _data.Name.Length == 0)
                    return string.Empty;

                fixed (char* ptr = &_data.Name[0])
                {
                    return new string(ptr);
                }
            }
        }

        /// <summary>
        /// Required level for the quest.
        /// </summary>
        public int RequiredLevel => _data.RequiredLevel;

        [StructLayout(LayoutKind.Sequential)]
        private struct QuestInfoStruct
        {
            public int QuestId;
            public int RequiredLevel;
            private uint Reserved;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
            public char[] Name;
        }
    }
}
