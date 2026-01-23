#nullable disable

namespace Styx.Logic.Inventory.Frames.Gossip
{
    /// <summary>
    /// Represents a gossip menu option.
    /// </summary>
    public struct GossipEntry
    {
        public string Text;
        public GossipEntryType Type;
        public int Index;

        public enum GossipEntryType
        {
            Unknown = 0,
            Banker = 1,
            BattleMaster = 2,
            Binder = 3,
            Gossip = 4,
            Healer = 5,
            Petition = 6,
            Tabard = 7,
            Taxi = 8,
            Trainer = 9,
            Unlearn = 10,
            Vendor = 11
        }
    }
}
