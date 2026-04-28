using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;

namespace Bots.DungeonBuddy.Avoidance
{
    public class AvoidLocation : Avoid
    {
        public AvoidLocation(AvoidInfo avoidInfo) : base(avoidInfo)
        {
        }

        public override WoWPoint Location => this.woWPoint_0;

        internal override void vmethod_0()
        {
            this.woWPoint_0 = this.IsValid ? base.AvoidInfo.LocationSelector() : WoWPoint.Zero;
        }

        private WoWPoint woWPoint_0;
    }
}
