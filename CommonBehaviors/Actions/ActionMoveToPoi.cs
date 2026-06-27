using System;
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
		// No-progress give-up (static interact destinations only).
		private float _stallDist;
		private DateTime _stallSince = DateTime.MinValue;

		protected override RunStatus Run(object context)
		{
			BotPoi botPoi = BotPoi.Current;

			if (botPoi.Location == WoWPoint.Zero)
			{
				Logging.Write("I don't want to move to (0, 0, 0).");
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

			// Mount if needed
			float precision = 40f;
			switch (botPoi.Type)
			{
				case PoiType.Hotspot:
				case PoiType.Kill:
					precision = 15f;
					break;
				case PoiType.Loot:
				case PoiType.Skin:
				case PoiType.Harvest:
					precision = 4.5f;  // Close enough to interact
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
				case PoiType.Train:  // Trainer needs close range like vendors
				case PoiType.Fly:    // Flight master needs close range
					precision = 4f;  // Close enough to interact
					break;
			}

			// No-progress give-up for STATIC interact destinations (vendor/loot/mailbox/trainer/etc.).
			// These are fixed points: if we can't get within range for 20s we're blocked by terrain
			// (partial navmesh path). Return Failure so the caller can blacklist/clear instead of walking
			// at an unreachable point forever. Kill/Hotspot/Quest are excluded — moving targets and roam
			// handle their own give-up (and would false-trip on legitimately-moving destinations).
			bool staticDest = botPoi.Type is PoiType.Loot or PoiType.Skin or PoiType.Harvest
				or PoiType.Sell or PoiType.Buy or PoiType.Mail or PoiType.Repair or PoiType.Train or PoiType.Fly;
			if (staticDest)
			{
				LocalPlayer? meNow = ObjectManager.Me;
				float dist = meNow != null ? meNow.Location.Distance(_lastLocation) : 0f;
				if (targetChanged || _stallSince == DateTime.MinValue || dist <= precision || dist < _stallDist - 3f)
				{
					_stallDist = dist;
					_stallSince = DateTime.UtcNow;
				}
				else if ((DateTime.UtcNow - _stallSince).TotalSeconds > 20)
				{
					Logging.Write("[Move] No progress toward {0} in 20s — giving up (unreachable?).", botPoi);
					_hasLoggedMove = false;
					_stallSince = DateTime.MinValue;
					return RunStatus.Failure;
				}
			}

			// HB 6.2.3 pattern: movement always goes through Flightor, which dispatches to
			// Navigator internally when ground nav is needed (ShouldWalk / IsInNoFlyZone).
			// FlightPaths still owns flight-master POIs; Flightor must not override that.
			Flightor.MoveTo(_lastLocation);
			return RunStatus.Success;
		}
	}
}
