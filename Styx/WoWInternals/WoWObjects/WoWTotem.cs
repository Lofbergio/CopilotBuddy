namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Known totem spell IDs for WotLK 3.3.5a.
    /// Each totem is identified by its SpellId.
    /// </summary>
    public enum WoWTotem
    {
        None = 0,
        
        // Earth Totems
        StrengthOfEarth = 57622,      // Strength of Earth Totem (Rank 8)
        Stoneskin = 58753,             // Stoneskin Totem (Rank 10)
        Stoneclaw = 58582,             // Stoneclaw Totem (Rank 10)
        Earthbind = 2484,              // Earthbind Totem
        Tremor = 8143,                 // Tremor Totem
        EarthElemental = 2062,         // Earth Elemental Totem
        
        // Fire Totems
        Searing = 58704,               // Searing Totem (Rank 10)
        Magma = 58734,                 // Magma Totem (Rank 7)
        Flametongue = 58656,           // Flametongue Totem (Rank 8)
        TotemOfWrath = 57722,          // Totem of Wrath (Elemental talent)
        FireElemental = 2894,          // Fire Elemental Totem
        FrostResistance = 58745,       // Frost Resistance Totem (Rank 6)
        
        // Water Totems
        HealingStream = 58757,         // Healing Stream Totem (Rank 9)
        ManaSpring = 58774,            // Mana Spring Totem (Rank 5)
        ManaTide = 16190,              // Mana Tide Totem (Restoration talent)
        Cleansing = 8170,              // Cleansing Totem
        FireResistance = 58739,        // Fire Resistance Totem (Rank 6)
        DiseaseCleansingTotem = 8170,  // Disease Cleansing Totem
        PoisonCleansingTotem = 8166,   // Poison Cleansing Totem
        
        // Air Totems
        Windfury = 8512,               // Windfury Totem
        WindWall = 15112,              // Windwall Totem (Rank 4)
        Grounding = 8177,              // Grounding Totem
        NatureResistance = 58749,      // Nature Resistance Totem (Rank 6)
        WrathOfAir = 3738,             // Wrath of Air Totem
        SentryTotem = 6495,            // Sentry Totem
    }
}
