#nullable disable
using System;
using System.Runtime.InteropServices;
using GreenMagic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Inventory.Frames.Merchant
{
    /// <summary>
    /// Represents an item in the merchant window.
    /// </summary>
    public class MerchantItem
    {
        private MerchantItemData _itemData;
        private readonly uint _baseAddress;
        private ItemInfo _itemInfo;

        public MerchantItem(uint ptr, int index)
        {
            _baseAddress = ptr;
            if (IsValid)
            {
                _itemData = ObjectManager.Wow.ReadStruct<MerchantItemData>(_baseAddress);
                Index = index + 1;
            }
        }

        private bool IsValid
        {
            get { return _baseAddress != 0U; }
        }

        public int Index { get; set; }

        public uint ItemId
        {
            get { return _itemData.ItemId; }
        }

        public uint TextureId
        {
            get { return _itemData.TextureId; }
        }

        public int NumAvailable
        {
            get { return _itemData.NumAvailable; }
        }

        public ulong BuyPrice
        {
            get { return (ulong)_itemData.BuyPrice; }
        }

        public uint ExtendedCostId
        {
            get { return _itemData.ExtendedCostId; }
        }

        /// <summary>
        /// The quantity of items in one purchase.
        /// For WoW 3.3.5, this is typically 1 unless the item stacks.
        /// </summary>
        public int Quantity
        {
            get { return _itemData.Quantity > 0 ? _itemData.Quantity : 1; }
        }

        public ItemInfo ItemInfo
        {
            get
            {
                if (_itemInfo == null)
                    _itemInfo = ItemInfo.FromId(ItemId);
                return _itemInfo;
            }
        }

        public string Name
        {
            get
            {
                if (ItemInfo == null)
                    return "(null)";
                return ItemInfo.Name;
            }
        }

        public override string ToString()
        {
            return string.Format(
                "Index:{0} ItemId:{1} TextureId:{2} NumAvailable:{3} BuyPrice:{4} Quantity:{5} ExtendedCostId:{6}",
                Index, ItemId, TextureId, NumAvailable, BuyPrice, Quantity, ExtendedCostId);
        }

        /// <summary>
        /// Internal structure for merchant item data.
        /// Size: 40 bytes (10 ints) to match HB 4.3.4 Struct48
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MerchantItemData
        {
            private uint _reserved0;
            private uint _reserved1;
            public uint ItemId;
            public uint TextureId;
            public int NumAvailable;
            public int BuyPrice;
            private uint _unknown;
            public int Quantity;
            public uint ExtendedCostId;
            private uint _reserved2;

            public override string ToString()
            {
                return string.Format(
                    "ItemId:{0} TextureId:{1} NumAvailable:{2} BuyPrice:{3} Quantity:{4} ExtendedCostId:{5}",
                    ItemId, TextureId, NumAvailable, BuyPrice, Quantity, ExtendedCostId);
            }
        }
    }
}
