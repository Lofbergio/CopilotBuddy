using System.Collections.Generic;
using Styx.Logic.Pathing;

namespace Bots.VibeGrinder.Selection
{
    /// <summary>Danger classification — leniency relaxes Risky, never Dangerous.</summary>
    public enum SpotClass
    {
        Safe,
        Risky,
        Dangerous,
    }

    /// <summary>A chosen grind spot: centroid, roam hotspots, target mob ids, and its danger class.</summary>
    public class GrindSpot
    {
        public WoWPoint Centroid { get; set; }
        public uint Map { get; set; }
        public List<WoWPoint> Hotspots { get; set; } = new List<WoWPoint>();
        public List<int> MobIds { get; set; } = new List<int>();
        public int DominantMaxLevel { get; set; }
        public SpotClass Classification { get; set; }
        public float Score { get; set; }

        public override string ToString()
        {
            return $"[GrindSpot {Classification} score={Score:F1} map={Map} mobs={Hotspots.Count}hs/{MobIds.Count}ids @ {Centroid}]";
        }
    }
}
