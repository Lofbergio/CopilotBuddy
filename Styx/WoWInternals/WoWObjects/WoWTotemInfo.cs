#nullable disable
using System;
using Styx.Helpers;

namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Information about an active totem.
    /// WotLK 3.3.5a implementation using Lua.
    /// </summary>
    public class WoWTotemInfo
    {
        #region Fields

        private readonly int _slot;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a totem info for a specific slot.
        /// </summary>
        /// <param name="slot">Slot index (0-3 for Fire, Earth, Water, Air)</param>
        public WoWTotemInfo(int slot)
        {
            _slot = slot;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the slot index (0=Fire, 1=Earth, 2=Water, 3=Air).
        /// </summary>
        public int Slot => _slot;

        /// <summary>
        /// Gets the totem type for this slot.
        /// </summary>
        public WoWTotemType Type
        {
            get
            {
                // Slots are 1-indexed in Lua: Fire=1, Earth=2, Water=3, Air=4
                // Our slots are 0-indexed: Fire=0, Earth=1, Water=2, Air=3
                return (WoWTotemType)(_slot + 1);
            }
        }

        /// <summary>
        /// Returns true if a totem is active in this slot.
        /// </summary>
        public bool Active
        {
            get
            {
                // Lua slot is 1-indexed
                int luaSlot = _slot + 1;
                var result = Lua.GetReturnVal<int>(
                    $"local haveTotem, name, startTime, duration = GetTotemInfo({luaSlot}); return haveTotem and 1 or 0", 0);
                return result == 1;
            }
        }

        /// <summary>
        /// Gets the name of the active totem.
        /// </summary>
        public string Name
        {
            get
            {
                int luaSlot = _slot + 1;
                return Lua.GetReturnVal<string>(
                    $"local haveTotem, name = GetTotemInfo({luaSlot}); return name or ''", 0) ?? "";
            }
        }

        /// <summary>
        /// Gets the spell ID that created this totem.
        /// </summary>
        public int SpellId
        {
            get
            {
                // WotLK GetTotemInfo doesn't return spellId directly
                // We need to find the totem unit and get CreatedBySpellId
                var unit = Unit;
                if (unit != null)
                    return (int)unit.CreatedBySpellId;
                
                // Fallback: try to match by name
                var name = Name;
                if (string.IsNullOrEmpty(name))
                    return 0;
                
                // Try to get spell ID from spell book
                var spellId = Lua.GetReturnVal<int>(
                    $"local name, rank, icon, cost, isFunnel, powerType, castTime, minRange, maxRange = GetSpellInfo('{name}'); " +
                    $"if name then for i=1,500 do local sName = GetSpellBookItemName(i, 'spell'); if sName == name then " +
                    $"local link = GetSpellLink(i, 'spell'); if link then return tonumber(link:match('spell:(%d+)')) or 0; end end end end return 0", 0);
                
                return spellId;
            }
        }

        /// <summary>
        /// Gets the WoWTotem enum value.
        /// </summary>
        public WoWTotem Totem
        {
            get
            {
                var spellId = SpellId;
                if (Enum.IsDefined(typeof(WoWTotem), spellId))
                    return (WoWTotem)spellId;
                return WoWTotem.None;
            }
        }

        /// <summary>
        /// Gets the WoWTotem enum value (Singular compatibility).
        /// </summary>
        public WoWTotem WoWTotem => Totem;

        /// <summary>
        /// Gets the unit representing this totem.
        /// </summary>
        public WoWUnit Unit
        {
            get
            {
                var guid = Guid;
                if (guid == 0)
                    return null;
                return ObjectManager.GetObjectByGuid<WoWUnit>(guid);
            }
        }

        /// <summary>
        /// Gets the GUID of the totem unit.
        /// </summary>
        public ulong Guid
        {
            get
            {
                // WotLK doesn't have direct totem GUID access
                // Find totem by searching nearby units owned by player
                var me = StyxWoW.Me;
                if (me == null)
                    return 0;

                var totemName = Name;
                if (string.IsNullOrEmpty(totemName))
                    return 0;

                // Search for our totem in object manager
                foreach (var obj in ObjectManager.ObjectList)
                {
                    if (!(obj is WoWUnit unit))
                        continue;
                    
                    if (unit.CreatedByGuid == me.Guid && 
                        unit.Name.Contains(totemName.Replace(" Totem", "")))
                    {
                        return unit.Guid;
                    }
                }

                return 0;
            }
        }

        /// <summary>
        /// Gets the time when the totem was placed.
        /// </summary>
        public DateTime StartTime
        {
            get
            {
                int luaSlot = _slot + 1;
                var startTime = Lua.GetReturnVal<double>(
                    $"local haveTotem, name, startTime = GetTotemInfo({luaSlot}); return startTime or 0", 0);
                
                if (startTime <= 0)
                    return DateTime.MinValue;
                
                // Convert game time to DateTime using GetTime()
                var currentGameTime = Lua.GetReturnVal<double>("return GetTime()", 0);
                var secondsAgo = currentGameTime - startTime;
                return DateTime.Now.AddSeconds(-secondsAgo);
            }
        }

        /// <summary>
        /// Gets the duration of the totem in seconds.
        /// </summary>
        public double Duration
        {
            get
            {
                int luaSlot = _slot + 1;
                return Lua.GetReturnVal<double>(
                    $"local haveTotem, name, startTime, duration = GetTotemInfo({luaSlot}); return duration or 0", 0);
            }
        }

        /// <summary>
        /// Gets the remaining time on the totem.
        /// </summary>
        public TimeSpan TimeLeft
        {
            get
            {
                int luaSlot = _slot + 1;
                var values = Lua.GetReturnValues(
                    $"local haveTotem, name, startTime, duration = GetTotemInfo({luaSlot}); " +
                    $"if haveTotem and duration > 0 then return duration - (GetTime() - startTime) else return 0 end");
                
                if (values == null || values.Count == 0)
                    return TimeSpan.Zero;
                
                double remaining;
                if (double.TryParse(values[0], out remaining) && remaining > 0)
                    return TimeSpan.FromSeconds(remaining);
                
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Returns true if the totem has expired.
        /// </summary>
        public bool Expired => !Active || TimeLeft <= TimeSpan.Zero;

        #endregion

        #region Methods

        /// <summary>
        /// Destroys this totem.
        /// </summary>
        public void Destroy()
        {
            int luaSlot = _slot + 1;
            Lua.DoString($"DestroyTotem({luaSlot})");
        }

        #endregion

        #region ToString

        public override string ToString()
        {
            return $"[Totem Slot {_slot}] {Type}: {Name} - Active:{Active} TimeLeft:{TimeLeft}";
        }

        #endregion
    }
}
