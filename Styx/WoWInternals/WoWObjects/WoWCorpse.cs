using System;
using Styx.Logic.Pathing;

namespace Styx.WoWInternals.WoWObjects
{
    public class WoWCorpse : WoWObject
    {
        #region Constants - Offsets 3.3.5a
        
        // Position offsets for Corpse
        private const uint CORPSE_POSITION_X_OFFSET = 0x24;
        private const uint CORPSE_POSITION_Y_OFFSET = 0x28;
        private const uint CORPSE_POSITION_Z_OFFSET = 0x2C;
        
        #endregion
        
        #region Descriptor Offsets - CORPSE_FIELD
        
        // CorpseFields pour 3.3.5a (offset depuis descriptors)
        private const int CORPSE_FIELD_OWNER = 0x0;           // 8 bytes (ulong) - Owner GUID
        private const int CORPSE_FIELD_PARTY = 0x8;           // 8 bytes (ulong) - Party GUID
        private const int CORPSE_FIELD_DISPLAY_ID = 0x10;     // 4 bytes
        private const int CORPSE_FIELD_ITEM = 0x14;           // Array de 19 x 4 bytes (equipment display)
        private const int CORPSE_FIELD_BYTES_1 = 0x60;        // 4 bytes (race, gender, skin, face)
        private const int CORPSE_FIELD_BYTES_2 = 0x64;        // 4 bytes (hair style, hair color, facial, flags)
        private const int CORPSE_FIELD_GUILD = 0x68;          // 4 bytes - Guild ID
        private const int CORPSE_FIELD_FLAGS = 0x6C;          // 4 bytes
        private const int CORPSE_FIELD_DYNAMIC_FLAGS = 0x70;  // 4 bytes
        
        #endregion
        
        #region Constructor
        public WoWCorpse(uint baseAddress) : base(baseAddress)
        {
        }
        
        #endregion
        
        #region Position Override
        public override float X
        {
            get
            {
                if (Memory == null) return 0f;
                return Memory.Read<float>(BaseAddress + CORPSE_POSITION_X_OFFSET);
            }
        }
        public override float Y
        {
            get
            {
                if (Memory == null) return 0f;
                return Memory.Read<float>(BaseAddress + CORPSE_POSITION_Y_OFFSET);
            }
        }
        public override float Z
        {
            get
            {
                if (Memory == null) return 0f;
                return Memory.Read<float>(BaseAddress + CORPSE_POSITION_Z_OFFSET);
            }
        }
        public override WoWPoint Location
        {
            get
            {
                return new WoWPoint(X, Y, Z);
            }
        }
        
        #endregion
        
        #region Owner Properties
        public ulong OwnerGuid => GetDescriptorField<ulong>(CORPSE_FIELD_OWNER);
        public ulong PartyGuid => GetDescriptorField<ulong>(CORPSE_FIELD_PARTY);
        public uint GuildId => GetDescriptorField<uint>(CORPSE_FIELD_GUILD);
        
        #endregion
        
        #region Display Properties
        public uint DisplayId => GetDescriptorField<uint>(CORPSE_FIELD_DISPLAY_ID);
        private uint Bytes1 => GetDescriptorField<uint>(CORPSE_FIELD_BYTES_1);
        private uint Bytes2 => GetDescriptorField<uint>(CORPSE_FIELD_BYTES_2);
        public WoWRace Race
        {
            get
            {
                return (WoWRace)(Bytes1 & 0xFF);
            }
        }
        public WoWGender Gender
        {
            get
            {
                return (WoWGender)((Bytes1 >> 8) & 0xFF);
            }
        }
        public byte Skin
        {
            get
            {
                return (byte)((Bytes1 >> 16) & 0xFF);
            }
        }
        public byte Face
        {
            get
            {
                return (byte)((Bytes1 >> 24) & 0xFF);
            }
        }
        
        #endregion
        
        #region Flags
        public CorpseFlags Flags => (CorpseFlags)GetDescriptorField<uint>(CORPSE_FIELD_FLAGS);
        public uint DynamicFlags => GetDescriptorField<uint>(CORPSE_FIELD_DYNAMIC_FLAGS);
        
        #endregion
        
        #region Helper Properties
        public bool IsMine
        {
            get
            {
                var me = ObjectManager.Me;
                if (me == null) return false;
                return OwnerGuid == me.Guid;
            }
        }
        public bool IsInMyParty
        {
            get
            {
                // Check if corpse owner is in party
                if (PartyGuid == 0)
                    return false;
                    
                // If it's our corpse, we're always in our party
                if (IsMine)
                    return true;
                    
                // Check if owner is a party member
                WoWPlayer? owner = ObjectManager.GetObjectByGuid<WoWPlayer>(OwnerGuid);
                if (owner == null)
                    return false;
                    
                return owner.IsInMyPartyOrRaid;
            }
        }
        public bool IsLootable
        {
            get
            {
                // Les corps de joueurs ne sont généralement pas lootables (sauf PvP)
                return (Flags & CorpseFlags.Lootable) != 0;
            }
        }
        public bool IsBones
        {
            get
            {
                return (Flags & CorpseFlags.Bones) != 0;
            }
        }
        
        #endregion
        
        #region Equipment Display
        public uint GetEquipmentDisplay(int slot)
        {
            if (slot < 0 || slot >= 19)
                return 0;
                
            return GetDescriptorField<uint>(CORPSE_FIELD_ITEM + (slot * 4));
        }
        
        #endregion
        
        #region ToString
        public override string ToString()
        {
            return $"[Corpse: Owner={OwnerGuid}, Race={Race}, Gender={Gender}, Distance={Distance:F1}]";
        }
        
        #endregion
    }
    
    #region Enums
    [Flags]
    public enum CorpseFlags : uint
    {
        None = 0x0,
        Bones = 0x1,           // Le corps est maintenant un squelette
        Unk1 = 0x2,
        Unk2 = 0x4,
        HideHelm = 0x8,
        HideCloak = 0x10,
        Lootable = 0x20,       // Peut être looté
        Unk3 = 0x40
    }
    
    #endregion
}
