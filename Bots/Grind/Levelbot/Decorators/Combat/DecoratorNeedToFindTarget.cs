using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.AreaManagement;
using Styx.Logic.Profiles;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Levelbot.Decorators.Combat
{
    public class DecoratorNeedToFindTarget : Decorator
    {
        public DecoratorNeedToFindTarget(Composite child) : base(child)
        {
        }

        protected override bool CanRun(object context)
        {
            WoWUnit firstUnit = Targeting.Instance.FirstUnit;

            if (firstUnit == null)
                return false;

            // Ground mount farming mode check
            if (LevelbotSettings.Instance.GroundMountFarmingMode && StyxWoW.Me.Mounted)
                return false;

            // Distance check
            if (firstUnit.DistanceSqr > Targeting.PullDistanceSqr)
                return false;

            // Mounted checks for grind area
            if (StyxWoW.Me.Mounted)
            {
                GrindArea currentGrindArea = StyxWoW.AreaManager.CurrentGrindArea;
                if (currentGrindArea != null && !Battlegrounds.IsInsideBattleground)
                {
                    // Check if target is within collection range of hotspot
                    if (firstUnit.Location.Distance(currentGrindArea.CurrentHotSpot.Position) > Targeting.CollectionRange 
                        && !Targeting.Instance.KillBetweenHotspots)
                        return false;

                    // Check faction filters
                    Profile currentProfile = ProfileManager.CurrentProfile;
                    if (currentProfile != null && currentProfile.Factions.Contains(firstUnit.FactionId))
                        return true;

                    if (currentGrindArea.Factions.Contains((int)firstUnit.FactionId))
                        return true;

                    // Check level filters
                    return currentGrindArea.TargetMaxLevel != int.MaxValue && IsWithinLevelRange(firstUnit, currentGrindArea);
                }
            }

            return true;
        }

        private static bool IsWithinLevelRange(WoWUnit unit, GrindArea grindArea)
        {
            if (grindArea == null)
                return false;

            int level = unit.Level;
            return level >= grindArea.TargetMinLevel && level <= grindArea.TargetMaxLevel;
        }
    }
}
