using Styx.WoWInternals.WoWObjects;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Represents the character's equipped items (paper doll).
    /// </summary>
    public class WoWPaperDoll : WoWBag
    {
        internal WoWPaperDoll(BagStructure bag)
            : base(bag)
        {
        }

        internal WoWPaperDoll(BagStructure bag, uint firstSlotIndex, uint slots)
            : base(bag, firstSlotIndex, slots)
        {
        }

        public WoWItem GetEquippedItem(WoWInventorySlot slot) => this.GetItemBySlot((uint)slot);

        public WoWItem Head => this.GetEquippedItem(WoWInventorySlot.Head);

        public WoWItem Neck => this.GetEquippedItem(WoWInventorySlot.Neck);

        public WoWItem Shoulders => this.GetEquippedItem(WoWInventorySlot.Shoulders);

        public WoWItem Shirt => this.GetEquippedItem(WoWInventorySlot.Shirt);

        public WoWItem Chest => this.GetEquippedItem(WoWInventorySlot.Chest);

        public WoWItem Waist => this.GetEquippedItem(WoWInventorySlot.Waist);

        public WoWItem Legs => this.GetEquippedItem(WoWInventorySlot.Legs);

        public WoWItem Feet => this.GetEquippedItem(WoWInventorySlot.Feet);

        public WoWItem Wrists => this.GetEquippedItem(WoWInventorySlot.Wrists);

        public WoWItem Hands => this.GetEquippedItem(WoWInventorySlot.Hands);

        public WoWItem Finger1 => this.GetEquippedItem(WoWInventorySlot.Finger1);

        public WoWItem Finger2 => this.GetEquippedItem(WoWInventorySlot.Finger2);

        public WoWItem Trinket1 => this.GetEquippedItem(WoWInventorySlot.Trinket1);

        public WoWItem Trinket2 => this.GetEquippedItem(WoWInventorySlot.Trinket2);

        public WoWItem Back => this.GetEquippedItem(WoWInventorySlot.Back);

        public WoWItem MainHand => this.GetEquippedItem(WoWInventorySlot.MainHand);

        public WoWItem OffHand => this.GetEquippedItem(WoWInventorySlot.OffHand);

        public WoWItem Ranged => this.GetEquippedItem(WoWInventorySlot.Ranged);

        public WoWItem Tabard => this.GetEquippedItem(WoWInventorySlot.Tabard);
    }
}
