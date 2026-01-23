using Styx.WoWInternals;
using TreeSharp;

namespace Levelbot.Decorators.Death
{
    public class DecoratorNeedToMoveToCorpse : Decorator
    {
        public DecoratorNeedToMoveToCorpse(Composite child) : base(child)
        {
        }

        protected override bool CanRun(object context)
        {
            return ObjectManager.Me.IsGhost && ObjectManager.Me.Location.Distance(ObjectManager.Me.CorpsePoint) > 36.0;
        }
    }
}
