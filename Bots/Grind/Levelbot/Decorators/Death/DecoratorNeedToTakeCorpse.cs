using Styx.WoWInternals;
using TreeSharp;

namespace Levelbot.Decorators.Death
{
    public class DecoratorNeedToTakeCorpse : Decorator
    {
        public DecoratorNeedToTakeCorpse(Composite child) : base(child)
        {
        }

        protected override bool CanRun(object context)
        {
            return ObjectManager.Me.IsGhost && ObjectManager.Me.CorpsePoint.Distance(ObjectManager.Me.Location) <= 36.0;
        }
    }
}
