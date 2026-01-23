using System;
using System.Collections.Generic;
using System.Linq;
using GreenMagic;
using Styx.Helpers;
using Styx.WoWInternals;

namespace Styx.Logic
{
    /// <summary>
    /// Manages map landmarks/POIs for 3.3.5a
    /// Offsets: NumMapLandmarks at 12488416 (0x00BE8260), LandmarkArray at 12488476 (0x00BE829C)
    /// </summary>
    public class Landmarks
    {
        public readonly List<WoWLandMark> LandmarkList = new List<WoWLandMark>();

        /// <summary>
        /// Offset: 0x00BE8260 (12488416) - NumMapLandmarks counter
        /// </summary>
        private const uint NumMapLandmarksOffset = 12488416;
        
        /// <summary>
        /// Offset: 0x00BE829C (12488476) - Landmark array base
        /// </summary>
        private const uint LandmarkArrayOffset = 12488476;
        
        /// <summary>
        /// Size of each landmark entry in memory (20 bytes = 0x14)
        /// </summary>
        private const uint LandmarkEntrySize = 20;

        public Landmarks()
        {
            Refresh();
        }

        /// <summary>
        /// Gets landmark by SotA landmark ID
        /// </summary>
        public WoWLandMark? GetLandmarkById(SotaLandmarks id)
        {
            return GetLandmarkById((uint)id);
        }

        /// <summary>
        /// Gets landmark by entry ID
        /// </summary>
        public WoWLandMark? GetLandmarkById(uint id)
        {
            return LandmarkList.FirstOrDefault(lm => lm.Entry == id);
        }

        /// <summary>
        /// Number of landmarks currently loaded
        /// </summary>
        public int NumMapLandmarks
        {
            get
            {
                return ObjectManager.Wow!.Read<int>(NumMapLandmarksOffset);
            }
        }

        /// <summary>
        /// Refreshes landmark list from memory
        /// </summary>
        public void Refresh()
        {
            LandmarkList.Clear();
            
            int numLandmarks = NumMapLandmarks;
            uint landmarkArrayBase = ObjectManager.Wow!.Read<uint>(LandmarkArrayOffset);

            for (uint i = 0; i < numLandmarks; i++)
            {
                uint landmarkPtr = landmarkArrayBase + (LandmarkEntrySize * i);
                uint entry = ObjectManager.Wow!.Read<uint>(landmarkPtr); // Read entry directly
                
                // Check if this is a SotA gate (special handling)
                if (entry == 2292 || entry == 2117 || entry == 2120 || 
                    entry == 2111 || entry == 2114 || entry == 2135)
                {
                    // Create SotAGate for Strand of the Ancients gates
                    LandmarkList.Add(new SotAGate(landmarkPtr));
                }
                else
                {
                    // Create normal landmark
                    LandmarkList.Add(new WoWLandMark(landmarkPtr));
                }
            }
        }

        /// <summary>
        /// Displays all landmarks to log
        /// </summary>
        public void Display()
        {
            foreach (var landmark in LandmarkList)
            {
                Logging.Write(landmark.ToString());
            }
        }
    }
}
