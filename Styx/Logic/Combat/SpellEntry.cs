#nullable disable
using System;
using System.Runtime.InteropServices;

namespace Styx.Logic.Combat
{
    public struct SpellEntry
    {
        private const int int_0 = 3;

        public uint Id;
        public uint Category;
        public uint Dispel;
        public uint Mechanic;
        public uint Attributes;
        public uint AttributesEx;
        public uint AttributesEx2;
        public uint AttributesEx3;
        public uint AttributesEx4;
        public uint AttributesEx5;
        public uint AttributesEx6;
        public uint AttributesEx7;
        public uint Stances;
        public uint unk_320_2;
        public uint StancesNot;
        public uint unk_320_3;
        public uint Targets;
        public uint TargetCreatureType;
        public uint RequiresSpellFocus;
        public uint FacingCasterFlags;
        public uint CasterAuraState;
        public uint TargetAuraState;
        public uint CasterAuraStateNot;
        public uint TargetAuraStateNot;
        public uint casterAuraSpell;
        public uint targetAuraSpell;
        public uint excludeCasterAuraSpell;
        public uint excludeTargetAuraSpell;
        public uint CastingTimeIndex;
        public uint RecoveryTime;
        public uint CategoryRecoveryTime;
        public uint InterruptFlags;
        public uint AuraInterruptFlags;
        public uint ChannelInterruptFlags;
        public uint procFlags;
        public uint procChance;
        public uint procCharges;
        public uint maxLevel;
        public uint baseLevel;
        public uint spellLevel;
        public uint DurationIndex;
        public uint powerType;
        public uint powerCost;
        public uint manaCostPerlevel;
        public uint manaPerSecond;
        public uint manaPerSecondPerLevel;
        public uint rangeIndex;
        public float speed;
        public uint modalNextSpell;
        public uint StackAmount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2, ArraySubType = UnmanagedType.U4)]
        public uint[] Totem;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8, ArraySubType = UnmanagedType.I4)]
        public int[] Reagent;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8, ArraySubType = UnmanagedType.U4)]
        public uint[] ReagentCount;

        public int EquippedItemClass;
        public int EquippedItemSubClassMask;
        public int EquippedItemInventoryTypeMask;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U4)]
        public uint[] Effect;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.I4)]
        public int[] EffectDieSides;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
        public float[] EffectRealPointsPerLevel;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.I4)]
        public int[] EffectBasePoints;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U4)]
        public uint[] EffectMechanic;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U4)]
        public uint[] EffectImplicitTargetA;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U4)]
        public uint[] EffectImplicitTargetB;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U4)]
        public uint[] EffectRadiusIndex;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U4)]
        public uint[] EffectApplyAuraName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U4)]
        public uint[] EffectAmplitude;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
        public float[] EffectMultipleValue;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U4)]
        public uint[] EffectChainTarget;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U4)]
        public uint[] EffectItemType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.I4)]
        public int[] EffectMiscValueA;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.I4)]
        public int[] EffectMiscValueB;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U4)]
        public uint[] EffectTriggerSpell;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
        public float[] EffectPointsPerComboPoint;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public SpellClassMask[] EffectSpellClassMask;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2, ArraySubType = UnmanagedType.U4)]
        public uint[] SpellVisual;

        public uint SpellIconID;
        public uint activeIconID;
        public uint spellPriority;
        public uint SpellName;
        public uint Rank;
        public uint Description;
        public uint ToolTip;
        public uint ManaCostPercentage;
        public uint StartRecoveryCategory;
        public uint StartRecoveryTime;
        public uint MaxTargetLevel;
        public uint SpellFamilyName;
        public SpellClassMask SpellFamilyFlags;
        public uint MaxAffectedTargets;
        public uint DmgClass;
        public uint PreventionType;
        public uint StanceBarOrder;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
        public float[] DmgMultiplier;

        public uint MinFactionId;
        public uint MinReputation;
        public uint RequiredAuraVision;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2, ArraySubType = UnmanagedType.U4)]
        public uint[] TotemCategory;

        public int AreaGroupId;
        public int SchoolMask;
        public uint runeCostID;
        public uint spellMissileID;
        public uint PowerDisplayId;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
        public float[] unk_320_4;

        public uint spellDescriptionVariableID;
        public uint SpellDifficultyId;
    }

    /// <summary>
    /// Represents a 96-bit spell class mask (3 x 32-bit values).
    /// Used for spell family flags and effect class masks in WoW.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SpellClassMask
    {
        public uint FlagsLow;
        public uint FlagsMid;
        public uint FlagsHigh;

        public SpellClassMask(uint low, uint mid, uint high)
        {
            FlagsLow = low;
            FlagsMid = mid;
            FlagsHigh = high;
        }

        public bool HasFlag(uint flag)
        {
            return (FlagsLow & flag) != 0 || (FlagsMid & flag) != 0 || (FlagsHigh & flag) != 0;
        }

        public bool IsEmpty => FlagsLow == 0 && FlagsMid == 0 && FlagsHigh == 0;

        public override string ToString()
        {
            return $"Low: {FlagsLow:X8}, Mid: {FlagsMid:X8}, High: {FlagsHigh:X8}";
        }
    }
}
