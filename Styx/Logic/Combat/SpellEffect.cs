using System;

namespace Styx.Logic.Combat
{
    /// <summary>
    /// Represents a single spell effect within a WoW spell.
    /// Each spell can have up to 3 effects (SpellEffect1, SpellEffect2, SpellEffect3).
    /// Contains all data about how the effect modifies targets, damage/healing values, etc.
    /// </summary>
    public class SpellEffect
    {
        /// <summary>
        /// Creates a new spell effect with the specified parameters.
        /// </summary>
        internal SpellEffect(
            WoWApplyAuraType auraType,
            float realPointsPerLevel,
            int basePoints,
            uint mechanic,
            uint implicitTargetA,
            uint implicitTargetB,
            uint radiusIndex,
            uint amplitude,
            float multipleValue,
            uint chainTarget,
            uint itemType,
            int miscValueA,
            int miscValueB,
            uint triggerSpell,
            float pointsPerComboPoint,
            SpellClassMask spellClassMask)
        {
            AuraType = auraType;
            RealPointsPerLevel = realPointsPerLevel;
            BasePoints = basePoints;
            Mechanic = mechanic;
            ImplicitTargetA = implicitTargetA;
            ImplicitTargetB = implicitTargetB;
            RadiusIndex = radiusIndex;
            Amplitude = amplitude;
            MultipleValue = multipleValue;
            ChainTarget = chainTarget;
            ItemType = itemType;
            MiscValueA = miscValueA;
            MiscValueB = miscValueB;
            TriggerSpell = triggerSpell;
            PointsPerComboPoint = pointsPerComboPoint;
            SpellClassMask = spellClassMask;
        }

        /// <summary>
        /// The type of aura this effect applies (if it's an aura effect).
        /// Used to identify modifiers like ModStealth, ModStealthDetect, etc.
        /// </summary>
        public WoWApplyAuraType AuraType { get; }

        /// <summary>
        /// Points gained per caster level (for scaling effects).
        /// </summary>
        public float RealPointsPerLevel { get; }

        /// <summary>
        /// Base value of the effect (damage, healing, modifier amount, etc.).
        /// This is the primary value used in calculations like GetTotalAuraModifier.
        /// </summary>
        public int BasePoints { get; }

        /// <summary>
        /// Mechanic type of the effect (stun, root, etc.).
        /// </summary>
        public uint Mechanic { get; }

        /// <summary>
        /// First implicit target (who/what the effect targets).
        /// </summary>
        public uint ImplicitTargetA { get; }

        /// <summary>
        /// Second implicit target (alternative targeting).
        /// </summary>
        public uint ImplicitTargetB { get; }

        /// <summary>
        /// Index into SpellRadius.dbc (effect area of effect).
        /// </summary>
        public uint RadiusIndex { get; }

        /// <summary>
        /// Amplitude for periodic effects (time between ticks in milliseconds).
        /// </summary>
        public uint Amplitude { get; }

        /// <summary>
        /// Multiplier value for certain effect calculations.
        /// </summary>
        public float MultipleValue { get; }

        /// <summary>
        /// Number of targets the effect can chain to.
        /// </summary>
        public uint ChainTarget { get; }

        /// <summary>
        /// Item type requirement for the effect.
        /// </summary>
        public uint ItemType { get; }

        /// <summary>
        /// First miscellaneous value (usage varies by effect type).
        /// Examples: school mask, stat type, creature type, etc.
        /// </summary>
        public int MiscValueA { get; }

        /// <summary>
        /// Second miscellaneous value (usage varies by effect type).
        /// </summary>
        public int MiscValueB { get; }

        /// <summary>
        /// Spell ID that this effect triggers (if it triggers another spell).
        /// </summary>
        public uint TriggerSpell { get; }

        /// <summary>
        /// Additional points gained per combo point spent.
        /// </summary>
        public float PointsPerComboPoint { get; }

        /// <summary>
        /// Spell class mask - identifies which spell classes this effect applies to.
        /// Used for talents that modify specific spell types.
        /// </summary>
        public SpellClassMask SpellClassMask { get; }

        public override string ToString()
        {
            return $"[SpellEffect: AuraType={AuraType}, BasePoints={BasePoints}, " +
                   $"ImplicitTargetA={ImplicitTargetA}, ImplicitTargetB={ImplicitTargetB}]";
        }
    }
}
