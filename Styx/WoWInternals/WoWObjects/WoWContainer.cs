using System;
using System.Collections.Generic;

namespace Styx.WoWInternals.WoWObjects
{
    public class WoWContainer : WoWItem
    {
        #region Descriptor Offsets - CONTAINER_FIELD
        
        // ContainerFields pour 3.3.5a (offset depuis descriptors)
        // Note: Container hérite de Item, donc les premiers offsets sont Item fields
        private const int CONTAINER_FIELD_NUM_SLOTS = 0x0;     // 4 bytes - après ItemFields
        private const int CONTAINER_FIELD_ALIGN_PAD = 0x4;     // 4 bytes - padding
        private const int CONTAINER_FIELD_SLOT_1 = 0x8;        // Array de 36 x 8 bytes (36 slots max)
        
        // Offset de base pour les container fields (après tous les item fields)
        private const int CONTAINER_FIELDS_BASE = 0xCC;        // Après ITEM_FIELD_CREATE_PLAYED_TIME + 4
        
        #endregion
        
        #region Constructor
        public WoWContainer(uint baseAddress) : base(baseAddress)
        {
        }
        
        #endregion
        
        #region Container Properties
        public uint NumSlots
        {
            get
            {
                return GetDescriptorField<uint>(CONTAINER_FIELDS_BASE + CONTAINER_FIELD_NUM_SLOTS);
            }
        }
        public uint FreeSlotsCount
        {
            get
            {
                uint free = 0;
                uint slots = NumSlots;
                for (uint i = 0; i < slots; i++)
                {
                    if (GetSlotGuid(i) == 0)
                        free++;
                }
                return free;
            }
        }

        /// <summary>
        /// Gets the number of free slots.
        /// Ported from HB 3.3.5a WoWContainer.FreeSlots
        /// </summary>
        public uint FreeSlots
        {
            get
            {
                try
                {
                    return FreeSlotsCount;
                }
                catch (Exception ex)
                {
                    Styx.Helpers.Logging.WriteException(ex);
                    return 0;
                }
            }
        }

        public bool IsFull => FreeSlotsCount == 0;
        public bool IsEmpty => FreeSlotsCount == NumSlots;
        
        #endregion
        
        #region Slot Access
        public ulong GetSlotGuid(uint slotIndex)
        {
            if (slotIndex >= NumSlots)
                return 0;
                
            int offset = CONTAINER_FIELDS_BASE + CONTAINER_FIELD_SLOT_1 + (int)(slotIndex * 8);
            return GetDescriptorField<ulong>(offset);
        }
        public WoWItem? GetSlotItem(uint slotIndex)
        {
            ulong guid = GetSlotGuid(slotIndex);
            if (guid == 0)
                return null;
                
            return ObjectManager.GetObjectByGuid<WoWItem>(guid);
        }
        public List<WoWItem> Items
        {
            get
            {
                var items = new List<WoWItem>();
                uint slots = NumSlots;
                for (uint i = 0; i < slots; i++)
                {
                    var item = GetSlotItem(i);
                    if (item != null)
                        items.Add(item);
                }
                return items;
            }
        }

        public ulong[] ItemGuids
        {
            get
            {
                uint slots = NumSlots;
                var guids = new ulong[slots];
                for (uint i = 0; i < slots; i++)
                    guids[i] = GetSlotGuid(i);
                return guids;
            }
        }

        /// <summary>
        /// Gets GUIDs of physical (non-zero) items.
        /// Ported from HB 3.3.5a WoWContainer.PhysicalItemGuids
        /// </summary>
        public ulong[] PhysicalItemGuids
        {
            get
            {
                try
                {
                    var guids = new List<ulong>();
                    uint slots = NumSlots;
                    for (uint i = 0; i < slots; i++)
                    {
                        ulong guid = GetSlotGuid(i);
                        if (guid != 0UL)
                            guids.Add(guid);
                    }
                    return guids.ToArray();
                }
                catch (Exception ex)
                {
                    Styx.Helpers.Logging.WriteException(ex);
                    throw;
                }
            }
        }
        
        #endregion
        
        #region Search Methods
        public int FindFirstFreeSlot()
        {
            uint slots = NumSlots;
            for (uint i = 0; i < slots; i++)
            {
                if (GetSlotGuid(i) == 0)
                    return (int)i;
            }
            return -1;
        }
        public WoWItem? FindItemByEntry(uint entryId)
        {
            uint slots = NumSlots;
            for (uint i = 0; i < slots; i++)
            {
                var item = GetSlotItem(i);
                if (item != null && item.Entry == entryId)
                    return item;
            }
            return null;
        }
        public uint CountItemsByEntry(uint entryId)
        {
            uint count = 0;
            uint slots = NumSlots;
            for (uint i = 0; i < slots; i++)
            {
                var item = GetSlotItem(i);
                if (item != null && item.Entry == entryId)
                    count += item.StackCount;
            }
            return count;
        }
        
        #endregion
        
        #region ToString
        public override string ToString()
        {
            return $"[Container: {Name} (Slots: {NumSlots}, Free: {FreeSlotsCount})]";
        }
        
        #endregion
    }
}
