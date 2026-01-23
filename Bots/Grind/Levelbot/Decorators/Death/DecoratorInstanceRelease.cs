using Styx.Logic;
using Styx.WoWInternals;
using TreeSharp;

namespace Levelbot.Decorators.Death
{
    public class DecoratorInstanceRelease : DecoratorNeedToRelease
    {
        public DecoratorInstanceRelease(Composite child) : base(child)
        {
        }

        protected override bool CanRun(object context)
        {
            if (ObjectManager.Me.IsGhost || !ObjectManager.Me.Dead)
                return base.CanRun(context);

            // Don't release in instances or battlegrounds
            return !Lua.GetReturnVal<bool>("return IsInInstance()", 0U) && !Battlegrounds.IsInsideBattleground;
        }
    }
}
