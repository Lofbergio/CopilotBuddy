#nullable disable
namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Extension methods for WoWTotem enum (HB 4.3.4 compatibility).
    /// WotLK 3.3.5a implementation.
    /// </summary>
    public static class WoWTotemExtensions
    {
        /// <summary>
        /// Gets the spell ID that summons this totem.
        /// In WotLK, the totem enum value IS the spell ID.
        /// </summary>
        public static int GetTotemSpellId(this WoWTotem totem)
        {
            return (int)totem;
        }

        /// <summary>
        /// Gets the totem type (Fire, Earth, Water, Air).
        /// </summary>
        public static WoWTotemType GetTotemType(this WoWTotem totem)
        {
            int spellId = (int)totem;

            // Fire totems
            if (spellId == 2894 || spellId == 2062 || // Fire/Earth Elemental
                spellId == 58704 || // Searing
                spellId == 58734 || // Magma
                spellId == 58656 || // Flametongue
                spellId == 57722 || // Totem of Wrath
                spellId == 58745)   // Frost Resistance
                return WoWTotemType.Fire;

            // Earth totems
            if (spellId == 57622 || // Strength of Earth
                spellId == 58753 || // Stoneskin
                spellId == 58582 || // Stoneclaw
                spellId == 2484 ||  // Earthbind
                spellId == 8143)    // Tremor
                return WoWTotemType.Earth;

            // Water totems
            if (spellId == 58757 || // Healing Stream
                spellId == 58774 || // Mana Spring
                spellId == 16190 || // Mana Tide
                spellId == 8170 ||  // Cleansing/Disease Cleansing
                spellId == 8166 ||  // Poison Cleansing
                spellId == 58739)   // Fire Resistance
                return WoWTotemType.Water;

            // Air totems
            if (spellId == 8512 ||  // Windfury
                spellId == 15112 || // Windwall
                spellId == 8177 ||  // Grounding
                spellId == 58749 || // Nature Resistance
                spellId == 3738 ||  // Wrath of Air
                spellId == 6495)    // Sentry
                return WoWTotemType.Air;

            return WoWTotemType.None;
        }
    }
}
