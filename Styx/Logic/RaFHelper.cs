using System;
using System.Linq;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic
{
    /// <summary>
    /// Recruit-A-Friend helper for 3.3.5a
    /// </summary>
    public static class RaFHelper
    {
        private static WoWPlayer? _leader;

        /// <summary>
        /// Current RAF leader
        /// </summary>
        public static WoWPlayer? Leader => _leader;

        /// <summary>
        /// Clears the RAF leader
        /// </summary>
        public static void ClearLeader()
        {
            _leader = null;
        }

        /// <summary>
        /// Sets RAF leader by base pointer
        /// </summary>
        public static void SetLeader(uint ptr)
        {
            _leader = ObjectManager.GetObjectsOfType<WoWPlayer>(false)
                .FirstOrDefault(p => p.BaseAddress == ptr);
        }

        /// <summary>
        /// Sets RAF leader by player object
        /// </summary>
        public static void SetLeader(WoWPlayer unit)
        {
            _leader = unit;
        }

        /// <summary>
        /// Sets RAF leader by GUID
        /// </summary>
        public static void SetLeader(ulong guid)
        {
            _leader = ObjectManager.GetObjectByGuid<WoWPlayer>(guid);
        }

        /// <summary>
        /// Sets RAF leader by name keyword
        /// </summary>
        public static void SetLeader(string keyword)
        {
            _leader = ObjectManager.GetObjectsOfType<WoWPlayer>(false)
                .FirstOrDefault(p => p.Name.Contains(keyword));
        }
    }
}
