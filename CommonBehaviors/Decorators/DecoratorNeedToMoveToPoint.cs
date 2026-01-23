using Styx.Logic.Pathing;
using Styx.WoWInternals;
using TreeSharp;

namespace CommonBehaviors.Decorators
{
	public class DecoratorNeedToMoveToPoint : Decorator
	{
		private readonly WoWPoint _point;
		private readonly float _distanceToPoint;

		public DecoratorNeedToMoveToPoint(WoWPoint point, float distanceToPoint, Composite decorated)
			: base(decorated)
		{
			_point = point;
			_distanceToPoint = distanceToPoint;
		}

		protected override bool CanRun(object context)
		{
			return ObjectManager.Me.Location.DistanceSqr(_point) > _distanceToPoint * _distanceToPoint;
		}
	}
}
