using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace CommonBehaviors.Actions
{
	/// <summary>
	/// HB MoP/WoD style ActionMoveToPoi with anti-spam logging.
	/// Tracks last target GUID and location to avoid spamming logs.
	/// </summary>
	public class ActionMoveToPoi : NavigationAction
	{
		private WoWPoint _lastLocation = WoWPoint.Empty;
		private ulong _lastGuid;
		private bool _hasLoggedMove;
		private WaitTimer _stuckCheckTimer = new WaitTimer(TimeSpan.FromSeconds(2));
		private WoWPoint _lastStuckResetLocation = WoWPoint.Empty;
		private ulong _lastStuckResetGuid;

		protected override RunStatus Run(object context)
		{
			BotPoi botPoi = BotPoi.Current;

			if (botPoi.Location == WoWPoint.Zero)
			{
				Logging.Write("ActionMoveToPoi: I don't want to move to (0,0,0)");
				_hasLoggedMove = false;
				return RunStatus.Failure;
			}

			// HB MoP/WoD: Track target unit for moving targets
			WoWObject? asObject = botPoi.AsObject;
			WoWUnit? unit = asObject?.ToUnit();
			bool targetChanged = false;

			if (unit != null)
			{
				ulong guid = unit.Guid;
				WoWPoint location = unit.Location;

				// If target is moving, update location only if significantly changed
				if (unit.IsMoving)
				{
					LocalPlayer? me = ObjectManager.Me;
					if (_lastLocation == WoWPoint.Empty || _lastGuid != guid || 
					    (me != null && _lastLocation.DistanceSqr(me.Location) < 900f))
					{
						targetChanged = (_lastGuid != guid);
						_lastGuid = guid;
						_lastLocation = location;
					}
				}
				else
				{
					// Target stopped, update if changed
					if (_lastGuid != guid || _lastLocation != location)
					{
						targetChanged = (_lastGuid != guid);
						_lastGuid = guid;
						_lastLocation = location;
					}
				}
			}
			else
			{
				// No unit target, use POI location
				if (_lastGuid != 0UL || _lastLocation != botPoi.Location)
				{
					targetChanged = true;
				}
				_lastGuid = 0UL;
				_lastLocation = botPoi.Location;
			}

			// Log only once when target changes (not every tick)
			if (targetChanged || !_hasLoggedMove)
			{
				Logging.Write("Moving to {0}", BotPoi.Current);
				_hasLoggedMove = true;
			}

			// Important: reset stuck state when destination/target changes.
			// Otherwise, the stuck timer can "age" while path is still being generated,
			// and we end up declaring stuck immediately when a new path finally appears.
			if (_lastStuckResetGuid != _lastGuid || _lastStuckResetLocation != _lastLocation)
			{
				_lastStuckResetGuid = _lastGuid;
				_lastStuckResetLocation = _lastLocation;
				Navigator.StuckHandler.Reset();
				_stuckCheckTimer.Reset();
			}

			// HB MoP/WoD: Use precision based on POI type
			float precision = 40f;
			switch (botPoi.Type)
			{
				case PoiType.Hotspot:
				case PoiType.Kill:
				case PoiType.Loot:
				case PoiType.Skin:
				case PoiType.Harvest:
					precision = 15f;
					break;
				case PoiType.Quest:
				case PoiType.QuestPickUp:
				case PoiType.QuestTurnIn:
					precision = 5f;  // Quest interactions need close range
					break;
				case PoiType.Sell:
				case PoiType.Buy:
				case PoiType.Mail:
				case PoiType.Repair:
					precision = 4f;  // Close enough to interact
					break;
			}

			// Mount if needed
			if (Mount.ShouldMount(_lastLocation))
			{
				Mount.StateMount(() => _lastLocation);
			}

			MoveResult moveResult = Navigator.MoveTo(_lastLocation, precision);

			// Check stuck only while we're actively issuing movement.
			// This matches HB behavior more closely and avoids false-stuck while waiting on pathing.
			if (moveResult == MoveResult.Moved && _stuckCheckTimer.IsFinished)
			{
				_stuckCheckTimer.Reset();
				if (Navigator.StuckHandler.IsStuck())
				{
					Navigator.StuckHandler.Unstick();
				}
			}

			return Navigator.GetRunStatusFromMoveResult(moveResult);
		}
	}
}
