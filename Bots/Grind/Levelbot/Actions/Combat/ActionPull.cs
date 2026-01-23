using System;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Levelbot.Actions.Combat
{
    public class ActionPull : TreeSharp.Action
    {
        private static LocalPlayer? Me => ObjectManager.Me;

        protected override RunStatus Run(object context)
        {
            if (Me == null || Me.CurrentTarget == null)
                return RunStatus.Failure;

            // Dismount first if mounted
            if (Me.Mounted)
            {
                Mount.Dismount("Pull");
                return RunStatus.Running;
            }

            // If in combat, pull is done
            if (ObjectManager.Me != null && ObjectManager.Me.Combat)
                return RunStatus.Success;

            WoWUnit target = Me.CurrentTarget;
            string targetName = target.Name;

            // Check if target is dead
            if (target.Dead)
            {
                Blacklist.Add(Me.CurrentTargetGuid, TimeSpan.FromSeconds(5));
                Me.ClearTarget();
                return RunStatus.Failure;
            }

            // Check if tagged by other
            if (target.TaggedByOther && !target.TaggedByMe && !Me.IsInParty && !Me.IsInRaid)
            {
                Logging.Write("{0} is tagged", targetName);
                Blacklist.Add(Me.CurrentTargetGuid, TimeSpan.FromSeconds(5));
                Me.ClearTarget();
                return RunStatus.Failure;
            }

            // Apply pre-pull buffs if needed
            if (RoutineManager.Current.NeedPullBuffs)
            {
                TreeRoot.StatusText = "Pre-pull buffs";
                RoutineManager.Current.PullBuff();
            }

            // Set status and pull
            if (!target.IsPlayer)
            {
                TreeRoot.StatusText = string.Format("Pulling {0} now...", targetName);
            }
            else
            {
                TreeRoot.StatusText = string.Format("Pulling level {0} {1} now...", target.Level, target.Class);
            }

            RoutineManager.Current.Pull();
            return RunStatus.Running;
        }
    }
}
