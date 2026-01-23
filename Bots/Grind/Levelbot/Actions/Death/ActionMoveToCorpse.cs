using CommonBehaviors.Actions;
using Styx.WoWInternals;
using TreeSharp;

namespace Levelbot.Actions.Death
{
    public class ActionMoveToCorpse : NavigationAction
    {
        protected override RunStatus Run(object context)
        {
            return base.Run(ObjectManager.Me.CorpsePoint);
        }
    }
}
