using System.Runtime.CompilerServices;
using Styx.Logic.Pathing;
using Tripper.Tools.Math;

namespace Bots.DungeonBuddy.Avoidance
{
    public abstract class Avoid
    {
        public AvoidInfo AvoidInfo { get; private set; }

        protected Avoid(AvoidInfo avoidInfo)
        {
            this.AvoidInfo = avoidInfo;
        }

        public float Radius => this.AvoidInfo.RadiusSelector();

        public WoWPoint LeashPoint
        {
            get
            {
                if (this.AvoidInfo.LeashPointSelector == null)
                    return WoWPoint.Zero;
                return this.AvoidInfo.LeashPointSelector();
            }
        }

        public float LeashRadius => this.AvoidInfo.LeashRadius;

        public bool IsBlocking => this.AvoidInfo.IsBlocking;

        public abstract WoWPoint Location { get; }

        public float RadiusSqr => this.Radius * this.Radius;

        public float LeashRadiusSqr => this.LeashRadius * this.LeashRadius;

        public virtual bool IsValid => this.AvoidInfo.CanRun(null);

        public bool IsPointInAvoid(Vector3 point)
        {
            float num = this.Radius + 0.25f;
            if (this.Location != WoWPoint.Zero)
                return this.Location.Distance2DSqr(point) <= num * num;
            return false;
        }

        internal abstract void vmethod_0();
    }
}
