using Styx;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TreeSharp;

namespace Levelbot.Actions.Death
{
    public class ActionRetrieveCorpse : TreeSharp.Action
    {
        protected override RunStatus Run(object context)
        {
            CorpseRetriever.HandleCorpseRetrieval();

            if (ObjectManager.Me.IsGhost)
                return RunStatus.Running;

            if (ObjectManager.Me.IsAlive)
            {
                WoWMovement.MoveStop();
                return RunStatus.Success;
            }

            WoWMovement.MoveStop();
            return RunStatus.Failure;
        }

        private static class CorpseRetriever
        {
            private static WoWPoint _safeSpot;
            private static DateTime _resTimeout = DateTime.MinValue;

            public static void HandleCorpseRetrieval()
            {
                // Check if there's a resurrection delay
                if (Lua.GetReturnVal<int>("return GetCorpseRecoveryDelay()", 0U) != 0)
                    return;

                // If far from corpse, move closer
                if (StyxWoW.Me.CorpsePoint.Distance(StyxWoW.Me.Location) > 35.0)
                {
                    Navigator.MoveTo(StyxWoW.Me.CorpsePoint);
                }
                // First time near corpse - calculate safe spot
                else if (_resTimeout == DateTime.MinValue)
                {
                    Logging.Write("Calculating safe spot...");
                    FindSafeSpot();
                    Logging.Write("Found safe spot at " + _safeSpot);
                    Logging.Write(string.Format("Safe location is {0}our corpse location.", 
                        _safeSpot == StyxWoW.Me.CorpsePoint ? "" : "not "));
                    _resTimeout = DateTime.Now.AddSeconds(15.0);
                    Navigator.MoveTo(_safeSpot);
                }
                // Timeout - res wherever we are
                else if (_resTimeout < DateTime.Now)
                {
                    Logging.Write("We failed to res. Popping wherever we are.");
                    GrabCorpse();
                }
                // At safe spot - grab corpse
                else if (StyxWoW.Me.Location.DistanceSqr(_safeSpot) < 64.0)
                {
                    Logging.Write("Grabbing our corpse...");
                    GrabCorpse();
                }
                // Keep moving to safe spot
                else if (!StyxWoW.Me.IsMoving)
                {
                    Logging.Write("Moving to {0}", _safeSpot);
                    Navigator.MoveTo(_safeSpot);
                }
            }

            private static void GrabCorpse()
            {
                Logging.Write("Clicking corpse popup...");
                Lua.DoString("RetrieveCorpse()");
                Thread.Sleep(2000);
                _safeSpot = WoWPoint.Empty;
                _resTimeout = DateTime.MinValue;
            }

            private static WoWPoint FindSafeSpot()
            {
                // Get hostile mobs
                List<WoWPoint> hostileLocations = ObjectManager.GetObjectsOfType<WoWUnit>(true, false)
                    .Where(u => !u.Dead && u.IsHostile)
                    .Select(u => u.Location)
                    .ToList();

                Logging.Write("There are {0} hostile mobs.", hostileLocations.Count);

                WoWPoint corpsePoint = StyxWoW.Me.CorpsePoint;

                // If current safe spot is still good, use it
                if (corpsePoint.DistanceSqr(_safeSpot) < 1600.0 && HasNearbyHostile(_safeSpot, hostileLocations))
                    return _safeSpot;

                _safeSpot = corpsePoint;
                float bestDistance = 0.0f;

                // Search for safest spot in a circle around corpse
                for (float angle = 0.0f; angle < 360.0f; angle += 15f)
                {
                    for (float distance = 0.0f; distance < 40.0f; distance += 5f)
                    {
                        WoWPoint testPoint = corpsePoint.RayCast((float)(angle * Math.PI / 180.0), distance);
                        float distToNearestHostile = testPoint.Distance(GetNearestPoint(testPoint, hostileLocations));

                        if (distToNearestHostile > bestDistance)
                        {
                            _safeSpot = testPoint;
                            bestDistance = distToNearestHostile;
                        }
                    }
                }

                return _safeSpot;
            }

            private static bool HasNearbyHostile(WoWPoint point, IEnumerable<WoWPoint> hostileLocations)
            {
                return hostileLocations.Any(h => h.DistanceSqr(point) < 900.0); // 30 yards
            }

            private static WoWPoint GetNearestPoint(WoWPoint point, IEnumerable<WoWPoint> points)
            {
                if (!points.Any())
                    return new WoWPoint(float.MaxValue, float.MaxValue, float.MaxValue);

                return points.OrderBy(p => p.DistanceSqr(point)).First();
            }
        }
    }
}
