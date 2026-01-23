using System;
using System.Runtime.InteropServices;

namespace Styx.Logic
{
    /// <summary>
    /// Battleground queue information structure for 3.3.5a
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct QueuedBattlegroundInfo
    {
        public byte ArenaType;
        public byte ArenaFlag;
        public uint TypeId;
        private ushort _padding;
        public ulong BattlegroundId;
        public BattlegroundType BattlegroundType;
        public uint Instance1;
        public BattlegroundStatus Status;
        public uint LowestLevel;
        public uint HighestLevel;
        public uint InstanceId;
        public uint PortCloseTime;
        public uint Time1;
        public uint Time2;
        public uint TeamSize;
        public uint IsRegisteredMatch;
        private uint _padding2;
    }
}
