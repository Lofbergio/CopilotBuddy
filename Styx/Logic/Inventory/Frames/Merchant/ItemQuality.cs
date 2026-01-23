#nullable disable

using System;

namespace Styx.Logic.Inventory.Frames.Merchant
{
    /// <summary>
    /// Item quality levels (rarity).
    /// </summary>
    [Flags]
    public enum ItemQuality
    {
        None = 0,
        Poor = 1,
        Common = 2,
        Uncommon = 4,
        Rare = 8,
        Epic = 16,
        Legendary = 32,
        Artifact = 32,
        Heirloom = 64
    }
}
