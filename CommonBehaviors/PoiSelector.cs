using Styx.Logic.POI;
using TreeSharp;

namespace CommonBehaviors
{
    public class PoiSelector : PrioritySelector
    {
        public PoiSelector(params Composite[] children)
            : base(children)
        {
        }

        public override RunStatus Tick(object context)
        {
            return base.Tick(BotPoi.Current);
        }
    }
}
