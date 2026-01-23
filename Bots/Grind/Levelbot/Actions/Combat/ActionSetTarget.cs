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
    public class ActionSetTarget : TreeSharp.Action
    {
        protected override RunStatus Run(object context)
        {
            Navigator.Clear();

            LocalPlayer me = StyxWoW.Me;
            if (me == null)
                return RunStatus.Failure;

            // Clear dead targets
            if (me.GotTarget && me.CurrentTarget != null && me.CurrentTarget.Dead)
            {
                me.ClearTarget();
                return RunStatus.Success;
            }

            WoWUnit firstUnit = Targeting.Instance.FirstUnit;
            if (firstUnit != null)
            {
                firstUnit.Target();

                try
                {
                    if (firstUnit.IsPlayer)
                    {
                        TreeRoot.StatusText = string.Format(
                            "Setting level {0} {1} {2} at {3:F1} yards as your target",
                            firstUnit.Level, firstUnit.Race, firstUnit.Class, firstUnit.Distance);
                    }
                    else if (firstUnit.OwnedByUnit != null)
                    {
                        TreeRoot.StatusText = string.Format(
                            "Setting level {0} {1} {2}'s pet at {3:F1} yards as your target",
                            firstUnit.OwnedByUnit.Level, firstUnit.OwnedByUnit.Race,
                            firstUnit.OwnedByUnit.Class, firstUnit.Distance);
                    }
                    else
                    {
                        TreeRoot.StatusText = string.Format(
                            "Setting {0} at {1:F1} yards as your target",
                            firstUnit.Name, firstUnit.Distance);
                    }
                }
                catch
                {
                    TreeRoot.StatusText = "Setting target...";
                }
            }

            // Dismount for combat if mounted
            if (ObjectManager.Me != null && ObjectManager.Me.Mounted)
            {
                Logging.WriteDebug("Dismounting for combat.");
                Mount.Dismount("Combat");
            }

            return RunStatus.Success;
        }
    }
}
