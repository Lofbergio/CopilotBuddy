using System;
using GreenMagic;
using Styx.Logic.Pathing;

namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Représente une chaise dans WoW.
    /// WoW 3.3.5a build 12340.
    /// </summary>
    public class WoWChair : WoWSubObject
    {
        internal WoWChair(uint baseAddress) : base(baseAddress)
        {
        }

        /// <summary>
        /// Positions des slots de chaise.
        /// Offset +16 (0x10), array de WoWPoint.
        /// </summary>
        public WoWPoint[] SlotPositions
        {
            get
            {
                Memory? wow = ObjectManager.Wow;
                if (wow == null) return Array.Empty<WoWPoint>();

                int slots = ChairSlots;
                if (slots <= 0) return Array.Empty<WoWPoint>();

                return wow.ReadStructArray<WoWPoint>(BaseAddress + 16, slots);
            }
        }

        /// <summary>
        /// Nombre de slots de chaise disponibles.
        /// Returns 1 by default - multi-slot chairs are rare in 3.3.5a.
        /// Full implementation would require reading GameObjectDataSlot.Data0.
        /// </summary>
        public int ChairSlots
        {
            get
            {
                // Most chairs in 3.3.5a are single-slot
                // Multi-slot benches would require GameObjectDataSlot.Data0 parsing
                // For bot purposes, assuming 1 slot is sufficient
                return 1;
            }
        }
    }
}
