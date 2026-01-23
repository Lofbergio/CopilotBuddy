using Styx.WoWInternals;
using TreeSharp;

namespace Levelbot.Actions.Death
{
    public class ActionSuceedIfDeadOrGhost : TreeSharp.Action
    {
        protected override RunStatus Run(object context)
        {
            if (ObjectManager.Me == null)
                return RunStatus.Failure;

            if (ObjectManager.Me.Dead || ObjectManager.Me.IsGhost)
                return RunStatus.Success;

            return RunStatus.Failure;
        }
    }
}
