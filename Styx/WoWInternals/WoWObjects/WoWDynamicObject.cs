using System;
using Styx.Logic.Pathing;

namespace Styx.WoWInternals.WoWObjects
{
    public class WoWDynamicObject : WoWObject
    {
        #region Constants - Offsets 3.3.5a
        
        // Position offsets for DynamicObject
        private const uint DYNOBJ_POSITION_X_OFFSET = 0xE8;    // Same as GameObject
        private const uint DYNOBJ_POSITION_Y_OFFSET = 0xEC;
        private const uint DYNOBJ_POSITION_Z_OFFSET = 0xF0;
        
        #endregion
        
        #region Descriptor Offsets - DYNAMICOBJECT_FIELD
        
        // DynamicObjectFields pour 3.3.5a (offset depuis descriptors)
        private const int DYNAMICOBJECT_CASTER = 0x0;          // 8 bytes (ulong) - Caster GUID
        private const int DYNAMICOBJECT_BYTES = 0x8;           // 4 bytes (type + unk)
        private const int DYNAMICOBJECT_SPELLID = 0xC;         // 4 bytes - Spell ID
        private const int DYNAMICOBJECT_RADIUS = 0x10;         // 4 bytes (float) - Radius
        private const int DYNAMICOBJECT_CASTTIME = 0x14;       // 4 bytes - Cast time
        
        #endregion
        
        #region Constructor
        public WoWDynamicObject(uint baseAddress) : base(baseAddress)
        {
        }
        
        #endregion
        
        #region Position Override
        public override float X
        {
            get
            {
                if (Memory == null) return 0f;
                return Memory.Read<float>(BaseAddress + DYNOBJ_POSITION_X_OFFSET);
            }
        }
        public override float Y
        {
            get
            {
                if (Memory == null) return 0f;
                return Memory.Read<float>(BaseAddress + DYNOBJ_POSITION_Y_OFFSET);
            }
        }
        public override float Z
        {
            get
            {
                if (Memory == null) return 0f;
                return Memory.Read<float>(BaseAddress + DYNOBJ_POSITION_Z_OFFSET);
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
        
        #region Caster Properties
        public ulong CasterGuid => GetDescriptorField<ulong>(DYNAMICOBJECT_CASTER);
        public WoWUnit? Caster
        {
            get
            {
                ulong guid = CasterGuid;
                if (guid == 0) return null;
                return ObjectManager.GetObjectByGuid<WoWUnit>(guid);
            }
        }
        public bool IsMine
        {
            get
            {
                var me = ObjectManager.Me;
                if (me == null) return false;
                return CasterGuid == me.Guid;
            }
        }
        
        #endregion
        
        #region Spell Properties
        public uint SpellId => GetDescriptorField<uint>(DYNAMICOBJECT_SPELLID);
        
        /// <summary>
        /// Gets the spell that created this dynamic object (HB 4.3.4 compatibility).
        /// </summary>
        public Logic.Combat.WoWSpell? Spell
        {
            get
            {
                var spellId = SpellId;
                if (spellId == 0)
                    return null;
                return Logic.Combat.WoWSpell.FromId((int)spellId);
            }
        }
        
        public float Radius => GetDescriptorField<float>(DYNAMICOBJECT_RADIUS);
        public uint CastTime => GetDescriptorField<uint>(DYNAMICOBJECT_CASTTIME);
        private uint Bytes => GetDescriptorField<uint>(DYNAMICOBJECT_BYTES);
        public DynamicObjectType DynObjType
        {
            get
            {
                return (DynamicObjectType)(Bytes & 0xFF);
            }
        }
        
        #endregion
        
        #region Helper Properties
        public bool IsHostile
        {
            get
            {
                var caster = Caster;
                if (caster == null) return false;
                
                // Si le caster est hostile au joueur, le dynobj est hostile
                // Check if caster is friendly to player
                if (caster.IsPlayer)
                    return true; // Player's own objects are friendly
                    
                return caster.IsFriendly;
            }
        }
        public bool AmIInRange
        {
            get
            {
                var me = ObjectManager.Me;
                if (me == null) return false;
                
                return Distance <= Radius;
            }
        }
        public bool IsPointInRange(WoWPoint point)
        {
            double dist = Location.Distance(point);
            return dist <= Radius;
        }
        
        #endregion
        
        #region ToString
        public override string ToString()
        {
            return $"[DynObj: SpellId={SpellId}, Radius={Radius:F1}, Type={DynObjType}, Distance={Distance:F1}]";
        }
        
        #endregion
    }
    
    #region Enums
    public enum DynamicObjectType : byte
    {
        Portal = 0,            // Portail de téléportation
        AreaSpell = 1,         // Sort de zone (Blizzard, Consecration, etc.)
        FarSightFocus = 2,     // Focus de Far Sight
        RaidMarker = 3         // Marqueur de raid
    }
    
    #endregion
}
