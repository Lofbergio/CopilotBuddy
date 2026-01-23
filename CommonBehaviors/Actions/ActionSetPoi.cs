using Styx.Helpers;
using Styx.Logic.POI;
using System;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	public class ActionSetPoi : TreeSharp.Action
	{
		private readonly RetrieveBotPoiDelegate? _poiRetrievalFunc;
		private readonly bool _ignorePreviousPoi;

		public ActionSetPoi(RetrieveBotPoiDelegate poiRetrievalFunc)
		{
			_poiRetrievalFunc = poiRetrievalFunc;
		}

		public ActionSetPoi(bool ignorePreviousPoi, RetrieveBotPoiDelegate poiRetrievalFunc)
			: this(poiRetrievalFunc)
		{
			_ignorePreviousPoi = ignorePreviousPoi;
		}

		protected override RunStatus Run(object context)
		{
			BotPoi? newPoi = null;

			if (context != null && _poiRetrievalFunc == null && context is BotPoi poi)
			{
				newPoi = poi;
			}
			else if (_poiRetrievalFunc != null)
			{
				try
				{
					newPoi = _poiRetrievalFunc(context);
				}
				catch (Exception ex)
				{
					Logging.Write("ActionSetPoi Error: {0}", ex);
				}
			}

			if (newPoi != null)
			{
				if (BotPoi.Current.Type != newPoi.Type)
				{
					BotPoi.Current = newPoi;
				}
				else if (_ignorePreviousPoi)
				{
					BotPoi.Current = newPoi;
				}
			}

			return Parent is Selector ? RunStatus.Failure : RunStatus.Success;
		}
	}
}
