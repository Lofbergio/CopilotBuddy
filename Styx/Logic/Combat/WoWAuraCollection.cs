using System;
using System.Collections.Generic;
using System.Linq;

namespace Styx.Logic.Combat
{
    /// <summary>
    /// Collection class for WoWAura objects.
    /// Extends List&lt;WoWAura&gt; with helper methods for aura management.
    /// </summary>
    public class WoWAuraCollection : List<WoWAura>
    {
        #region Constructors
        
        /// <summary>
        /// Creates an empty aura collection.
        /// </summary>
        public WoWAuraCollection()
        {
        }
        
        /// <summary>
        /// Creates an aura collection with the specified initial capacity.
        /// </summary>
        public WoWAuraCollection(int capacity) : base(capacity)
        {
        }
        
        /// <summary>
        /// Creates an aura collection from an existing collection of auras.
        /// </summary>
        public WoWAuraCollection(IEnumerable<WoWAura> collection) : base(collection)
        {
        }
        
        #endregion
        
        #region Query Methods
        
        /// <summary>
        /// Gets all buffs (non-harmful auras) in the collection.
        /// </summary>
        public IEnumerable<WoWAura> Buffs => this.Where(a => !a.IsHarmful);
        
        /// <summary>
        /// Gets all debuffs (harmful auras) in the collection.
        /// </summary>
        public IEnumerable<WoWAura> Debuffs => this.Where(a => a.IsHarmful);
        
        /// <summary>
        /// Gets all passive auras in the collection.
        /// </summary>
        public IEnumerable<WoWAura> PassiveAuras => this.Where(a => a.IsPassive);
        
        /// <summary>
        /// Gets all active (non-passive) auras in the collection.
        /// </summary>
        public IEnumerable<WoWAura> ActiveAuras => this.Where(a => a.IsActive && !a.IsPassive);
        
        /// <summary>
        /// Gets an aura by spell ID.
        /// </summary>
        /// <param name="spellId">The spell ID to search for.</param>
        /// <returns>The aura with the matching spell ID, or null if not found.</returns>
        public WoWAura? GetById(int spellId)
        {
            return this.FirstOrDefault(a => a.SpellId == spellId);
        }
        
        /// <summary>
        /// Gets an aura by name.
        /// </summary>
        /// <param name="name">The name to search for (case-insensitive).</param>
        /// <returns>The aura with the matching name, or null if not found.</returns>
        public WoWAura? GetByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
                
            return this.FirstOrDefault(a => 
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// Checks if an aura with the specified spell ID exists in the collection.
        /// </summary>
        public bool Contains(int spellId)
        {
            return this.Any(a => a.SpellId == spellId);
        }
        
        /// <summary>
        /// Checks if an aura with the specified name exists in the collection.
        /// </summary>
        public bool ContainsByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
                
            return this.Any(a => 
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// Gets all auras cast by the specified unit.
        /// </summary>
        /// <param name="creatorGuid">The GUID of the creator unit.</param>
        /// <returns>A collection of auras from the specified creator.</returns>
        public WoWAuraCollection GetByCreator(ulong creatorGuid)
        {
            return new WoWAuraCollection(this.Where(a => a.CreatorGuid == creatorGuid));
        }
        
        /// <summary>
        /// Gets all auras of a specific apply aura type.
        /// </summary>
        public WoWAuraCollection GetByAuraType(WoWApplyAuraType auraType)
        {
            return new WoWAuraCollection(this.Where(a => a.ApplyAuraType == auraType));
        }
        
        #endregion
        
        #region Modifier Methods
        
        /// <summary>
        /// Calculates the total modifier from all auras of a specific type.
        /// This is useful for calculating stat bonuses from buffs.
        /// </summary>
        /// <param name="auraType">The aura type to sum.</param>
        /// <returns>The total modifier value.</returns>
        public int GetTotalAuraModifier(WoWApplyAuraType auraType)
        {
            int total = 0;
            
            foreach (var aura in this)
            {
                // Check each of the 3 possible spell effects
                for (int i = 0; i < 3; i++)
                {
                    // Check if this effect is active via the flags
                    if (((int)aura.Flags & (1 << i)) != 0)
                    {
                        var spell = aura.Spell;
                        if (spell != null)
                        {
                            var effect = spell.GetSpellEffect(i);
                            if (effect != null && effect.AuraType == auraType)
                            {
                                total += effect.BasePoints;
                            }
                        }
                    }
                }
            }
            
            return total;
        }
        
        /// <summary>
        /// Gets the maximum stack count among all auras with the specified spell ID.
        /// </summary>
        public uint GetMaxStackCount(int spellId)
        {
            var aura = GetById(spellId);
            return aura?.StackCount ?? 0;
        }
        
        /// <summary>
        /// Gets the minimum time remaining among all auras with the specified spell ID.
        /// </summary>
        public TimeSpan GetMinTimeLeft(int spellId)
        {
            var aura = GetById(spellId);
            return aura?.TimeLeft ?? TimeSpan.Zero;
        }
        
        #endregion
        
        #region Dictionary Conversion
        
        /// <summary>
        /// Converts the collection to a dictionary keyed by spell ID.
        /// </summary>
        public Dictionary<int, WoWAura> ToDictionaryBySpellId()
        {
            var dict = new Dictionary<int, WoWAura>();
            foreach (var aura in this)
            {
                // Only add if not already present (first occurrence wins)
                if (!dict.ContainsKey(aura.SpellId))
                {
                    dict[aura.SpellId] = aura;
                }
            }
            return dict;
        }
        
        /// <summary>
        /// Converts the collection to a dictionary keyed by name.
        /// </summary>
        public Dictionary<string, WoWAura> ToDictionaryByName()
        {
            var dict = new Dictionary<string, WoWAura>(StringComparer.OrdinalIgnoreCase);
            foreach (var aura in this)
            {
                var name = aura.Name;
                if (!string.IsNullOrEmpty(name) && !dict.ContainsKey(name))
                {
                    dict[name] = aura;
                }
            }
            return dict;
        }
        
        #endregion
    }
}
