using Styx.Logic.POI;
using System;
using System.Collections.Generic;
using System.Linq;
using TreeSharp;

namespace CommonBehaviors.Decorators
{
	public class DecoratorIsPoiType : Decorator
	{
		private readonly IEnumerable<PoiType> _poiTypes;

		public DecoratorIsPoiType(PoiType type, Composite child)
			: this(new PoiType[] { type }, child)
		{
		}

		public DecoratorIsPoiType(IEnumerable<PoiType> types, Composite child)
			: base(child)
		{
			_poiTypes = types;
		}

		protected override bool CanRun(object context)
		{
			return _poiTypes.Any(poiType => poiType == BotPoi.Current.Type);
		}
	}
}
