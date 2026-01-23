using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Numerics;
using System.Runtime.InteropServices;
using GreenMagic;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Patchables;
using Styx.WoWInternals.WoWCache;

namespace Styx.WoWInternals.WoWObjects
{
    public class WoWGameObject : WoWObject, IComparable<WoWGameObject>, IComparable<WoWUnit>, IComparer<WoWUnit>, IComparer<WoWGameObject>
    {
        #region Constants - Offsets 3.3.5a
        
        // Position offsets for GameObjects (different from Unit)
        private const uint GO_POSITION_X_OFFSET = 0xE8;     // 232
        private const uint GO_POSITION_Y_OFFSET = 0xEC;     // 236
        private const uint GO_POSITION_Z_OFFSET = 0xF0;     // 240
        private const uint GO_ROTATION_OFFSET = 0xF8;       // 248
        
        #endregion
        
        #region Descriptor Offsets - GAMEOBJECT_FIELD
        
        // GameObjectFields pour 3.3.5a (0x0 offset + index * 4)
        private const int GAMEOBJECT_CREATED_BY = 0x0;      // 8 bytes (ulong)
        private const int GAMEOBJECT_DISPLAYID = 0x8;       // 4 bytes
        private const int GAMEOBJECT_FLAGS = 0xC;           // 4 bytes
        private const int GAMEOBJECT_PARENTROTATION = 0x10; // 16 bytes (4 floats)
        private const int GAMEOBJECT_DYNAMIC = 0x20;        // 2 bytes (state + type)
        private const int GAMEOBJECT_FACTION = 0x24;        // 4 bytes
        private const int GAMEOBJECT_LEVEL = 0x28;          // 4 bytes
        private const int GAMEOBJECT_BYTES_1 = 0x2C;        // 4 bytes (state, type, artkit, anim)
        
        #endregion
        
        #region Constructor
        public WoWGameObject(uint baseAddress) : base(baseAddress)
        {
        }
        
        #endregion
        
        #region Position Override
        public override float X
        {
            get
            {
                if (Memory == null) return 0f;
                return Memory.Read<float>(BaseAddress + GO_POSITION_X_OFFSET);
            }
        }
        public override float Y
        {
            get
            {
                if (Memory == null) return 0f;
                return Memory.Read<float>(BaseAddress + GO_POSITION_Y_OFFSET);
            }
        }
        public override float Z
        {
            get
            {
                if (Memory == null) return 0f;
                return Memory.Read<float>(BaseAddress + GO_POSITION_Z_OFFSET);
            }
        }
        public override WoWPoint Location
        {
            get
            {
                return new WoWPoint(X, Y, Z);
            }
        }
        public override float Rotation
        {
            get
            {
                if (Memory == null) return 0f;
                return Memory.Read<float>(BaseAddress + GO_ROTATION_OFFSET);
            }
        }
        
        #endregion
        
        #region Descriptor Properties
        public ulong CreatedByGuid => GetDescriptorField<ulong>(GAMEOBJECT_CREATED_BY);
        public WoWUnit? CreatedBy => ObjectManager.GetObjectByGuid<WoWUnit>(CreatedByGuid);
        public uint DisplayId => GetDescriptorField<uint>(GAMEOBJECT_DISPLAYID);
        public GameObjectFlags Flags => (GameObjectFlags)GetDescriptorField<uint>(GAMEOBJECT_FLAGS);
        public ushort DynamicFlags => GetDescriptorField<ushort>(GAMEOBJECT_DYNAMIC);
        public uint Faction => GetDescriptorField<uint>(GAMEOBJECT_FACTION);
        public int Level => GetDescriptorField<int>(GAMEOBJECT_LEVEL);
        public uint Bytes1 => GetDescriptorField<uint>(GAMEOBJECT_BYTES_1);
        public ushort FlagsDynamic => DynamicFlags;
        public uint FactionTemplateId => Faction;
        
        #endregion
        
        #region State Properties (from Bytes1)
        public WoWGameObjectState State
        {
            get
            {
                return (WoWGameObjectState)(Bytes1 & 0xFF);
            }
        }
        public WoWGameObjectType SubType
        {
            get
            {
                return (WoWGameObjectType)((Bytes1 >> 8) & 0xFF);
            }
        }

        public Vector4 ParentRotation
        {
            get
            {
                float x = GetDescriptorField<float>(GAMEOBJECT_PARENTROTATION + 0);
                float y = GetDescriptorField<float>(GAMEOBJECT_PARENTROTATION + 1);
                float z = GetDescriptorField<float>(GAMEOBJECT_PARENTROTATION + 2);
                float w = GetDescriptorField<float>(GAMEOBJECT_PARENTROTATION + 3);
                return new Vector4(x, y, z, w);
            }
        }

        /// <summary>
        /// Sub-object cache entry (InteractDistance, SpellFocus, etc.). Returns a lightweight placeholder when cache is missing.
        /// </summary>
        public GameObjectSubData SubObj => new GameObjectSubData
        {
            InteractDistance = 4.5f,
            SpellFocusId = 0,
            ModelId = 0
        };
        public byte ArtKit
        {
            get
            {
                return (byte)((Bytes1 >> 16) & 0xFF);
            }
        }
        public byte AnimationProgress
        {
            get
            {
                return (byte)((Bytes1 >> 24) & 0xFF);
            }
        }
        
        #endregion
        
        #region Helper Properties
        public bool Locked => (Flags & GameObjectFlags.Locked) != 0;
        public bool InUse => (Flags & GameObjectFlags.InUse) != 0;
        public bool Triggered => (Flags & GameObjectFlags.Triggered) != 0;
        public bool Transport => (Flags & GameObjectFlags.Transport) != 0;
        public bool IsTransport => Transport;
        public bool CanLoot
        {
            get
            {
                // Un coffre peut être looté s'il n'est pas verrouillé et en état Ready
                if (SubType == WoWGameObjectType.Chest || SubType == WoWGameObjectType.Fishinghole)
                {
                    return !Locked && State == WoWGameObjectState.Ready;
                }
                return false;
            }
        }
        public bool CanMine
        {
            get
            {
                return SubType == WoWGameObjectType.MiningNode && 
                       State == WoWGameObjectState.Ready;
            }
        }
        public bool IsMineral => SubType == WoWGameObjectType.MiningNode;
        public bool CanHarvest
        {
            get
            {
                return SubType == WoWGameObjectType.Herb && 
                       State == WoWGameObjectState.Ready;
            }
        }
        public bool CanFish
        {
            get
            {
                return SubType == WoWGameObjectType.FishingBobber;
            }
        }
        
        #endregion
        
        #region Type Helpers
        public bool IsChest => SubType == WoWGameObjectType.Chest;
        public bool IsMiningNode => SubType == WoWGameObjectType.MiningNode;
        public bool IsHerb => SubType == WoWGameObjectType.Herb;
        public bool IsDoor => SubType == WoWGameObjectType.Door;
        public bool IsButton => SubType == WoWGameObjectType.Button;
        public bool IsQuestGiver => SubType == WoWGameObjectType.Questgiver;
        public bool IsMailbox => SubType == WoWGameObjectType.Mailbox;
        public string Model => Name;
        public WoWSpellFocus SpellFocus
        {
            get
            {
                if (GetDataSlot(GameObjectDataSlot.SpellFocusId, out int id))
                    return (WoWSpellFocus)id;
                return WoWSpellFocus.None;
            }
        }
        public override float InteractRange => Math.Max(0f, SubObj.InteractDistance - 0.25f);
        
        public WoWUnitReaction GetReactionTowards(WoWUnit unit)
        {
            var creator = CreatedBy;
            if (creator != null)
                return creator.GetReactionTowards(unit);
            
            // Check faction template
            return WoWUnitReaction.Neutral;
        }

        /// <summary>
        /// Gets the required skill level to interact with this object (herbs, minerals, chests).
        /// </summary>
        public uint? RequiredSkill
        {
            get
            {
                if (IsHerb || IsMineral || IsChest)
                {
                    if (!GetDataSlot(GameObjectDataSlot.LockId, out int lockId))
                        return null;
                    
                    if (lockId != 0)
                    {
                        // NOTE: Full implementation requires reading Lock.dbc from MPQs
                        // For basic bot functionality, we can rely on server-side checks
                        // when attempting to interact with locked objects.
                        // Return null for now - interaction will fail if skill too low
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Gets the cached game object info.
        /// </summary>
        public bool GetCachedInfo(out WoWCache.WoWCache.GameObjectCacheEntry info)
        {
            // NOTE: Cache entry reading requires exact offset verification for 3.3.5a
            // Cache structure: BaseAddress -> Guid -> CacheEntry (offset varies)
            // For now, return false - most GameObject operations work without cache
            // Full implementation would be:
            // uint cachePtr = ObjectManager.Wow.Read<uint>(BaseAddress + 0x1A4); // 420 decimal
            // if (cachePtr != 0) { info = ObjectManager.Wow.ReadStruct<...>(cachePtr); return true; }
            info = default;
            return false;
        }
        
        #endregion
        
        #region IComparable Implementation
        public int CompareTo(WoWGameObject? other)
        {
            if (other == null) return 1;
            return Distance.CompareTo(other.Distance);
        }
        public int CompareTo(WoWUnit? other)
        {
            if (other == null) return 1;
            return Distance.CompareTo(other.Distance);
        }

        public int Compare(WoWUnit? x, WoWUnit? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return x.Distance.CompareTo(y.Distance);
        }

        public int Compare(WoWGameObject? x, WoWGameObject? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return x.Distance.CompareTo(y.Distance);
        }

        public bool CanUse() => !Locked && !InUse;
        public bool CanUseNow() => CanUse();
        public bool CanUseNow(out GameError reason)
        {
            reason = 0; // No error
            if (Locked)
            {
                reason = GameError.ChestInUse;
                return false;
            }
            if (InUse)
            {
                reason = GameError.ChestInUse;
                return false;
            }
            return true;
        }

        public bool GetDataSlot(GameObjectDataSlot slot, out int value)
        {
            return GetDataSlot((uint)slot, out value);
        }

        public bool GetDataSlot(GameObjectDataSlot slot, out bool value)
        {
            int temp;
            bool result = GetDataSlot((uint)slot, out temp);
            value = temp != 0;
            return result;
        }

        public bool GetDataSlot(uint dataSlot, out int value)
        {
            // TODO: Read from cache entry data slots
            value = 0;
            return false;
        }

        public Matrix4x4 WorldMatrix => GetWorldMatrix();

        public Matrix4x4 GetWorldMatrix()
        {
            return BuildWorldMatrix(Location, Rotation);
        }

        public Matrix4x4 GetWorldMatrix(bool includeScale)
        {
            return GetWorldMatrix();
        }

        #endregion
        
        #region ToString
        public override string ToString()
        {
            return $"[GameObject: {Name} (Entry: {Entry}, Type: {SubType}, State: {State}, Distance: {Distance:F1})]";
        }

        private static Matrix4x4 BuildWorldMatrix(WoWPoint location, float rotation)
        {
            var rotationZ = Matrix4x4.CreateRotationZ(rotation);
            rotationZ.Translation = new Vector3(location.X, location.Y, location.Z);
            return rotationZ;
        }

        #endregion
    }

    /// <summary>
    /// Minimal cached gameobject sub-data (distance, focus id, model id).
    /// </summary>
    public sealed class GameObjectSubData
    {
        public float InteractDistance { get; set; }
        public int SpellFocusId { get; set; }
        public int ModelId { get; set; }
    }
    
    #region Enums
    [Flags]
    public enum GameObjectFlags : uint
    {
        None = 0x0,
        InUse = 0x1,           // Est utilisé par quelqu'un
        Locked = 0x2,          // Verrouillé (besoin de clé ou lockpicking)
        ConditionInteract = 0x4,
        Transport = 0x8,       // Est un transport (bateau, zeppelin)
        NotSelectable = 0x10,  // Non sélectionnable
        NoDespawn = 0x20,      // Ne disparaît pas
        Triggered = 0x40,      // A été déclenché
        Damaged = 0x200,       // Endommagé
        Destroyed = 0x400      // Détruit
    }
    public enum WoWGameObjectState : byte
    {
        Ready = 0,
        Active = 1,
        ActiveAlternative = 2,
        Destroyed = 3
    }
    public enum WoWGameObjectType : byte
    {
        Door = 0,
        Button = 1,
        Questgiver = 2,
        Chest = 3,
        Binder = 4,
        Generic = 5,
        Trap = 6,
        Chair = 7,
        SpellFocus = 8,
        Text = 9,
        Goober = 10,
        Transport = 11,
        AreaDamage = 12,
        Camera = 13,
        MapObject = 14,
        MOTransport = 15,
        DuelArbiter = 16,
        FishingBobber = 17,
        Ritual = 18,
        Mailbox = 19,
        AuctionHouse = 20,
        SpellCaster = 22,
        MeetingStone = 23,
        FlagStand = 24,
        Fishinghole = 25,
        FlagDrop = 26,
        MiniGame = 27,
        LotteryKiosk = 28,
        CapturePoint = 29,
        AuraGenerator = 30,
        DungeonDifficulty = 31,
        BarberChair = 32,
        DestructibleBuilding = 33,
        GuildBank = 34,
        Trapdoor = 35,
        
        // Types spéciaux pour gathering
        Herb = 50,         // Ajouté pour clarté
        MiningNode = 51    // Ajouté pour clarté
    }

    /// <summary>
    /// Lock entry from Lock.dbc - used for lockpicking/mining/herbalism requirements.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 56)]
    public struct LockEntry
    {
        public int Id;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public int[] Type;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] LockProperties;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] RequiredSkill;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] Action;
    }
    
    #endregion
}
