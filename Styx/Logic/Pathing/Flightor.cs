// Flightor.cs - Ported from HB 4.3.4 and adapted for WoW 3.3.5a
// Flying pathfinding and movement - supports WotLK flying mounts
// Trinity mmaps support flying everywhere

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Styx.Logic.Pathing
{
    /// <summary>
    /// Flightor - Flying movement and pathfinding
    /// Ported from HB 4.3.4, adapted for WotLK with Trinity mmap support
    /// </summary>
    public static class Flightor
    {
        private static int _unstuckAttempts = 1;
        private static int _pulseCount;
        private static WoWPoint _lastDestination;
        private static readonly object _lockObject = new object();

        static Flightor()
        {
            BotEvents.OnBotStop += args => Clear();
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

            // If close and can navigate on ground, use ground navigation
            if (!MountHelper.Mounted &&
                !me.HasAura("Sea Legs") &&
                destination.Distance(myLocation) < 60.0 &&
                Navigator.CanNavigateFully(myLocation, destination) &&
                GetPathDistance(destination) < 60.0)
            {
                Navigator.MoveTo(destination);
                return;
            }

            // Anti-stuck check
            if (AntiStuck)
                return;

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
                        WoWPoint p = GetPointInDirection(myLocation, 10f, neededFacing, WoWMathHelper.DegreesToRadians(45f));
                        Navigator.PlayerMover.MoveTowards(new Tripper.XNAMath.Vector3(p.X, p.Y, p.Z));
                    }
                    // Druid flight form while swimming
                    else if (!me.HasAura("Sea Legs") && me.IsSwimming &&
                             me.Class == WoWClass.Druid &&
                             (SpellManager.HasSpell("Flight Form") || SpellManager.HasSpell("Swift Flight Form")))
                    {
                        WoWMovement.Move(WoWMovement.MovementDirection.JumpAscend);
                        Thread.Sleep(50);
                        MountHelper.MountUpInternal(true);
                        Thread.Sleep(50);
                        MountHelper.MountUpInternal(true);
                        Thread.Sleep(50);
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
                // Already mounted and flying
                // Crusader Aura for paladins
                if (myLocation.Distance(destination) > 100.0 && me.IsAlive &&
                    SpellManager.CanCast("Crusader Aura") && !me.HasAura("Crusader Aura"))
                {
                    SpellManager.Cast("Crusader Aura");
                }

                // Only process every other pulse
                ++_pulseCount;
                if (_pulseCount % 2 != 0)
                    return;
                _pulseCount = 0;

                float pathCheckDist = 30f;
                if (WoWMovement.ClickToMoveInfo.IsClickMoving &&
                    _lastDestination == destination &&
                    WoWMovement.ClickToMoveInfo.ClickPos.DistanceSqr(myLocation) > pathCheckDist * pathCheckDist)
                    return;

                _lastDestination = destination;

                // Calculate flight point
                WoWPoint flightPoint = CalculateFlightPoint(destination, minHeight);
                if (flightPoint == WoWPoint.Empty)
                    return;

                Navigator.PlayerMover.MoveTowards(new Tripper.XNAMath.Vector3(flightPoint.X, flightPoint.Y, flightPoint.Z));

                // Take off if on ground with flying mount
                if ((me.HasAura("Sea Legs") || !me.MovementInfo.CanFly || me.MovementInfo.IsFlying || me.IsSwimming) &&
                    (!me.HasAura("Sea Legs") || me.IsSwimming))
                    return;

                Thread.Sleep(100);
                WoWMovement.Move(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend, TimeSpan.FromMilliseconds(100.0));
                Navigator.PlayerMover.MoveTowards(new Tripper.XNAMath.Vector3(flightPoint.X, flightPoint.Y, flightPoint.Z));
            }
        }

        /// <summary>
        /// Calculate the next flight point with obstacle avoidance
        /// </summary>
        private static WoWPoint CalculateFlightPoint(WoWPoint destination, float minHeight)
        {
            LocalPlayer me = StyxWoW.Me;
            WoWPoint traceLinePos = me.GetTraceLinePos();
            WoWPoint myLocation = me.Location;

            // Direct path if close and clear
            if (destination.Z != 0.0 &&
                traceLinePos.DistanceSqr(destination) < 40000.0 &&
                GameWorld.IsInLineOfSight(traceLinePos, destination.Add(0.0f, 0.0f, 2f)))
            {
                return destination;
            }

            float neededFacing = WoWMathHelper.CalculateNeededFacing(traceLinePos, destination);
            float rayLength = 60f;
            float heightNum = 200f;
            float pitch = 0.0f;

            // Close to destination
            if (traceLinePos.Distance2D(destination) < 60.0 && destination.Z != 0.0)
            {
                float distance = traceLinePos.Distance(destination);
                rayLength = distance - 1.5f;
                float heightDiff = Math.Abs(destination.Z - me.Z);
                float angle = (float)Math.Asin(heightDiff / distance);
                pitch = traceLinePos.Z > destination.Z ? -angle : angle;
            }
            else if (!me.HasAura("Sea Legs"))
            {
                // Check if too low
                if (GameWorld.TraceLine(traceLinePos, traceLinePos.Add(0.0f, 0.0f, -minHeight),
                    GameWorld.CGWorldFrameHitFlags.HitTestGround | GameWorld.CGWorldFrameHitFlags.HitTestLiquid))
                {
                    pitch = WoWMathHelper.DegreesToRadians(20f);
                }
                // Check if very high
                else if (!GameWorld.TraceLine(traceLinePos, traceLinePos.Add(0.0f, 0.0f, -heightNum),
                    GameWorld.CGWorldFrameHitFlags.HitTestWMO | GameWorld.CGWorldFrameHitFlags.HitTestGround | GameWorld.CGWorldFrameHitFlags.HitTestLiquid))
                {
                    pitch = WoWMathHelper.DegreesToRadians(-60f);
                }
            }

            // Northrend forced up (for Dalaran area)
            if (me.ZoneId == 3540U)
                pitch = WoWMathHelper.DegreesToRadians(20f);

            WoWPoint targetPoint = GetPointInDirection(traceLinePos, rayLength, neededFacing, pitch);

            // Check if direct path is clear
            if (!GameWorld.TraceLine(traceLinePos, targetPoint, GameWorld.CGWorldFrameHitFlags.HitTestLOS))
                return targetPoint;

            // Need to find alternative path - try multiple directions
            List<WorldLine> testLines = new List<WorldLine>();
            int angleStep = 15;

            // Try pitching up first
            for (int i = 1; i <= 3; ++i)
            {
                WoWPoint end = GetPointInDirection(traceLinePos, rayLength, neededFacing,
                    pitch + WoWMathHelper.DegreesToRadians(i * angleStep));
                testLines.Add(new WorldLine(traceLinePos, end));
            }

            // Try turning left/right
            for (int i = 1; i <= 3; ++i)
            {
                WoWPoint endLeft = GetPointInDirection(traceLinePos, rayLength,
                    neededFacing + WoWMathHelper.DegreesToRadians(i * -angleStep), pitch);
                testLines.Add(new WorldLine(traceLinePos, endLeft));

                WoWPoint endRight = GetPointInDirection(traceLinePos, rayLength,
                    neededFacing + WoWMathHelper.DegreesToRadians(i * angleStep), pitch);
                testLines.Add(new WorldLine(traceLinePos, endRight));
            }

            // Try more aggressive pitching up
            for (int i = 4; i <= 6; ++i)
            {
                WoWPoint end = GetPointInDirection(traceLinePos, rayLength, neededFacing,
                    pitch + WoWMathHelper.DegreesToRadians(i * angleStep));
                testLines.Add(new WorldLine(traceLinePos, end));
            }

            // Try wider turns
            for (int i = 4; i <= 8; ++i)
            {
                WoWPoint endLeft = GetPointInDirection(traceLinePos, rayLength,
                    neededFacing + WoWMathHelper.DegreesToRadians(i * -angleStep), pitch);
                testLines.Add(new WorldLine(traceLinePos, endLeft));

                WoWPoint endRight = GetPointInDirection(traceLinePos, rayLength,
                    neededFacing + WoWMathHelper.DegreesToRadians(i * angleStep), pitch);
                testLines.Add(new WorldLine(traceLinePos, endRight));
            }

            // Even more aggressive pitching
            for (int i = 7; i <= 10; ++i)
            {
                WoWPoint end = GetPointInDirection(traceLinePos, rayLength, neededFacing,
                    pitch + WoWMathHelper.DegreesToRadians(i * angleStep));
                testLines.Add(new WorldLine(traceLinePos, end));
            }

            // Mass trace to find clear path
            WorldLine[] linesArray = testLines.ToArray();
            bool[] hitResults;
            GameWorld.MassTraceLine(linesArray, GameWorld.CGWorldFrameHitFlags.HitTestLOS, out hitResults);

            for (int i = 0; i < hitResults.Length; ++i)
            {
                if (!hitResults[i])
                    return linesArray[i].End;
            }

            return WoWPoint.Empty;
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
        /// Clear cached path data
        /// </summary>
        public static void Clear()
        {
            _lastDestination = WoWPoint.Empty;
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
        /// Anti-stuck detection
        /// </summary>
        private static bool AntiStuck
        {
            get
            {
                LocalPlayer me = StyxWoW.Me;
                if (!me.IsMoving || me.MovementInfo.TimeMoved != 0U ||
                    (!me.IsFlying && (!me.IsSwimming || !me.HasAura("Sea Legs"))))
                    return false;

                Logging.WriteNavigator("[Flightor]: Unstuck attempt {0}", _unstuckAttempts);
                ++_unstuckAttempts;
                DoAntiStuck();
                return true;
            }
        }

        /// <summary>
        /// Perform anti-stuck maneuver
        /// </summary>
        public static void DoAntiStuck()
        {
            // S5.3: Reduced Thread.Sleep durations from 900ms total to ~400ms
            // Original HB had 200+100+200+100+300 = 900ms blocking time
            // Shorter durations still allow movement direction changes while being less disruptive
            WoWMovement.Move(WoWMovement.MovementDirection.Backwards);
            Thread.Sleep(100);
            WoWMovement.MoveStop(WoWMovement.MovementDirection.Backwards);
            Thread.Sleep(50);

            WoWMovement.Move(WoWMovement.MovementDirection.StrafeRight | WoWMovement.MovementDirection.JumpAscend);
            Thread.Sleep(100);
            WoWMovement.MoveStop(WoWMovement.MovementDirection.StrafeRight | WoWMovement.MovementDirection.JumpAscend);
            Thread.Sleep(50);

            WoWMovement.Move(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.StrafeLeft);
            Thread.Sleep(100);
            WoWMovement.MoveStop();
        }

        /// <summary>
        /// Flying mount helper - manages mounting/dismounting
        /// </summary>
        public static class MountHelper
        {
            private static readonly Random _random = new Random();

            /// <summary>
            /// Get best available flying mount spell
            /// </summary>
            private static WoWSpell FlyingMount
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

                    // Druid flight forms
                    if (me.Class == WoWClass.Druid)
                    {
                        if (SpellManager.HasSpell("Swift Flight Form"))
                            return SpellManager.Spells["Swift Flight Form"];
                        if (SpellManager.HasSpell("Flight Form"))
                            return SpellManager.Spells["Flight Form"];
                    }

                    // Configured flying mount
                    if (!string.IsNullOrEmpty(CharacterSettings.Instance.FlyingMountName))
                    {
                        var mount = Styx.Logic.MountHelper.FlyingMounts.FirstOrDefault(m =>
                            m.Name == CharacterSettings.Instance.FlyingMountName ||
                            m.CreatureId.ToString() == CharacterSettings.Instance.FlyingMountName ||
                            m.Name.ToLower().Contains(CharacterSettings.Instance.FlyingMountName.ToLower()));

                        if (mount != null)
                            return mount.CreatureSpell;

                        // Fallback to any flying mount
                        return Styx.Logic.MountHelper.FlyingMounts.FirstOrDefault()?.CreatureSpell;
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

                    // Can fly or on transport
                    return me.MovementInfo.CanFly || me.IsOnTransport;
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
                    SpellManager.Cast(flyingMount);

                    if (!quick)
                    {
                        StyxWoW.SleepForLagDuration();
                        Thread.Sleep((int)flyingMount.CastTime);
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

                // Druid forms
                if (me.HasAura("Swift Flight Form") ||
                    me.HasAura("Flight Form") ||
                    me.HasAura("Aquatic Form"))
                {
                    Lua.DoString("CancelShapeshiftForm()");
                    StyxWoW.SleepForLagDuration();
                }
                else
                {
                    Lua.DoString("Dismount()");
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

                    if (me.HasAura("Swift Flight Form") ||
                        me.HasAura("Flight Form") ||
                        me.HasAura("Aquatic Form"))
                    {
                        Lua.DoString("CancelShapeshiftForm()");
                        Thread.Sleep(250);
                        return RunStatus.Success;
                    }

                    Lua.DoString("Dismount()");
                    return RunStatus.Success;
                }
            }
        }
    }
}
