using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Styx.Combat.CombatRoutine;
using Styx.Offsets;

namespace Styx.WoWInternals.WoWObjects
{
    public class WoWPlayer : WoWUnit
    {
        #region Constants - Player Descriptor Fields
        
        // Player descriptor fields (offset = field * 4 from descriptor base)
        private const uint DescRace = 0x34;              // Race dans le descriptor
        private const uint DescClass = 0x34;             // Classe (partagé avec race, bits différents)
        private const uint DescGender = 0x34;            // Genre (partagé, bits différents)
        private const uint DescCoinage = 0x105;          // Player coinage (copper)
        private const uint DescHonor = 0x109;            // Player honor
        private const uint DescXP = 0x107;               // Player experience
        private const uint DescNextLevelXP = 0x108;      // Next level XP
        private const uint DescPlayerFlags = 0x106;      // Player flags (AFK, DND, GM, etc)
        
        // Race, Class, Gender are inherited from WoWUnit (offset 0x17 - UNIT_FIELD_BYTES_0)
        
        #endregion
        
        #region Constructor
        public WoWPlayer(uint baseAddress) : base(baseAddress)
        {
        }
        
        #endregion
        
        #region Gender Helper Properties
        // Race, Class, Gender are inherited from WoWUnit (offset 0x17 - UNIT_FIELD_BYTES_0)
        // No redefinition needed for 3.3.5a - using correct parent implementation
        
        public bool IsMale => Gender == WoWGender.Male;
        public bool IsFemale => Gender == WoWGender.Female;
        
        #endregion
        
        #region Faction Properties
        public bool IsAlliance
        {
            get
            {
                return Race switch
                {
                    WoWRace.Human => true,
                    WoWRace.Dwarf => true,
                    WoWRace.NightElf => true,
                    WoWRace.Gnome => true,
                    WoWRace.Draenei => true,
                    _ => false
                };
            }
        }
        public bool IsHorde
        {
            get
            {
                return Race switch
                {
                    WoWRace.Orc => true,
                    WoWRace.Undead => true,
                    WoWRace.Tauren => true,
                    WoWRace.Troll => true,
                    WoWRace.BloodElf => true,
                    _ => false
                };
            }
        }
        
        #endregion
        
        #region Class Helpers
        public bool IsTank
        {
            get
            {
                return Class == WoWClass.Warrior || 
                       Class == WoWClass.Paladin || 
                       Class == WoWClass.DeathKnight ||
                       Class == WoWClass.Druid;
                // Note: Devrait vérifier la spec/stance, simplifié ici
            }
        }
        public bool IsHealer
        {
            get
            {
                return Class == WoWClass.Priest ||
                       Class == WoWClass.Paladin ||
                       Class == WoWClass.Shaman ||
                       Class == WoWClass.Druid;
                // Note: Devrait vérifier la spec, simplifié ici
            }
        }
        public bool IsCaster
        {
            get
            {
                return Class == WoWClass.Mage ||
                       Class == WoWClass.Warlock ||
                       Class == WoWClass.Priest ||
                       Class == WoWClass.Shaman ||
                       Class == WoWClass.Druid;
            }
        }
        public bool IsMelee
        {
            get
            {
                return Class == WoWClass.Warrior ||
                       Class == WoWClass.Rogue ||
                       Class == WoWClass.DeathKnight ||
                       Class == WoWClass.Paladin;
            }
        }
        
        #endregion
        
        #region Override Properties
        public override WoWObjectType Type => WoWObjectType.Player;
        public override float InteractRange => 3f;
        
        #endregion
        
        #region Group Properties
        
        /// <summary>
        /// Whether this player is in the local player's party (not including self).
        /// </summary>
        public bool IsInMyParty
        {
            get
            {
                LocalPlayer? me = ObjectManager.Me;
                if (me == null || IsMe) return false;
                
                ulong myGuid = Guid;
                for (int i = 0; i < 4; i++)
                {
                    if (me.GetPartyMemberGUID(i) == myGuid)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// HB compatibility alias.
        /// </summary>
        public bool InMyParty => IsInMyParty;
        
        /// <summary>
        /// Whether this player is in the local player's raid (not including self).
        /// </summary>
        public bool IsInMyRaid
        {
            get
            {
                LocalPlayer? me = ObjectManager.Me;
                if (me == null || IsMe) return false;
                
                ulong myGuid = Guid;
                for (int i = 0; i < 40; i++)
                {
                    if (me.GetRaidMemberGUID(i) == myGuid)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// HB compatibility alias.
        /// </summary>
        public bool InMyRaid => IsInMyRaid;

        /// <summary>
        /// Whether this player is in the local player's party or raid.
        /// </summary>
        public bool IsInMyPartyOrRaid => IsInMyParty || IsInMyRaid;

        /// <summary>
        /// HB compatibility alias. 
        /// </summary>
        public bool InMyPartyOrRaid => IsInMyPartyOrRaid;
        
        #endregion
        
        #region Player State Properties
        
        /// <summary>
        /// Whether this player is currently a ghost (dead and waiting to resurrect).
        /// In WoW 3.3.5a, this is determined by PlayerFlags bit 0x10 (16)
        /// </summary>
        public virtual bool IsGhost
        {
            get
            {
                uint flags = ReadDescriptor<uint>(DescPlayerFlags);
                return (flags & 0x10) != 0;
            }
        }
        
        #endregion
        
        #region Money & Experience Properties
        
        public int Coinage => ReadDescriptor<int>(DescCoinage);
        
        public int Gold => Coinage / 10000;
        public int Silver => (Coinage % 10000) / 100;
        public int Copper => Coinage % 100;
        
        public uint Honor => ReadDescriptor<uint>(DescHonor);
        
        public uint XP => ReadDescriptor<uint>(DescXP);
        public uint NextLevelXP => ReadDescriptor<uint>(DescNextLevelXP);

        public float LevelFraction => NextLevelXP > 0 ? (float)(Level + (double)XP / NextLevelXP) : Level;
        
        // Aliases for HB compatibility
        public uint Experience => XP;
        public uint NextLevelExperience => NextLevelXP;
        
        public double XPPercent => NextLevelXP > 0 ? (double)XP / NextLevelXP * 100 : 0;
        
        private BitVector32 PlayerFlags => new((int)ReadDescriptor<uint>(DescPlayerFlags));
        
        #endregion
        
        #region Player Flags
        
        public bool IsGroupLeader => PlayerFlags[1];
        public bool IsAFKFlagged => PlayerFlags[2];
        public bool IsDNDFlagged => PlayerFlags[4];
        public bool IsGM => PlayerFlags[8];
        public bool IsResting => PlayerFlags[32];
        public bool IsFFAPvPFlagged => PlayerFlags[128];
        public bool ContestedPvPFlagged => PlayerFlags[256];
        public bool IsPvPFlagged => PlayerFlags[512];
        public bool IsHidingHelm => PlayerFlags[1024];
        public bool IsHidingCloak => PlayerFlags[2048];
        public bool IsOutOfBounds => PlayerFlags[4096];
        public bool IsInsideSanctuary => PlayerFlags[8192];
        public bool IsPvPTimerActive => PlayerFlags[16384];
        
        #endregion

        #region Duel Properties

        /// <summary>
        /// Gets the player's duel team (0 = not dueling, 1 or 2 = team number)
        /// </summary>
        public uint DuelTeam => GetPlayerDescriptor<uint>(WoWPlayerFields.PLAYER_DUEL_TEAM);

        /// <summary>
        /// Gets the GUID of the duel arbiter (the flag in the ground during a duel)
        /// </summary>
        public ulong DuelArbiterGuid => GetPlayerDescriptor<ulong>(WoWPlayerFields.PLAYER_DUEL_ARBITER);

        /// <summary>
        /// Returns true if this player is currently in a duel.
        /// </summary>
        public bool IsDueling => DuelTeam != 0;

        /// <summary>
        /// Gets a player descriptor field.
        /// Player descriptors start after UNIT_END (0x8E).
        /// </summary>
        private T GetPlayerDescriptor<T>(WoWPlayerFields field) where T : struct
        {
            // Player fields are relative to UNIT_END (0x8E)
            // Each field is 4 bytes
            const int UNIT_END = 0x8E;
            int offset = (UNIT_END + (int)field) * 4;
            return GetDescriptorField<T>(offset);
        }

        #endregion
        
        #region Equipment Properties
        
        public uint MainhandEntryId => ReadDescriptor<uint>(0x11D);  // Item entry ID in mainhand slot
        public uint OffhandEntryId => ReadDescriptor<uint>(0x11F);   // Item entry ID in offhand slot
        
        public WoWItem? Mainhand
        {
            get
            {
                return ObjectManager.GetObjectsOfType<WoWItem>().FirstOrDefault();
            }
        }
        
        public WoWItem? Offhand
        {
            get
            {
                return ObjectManager.GetObjectsOfType<WoWItem>().Skip(1).FirstOrDefault();
            }
        }
        
        #endregion
        
        #region Mounted Override
        
        public override bool Mounted
        {
            get
            {
                if (Class == WoWClass.Druid)
                {
                    var shapeshift = Shapeshift;
                    if (shapeshift == ShapeshiftForm.FlightForm || shapeshift == ShapeshiftForm.EpicFlightForm)
                        return true;
                }
                return base.Mounted;
            }
        }
        
        #endregion

        #region Minions Property
        
        /// <summary>
        /// Gets all minions (pets, totems, etc.) controlled by this player.
        /// Excludes non-combat pets.
        /// </summary>
        public List<WoWUnit> Minions
        {
            get
            {
                return ObjectManager.GetObjectsOfType<WoWUnit>()
                    .Where(u => !u.IsNonCombatPet && (WoWObject)u.ControllingPlayer == (WoWObject)this)
                    .ToList();
            }
        }

        #endregion
    }
}
