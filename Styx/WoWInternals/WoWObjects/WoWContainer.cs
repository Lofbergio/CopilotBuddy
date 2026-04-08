using System;
using System.Collections.Generic;
using System.Linq;
using GreenMagic;

namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Represents an equipped bag container in the player's inventory.
    /// Ported from HB 4.3.4 WoWContainer — delegates all item access to an internal WoWBag
    /// constructed from BagStructure at containerBaseAddress + 1888 (3.3.5a offset).
    /// </summary>
    public class WoWContainer : WoWItem
    {
        #region Constructor

        public WoWContainer(uint baseAddress) : base(baseAddress)
        {
        }

        #endregion

        #region Bag — core delegation target

        /// <summary>
        /// Internal WoWBag built from BagStructure at BaseAddress + 1888.
        /// HB 4.3.4: new WoWBag(ObjectManager.Wow.ReadStruct&lt;Struct58&gt;(base.BaseAddress + offset(4998)))
        /// HB 3.3.5a offset 4998 = 1888.
        /// </summary>
        public WoWBag Bag
        {
            get
            {
                Memory? wow = ObjectManager.Wow;
                if (wow == null) return null!;
                return new WoWBag(wow.Read<BagStructure>(new uint[] { BaseAddress + 1888U }));
            }
        }

        #endregion

        #region BagType

        /// <summary>
        /// Gets the bag type from the item's SubClass.
        /// WotLK bag SubClass mapping: 0=Normal, 1=SoulShard, 2=Herb, 3=Enchanting,
        /// 4=Engineering, 5=Gem, 6=Mining, 7=Leatherworking, 8=Inscription, 9=Ammo.
        /// </summary>
        public BagType BagType
        {
            get
            {
                return (BagType)(ItemInfo?.SubClassId ?? 0);
            }
        }

        #endregion

        #region Container Properties — delegated to Bag

        /// <summary>
        /// Number of slots in this container. Ported from HB 4.3.4 WoWContainer.Slots.
        /// </summary>
        public uint Slots => Bag.Slots;

        /// <summary>
        /// Number of occupied slots. Ported from HB 4.3.4 WoWContainer.UsedSlots.
        /// </summary>
        public uint UsedSlots => Bag.UsedSlots;

        /// <summary>
        /// Number of free slots. Ported from HB 4.3.4 WoWContainer.FreeSlots.
        /// </summary>
        public uint FreeSlots => Bag.FreeSlots;

        public bool IsFull => FreeSlots == 0;
        public bool IsEmpty => FreeSlots == Slots;

        /// <summary>
        /// Gets the bag slot index (0-10) this container occupies, or -1 if not found.
        /// Ported from HB 4.3.4 WoWContainer.BagIndex.
        /// </summary>
        public new int BagIndex
        {
            get
            {
                for (uint i = 0U; i <= 10U; i++)
                {
                    if (ObjectManager.Me.GetBagGuidAtIndex(i) == Guid)
                        return (int)i;
                }
                return -1;
            }
        }

        #endregion

        #region Slot Access — delegated to Bag

        /// <summary>
        /// All item GUIDs (including empty slots as 0). Ported from HB 4.3.4.
        /// </summary>
        public ulong[] ItemGuids => Bag.ItemGuids;

        /// <summary>
        /// All items in the container (non-null only). Ported from HB 4.3.4.
        /// </summary>
        public new List<WoWItem> Items => Bag.Items.Where(i => i != null).ToList();

        /// <summary>
        /// GUIDs of physical (non-zero) items. Ported from HB 4.3.4.
        /// </summary>
        public ulong[] PhysicalItemGuids => Bag.PhysicalItemGuids;

        /// <summary>
        /// Non-null items as an array. Ported from HB 4.3.4.
        /// </summary>
        public WoWItem[] PhysicalItems => Bag.PhysicalItems;

        /// <summary>
        /// Returns the item GUID at the given slot. Ported from HB 4.3.4.
        /// </summary>
        public ulong GetItemGuidBySlot(uint slot) => Bag.GetItemGuidBySlot(slot);

        /// <summary>
        /// Returns the item at the given slot. Ported from HB 4.3.4.
        /// </summary>
        public WoWItem? GetItemBySlot(uint slot) => Bag.GetItemBySlot(slot);

        #endregion

        #region Search Methods

        public int FindFirstFreeSlot()
        {
            ulong[] guids = ItemGuids;
            for (int i = 0; i < guids.Length; i++)
            {
                if (guids[i] == 0UL)
                    return i;
            }
            return -1;
        }

        public WoWItem? FindItemByEntry(uint entryId)
        {
            return Items.FirstOrDefault(item => item.Entry == entryId);
        }

        public uint CountItemsByEntry(uint entryId)
        {
            uint count = 0;
            foreach (var item in Items)
            {
                if (item.Entry == entryId)
                    count += item.StackCount;
            }
            return count;
        }

        #endregion

        #region Implicit Conversion

        /// <summary>
        /// Implicit conversion to WoWBag. Ported from HB 4.3.4.
        /// </summary>
        public static implicit operator WoWBag(WoWContainer container)
        {
            return container?.Bag!;
        }

        #endregion

        #region ToString

        public override string ToString()
        {
            return $"[Container: {Name} (Slots: {Slots}, Free: {FreeSlots})]";
        }

        #endregion
    }
}
