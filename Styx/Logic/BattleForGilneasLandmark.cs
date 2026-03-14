using System;

namespace Styx.Logic
{
    public class BattleForGilneasLandmark : WoWLandMark
    {
        public BattleForGilneasLandmark(uint ptr)
            : base(ptr)
        {
        }

        public BattleForGilneasLandmarkType LandmarkType
        {
            get { return GetLandmarkType(this); }
        }

        public LandmarkControlType ControlType
        {
            get
            {
                switch (NormalIcon)
                {
                    case 6:
                    case 16:
                    case 26:
                        return LandmarkControlType.Uncontrolled;
                    case 9:
                    case 12:
                    case 17:
                    case 19:
                    case 27:
                    case 29:
                        return LandmarkControlType.InConflict;
                    case 10:
                    case 20:
                    case 30:
                        return LandmarkControlType.HordeControlled;
                    case 11:
                    case 18:
                    case 28:
                        return LandmarkControlType.AllianceControlled;
                    default:
                        return LandmarkControlType.Unknown;
                }
            }
        }

        private static BattleForGilneasLandmarkType GetLandmarkType(WoWLandMark landmark)
        {
            switch (landmark.Entry)
            {
                case 2400:
                case 2401:
                case 2402:
                case 2403:
                case 2404:
                    return BattleForGilneasLandmarkType.Mines;
                case 2405:
                case 2406:
                case 2407:
                case 2408:
                case 2409:
                    return BattleForGilneasLandmarkType.Waterworks;
                case 2410:
                case 2411:
                case 2412:
                case 2413:
                case 2414:
                    return BattleForGilneasLandmarkType.Lighthouse;
                default:
                    return BattleForGilneasLandmarkType.Unknown;
            }
        }
    }
}
