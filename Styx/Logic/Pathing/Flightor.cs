// Flightor.cs - Ported from HB 4.3.4 and adapted for WoW 3.3.5a
// Flying pathfinding and movement - supports WotLK flying mounts
// Trinity mmaps support flying everywhere

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing.FlightorNavigation;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Vector2 = Tripper.XNAMath.Vector2;

namespace Styx.Logic.Pathing
{
    /// <summary>
    /// Flightor - Flying movement and pathfinding
    /// Ported from HB 4.3.4, adapted for WotLK with Trinity mmap support
    /// </summary>
    public static class Flightor
    {
        private static int _pulseCount;
        private static WoWPoint _lastDestination = WoWPoint.Zero;
        private static WoWPoint _prevDestination = WoWPoint.Zero;

        // Anti-stuck state (WoD smethod_14 port)
        private static WoWPoint _antiStuckCheckPos = WoWPoint.Empty;
        private static DateTime _antiStuckLastCheck = DateTime.MinValue;
        private static readonly WaitTimer _antiStuckTimer = new WaitTimer(TimeSpan.FromMilliseconds(500));
        private static bool _asAscended;
        private static bool _asStrafedLeft;
        private static bool _asStrafedRight;
        private static WoWPoint _antiStuckStartPos = WoWPoint.Empty;

        // PolyNav path state
        private static FlightPath _flightPath;
        private static PolyNav _polyNav;
        private static uint? _polyNavMapId;

        // HB 6.2.3 woWPoint_4/5: cached outdoor takeoff spot and its associated destination.
        // When the bot can't fly from its current location, it navigates to _takeoffSpot first.
        // Cache is invalidated when destination moves >30y (DistanceSqr > 900f).
        // Ported from Flightor.smethod_10 / smethod_4 / smethod_5.
        private static WoWPoint _takeoffSpot        = WoWPoint.Empty;
        private static WoWPoint _takeoffDestination = WoWPoint.Empty;

        static Flightor()
        {
            BotEvents.OnBotStop += args => Clear();
        }

        /// <summary>
        /// True if the local player is currently able to fly.
        /// Ported from HB 6.2.3 Flightor.CanFly, adapted for WotLK (no WoD zone-map infrastructure).
        /// </summary>
        public static bool CanFly
        {
            get
            {
                WoWUnit activeMover = WoWMovement.ActiveMover;
                if (activeMover == null) return false;
                // NOTE: do NOT add MovementInfo.CanFly fast-path here.
                // That bypasses IsFlyableArea() and causes Navigator→Flightor→Navigator recursion
                // when the player is already airborne in a no-fly zone (Dalaran etc.).
                if (!activeMover.IsMe || StyxWoW.Me.InVehicle) return false;
                bool hasFlyingRiding = SpellManager.HasSpell("Expert Riding") ||
                                       SpellManager.HasSpell("Artisan Riding") ||
                                       SpellManager.HasSpell("Master Riding");
                bool hasDruidFlightForm = StyxWoW.Me.Class == WoWClass.Druid &&
                                          (SpellManager.HasSpell("Swift Flight Form") ||
                                           SpellManager.HasSpell("Flight Form"));

                return (hasFlyingRiding || hasDruidFlightForm)
                    && !StyxWoW.Me.IsSwimming                             // WotLK: flight form cancelled immediately in water
                    && Lua.GetReturnVal<bool>("return IsFlyableArea()", 0U)
                    && MountHelper.FlyingMount != null
                    && (StyxWoW.Me.Level >= 60 || StyxWoW.Me.Class == WoWClass.Druid)
                    && (StyxWoW.Me.Level >= 58 || StyxWoW.Me.Class != WoWClass.Druid)
                    && (StyxWoW.Me.MapId != 571U || SpellManager.HasSpell("Cold Weather Flying"));
            }
        }

        /// <summary>
        /// Flying speed multiplier used for walk-vs-fly time comparison.
        /// Ported from HB 6.2.3 Flightor.Single_0.
        /// </summary>
        private static float FlySpeedMultiplier
        {
            get
            {
                if (SpellManager.HasSpell("Master Riding"))  return 4.1f;
                if (SpellManager.HasSpell("Artisan Riding")) return 3.8f;
                if (SpellManager.HasSpell("Expert Riding"))  return 2.5f;
                return 0f;
            }
        }

        /// <summary>
        /// True if ground navigation is faster than mounting and flying to <paramref name="destination"/>.
        /// Ported from HB 6.2.3 Flightor.smethod_9.
        /// </summary>
        private static bool ShouldWalk(WoWPoint destination)
        {
            if (StyxWoW.Me.HasAura("Sea Legs")) return false;
            if (MountHelper.Mounted)             return false;
            // WotLK: when swimming, ground nav fails underwater — let code fall through to
            // the liquid-ascent handlers instead of spamming Navigator.MoveTo.
            if (StyxWoW.Me.IsSwimming)           return false;
            if (!CanFly)                         return true;

            double dist = destination.Distance(StyxWoW.Me.Location);
            if (BotPoi.Current.Type == PoiType.Kill)
            {
                if (dist < Targeting.PullDistance) return true;
                dist -= Targeting.PullDistance;
            }

            float flyMult = FlySpeedMultiplier;
            if (flyMult <= 0f) return false;  // no riding skill — don't walk

            // WoD smethod_9: reads cast time from mount spell (WoWSpell_0.CastTime / 1000.0)
            WoWSpell mountSpell = MountHelper.FlyingMount;
            double mountCastTime = mountSpell != null ? mountSpell.CastTime / 1000.0 : 3.0;
            double walkTime = dist / StyxWoW.Me.MovementInfo.RunSpeed;
            // flyTime + mountCastTime + 2 s fudge > walkTime  →  faster to walk
            return walkTime / flyMult + mountCastTime + 2.0 > walkTime
                && Navigator.CanNavigateWithin(StyxWoW.Me.Location, destination, 5f);
        }

        /// <summary>
        /// Move to destination using flying mount
        /// </summary>
        public static void MoveTo(WoWPoint destination) => MoveTo(destination, 40f);

        /// <summary>
        /// Move to destination with minimum height
        /// </summary>
        public static void MoveTo(WoWPoint destination, float minHeight)
        {
            LocalPlayer me = StyxWoW.Me;
            if (me == null) return;

            // P6.10: Refuse to fly in no-fly zones (Dalaran, indoor dungeons)
            // Force ground navigation instead of trying to mount a flying mount
            if (Navigator.IsInNoFlyZone)
            {
                Navigator.MoveTo(destination);
                return;
            }

            // Don't attempt flying while riding an elevator
            if (Navigator.IsRidingElevator)
            {
                Navigator.MoveTo(destination);
                return;
            }

            // HB 6.2.3 smethod_10: NO combat early-return here.
            // When in combat and not mounted, CanMount returns false (checks !me.Combat),
            // which falls through to Navigator.MoveTo() — the bot walks on foot to the
            // destination instead of freezing. This matches HB behavior exactly.

            WoWPoint myLocation = me.Location;
            bool hasSeaLegs = me.HasAura("Sea Legs");

            // Ground nav is faster than mounting and flying: prefer walking (HB smethod_9)
            if (ShouldWalk(destination))
            {
                Navigator.MoveTo(destination);
                return;
            }

            WoWPoint traceLinePos = me.GetTraceLinePos();

            // Not mounted - need to mount up
            if (!MountHelper.Mounted)
            {
                // HB 6.2.3 smethod_10: can we take off from the current position?
                // Uses 10f LOS height (normal mode, not checkIndoors/Garrison). (F6)
                bool canFlyFromHere = me.IsOutdoors
                    && !Mount.IsInCantMountSpot(myLocation)
                    && (hasSeaLegs || GameWorld.IsInLineOfSight(traceLinePos, myLocation.Add(0f, 0f, 10f)));

                // F3: Invalidate takeoff cache if destination moved >30y (HB woWPoint_5 guard).
                if (_takeoffDestination != WoWPoint.Empty && _takeoffDestination.DistanceSqr(destination) > 900f)
                {
                    _takeoffSpot        = WoWPoint.Empty;
                    _takeoffDestination = WoWPoint.Empty;
                }

                // F3: Clear takeoff cache when we're already in a good takeoff position.
                if (canFlyFromHere)
                {
                    _takeoffSpot        = WoWPoint.Empty;
                    _takeoffDestination = WoWPoint.Empty;
                }

                // F3/F4: Navigate to cached takeoff spot (with mount-up suppression).
                if (_takeoffSpot != WoWPoint.Empty)
                {
                    if (_takeoffSpot.DistanceSqr(myLocation) < 16f)            // arrived (<4y)
                    {
                        _takeoffSpot        = WoWPoint.Empty;
                        _takeoffDestination = WoWPoint.Empty;
                    }
                    else if (Navigator.CanNavigateWithin(myLocation, _takeoffSpot, Navigator.PathPrecision))
                    {
                        NavigateToTakeoffSpot();                                // suppresses OnMountUp
                        return;
                    }
                    else
                    {
                        _takeoffSpot        = WoWPoint.Empty;
                        _takeoffDestination = WoWPoint.Empty;
                    }
                }

                // F2/F3: If blocked from flying, find an outdoor spot and cache it (HB smethod_5).
                // Guard: only search while stationary on the ground — mirrors HB's exact condition.
                if (!me.IsMoving && !me.MovementInfo.IsFlying && !canFlyFromHere)
                {
                    WoWObject candidate = FindTakeoffCandidate(myLocation, 10f);
                    if (candidate != null)
                    {
                        Logging.WriteDiagnostic("[Flightor] Can't take off here. Moving to: {0}", candidate.Location);
                        _takeoffSpot        = candidate.Location;
                        _takeoffDestination = destination;
                        NavigateToTakeoffSpot();
                        return;
                    }
                }

                // Try to mount
                if (MountHelper.CanMount)
                {
                    // Swimming - move up first
                    if (me.IsSwimming && !me.HasAura("Sea Legs") &&
                        !GameWorld.TraceLine(traceLinePos, myLocation, GameWorld.CGWorldFrameHitFlags.HitTestLiquid))
                    {
                        float neededFacing = WoWMathHelper.CalculateNeededFacing(myLocation, destination);
                        WoWPoint p = GetPointInDirection(myLocation, 10f, neededFacing, WoWMathHelper.DegreesToRadians(60f));
                        Navigator.PlayerMover.MoveTowards(p);
                    }
                    // Druid flight form while swimming or standing on riverbed.
                    // WotLK: IsSwimming=false when standing on a riverbed (feet below water
                    // surface, head above) — TraceLine(eye→feet, HitTestLiquid) detects this.
                    // HB 4.3.4 relied on IsSwimming=true in all liquid scenarios (Cata behaviour);
                    // the TraceLine addition covers the WotLK-specific shallow-water case.
                    else if (!me.HasAura("Sea Legs") &&
                             (me.IsSwimming || GameWorld.TraceLine(traceLinePos, myLocation, GameWorld.CGWorldFrameHitFlags.HitTestLiquid)) &&
                             me.Class == WoWClass.Druid &&
                             (SpellManager.HasSpell("Flight Form") || SpellManager.HasSpell("Swift Flight Form")))
                    {
                        WoWMovement.Move(WoWMovement.MovementDirection.JumpAscend);
                        StyxWoW.Sleep(50);
                        MountHelper.MountUpInternal(true);
                        StyxWoW.Sleep(50);
                        MountHelper.MountUpInternal(true);
                        StyxWoW.Sleep(50);
                        MountHelper.MountUpInternal(true);
                        WoWMovement.MoveStop();
                    }
                    else
                    {
                        MountHelper.MountUp();
                    }
                }
                else
                {
                    // Can't mount, use ground navigation
                    Navigator.MoveTo(destination);
                }
            }
            else
            {
                // Already mounted — process flight using PolyNav path queue.
                // Ported from WoD smethod_10 (Flightor.cs, HB 6.2.3).

                // Crusader Aura for paladins
                if (myLocation.Distance(destination) > 100.0 && me.IsAlive &&
                    SpellManager.CanCast("Crusader Aura") && !me.HasAura("Crusader Aura"))
                {
                    SpellManager.Cast("Crusader Aura");
                }

                WoWUnit activeMover = WoWMovement.ActiveMover ?? me;

                // WoD: increment pulse counter, check anti-stuck (resets counter), skip odd pulses
                ++_pulseCount;
                if (AntiStuck)
                    _pulseCount = 0;
                if (_pulseCount % 2 != 0)
                    return;
                _pulseCount = 0;

                // Step 1: Force ascent BEFORE path computation (WoD: mounted but not yet flying)
                if (MountHelper.Mounted && ((!hasSeaLegs && !activeMover.IsFlying) || (hasSeaLegs && !activeMover.IsSwimming)))
                {
                    WoWMovement.Move(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                    StyxWoW.Sleep(100);
                    WoWMovement.MoveStop(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                }

                // Step 2: CTM early return — already making progress toward same destination
                WoWMovement.ClickToMoveInfoStruct ctm = WoWMovement.ClickToMoveInfo;
                if (activeMover.IsMoving && _lastDestination == destination &&
                    ctm.IsClickMoving && ctm.ClickPos.DistanceSqr(activeMover.Location) > 900f)
                    return;

                // Step 3: Destination change → discard cached path
                if (_lastDestination != destination)
                    _flightPath = null;
                _lastDestination = destination;

                // Step 4: Build path if we don't have one
                var myPos2D = new Vector2(myLocation.X, myLocation.Y);
                var dest2D  = new Vector2(destination.X, destination.Y);
                if (_flightPath == null)
                    _flightPath = BuildPath(myPos2D, dest2D);

                // Step 5: Advance ONE waypoint per pulse (WoD: single dequeue, not while-loop)
                Vector2 waypointVec = _flightPath.Waypoints.Peek();
                if (_flightPath.Waypoints.Count > 1 &&
                    myLocation.Distance2DSqr(new WoWPoint(waypointVec.X, waypointVec.Y, 0)) <= 900f)
                {
                    waypointVec = _flightPath.Waypoints.Dequeue();
                }

                // Step 6: Smart Z + dispatch by remaining queue depth
                WoWPoint flightPoint;
                if (_flightPath.Waypoints.Count == 1)
                {
                    // Final stretch — aim directly at the actual destination
                    flightPoint = CalculateFlightPoint(destination, minHeight);
                }
                else
                {
                    // Intermediate waypoint — use dest.Z only within 200m, else maintain current altitude
                    float smartZ = destination.DistanceSqr(myLocation) < 40000f ? destination.Z : myLocation.Z;
                    flightPoint = CalculateFlightPoint(new WoWPoint(waypointVec.X, waypointVec.Y, smartZ), minHeight);
                }

                // Step 7: Apply movement or trigger anti-stuck
                if (flightPoint != WoWPoint.Empty)
                {
                    // Only re-issue CTM if not already moving to the exact same point
                    if (!activeMover.IsMoving || ctm.ClickPos != flightPoint || !ctm.IsClickMoving)
                        Navigator.PlayerMover.MoveTowards(flightPoint);

                    // Second ascent check after issuing movement command
                    if (MountHelper.Mounted && ((!hasSeaLegs && !activeMover.IsFlying) || (hasSeaLegs && !activeMover.IsSwimming)))
                    {
                        StyxWoW.Sleep(100);
                        WoWMovement.Move(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                        StyxWoW.Sleep(100);
                        WoWMovement.MoveStop(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                        Navigator.PlayerMover.MoveTowards(flightPoint);
                        return;
                    }
                }
                else
                {
                    DoAntiStuck();
                }
                return;
            }
        }

        // ── Inner types ───────────────────────────────────────────────────────

        /// <summary>
        /// Represents a pre-computed 2D flight path with an ordered waypoint queue.
        /// Ported from WoD Class1052.
        /// </summary>
        private class FlightPath
        {
            public Vector2 StartPoint;
            public Vector2 EndPoint;
            public Queue<Vector2> Waypoints = new Queue<Vector2>();
        }

        /// <summary>
        /// Build the candidate ray list for CalculateFlightPoint.
        /// Ported from WoD smethod_12 (Flightor.cs, HB 6.2.3).
        /// </summary>
        private static List<WorldLine> BuildRayList(WoWPoint origin, float rayLength, float heading, float pitch)
        {
            const int angleStep = 15;
            var lines = new List<WorldLine>();

            // Pitch up first (most likely to clear terrain)
            for (int i = 1; i <= 3; ++i)
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading, pitch + WoWMathHelper.DegreesToRadians(i * angleStep))));

            // Turn left / right
            for (int i = 1; i <= 3; ++i)
            {
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading + WoWMathHelper.DegreesToRadians(i * -angleStep), pitch)));
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading + WoWMathHelper.DegreesToRadians(i *  angleStep), pitch)));
            }

            // More aggressive pitch up
            for (int i = 4; i <= 6; ++i)
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading, pitch + WoWMathHelper.DegreesToRadians(i * angleStep))));

            // Wider turns
            for (int i = 4; i <= 8; ++i)
            {
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading + WoWMathHelper.DegreesToRadians(i * -angleStep), pitch)));
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading + WoWMathHelper.DegreesToRadians(i *  angleStep), pitch)));
            }

            // Extreme pitch up (WoD: 7..9 inclusive)
            for (int i = 7; i <= 9; ++i)
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading, pitch + WoWMathHelper.DegreesToRadians(i * angleStep))));

            // Pitch down — descend angles (WoD: 4 downward rays, n=1..4)
            for (int n = 1; n <= 4; ++n)
                lines.Add(new WorldLine(origin, GetPointInDirection(origin, rayLength, heading, pitch + WoWMathHelper.DegreesToRadians(n * -angleStep))));

            return lines;
        }

        /// <summary>
        /// Calculate the next waypoint for flight, routed through the PolyNav
        /// visibility graph.  Ported from WoD smethod_11 (Flightor.cs, HB 6.2.3).
        /// Returns WoWPoint.Empty when all raycasts are blocked — caller must invoke DoAntiStuck().
        /// </summary>
        private static WoWPoint CalculateFlightPoint(WoWPoint destination, float minHeight)
        {
            LocalPlayer me = StyxWoW.Me;
            WoWPoint traceLinePos = me.GetTraceLinePos();
            WoWPoint myLocation   = me.Location;

            // Direct LOS to target — go straight.
            // IMPORTANT: must use HitTestGroundAndStructures (0x100111), NOT IsInLineOfSight
            // (which uses HitTestLOS = 0x100011, missing HitTestGround = 0x100).
            // Without the ground flag, mountains are invisible to this check and the bot
            // flies straight through solid terrain to reach nodes on the other side.
            // HB 4.3.4 used HitTestLOS = 0x100121 which includes HitTestGround — same intent.
            if (destination.Z != 0.0 &&
                traceLinePos.DistanceSqr(destination) < 40000.0 &&
                !GameWorld.TraceLine(traceLinePos, destination.Add(0.0f, 0.0f, 2f),
                    GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures))
            {
                return destination;
            }

            // WoD: heading and distance checks use player location (not eye-level traceLinePos)
            float neededFacing = WoWMathHelper.CalculateNeededFacing(myLocation, destination);
            float rayLength = 60f;
            float heightNum = 200f;
            float pitch     = 0.0f;

            // Close approach: match target altitude
            if (myLocation.Distance2D(destination) < 100.0 && destination.Z != 0.0)
            {
                float distance   = myLocation.Distance(destination); // WoD smethod_11: uses location, not traceLinePos
                rayLength        = distance - 1.5f;
                float heightDiff = Math.Abs(destination.Z - myLocation.Z);
                // Math.Min(1f, ...) prevents NaN when heightDiff > distance (WoD fix)
                float angle = (float)Math.Asin(Math.Min(1f, heightDiff / distance));
                pitch = traceLinePos.Z > destination.Z ? -angle : angle;
            }
            else if (!me.HasAura("Sea Legs"))
            {
                if (GameWorld.TraceLine(traceLinePos, traceLinePos.Add(0.0f, 0.0f, -minHeight),
                    GameWorld.CGWorldFrameHitFlags.HitTestGround | GameWorld.CGWorldFrameHitFlags.HitTestLiquid))
                {
                    // Below minimum height — climb
                    pitch = WoWMathHelper.DegreesToRadians(20f);
                }
                else if (!GameWorld.TraceLine(traceLinePos, traceLinePos.Add(0.0f, 0.0f, -heightNum),
                    GameWorld.CGWorldFrameHitFlags.HitTestWMO | GameWorld.CGWorldFrameHitFlags.HitTestGround | GameWorld.CGWorldFrameHitFlags.HitTestLiquid))
                {
                    // Very high — descend if far from destination, not Dalaran, not blocked by Outland terrain
                        // WoD: uses StyxWoW.Me.Rotation (current facing) for the Outland forward trace,
                        // not the heading to destination. Tests if path ahead is blocked.
                        if (!me.HasAura("Sea Legs") && me.ZoneId != 3540U && myLocation.Distance2D(destination) > 300f &&
                            (me.MapId != 530U || !GameWorld.TraceLine(traceLinePos,
                                GetPointInDirection(traceLinePos, 300f, me.Rotation, 0f),
                            GameWorld.CGWorldFrameHitFlags.HitTestWMO | GameWorld.CGWorldFrameHitFlags.HitTestGround)))
                        pitch = WoWMathHelper.DegreesToRadians(-60f);
                }
            }

            // Dalaran (ZoneId 3540): force gentle ascent to clear the crater rim
            if (me.ZoneId == 3540U)
                pitch = WoWMathHelper.DegreesToRadians(30f);

            WoWPoint targetPoint = GetPointInDirection(traceLinePos, rayLength, neededFacing, pitch);

            // Check if direct path is clear
            if (!GameWorld.TraceLine(traceLinePos, targetPoint, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures))
                return targetPoint;

            // First pass: standard-length rays
            List<WorldLine> testLines = BuildRayList(traceLinePos, rayLength, neededFacing, pitch);
            WorldLine[] linesArray = testLines.ToArray();
            GameWorld.MassTraceLine(linesArray, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures, out bool[] hitResults);
            for (int i = 0; i < hitResults.Length; ++i)
            {
                if (!hitResults[i])
                    return linesArray[i].End;
            }

            // Second pass: shorter rays (WoD fallback — rayLength/3f, last resort before DoAntiStuck)
            List<WorldLine> shortLines = BuildRayList(traceLinePos, rayLength / 3f, neededFacing, pitch);
            WorldLine[] shortArray = shortLines.ToArray();
            GameWorld.MassTraceLine(shortArray, GameWorld.CGWorldFrameHitFlags.HitTestGroundAndStructures, out bool[] shortHits);
            for (int j = 0; j < shortHits.Length; j++)
            {
                if (!shortHits[j])
                    return shortArray[j].End;
            }

            // All rays blocked — caller must invoke DoAntiStuck()
            return WoWPoint.Empty;
        }

        /// <summary>
        /// Build a 2D PolyNav path from current position to destination.
        /// Reuses the cached PolyNav instance when the map has not changed.
        /// (WoD smethod_14 port)
        /// </summary>
        private static FlightPath BuildPath(Vector2 from, Vector2 to)
        {
            uint mapId = StyxWoW.Me.MapId;

            if (_polyNav == null || _polyNavMapId != mapId)
            {
                if (!Areas.ContinentAreas.TryGetValue(mapId, out Vector2[] area))
                {
                    // Unknown map — use a huge square so PolyNav still works
                    area = new Vector2[]
                    {
                        new Vector2( 20000f,  20000f),
                        new Vector2(-20000f,  20000f),
                        new Vector2(-20000f, -20000f),
                        new Vector2( 20000f, -20000f)
                    };
                }
                _polyNavMapId = mapId;
                _polyNav = new PolyNav(area, Styx.Logic.Pathing.FlightorNavigation.BlackspotManager.Blackspots);
            }

            Vector2[] rawPath = _polyNav.FindPath(from, to);
            var queue = new Queue<Vector2>(rawPath.Length > 0 ? rawPath : new[] { to });

            // Skip the start point — bot is already there (WoD smethod_14 port)
            if (queue.Count > 1)
                queue.Dequeue();

            return new FlightPath { StartPoint = from, EndPoint = to, Waypoints = queue };
        }

        /// <summary>
        /// Calculate a point in 3D space given direction
        /// </summary>
        private static WoWPoint GetPointInDirection(WoWPoint origin, float distance, float heading, float pitch)
        {
            float x = (float)(Math.Cos(pitch) * Math.Cos(heading)) * distance;
            float y = (float)(Math.Cos(pitch) * Math.Sin(heading)) * distance;
            float z = (float)Math.Sin(pitch) * distance;
            return origin + new WoWPoint(x, y, z);
        }

        /// <summary>
        /// Find a nearby WoW object from which the bot can safely take off.
        /// Ported from HB 6.2.3 Flightor.smethod_5.
        /// </summary>
        /// <param name="from">Current player location.</param>
        /// <param name="losHeight">Check: IsInLineOfSight(object+2z, object+losHeight) — 10f normal, 200f checkIndoors.</param>
        private static WoWObject FindTakeoffCandidate(WoWPoint from, float losHeight)
        {
            float minDistSq = Navigator.PathPrecision * Navigator.PathPrecision;
            return ObjectManager.GetObjectsOfType<WoWObject>(true, false)
                .Where(o => o is WoWGameObject || o is WoWUnit)
                .OrderBy(o => o.DistanceSqr)
                .FirstOrDefault(o =>
                    o.DistanceSqr >= minDistSq
                    && !Blacklist.Contains(o)
                    && !Mount.IsInCantMountSpot(o.Location)
                    && o.IsOutdoors
                    && Navigator.CanNavigateWithin(from, o.Location, Navigator.PathPrecision)
                    && GameWorld.IsInLineOfSight(o.Location.Add(0f, 0f, 2f), o.Location.Add(0f, 0f, losHeight)));
        }

        /// <summary>
        /// Navigate to <see cref="_takeoffSpot"/> while suppressing any OnMountUp event.
        /// Ported from HB 6.2.3 Flightor.smethod_4.
        /// </summary>
        private static void NavigateToTakeoffSpot()
        {
            void CancelMount(object sender, MountUpEventArgs e) => e.Cancel = true;
            try
            {
                Mount.OnMountUp += CancelMount;
                Navigator.MoveTo(_takeoffSpot);
            }
            finally
            {
                Mount.OnMountUp -= CancelMount;
            }
        }

        /// <summary>
        /// Clear all cached path and anti-stuck state (WoD Flightor.Clear port).
        /// Called when the bot stops, or when blackspots/areas change.
        /// </summary>
        public static void Clear()
        {
            _prevDestination    = WoWPoint.Empty;
            _antiStuckStartPos  = WoWPoint.Empty;
            _antiStuckCheckPos  = WoWPoint.Empty;
            _asAscended = _asStrafedLeft = _asStrafedRight = false;
            _flightPath   = null;
            _polyNav      = null;
            _polyNavMapId = null;
            _lastDestination = _prevDestination = WoWPoint.Zero;
            _takeoffSpot        = WoWPoint.Empty;
            _takeoffDestination = WoWPoint.Empty;
            // Release any held movement keys (e.g. Descend stuck active when bot stops
            // mid-sequence in [4] before MoveStop() fires). Prevents the "key stuck" glitch.
            WoWMovement.MoveStop();
        }

        /// <summary>
        /// Calculate total path distance
        /// </summary>
        private static float GetPathDistance(WoWPoint destination)
        {
            WoWPoint[] path = Navigator.GeneratePath(StyxWoW.Me.Location, destination);
            if (path == null || path.Length == 0)
                return float.MaxValue;

            float total = StyxWoW.Me.Location.Distance(path[0]);
            for (int i = 1; i < path.Length; ++i)
                total += path[i].Distance(path[i - 1]);

            return total;
        }

        /// <summary>
        /// Anti-stuck detection based on WaitTimer + displacement check (WoD port).
        /// Returns true and calls DoAntiStuck() when the bot has been stationary too long.
        /// </summary>
        private static bool AntiStuck
        {
            get
            {
                WoWUnit mover = WoWMovement.ActiveMover;
                if (mover == null || mover.Stunned || mover.Fleeing) return false;

                var now = DateTime.Now;
                if (now.Subtract(_antiStuckLastCheck).TotalMilliseconds > 500.0)
                {
                    // New check window — reset and start fresh
                    _antiStuckCheckPos  = WoWPoint.Empty;
                    _antiStuckLastCheck = now;
                    return false;
                }
                _antiStuckLastCheck = now;

                if (!_antiStuckTimer.IsFinished) return false;

                WoWPoint loc = mover.Location;
                if (_antiStuckCheckPos != WoWPoint.Empty &&
                    _antiStuckCheckPos.DistanceSqr(loc) < 9f)
                {
                    // Less than 3m moved over 500ms — stuck
                    Logging.Write(Colors.Red, "[Flightor] We are stuck! ({0})", loc);
                    DoAntiStuck();
                    return true;
                }

                if (mover.MovementInfo.TimeMoved == 0U)
                {
                    // Not moving at all — prime the next check
                    _antiStuckCheckPos = loc;
                    _antiStuckTimer.Reset();
                    return false;
                }

                _antiStuckCheckPos = WoWPoint.Empty;
                return false;
            }
        }

        /// <summary>
        /// Stateful 3-step anti-stuck maneuver (WoD port).
        /// Steps: JumpAscend → StrafeLeft → StrafeRight → Backwards → reset.
        /// Each call advances one step; state resets when the bot moves > 10m.
        /// </summary>
        public static void DoAntiStuck()
        {
            WoWUnit mover = WoWMovement.ActiveMover;
            if (mover == null) return;

            WoWPoint loc = mover.Location;

            // Reset if we've moved far enough since the last stuck event
            if (_antiStuckStartPos != WoWPoint.Empty &&
                _antiStuckStartPos.Distance2DSqr(loc) > 100f)
            {
                _asAscended = _asStrafedLeft = _asStrafedRight = false;
            }
            _antiStuckStartPos = loc;

            if (mover.IsMoving)
            {
                WoWMovement.MoveStop();
                StyxWoW.Sleep(100);
            }

            if (!_asAscended)
            {
                Logging.WriteDiagnostic("[Stuck] Trying to ascend.");
                WoWMovement.Move(WoWMovement.MovementDirection.JumpAscend);
                StyxWoW.Sleep(200);
                WoWMovement.MoveStop(WoWMovement.MovementDirection.JumpAscend);
                StyxWoW.Sleep(100);
                _asAscended = true;
                return;
            }
            if (!_asStrafedLeft)
            {
                Logging.WriteDiagnostic("[Stuck] Trying strafing left.");
                WoWMovement.Move(WoWMovement.MovementDirection.StrafeLeft);
                StyxWoW.Sleep(300);
                WoWMovement.MoveStop(WoWMovement.MovementDirection.StrafeLeft);
                StyxWoW.Sleep(100);
                _asStrafedLeft = true;
                return;
            }
            if (!_asStrafedRight)
            {
                Logging.WriteDiagnostic("[Stuck] Trying strafing right.");
                WoWMovement.Move(WoWMovement.MovementDirection.StrafeRight);
                StyxWoW.Sleep(300);
                WoWMovement.MoveStop(WoWMovement.MovementDirection.StrafeRight);
                StyxWoW.Sleep(100);
                _asStrafedRight = true;
                return;
            }

            // Final step: reverse
            Logging.WriteDiagnostic("[Stuck] Trying to backup.");
            WoWMovement.Move(WoWMovement.MovementDirection.Backwards);
            StyxWoW.Sleep(500);
            WoWMovement.MoveStop(WoWMovement.MovementDirection.Backwards);
            StyxWoW.Sleep(100);
            _asAscended = _asStrafedLeft = _asStrafedRight = false;
        }

        /// <summary>
        /// Flying mount helper - manages mounting/dismounting
        /// </summary>
        public static class MountHelper
        {
            private static readonly Random _random = new Random();

            /// <summary>
            /// Get best available flying mount spell. Internal to match WoD WoWSpell_0 visibility.
            /// </summary>
            internal static WoWSpell FlyingMount
            {
                get
                {
                    LocalPlayer me = StyxWoW.Me;

                    // Sea Legs handling (Vashj'ir) - not in WotLK but keep for future
                    if (me.HasAura("Sea Legs"))
                    {
                        if (!me.IsOutdoors)
                            return null;

                        WoWPoint location = me.Location;
                        if (!GameWorld.TraceLine(location.Add(0.0f, 0.0f, me.BoundingHeight), location,
                            GameWorld.CGWorldFrameHitFlags.HitTestLiquid))
                        {
                            if (SpellManager.HasSpell("Aquatic Form"))
                                return SpellManager.Spells["Aquatic Form"];
                        }
                    }

                    // Configured flying mount (HB 6.2.3: name takes priority over Druid form).
                    if (!string.IsNullOrEmpty(CharacterSettings.Instance.FlyingMountName))
                    {
                        string mountName = CharacterSettings.Instance.FlyingMountName;

                        // Try FlyingMounts list first (correctly classified)
                        var mount = Styx.Logic.MountHelper.FlyingMounts.FirstOrDefault(m =>
                            m.Name == mountName ||
                            m.CreatureId.ToString() == mountName ||
                            m.Name.ToLower().Contains(mountName.ToLower()));

                        if (mount != null)
                            return mount.CreatureSpell;

                        // Fallback: search ALL mounts by name — WotLK classification via aura types
                        // may still leave some mounts as MountType.Ground if the spell data differs.
                        mount = Styx.Logic.MountHelper.Mounts.FirstOrDefault(m =>
                            m.Name == mountName ||
                            m.CreatureId.ToString() == mountName ||
                            m.Name.ToLower().Contains(mountName.ToLower()));

                        if (mount != null)
                            return mount.CreatureSpell;

                        // Last resort for Druid: flight form keeps the bot airborne
                        // (configured mount not found, avoid returning null and blocking CanMount)
                        if (me.Class == WoWClass.Druid)
                        {
                            if (SpellManager.HasSpell("Swift Flight Form"))
                                return SpellManager.Spells["Swift Flight Form"];
                            if (SpellManager.HasSpell("Flight Form"))
                                return SpellManager.Spells["Flight Form"];
                        }

                        return null;
                    }

                    // No mount configured: Druid uses flight form (HB 4.3.4 lines 622-628)
                    if (me.Class == WoWClass.Druid)
                    {
                        if (SpellManager.HasSpell("Swift Flight Form"))
                            return SpellManager.Spells["Swift Flight Form"];
                        if (SpellManager.HasSpell("Flight Form"))
                            return SpellManager.Spells["Flight Form"];
                    }

                    // Random flying mount
                    if (Styx.Logic.MountHelper.FlyingMounts.Count > 0)
                    {
                        int index = _random.Next(0, Styx.Logic.MountHelper.FlyingMounts.Count);
                        return Styx.Logic.MountHelper.FlyingMounts[index].CreatureSpell;
                    }

                    return null;
                }
            }

            /// <summary>
            /// Check if we can mount a flying mount
            /// WotLK: Cold Weather Flying required for Northrend
            /// </summary>
            public static bool CanMount
            {
                get
                {
                    LocalPlayer me = StyxWoW.Me;
                    if (me == null) return false;

                    // WotLK: Cold Weather Flying for Northrend
                    if (!SpellManager.HasSpell("Cold Weather Flying") && me.MapId == 571U)
                        return false;

                    // Must be outdoors
                    if (!me.IsOutdoors)
                        return false;

                    // Must have a flying mount
                    if (FlyingMount == null)
                        return false;

                    // Respect post-combat and post-mount cooldowns from Mount.cs.
                    // HB 6.2.3 Flightor delegates to Mount.CanMount() for these timers.
                    // Exception: bypass timer when in liquid so the swimming-ascent handlers
                    // fire every tick until the player clears the water.
                    // WotLK: IsSwimming=false when standing on a riverbed (feet in water, head
                    // above surface); TraceLine(eye→feet, HitTestLiquid) catches that case.
                    // Without bypass, a failed mount attempt (WoW rejects flying mounts in liquid
                    // even when IsSwimming=false) sets the 10 s timer → Navigator.MoveTo spam
                    // every tick from an underwater position where nav has no mesh data.
                    bool nearLiquid = me.IsSwimming ||
                        GameWorld.TraceLine(me.GetTraceLinePos(), me.Location, GameWorld.CGWorldFrameHitFlags.HitTestLiquid);
                    if (!Mount.AreMountTimersReady && !nearLiquid)
                        return false;

                    // Check for overhead clearance
                    float boundingHeight = me.BoundingHeight;
                    WoWPoint from = me.Location + new WoWPoint(0.0f, 0.0f, boundingHeight);
                    WoWPoint to = from + new WoWPoint(0.0f, 0.0f, boundingHeight / 2f);
                    bool blocked = GameWorld.TraceLine(from, to, GameWorld.CGWorldFrameHitFlags.HitTestLOS);

                    // Not in combat and not blocked above
                    return !me.Combat && !blocked;
                }
            }

            /// <summary>
            /// Check if currently on a flying mount.
            /// WotLK-specific: checks druid shapeshift field first because CMovementData.CanFly
            /// (0x800000) lags behind the shapeshift state by several ticks after form invocation.
            /// Using CanFly alone causes an infinite mount-spam loop for druids.
            /// </summary>
            public static bool Mounted
            {
                get
                {
                    LocalPlayer me = StyxWoW.Me;
                    if (me == null) return false;

                    // Sea Legs: aquatic mounts, aquatic form, ghost — all count as "mounted".
                    if (me.HasAura("Sea Legs"))
                    {
                        if (me.HasAura("Aquatic Form") || me.IsGhost)
                            return true;

                        foreach (var mount in Styx.Logic.MountHelper.UnderwaterMounts)
                        {
                            if (me.Auras.Values.Any(a => a.SpellId == mount.CreatureSpellId))
                                return true;
                        }
                    }

                    // Druid flight form: shapeshift field updates faster than CMovementData.CanFly.
                    // Must check this before the CanFly flag to avoid mount-spam on every tick.
                    if (me.Class == WoWClass.Druid &&
                        (me.Shapeshift == ShapeshiftForm.EpicFlightForm ||
                         me.Shapeshift == ShapeshiftForm.FlightForm))
                        return true;

                    // Primary check: CanFly movement flag (set via SMSG_MOVE_SET_CAN_FLY).
                    // ActiveMover handles vehicle possession edge cases.
                    WoWUnit activeMover = WoWMovement.ActiveMover;
                    if ((activeMover != null && activeMover.MovementInfo.CanFly) || me.IsOnTransport)
                        return true;

                    // Fallback for regular flying mounts: the aura is applied before CanFly is set.
                    WoWSpell flyingMount = FlyingMount;
                    return me.Mounted &&
                           flyingMount != null &&
                           me.Auras.Values.Any(a => a.SpellId == flyingMount.Id);
                }
            }

            /// <summary>
            /// Mount up on flying mount
            /// </summary>
            public static void MountUp() => MountUpInternal(false);

            /// <summary>
            /// Internal mount up implementation
            /// </summary>
            internal static void MountUpInternal(bool quick)
            {
                if (!CanMount || Mounted)
                    return;

                // Block mount-up while riding elevator (HB 6.2.3 MeshNavigator.method_17)
                if (Navigator.IsRidingElevator)
                    return;

                WoWSpell flyingMount = FlyingMount;
                if (flyingMount == null)
                    return;

                // Druid flight form transitions directly from any shapeshift form.
                // Cancelling the current form first is unnecessary and risks a tick in
                // caster form. HB 4.3.4: Druid path skips ClearShapeshift before flight form.
                bool isDruidFlightForm = StyxWoW.Me.Class == WoWClass.Druid
                    && (flyingMount.Name == "Swift Flight Form" || flyingMount.Name == "Flight Form");
                if (!isDruidFlightForm)
                    Mount.ClearShapeshift();

                // Stop moving
                if (StyxWoW.Me.IsMoving)
                {
                    Navigator.PlayerMover.MoveStop();
                    if (!quick)
                        StyxWoW.SleepForLagDuration();
                }

                Logging.Write("Mounting: {0}", flyingMount.Name);
                SpellManager.Cast(flyingMount);
                // Reset the mount timer so CanMount returns false for the next ~10s,
                // preventing spam if the cast is cancelled (e.g. by water or GCD).
                Mount.ResetMountTimer();

                if (!quick)
                {
                    StyxWoW.SleepForLagDuration();
                    StyxWoW.Sleep((int)flyingMount.CastTime + 100);
                    StyxWoW.SleepForLagDuration();
                }
            }

            /// <summary>
            /// Dismount from flying mount
            /// </summary>
            public static void Dismount()
            {
                if (!Mounted)
                    return;

                LocalPlayer me = StyxWoW.Me;

                // Stop moving first
                if (me.IsMoving)
                {
                    WoWMovement.MoveStop();
                    StyxWoW.SleepForLagDuration();
                }

                if (!me.HasAura("Swift Flight Form") && !me.HasAura("Flight Form") && !me.HasAura("Aquatic Form"))
                {
                    Lua.DoString("Dismount()");
                }
                else
                {
                    Lua.DoString("CancelShapeshiftForm()");
                    StyxWoW.SleepForLagDuration();
                }
            }

            /// <summary>
            /// TreeSharp action for dismounting
            /// </summary>
            public class DisMount : TreeSharp.Action
            {
                protected override RunStatus Run(object context)
                {
                    if (!Mounted)
                        return RunStatus.Failure;

                    LocalPlayer me = StyxWoW.Me;

                    if (me.IsMoving)
                    {
                        WoWMovement.MoveStop();
                        StyxWoW.SleepForLagDuration();
                    }

                    if (!me.HasAura("Swift Flight Form") && !me.HasAura("Flight Form") && !me.HasAura("Aquatic Form"))
                    {
                        Lua.DoString("Dismount()");
                        return RunStatus.Success;
                    }

                    Lua.DoString("CancelShapeshiftForm()");
                    StyxWoW.Sleep(250);
                    return RunStatus.Success;
                }
            }
        }
    }
}
