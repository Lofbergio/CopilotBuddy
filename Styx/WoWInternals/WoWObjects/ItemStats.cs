using System;
using System.Collections.Generic;
using Styx.Helpers;
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
        
        #region Internal Methods
        private static ItemStats GetItemStatsFromLink(string itemLink)
        {
            // Parse item stats using Lua tooltip scanning (3.3.5a compatible)
            var stats = new ItemStats
            {
                DPS = 0f,
                Stats = new Dictionary<StatTypes, int>()
            };
            
            try
            {
                // Use GameTooltip to scan item stats
                string lua = string.Format(@"
                    local stats = {{}};
                    GameTooltip:SetOwner(UIParent, 'ANCHOR_NONE');
                    GameTooltip:SetHyperlink('{0}');
                    
                    for i = 2, GameTooltip:NumLines() do
                        local line = _G['GameTooltipTextLeft'..i];
                        if line then
                            local text = line:GetText();
                            if text then
                                -- Parse DPS
                                local dps = text:match('%(([%d%.]+) damage per second%)');
                                if dps then stats.dps = tonumber(dps); end
                                
                                -- Parse stats (+X Stat)
                                local val, stat = text:match('%+(%d+)%s+(.+)');
                                if val and stat then
                                    stats[stat] = tonumber(val);
                                end
                            end
                        end
                    end
                    
                    GameTooltip:Hide();
                    return stats.dps or 0;
                ", itemLink);
                
                string dpsStr = Lua.GetReturnVal<string>(lua, 0);
                if (!string.IsNullOrEmpty(dpsStr) && float.TryParse(dpsStr, out float dps))
                {
                    stats.DPS = dps;
                }
                
                // Note: Full stat parsing would require additional Lua calls
                // For now, DPS is the most critical stat for weapon comparison
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[ItemStats] Failed to parse item link: {0}", ex.Message);
            }
            
            return stats;
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
    
    #region StatTypes Enum
    public enum StatTypes
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
        FeralAttackPower = 40,  // Obsolète en 3.3.5
        SpellHealingDone = 41,
        SpellDamageDone = 42,
        ManaRegeneration = 43,
        ArmorPenetrationRating = 44,
        SpellPower = 45,
        HealthRegen = 46,
        SpellPenetration = 47,
        BlockValue = 48,
        
        // Stats supplémentaires
        Armor = 50,
        FireResistance = 51,
        FrostResistance = 52,
        HolyResistance = 53,
        ShadowResistance = 54,
        NatureResistance = 55,
        ArcaneResistance = 56,
        
        // Combat stats
        DamageMin = 60,
        DamageMax = 61,
        AttackSpeed = 62,
        
        // Rating conversion (pour affichage)
        HitPercent = 70,
        CritPercent = 71,
        HastePercent = 72,
        ExpertisePercent = 73
    }
    
    #endregion
    
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
