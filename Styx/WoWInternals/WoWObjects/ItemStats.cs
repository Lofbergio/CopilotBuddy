using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Styx.Helpers;
using Styx;
using GreenMagic;

namespace Styx.WoWInternals.WoWObjects
{
    public class ItemStats
    {
        #region Constructor
        public ItemStats()
        {
            Stats = new Dictionary<StatTypes, int>();
            DPS = 0f;
        }
        
        public ItemStats(string itemLink)
        {
            ItemStats itemStats = GetItemStatsFromLink(itemLink);
            this.Stats = itemStats.Stats;
            this.DPS = itemStats.DPS;
        }
        
        #endregion

        #region Stat mapping (the fix)

        // Items report stats keyed by WoWItemStatType (raw WotLK ITEM_MOD_* ids). This maps them to
        // StatTypes *by meaning*, so StatTypes.ToString() parses into the Stat enum the weight sets use.
        // The old code did (StatTypes)(int)key, which is a garbage cast between two unrelated enumerations
        // (Agility(3)->NatureResistance, SpellPower(45)->CritAvoidance, ...) and silently zeroed scoring.
        public static readonly Dictionary<WoWItemStatType, StatTypes> RawToStatType =
            new Dictionary<WoWItemStatType, StatTypes>
        {
            { WoWItemStatType.Health, StatTypes.Health },
            { WoWItemStatType.Mana, StatTypes.Mana },
            { WoWItemStatType.Agility, StatTypes.Agility },
            { WoWItemStatType.Strength, StatTypes.Strength },
            { WoWItemStatType.Intellect, StatTypes.Intellect },
            { WoWItemStatType.Spirit, StatTypes.Spirit },
            { WoWItemStatType.Stamina, StatTypes.Stamina },
            { WoWItemStatType.DefenseSkillRating, StatTypes.DefenseRating },
            { WoWItemStatType.DodgeRating, StatTypes.DodgeRating },
            { WoWItemStatType.ParryRating, StatTypes.ParryRating },
            { WoWItemStatType.BlockRating, StatTypes.BlockRating },
            { WoWItemStatType.HitMeleeRating, StatTypes.HitRatingMelee },
            { WoWItemStatType.HitRangedRating, StatTypes.HitRatingRanged },
            { WoWItemStatType.HitSpellRating, StatTypes.HitRatingSpell },
            { WoWItemStatType.CritMeleeRating, StatTypes.CriticalStrikeRatingMelee },
            { WoWItemStatType.CritRangedRating, StatTypes.CriticalStrikeRatingRanged },
            { WoWItemStatType.CritSpellRating, StatTypes.CriticalStrikeRatingSpell },
            { WoWItemStatType.HitTakenMeleeRating, StatTypes.HitAvoidanceRatingMelee },
            { WoWItemStatType.HitTakenRangedRating, StatTypes.HitAvoidanceRatingRanged },
            { WoWItemStatType.HitTakenSpellRating, StatTypes.HitAvoidanceRatingSpell },
            { WoWItemStatType.CritTakenMeleeRating, StatTypes.CriticalStrikeAvoidanceRatingMelee },
            { WoWItemStatType.CritTakenRangedRating, StatTypes.CriticalStrikeAvoidanceRatingRanged },
            { WoWItemStatType.CritTakenSpellRating, StatTypes.CriticalStrikeAvoidanceRatingSpell },
            { WoWItemStatType.HasteMeleeRating, StatTypes.HasteRatingMelee },
            { WoWItemStatType.HasteRangedRating, StatTypes.HasteRatingRanged },
            { WoWItemStatType.HasteSpellRating, StatTypes.HasteRatingSpell },
            { WoWItemStatType.HitRating, StatTypes.HitRating },
            { WoWItemStatType.CritRating, StatTypes.CriticalStrikeRating },
            { WoWItemStatType.HitTakenRating, StatTypes.HitAvoidanceRating },
            { WoWItemStatType.CritTakenRating, StatTypes.CriticalStrikeAvoidanceRating },
            { WoWItemStatType.ResilienceRating, StatTypes.ResilienceRating },
            { WoWItemStatType.HasteRating, StatTypes.HasteRating },
            { WoWItemStatType.ExpertiseRating, StatTypes.ExpertiseRating },
            { WoWItemStatType.AttackPower, StatTypes.AttackPower },
            { WoWItemStatType.RangedAttackPower, StatTypes.RangedAttackPower },
            { WoWItemStatType.FeralAttackPower, StatTypes.AttackPowerInForms },
            // Legacy split spell stats -> unified SpellPower (WotLK gear uses SpellPower directly).
            { WoWItemStatType.SpellHealingDone, StatTypes.SpellPower },
            { WoWItemStatType.SpellDamageDone, StatTypes.SpellPower },
            { WoWItemStatType.ManaRegeneration, StatTypes.ManaRegeneration },
            { WoWItemStatType.ArmorPenetrationRating, StatTypes.ArmorPenetrationRating },
            { WoWItemStatType.SpellPower, StatTypes.SpellPower },
            { WoWItemStatType.HealthRegeneration, StatTypes.HealthPer5Sec },
            { WoWItemStatType.SpellPenetration, StatTypes.SpellPenetration },
            { WoWItemStatType.BlockValue, StatTypes.BlockValue },
        };

        // Builds an ItemStats from an item's raw stat dictionary (ItemInfo.GetItemStats()), correctly mapped.
        public static ItemStats FromRaw(Dictionary<WoWItemStatType, int> raw, float dps)
        {
            var stats = new ItemStats { DPS = dps };
            if (raw != null)
            {
                foreach (var kvp in raw)
                {
                    if (kvp.Value == 0)
                        continue;
                    StatTypes mapped;
                    if (!RawToStatType.TryGetValue(kvp.Key, out mapped))
                        continue; // unknown / non-scoring stat
                    int current;
                    stats.Stats.TryGetValue(mapped, out current);
                    stats.Stats[mapped] = current + kvp.Value;
                }
            }
            return stats;
        }

        #endregion

        #region Internal Methods
        private static ItemStats GetItemStatsFromLink(string itemLink)
        {
            // Resolve the base item and read its real stats (mapped correctly). Far more reliable than
            // locale-dependent tooltip scraping, and consistent with the equip path.
            uint? id = ParseItemId(itemLink);
            if (id.HasValue)
            {
                var info = ItemInfo.FromId(id.Value);
                if (info != null)
                    return FromRaw(info.GetItemStats(), info.IsWeapon ? info.DPS : 0f);
            }
            return new ItemStats();
        }

        private static uint? ParseItemId(string itemLink)
        {
            if (string.IsNullOrEmpty(itemLink))
                return null;
            var m = Regex.Match(itemLink, @"item:(\d+)");
            uint id;
            if (m.Success && uint.TryParse(m.Groups[1].Value, out id) && id != 0)
                return id;
            return null;
        }
        #endregion
        
        #region Properties
        public float DPS;
        public Dictionary<StatTypes, int> Stats;
        
        #endregion
        
        #region Helper Methods
        public int GetStat(StatTypes type)
        {
            if (Stats == null) return 0;
            return Stats.TryGetValue(type, out int value) ? value : 0;
        }
        public bool HasStat(StatTypes type)
        {
            return Stats != null && Stats.ContainsKey(type);
        }
        public int TotalStats
        {
            get
            {
                if (Stats == null) return 0;
                int total = 0;
                foreach (var kvp in Stats)
                {
                    total += kvp.Value;
                }
                return total;
            }
        }
        
        #endregion
        
        #region ToString
        public override string ToString()
        {
            if (Stats == null || Stats.Count == 0)
            {
                return $"ItemStats [DPS: {DPS:F1}, Stats: None]";
            }
            return $"ItemStats [DPS: {DPS:F1}, Stats: {Stats.Count}]";
        }
        
        #endregion
    }
    
    // Use Styx.StatTypes (canonical) instead of redeclaring here.
    
    #region WoWItemStatType Enum
    public enum WoWItemStatType
    {
        None = 0,
        Health = 1,
        Mana = 2,
        Agility = 3,
        Strength = 4,
        Intellect = 5,
        Spirit = 6,
        Stamina = 7,
        DefenseSkillRating = 12,
        DodgeRating = 13,
        ParryRating = 14,
        BlockRating = 15,
        HitMeleeRating = 16,
        HitRangedRating = 17,
        HitSpellRating = 18,
        CritMeleeRating = 19,
        CritRangedRating = 20,
        CritSpellRating = 21,
        HitTakenMeleeRating = 22,
        HitTakenRangedRating = 23,
        HitTakenSpellRating = 24,
        CritTakenMeleeRating = 25,
        CritTakenRangedRating = 26,
        CritTakenSpellRating = 27,
        HasteMeleeRating = 28,
        HasteRangedRating = 29,
        HasteSpellRating = 30,
        HitRating = 31,
        CritRating = 32,
        HitTakenRating = 33,
        CritTakenRating = 34,
        ResilienceRating = 35,
        HasteRating = 36,
        ExpertiseRating = 37,
        AttackPower = 38,
        RangedAttackPower = 39,
        FeralAttackPower = 40,
        SpellHealingDone = 41,
        SpellDamageDone = 42,
        ManaRegeneration = 43,
        ArmorPenetrationRating = 44,
        SpellPower = 45,
        HealthRegeneration = 46,
        SpellPenetration = 47,
        BlockValue = 48
    }
    
    #endregion
    
    #region WoWSocketColor Enum
    [Flags]
    public enum WoWSocketColor
    {
        None = 0,
        Meta = 1,
        Red = 2,
        Yellow = 4,
        Blue = 8,
        
        // Combinaisons
        Orange = Red | Yellow,      // 6
        Purple = Red | Blue,        // 10
        Green = Yellow | Blue,      // 12
        Prismatic = Red | Yellow | Blue  // 14
    }
    
    #endregion
}
