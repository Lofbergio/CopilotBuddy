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
        // A gap this long between consecutive runs means a higher-priority behavior (combat, loot, rest)
        // ran in between — that wasn't time spent failing to reach the target, so don't count it toward
        // the 45s reach-timeout. Normal pursuit re-runs every tick (tens of ms).
        private const int PreemptGapMs = 1500;

        private int _moveStartTime;
        private int _lastRunTime;

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
            int now = Environment.TickCount;
            // Pursuit was interrupted (we weren't ticked for a while) — restart the timeout so only
            // continuous time spent trying to reach this target counts, not diversions to fight/loot/rest.
            if (_moveStartTime != 0 && now - _lastRunTime > PreemptGapMs)
                _moveStartTime = 0;
            _lastRunTime = now;

            if (_moveStartTime == 0)
                _moveStartTime = now;

            WoWUnit target = Targeting.Instance.FirstUnit;
            if (target == null)
                return RunStatus.Failure;

            // Timeout check - 45 seconds trying to reach target (HB 3.3.5a value)
            if (Environment.TickCount - _moveStartTime >= 45000)
            {
                _moveStartTime = 0;
                TimeSpan blacklistTime = TimeSpan.FromMinutes(10);
                if (target is WoWPlayer)
                    blacklistTime = TimeSpan.FromSeconds(45);

                Blacklist.Add(target.Guid, blacklistTime);
                StyxWoW.Me?.ClearTarget();
                Logging.Write("Tried to move to {0} for 45 seconds, blacklisting.", target.Name);
                return RunStatus.Failure;
            }

            // HB 3.3.5a: If within PullDistance and line of sight, we're done
            if (target.Location.Distance(ObjectManager.Me.Location) <= Targeting.PullDistance && target.InLineOfSpellSight)
            {
                _moveStartTime = 0;
                Navigator.Clear();
                return RunStatus.Success;
            }

            // Generate path and check if reachable
            WoWPoint targetLocation = target.Location;
            WoWPoint[] path = Navigator.GeneratePath(StyxWoW.Me.Location, targetLocation);

            if (path.Length > 0 && IsPathEndCloseToTarget(path[path.Length - 1], targetLocation))
            {
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

            // Cannot generate path - blacklist for 10 minutes (HB 3.3.5a value)
            _moveStartTime = 0;
            TimeSpan blacklist = TimeSpan.FromMinutes(10);
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
