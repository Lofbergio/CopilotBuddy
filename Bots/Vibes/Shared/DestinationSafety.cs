using Bots.Vibes.Shared.GrindData;
using Styx.Logic.Pathing;

namespace Bots.Vibes.Shared
{
    /// <summary>
    /// "Can this character survive standing at that point?", answered from GrindMobs.db before we walk
    /// there. Two screens, both level-relative so a rejection re-checks naturally on level-up.
    ///
    /// One implementation because there is one question. It was written twice — once for vendor stops,
    /// once for quest givers — under names that said "Vendor" in a file screening quest NPCs; the
    /// numbers were kept in VibeGrinderSettings, so a bot that wanted the screen had to import
    /// VibeGrinder to get it. They are not user tuning and never were: every one is a
    /// <c>[Browsable(false)]</c> literal that no UI ever showed.
    ///
    /// Fail-OPEN by contract: no faction context or no DB means we cannot judge, and refusing to shop
    /// or quest because a database is missing is worse than the hazard it screens for.
    /// </summary>
    public static class DestinationSafety
    {
        /// <summary>Player-hostile spawns this close to the point make it enemy territory. The DB flags
        /// service NPCs inside opposing-faction camps (Prospector Khazgorm at Bael Modan), and quest
        /// givers sit in them too.</summary>
        private const float HostileRadius = 45f;
        private const int HostileThreshold = 3;

        /// <summary>Stay-in-your-zone. Destination resolution is continent-wide (the Barrens, Dustwallow
        /// and Mulgore are all map 1), so the nearest anything can sit one zone over in much higher-level
        /// country — a lvl-21 toon routed from the Barrens into Dustwallow dies crossing the border.
        /// Over-level is the gate, NOT distance: a far same-level destination is fine.</summary>
        private const float AreaScanRadius = 200f;
        private const int AreaLevelMargin = 7;

        /// <summary>
        /// True when the point is worth walking to. <paramref name="reason"/> is a short lower-case
        /// phrase that reads after a subject ("the vendor <reason>", "giver <reason>").
        /// </summary>
        public static bool IsSurvivable(uint mapId, WoWPoint point, int myLevel,
                                        FactionResolver factions, out string reason)
        {
            reason = null;
            if (factions == null || !GrindMobsRepository.IsAvailable) return true;

            int hostiles = GrindMobsRepository.HostileSpawnCountNear(mapId, point, HostileRadius, factions);
            if (hostiles >= HostileThreshold)
            {
                reason = string.Format("is in enemy territory ({0} hostile spawns within {1:F0}yd)",
                                       hostiles, HostileRadius);
                return false;
            }

            float areaLevel = GrindMobsRepository.AverageAttackableLevelNear(mapId, point, AreaScanRadius, factions);
            if (areaLevel > myLevel + AreaLevelMargin)
            {
                reason = string.Format("is in over-level country (area averages {0:F0}, I am {1})",
                                       areaLevel, myLevel);
                return false;
            }
            return true;
        }
    }
}
