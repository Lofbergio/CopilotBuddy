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

            // Don't fly if in combat and not already mounted
            if (me.Combat && !MountHelper.Mounted && !me.HasAura("Sea Legs"))
                return;

            WoWPoint myLocation = me.Location;
            bool hasSeaLegs = me.HasAura("Sea Legs");

            if (!MountHelper.Mounted &&
                !hasSeaLegs &&
                destination.Distance(myLocation) < 60f &&
                Navigator.CanNavigateFully(myLocation, destination) &&
                GetPathDistance(destination) < 60f)
            {
                Navigator.MoveTo(destination);
                return;
            }

            // Ground nav is faster than mounting and flying: prefer walking (WoD smethod_9 port)
            if (ShouldWalk(destination))
            {
                Navigator.MoveTo(destination);
                return;
            }

            WoWPoint traceLinePos = me.GetTraceLinePos();

            // Not mounted - need to mount up
            if (!MountHelper.Mounted)
            {
                // Check if indoors or blocked above
                if (!me.IsOutdoors ||
                    (!me.HasAura("Sea Legs") &&
                     !GameWorld.IsInLineOfSight(traceLinePos, myLocation.Add(0.0f, 0.0f, 30f))))
                {
                    // Find outdoor location
                    WoWObject outdoorObject = ObjectManager.GetObjectsOfType<WoWObject>(true, false)
                        .Where(o => o is WoWGameObject || o is WoWUnit)
                        .OrderBy(o => o.DistanceSqr)
                        .FirstOrDefault(o => o.IsOutdoors && o.InLineOfSight);

                    if (outdoorObject != null)
                    {
                        if (outdoorObject.DistanceSqr <= 4.0)
                        {
                            Blacklist.Add(outdoorObject, TimeSpan.FromSeconds(10.0));
                            return;
                        }
                        Navigator.MoveTo(outdoorObject.Location);
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
                        Navigator.PlayerMover.MoveTowards(new Tripper.XNAMath.Vector3(p.X, p.Y, p.Z));
                    }
                    // Druid flight form while swimming
                    else if (!me.HasAura("Sea Legs") && me.IsSwimming &&
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
                        Navigator.PlayerMover.MoveTowards(new Tripper.XNAMath.Vector3(flightPoint.X, flightPoint.Y, flightPoint.Z));

                    // Second ascent check after issuing movement command
                    if (MountHelper.Mounted && ((!hasSeaLegs && !activeMover.IsFlying) || (hasSeaLegs && !activeMover.IsSwimming)))
                    {
                        StyxWoW.Sleep(100);
                        WoWMovement.Move(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                        StyxWoW.Sleep(100);
                        WoWMovement.MoveStop(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                        Navigator.PlayerMover.MoveTowards(new Tripper.XNAMath.Vector3(flightPoint.X, flightPoint.Y, flightPoint.Z));
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

            // Direct LOS to target — go straight
            if (destination.Z != 0.0 &&
                traceLinePos.DistanceSqr(destination) < 40000.0 &&
                GameWorld.IsInLineOfSight(traceLinePos, destination.Add(0.0f, 0.0f, 2f)))
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
                _polyNav = new PolyNav(area, AerialBlackspotManager.Blackspots);
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
            /// Check if currently on a flying mount
            /// </summary>
            public static bool Mounted
            {
                get
                {
                    LocalPlayer me = StyxWoW.Me;
                    if (me == null) return false;

                    // Sea Legs with aquatic form
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

                    // Druid flight form: check Shapeshift field (updates faster than CanFly movement flag)
                    if (me.Class == WoWClass.Druid &&
                        (me.Shapeshift == ShapeshiftForm.EpicFlightForm ||
                         me.Shapeshift == ShapeshiftForm.FlightForm))
                        return true;

                    WoWUnit activeMover = WoWMovement.ActiveMover;
                    if ((activeMover != null && activeMover.MovementInfo.CanFly) || me.IsOnTransport)
                        return true;

                    WoWSpell flyingMount = FlyingMount;
                    return me.Mounted &&
                           flyingMount != null &&
                           (me.Auras.Values.Any(a => a.SpellId == flyingMount.Id) ||
                            IsCompanionMountActive(flyingMount.Id));
                }
            }

            private static bool IsCompanionMountActive(int spellId)
            {
                return Lua.GetReturnVal<bool>(
                    "for i=1,GetNumCompanions('MOUNT') do " +
                    "local _,_,id,_,active=GetCompanionInfo('MOUNT', i); " +
                    "if active and tonumber(id)==" + spellId + " then return true end " +
                    "end return false", 0U);
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

                // Clear shapeshift if druid
                Mount.ClearShapeshift();

                // Stop moving
                if (StyxWoW.Me.IsMoving)
                {
                    Navigator.PlayerMover.MoveStop();
                    if (!quick)
                        StyxWoW.SleepForLagDuration();
                }

                // Cast mount spell
                WoWSpell flyingMount = FlyingMount;
                if (flyingMount != null)
                {
                    Logging.Write("Mounting: {0}", flyingMount.Name);
                    SpellManager.Cast(flyingMount);

                    if (!quick)
                    {
                        StyxWoW.SleepForLagDuration();
                        StyxWoW.Sleep((int)flyingMount.CastTime);
                        StyxWoW.SleepForLagDuration();
                    }
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
