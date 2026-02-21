using System;
using System.Collections.Generic;
using GreenMagic;
using Styx.WoWInternals.WoWCache;

namespace Styx.WoWInternals.WoWObjects
{
    public class ItemInfo
    {
        #region Fields

        private readonly WoWCache.WoWCache.InfoBlock _infoBlock;
        private readonly WoWCache.WoWCache.ItemCacheEntry _cacheEntry;
        private string? _name;
        private string? _description;

        #endregion

        #region Constructor

        private ItemInfo(WoWCache.WoWCache.InfoBlock block)
        {
            _infoBlock = block;
            Id = block.Id;
            _cacheEntry = block.Item;
        }

        #endregion

        #region Static Factory

        public static ItemInfo? FromId(uint itemId)
        {
            var cache = StyxWoW.Cache[CacheDb.Item];
            var infoBlock = cache?.GetInfoBlockById(itemId);
            if (infoBlock == null)
                return null;
            
            return new ItemInfo(infoBlock);
        }

        #endregion

        #region Core Properties

        public uint Id { get; private set; }

        public WoWCache.WoWCache.ItemCacheEntry InternalInfo => _cacheEntry;

        public uint BaseAddress => _infoBlock?.Address ?? 0;

        #endregion

        #region Basic Item Properties

        public int TypeFlags => _cacheEntry.TypeFlags;

        public int SubClassId => _cacheEntry.SubClassId;

        public float RangeModifier => _cacheEntry.RangeModifier;

        public int AllowedClasses => _cacheEntry.AllowedClasses;

        public int AllowedRaces => _cacheEntry.AllowedRaces;

        public int BookStationaryId => _cacheEntry.BookStationaryId;
        public int MaterialId => _cacheEntry.MaterialId;
        public int SheathId => _cacheEntry.SheathId;
        public int RandomPropertyId => _cacheEntry.RandomPropertyId;
        public int RandomPropertyId2 => _cacheEntry.RandomPropertyId2;
        public int BlockValue => _cacheEntry.BlockValue;
        public int ItemSetId => _cacheEntry.ItemSetId;
        public int DurabilityValue => _cacheEntry.DurabilityValue;
        public int ItemAreaId => _cacheEntry.ItemAreaId;
        public int ItemMapId => _cacheEntry.ItemMapId;
        public int BagFamily => _cacheEntry.BagFamily;
        public int TotemCategory => _cacheEntry.TotemCategory;
        public float ArmorDamageModifier => _cacheEntry.ArmorDamageModifier;

        #endregion

        #region Stat Arrays
        public int[] StatId => _cacheEntry.StatId;
        public int[] StatValue => _cacheEntry.StatValue;
        public float[] DamageMin => _cacheEntry.DamageMin;
        public float[] DamageMax => _cacheEntry.DamageMax;
        public int[] DamageType => _cacheEntry.DamageType;

        #endregion

        #region Spell Arrays
        public int[] SpellId => _cacheEntry.SpellId;
        public int[] SpellTriggerId => _cacheEntry.SpellTriggerId;
        public int[] SpellCharges => _cacheEntry.SpellCharges;
        public int[] SpellCooldown => _cacheEntry.SpellCooldown;
        public int[] SpellCategory => _cacheEntry.SpellCategory;
        public int[] SpellCategoryCooldown => _cacheEntry.SpellCategoryCooldown;

        #endregion

        #region Socket Properties
        public WoWCache.WoWCache.SocketColorFlags[] SocketColor => _cacheEntry.SocketColor;

        #endregion

        #region Name & Description
        public string Name
        {
            get
            {
                if (_name == null && _cacheEntry.NamePtr != 0)
                {
                    var wow = ObjectManager.Wow;
                    _name = wow?.Read<string>(_cacheEntry.NamePtr) ?? string.Empty;
                }
                return _name ?? string.Empty;
            }
        }
        public string Description
        {
            get
            {
                if (_description == null && _cacheEntry.DescriptionPtr != 0)
                {
                    var wow = ObjectManager.Wow;
                    _description = wow?.Read<string>(_cacheEntry.DescriptionPtr) ?? string.Empty;
                }
                return _description ?? string.Empty;
            }
        }

        #endregion

        #region Item Class Properties
        public WoWItemClass ItemClass => (WoWItemClass)_cacheEntry.ClassId;
        public WoWItemWeaponClass WeaponClass => (WoWItemWeaponClass)_cacheEntry.SubClassId;
        public WoWItemGlyphClass GlyphClass => (WoWItemGlyphClass)_cacheEntry.SubClassId;
        public WoWItemKeyClass KeyClass => (WoWItemKeyClass)_cacheEntry.SubClassId;
        public WoWItemMiscClass MiscClass => (WoWItemMiscClass)_cacheEntry.SubClassId;
        public WoWItemRecipeClass RecipeClass => (WoWItemRecipeClass)_cacheEntry.SubClassId;
        public WoWItemTradeGoodsClass TradeGoodsClass => (WoWItemTradeGoodsClass)_cacheEntry.SubClassId;
        public WoWItemProjectileClass ProjectileClass => (WoWItemProjectileClass)_cacheEntry.SubClassId;
        public WoWItemArmorClass ArmorClass => (WoWItemArmorClass)_cacheEntry.SubClassId;
        public WoWItemGemClass GemClass => (WoWItemGemClass)_cacheEntry.SubClassId;
        public WoWItemContainerClass ContainerClass => (WoWItemContainerClass)_cacheEntry.SubClassId;

        #endregion

        #region Weapon Properties
        public float WeaponDelay => _cacheEntry.WeaponDelay / 1000f;
        public float MinDamage
        {
            get
            {
                if (!IsWeapon) return 0f;
                int idx = GetPhysicalDamageIndex();
                return idx != -1 ? _cacheEntry.DamageMin[idx] : 0f;
            }
        }
        public float MaxDamage
        {
            get
            {
                if (!IsWeapon) return 0f;
                int idx = GetPhysicalDamageIndex();
                return idx != -1 ? _cacheEntry.DamageMax[idx] : 0f;
            }
        }
        public float DPS
        {
            get
            {
                if (!IsWeapon) return 0f;
                int idx = GetPhysicalDamageIndex();
                if (idx == -1) return 0f;
                
                float avgDamage = (_cacheEntry.DamageMin[idx] + _cacheEntry.DamageMax[idx]) / 2f;
                float delay = _cacheEntry.WeaponDelay / 1000f;
                return delay > 0 ? avgDamage / delay : 0f;
            }
        }
        public int WeaponSpeed => _cacheEntry.WeaponDelay;
        public bool IsWeapon => _cacheEntry.ClassId == 2;

        private int GetPhysicalDamageIndex()
        {
            if (_cacheEntry.DamageType == null) return -1;
            if (_cacheEntry.DamageType[0] == 0) return 0;
            if (_cacheEntry.DamageType.Length > 1 && _cacheEntry.DamageType[1] == 0) return 1;
            return -1;
        }

        #endregion

        #region Display & Quality
        public int DisplayInfoId => _cacheEntry.DisplayInfoId;
        public WoWItemQuality Quality => (WoWItemQuality)_cacheEntry.Rarity;
        public int Faction => _cacheEntry.Faction;

        #endregion

        #region Prices
        public int SellPrice => _cacheEntry.SellPrice;
        public int BuyPrice => _cacheEntry.BuyPrice;

        #endregion

        #region Equipment
        // HonorBuddy compatibility: EquipSlot returns InventoryType
        public InventoryType EquipSlot => (InventoryType)_cacheEntry.EquipSlot;

        // Preserve original WoWInventorySlot access under a distinct name
        public WoWInventorySlot WoWEquipSlot => (WoWInventorySlot)_cacheEntry.EquipSlot;

        public WoWItemAmmoType AmmoType => (WoWItemAmmoType)_cacheEntry.AmmoType;
        public InventoryType InventoryType => (InventoryType)_cacheEntry.EquipSlot;

        #endregion

        #region Levels & Requirements
        public int Level => _cacheEntry.ItemLevel;
        public int RequiredLevel => _cacheEntry.RequiredLevel;
        public int RequiredSkillId => _cacheEntry.RequiredSkill;
        public int RequiredSkillLevel => _cacheEntry.RequiredSkillLevel;
        public int RequiredSpellId => _cacheEntry.RequireSpell;
        public int RequiredHonorRank => _cacheEntry.RequiredHonorRank;
        public int RequiredReputationFactionId => _cacheEntry.RequiredReputationFaction;
        public int RequiredReputationRank => _cacheEntry.RequiredReputationRank;

        #endregion

        #region Stacking & Uniqueness
        public int UniqueCount => _cacheEntry.UniqueCount;
        public int MaxStackSize => _cacheEntry.MaxStackSize;
        public int BagSlots => _cacheEntry.BagSlots;
        public int StatsCount => _cacheEntry.NumberOfStats;

        #endregion

        #region Resistances
        public int Armor => _cacheEntry.ResistPhysical;
        public int HolyResistance => _cacheEntry.ResistHoly;
        public int FireResistance => _cacheEntry.ResistFire;
        public int NatureResistance => _cacheEntry.ResistNature;
        public int FrostResistance => _cacheEntry.ResistFrost;
        public int ShadowResistance => _cacheEntry.ResistShadow;
        public int ArcaneResistance => _cacheEntry.ResistArcane;

        #endregion

        #region Binding & Book
        public WoWItemBondType Bond => (WoWItemBondType)_cacheEntry.BondId;
        public int LockPickSkillRequired => _cacheEntry.LockPickSkillRequired;
        public int BookTextId => _cacheEntry.BookTextId;
        public int BookPages => _cacheEntry.BookPages;
        public int BeginQuestId => _cacheEntry.BeginQuestId;

        #endregion

        #region Methods
        public Dictionary<WoWItemStatType, int> GetItemStats()
        {
            var stats = new Dictionary<WoWItemStatType, int>();
            
            if (_cacheEntry.StatId == null || _cacheEntry.StatValue == null)
                return stats;
            
            for (int i = 0; i < _cacheEntry.NumberOfStats && i < _cacheEntry.StatId.Length; i++)
            {
                var statType = (WoWItemStatType)_cacheEntry.StatId[i];
                if (!stats.ContainsKey(statType))
                    stats.Add(statType, _cacheEntry.StatValue[i]);
            }
            
            return stats;
        }

        #endregion

        #region ToString

        public override string ToString()
        {
            return $"[ItemInfo: {Name} (Id: {Id}, Quality: {Quality}, iLvl: {Level})]";
        }

        #endregion
    }
}
