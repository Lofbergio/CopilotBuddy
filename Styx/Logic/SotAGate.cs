using System;

namespace Styx.Logic
{
    /// <summary>
    /// Strand of the Ancients gate (specialized landmark)
    /// </summary>
    public class SotAGate : WoWLandMark
    {
        public SotAGate(uint ptr) : base(ptr)
        {
        }

        /// <summary>
        /// Gate type based on entry ID and icon
        /// </summary>
        public SotAGateType GateType
        {
            get
            {
                // Ancient gate (entry 2292)
                if (Entry == 2292)
                {
                    return SotAGateType.Ancient;
                }

                // Determine gate type by icon ID
                int icon = NormalIcon;
                
                // Red gate (icons 77-79)
                if (icon >= 77 && icon <= 79)
                {
                    return SotAGateType.Red;
                }
                // Blue gate (icons 80-82)
                else if (icon >= 80 && icon <= 82)
                {
                    return SotAGateType.Blue;
                }
                // Yellow gate (icons 102-104)
                else if (icon >= 102 && icon <= 104)
                {
                    return SotAGateType.Yellow;
                }
                // Purple gate (icons 105-107)
                else if (icon >= 105 && icon <= 107)
                {
                    return SotAGateType.Purple;
                }
                // Green gate (icons 108-110)
                else if (icon >= 108 && icon <= 110)
                {
                    return SotAGateType.Green;
                }

                return SotAGateType.None;
            }
        }

        /// <summary>
        /// Whether the gate is destroyed (WorldState 3 or 6)
        /// </summary>
        public bool IsDestroyed
        {
            get
            {
                uint state = WorldState;
                return state == 3 || state == 6;
            }
        }
    }
}
