using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Styx.WoWInternals.WoWObjects;

namespace Bots.DungeonBuddy.Avoidance
{
    public class AvoidObject : Avoid
    {
        public AvoidObject(AvoidInfo avoidInfo, WoWObject obj) : base(avoidInfo)
        {
            this.Object = obj;
        }

        public WoWObject Object { get; private set; }

        public override WoWPoint Location => this.woWPoint_0;

        public override bool IsValid
        {
            get
            {
                if (this.Object != null && this.Object.IsValid && base.AvoidInfo.CanRun(this.Object))
                    return base.AvoidInfo.ObjectSelector(this.Object);
                return false;
            }
        }

        internal override void vmethod_0()
        {
            this.woWPoint_0 = (this.Object != null && this.Object.IsValid) ? this.Object.Location : WoWPoint.Zero;
        }

        private WoWPoint woWPoint_0;

        [CompilerGenerated]
        private WoWObject woWObject_0;
    }
}
