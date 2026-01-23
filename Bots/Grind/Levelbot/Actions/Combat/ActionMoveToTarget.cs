using System;
using CommonBehaviors.Actions;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Levelbot.Actions.Combat
{
    public class ActionMoveToTarget : NavigationAction
    {
        private int _moveStartTime;

        public ActionMoveToTarget()
        {
            BotEvents.Player.OnMobKilled += OnMobKilled;
        }

        private void OnMobKilled(BotEvents.Player.MobKilledEventArgs args)
        {
            _moveStartTime = 0;
        }

        protected override RunStatus Run(object context)
        {
            if (_moveStartTime == 0)
                _moveStartTime = Environment.TickCount;

            WoWUnit target = Targeting.Instance.FirstUnit;
            if (target == null)
                return RunStatus.Failure;

            // Timeout check - 45 seconds trying to reach target
            if (Environment.TickCount - _moveStartTime >= 45000)
            {
                _moveStartTime = 0;
                TimeSpan blacklistTime = TimeSpan.FromSeconds(10);
                if (target is WoWPlayer)
                    blacklistTime = TimeSpan.FromSeconds(45);

                Blacklist.Add(target.Guid, blacklistTime);
                StyxWoW.Me?.ClearTarget();
                Logging.Write("Tried to move to {0} for 45 seconds, blacklisting.", target.Name);
                return RunStatus.Failure;
            }

            // Check if in range and line of sight
            if (target.Location.Distance(ObjectManager.Me?.Location ?? WoWPoint.Zero) <= Targeting.PullDistance 
                && target.InLineOfSight)
            {
                _moveStartTime = 0;
                Navigator.Clear();
                return RunStatus.Success;
            }

            // Generate path and check if reachable
            WoWPoint targetLocation = target.Location;
            var path = Navigator.GeneratePath(StyxWoW.Me?.Location ?? WoWPoint.Zero, targetLocation);

            if (path != null && path.Length > 0 && IsPathEndCloseToTarget(path[path.Length - 1], targetLocation))
            {
                // Update status
                if (target.Type == WoWObjectType.Player)
                {
                    TreeRoot.StatusText = string.Format("Moving towards level {0} {1} {2}",
                        target.Level, target.Race, target.Class);
                }
                else
                {
                    TreeRoot.StatusText = "Moving towards " + target.Name;
                }

                return Navigator.GetRunStatusFromMoveResult(Navigator.MoveTo(target.Location));
            }

            // Cannot generate path - blacklist
            _moveStartTime = 0;
            TimeSpan blacklist = TimeSpan.FromSeconds(10);
            if (target is WoWPlayer)
                blacklist = TimeSpan.FromSeconds(45);

            Blacklist.Add(target.Guid, blacklist);
            StyxWoW.Me?.ClearTarget();
            Logging.Write("MoveToTarget: Could not generate path to target {0}, blacklisting.", target.Name);
            return RunStatus.Failure;
        }

        private static bool IsPathEndCloseToTarget(WoWPoint pathEnd, WoWPoint targetLocation)
        {
            if (pathEnd.Distance2DSqr(targetLocation) > Navigator.PathPrecision * Navigator.PathPrecision)
                return false;

            return Math.Abs(pathEnd.Z - targetLocation.Z) < 3f;
        }
    }
}
