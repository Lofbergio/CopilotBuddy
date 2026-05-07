using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using Styx.CommonBot;
using Styx.Helpers;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.World;

namespace Styx.Logic.Pathing
{
    /// <summary>
    /// Handles stuck detection and recovery for WoW 3.3.5a.
    /// Direct port of HB 6.2.3 Class469 (MeshNavigator stuck handler).
    /// Sequence: Dismount → Jump(1x, LOS-gated) → StrafeFwdL → StrafeFwdR → Dismount2 → StrafeL → StrafeR → Blackspot+Reverse
    /// </summary>
    internal class StuckHandler : IStuckHandler
    {
        private const float UnstickResetDistanceSqr = 100f;
        private const float DismountRaycastDistance = 3f;
        private const float JumpRaycastDistance = 2f;
        private const float ExpectedDistanceScale = 0.6f;

        private readonly WaitTimer _mountUpBlockTimer = new WaitTimer(TimeSpan.FromSeconds(10.0));
        private readonly Stopwatch _movementStopwatch = new Stopwatch();

        private WoWPoint _lastCheckLocation = WoWPoint.Empty;
        private WoWPoint _lastUnstickLocation = WoWPoint.Empty;
        private long _unstickAttemptCount = 1;

        private bool _triedDismount;
        private bool _triedJump;
        private bool _triedStrafeForwardLeft;
        private bool _triedStrafeForwardRight;
        private bool _triedStrafeLeft;
        private bool _triedStrafeRight;
        private bool _isCurrent;

        public StuckHandler()
        {
            _lastCheckLocation = WoWPoint.Empty;
            _movementStopwatch.Restart();
        }

        public void OnSetAsCurrent()
        {
            if (_isCurrent)
                return;

            Mount.OnMountUp += OnMountUp;
            WoWMovement.OnMovementFlagsChanged += OnMovementFlagsChanged;
            _isCurrent = true;
        }

        public void OnRemoveAsCurrent()
        {
            if (!_isCurrent)
                return;

            WoWMovement.OnMovementFlagsChanged -= OnMovementFlagsChanged;
            Mount.OnMountUp -= OnMountUp;
            _isCurrent = false;
        }

        private void OnMountUp(object? sender, MountUpEventArgs e)
        {
            e.Cancel = !_mountUpBlockTimer.IsFinished;
        }

        private void OnMovementFlagsChanged(WoWMovement.MovementEventArgs e)
        {
            if (e.Stop)
            {
                _movementStopwatch.Restart();
                _lastCheckLocation = WoWPoint.Empty;
            }
        }

        public bool IsStuck()
        {
            var me = ObjectManager.Me;
            if (me == null)
                return false;

            if (me.Stunned || me.Fleeing || me.Dazed || me.Rooted)
            {
                Reset();
                return false;
            }

            WoWUnit? activeMover = WoWMovement.ActiveMover;
            if (activeMover == null || !activeMover.IsMe)
                return false;

            if (_movementStopwatch.ElapsedMilliseconds < 500L)
                return false;

            WoWPoint currentLocation = activeMover.Location;
            if (_lastCheckLocation != WoWPoint.Empty)
            {
                float expectedDistance = GetExpectedTravelDistance(activeMover, _movementStopwatch.Elapsed) * ExpectedDistanceScale;
                float? pathDistance = Navigator.PathDistance(_lastCheckLocation, currentLocation, expectedDistance);
                if (pathDistance != null && pathDistance < expectedDistance)
                {
                    Logging.WriteVerbose(Colors.Red, "We are stuck! (TPS: {0:F1}, Latency: {1}, loc: {2})!",
                        GameStats.TicksPerSecond,
                        StyxWoW.WoWClient.Latency,
                        currentLocation);
                    LogNearestGameObject();
                    // If a game object is physically blocking us (<2.5 yards), immediately blackspot it.
                    // AddBlackspot at the OBJECT location (not player location) so the next path
                    // regeneration routes around the specific obstacle, not just the stuck spot.
                    CheckAndBlackspotNearbyObject();
                    _lastCheckLocation = currentLocation;
                    return true;
                }
            }

            _movementStopwatch.Restart();
            _lastCheckLocation = currentLocation;
            return false;
        }

        public void Unstick()
        {
            var me = ObjectManager.Me;
            if (me == null)
                return;

            _mountUpBlockTimer.Reset();

            WoWPoint location = me.Location;
            if (_lastUnstickLocation.DistanceSqr(location) >= UnstickResetDistanceSqr)
            {
                ResetUnstickAttempts();
            }
            _lastUnstickLocation = location;

            int duration = (_unstickAttemptCount % 2L == 0L) ? 1000 : 600;
            _unstickAttemptCount++;

            float rotation = me.Rotation;

            // HB pattern: Dismount → Jump → Strafe sides → Blackspot
            if (!_triedDismount && me.Mounted)
            {
                WoWPoint forward = location.RayCast(rotation, DismountRaycastDistance).Add(0f, 0f, 1f);
                WoWPoint upper = location.Add(0f, 0f, me.BoundingHeight + 2f);
                WoWPoint middle = location.Add(0f, 0f, me.BoundingHeight / 2f);

                if (!GameWorld.IsInLineOfSight(upper, forward) || !GameWorld.IsInLineOfSight(middle, forward))
                {
                    Logging.WriteVerbose("Trying dismount");
                    Mount.Dismount("Stuck Handler");
                }
                _triedDismount = true;
            }
            else if (!_triedJump)
            {
                // HB 6.2.3 Class469: ONE jump attempt, only if forward path is clear (LOS check).
                // If blocked ahead, jumping won't help — skip straight to strafe.
                WoWPoint fwd = location.RayCast(rotation, JumpRaycastDistance).Add(0f, 0f, 2f);
                WoWPoint src = location.Add(0f, 0f, 2f);

                if (GameWorld.IsInLineOfSight(src, fwd))
                {
                    Logging.WriteVerbose("Trying jump");
                    WoWMovement.Move(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                    StyxWoW.Sleep(100);
                    WoWMovement.MoveStop(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.JumpAscend);
                    StyxWoW.Sleep(200);
                }
                _triedJump = true;
            }
            else if (!_triedStrafeForwardLeft)
            {
                Logging.WriteVerbose("Trying strafe forward left for {0}ms", duration);
                MoveInDirection(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.StrafeLeft, duration);
                _triedStrafeForwardLeft = true;
            }
            else if (!_triedStrafeForwardRight)
            {
                Logging.WriteVerbose("Trying strafe forward right for {0}ms", duration);
                MoveInDirection(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.StrafeRight, duration);
                _triedStrafeForwardRight = true;
            }
            else if (me.Mounted)
            {
                Logging.WriteVerbose("Trying dismount");
                Mount.Dismount("Stuck Handler");
            }
            else if (!_triedStrafeLeft)
            {
                Logging.WriteVerbose("Trying strafe left for {0}ms", duration);
                MoveInDirection(WoWMovement.MovementDirection.StrafeLeft, duration);
                _triedStrafeLeft = true;
            }
            else if (!_triedStrafeRight)
            {
                Logging.WriteVerbose("Trying strafe right for {0}ms", duration);
                MoveInDirection(WoWMovement.MovementDirection.StrafeRight, duration);
                _triedStrafeRight = true;
            }
            else
            {
                AddBlackspotAndReverse(duration * 2);
                ResetUnstickAttempts();
            }

            _movementStopwatch.Restart();
        }

        public void Reset()
        {
            _movementStopwatch.Restart();
            _lastCheckLocation = WoWPoint.Empty;
            ResetUnstickAttempts();
        }

        private void ResetUnstickAttempts()
        {
            _triedDismount = false;
            _triedJump = false;
            _triedStrafeForwardLeft = false;
            _triedStrafeForwardRight = false;
            _triedStrafeLeft = false;
            _triedStrafeRight = false;
        }

        /// <summary>
        /// HB 6.2.3 pattern: CTMStop → Move → Sleep(duration) → MoveStop → Sleep(200).
        /// ClickToMoveStop() must precede keyboard movement to cancel any pending CTM;
        /// without it, WoW's CTM state machine keeps driving the character toward the
        /// old click target during the strafe, leaving the character facing sideways.
        /// </summary>
        private void MoveInDirection(WoWMovement.MovementDirection direction, int milliseconds)
        {
            WoWMovement.ClickToMoveStop(); // Cancel pending CTM before keyboard movement
            WoWMovement.Move(direction);
            StyxWoW.Sleep(milliseconds);
            WoWMovement.MoveStop(direction);
            StyxWoW.Sleep(200);
        }

        private void AddBlackspotAndReverse(int milliseconds)
        {
            var me = ObjectManager.Me;
            if (me == null)
                return;

            Logging.WriteVerbose("Adding blackspot at current location and backing off for {0}ms", milliseconds);
            BlackspotManager.AddBlackspot(me.Location, 5f, 3f, "StuckHandler");
            WoWMovement.MoveStop();
            StyxWoW.Sleep(100);
            WoWMovement.Move(WoWMovement.MovementDirection.Backwards);
            StyxWoW.Sleep(milliseconds);
            WoWMovement.MoveStop();
            StyxWoW.Sleep(200);

            Logging.WriteDebug("[STUCK] Clearing path to regenerate around blackspot.");
            Navigator.Clear();
        }

        private void LogNearestGameObject()
        {
            WoWGameObject? nearestGameObject = ObjectManager.GetObjectsOfType<WoWGameObject>()
                .Where(gameObject => gameObject.DistanceSqr < 2500.0)
                .OrderBy(gameObject => gameObject.DistanceSqr)
                .FirstOrDefault();

            if (nearestGameObject == null)
                return;

            Logging.WriteDiagnostic("Nearest game object, distance {0:F2} yards, entry {1}, type {2}, name {3}",
                nearestGameObject.Distance,
                nearestGameObject.Entry,
                nearestGameObject.SubType,
                nearestGameObject.Name);
        }

        // Types that have NO physical collision in WoW and must never be blackspotted.
        // Campfires (entry 2061) are WoWGameObjectType.Trap — purely decorative, zero collision.
        // Blackspotting them starves the navmesh until no path can be found at all.
        private static readonly HashSet<WoWGameObjectType> _nonCollidingTypes = new HashSet<WoWGameObjectType>
        {
            WoWGameObjectType.Trap,        // campfires, spell traps — no geometry
            WoWGameObjectType.AreaDamage,  // AoE ground effects
            WoWGameObjectType.DuelFlag,
            WoWGameObjectType.Ritual,
            WoWGameObjectType.SpellCaster,
            WoWGameObjectType.MeetingStone,
            WoWGameObjectType.FlagStand,
            WoWGameObjectType.FishingHole,
            WoWGameObjectType.FishingBobber,
            WoWGameObjectType.Text,
            WoWGameObjectType.SpellFocus,
            WoWGameObjectType.QuestGiver,
        };

        /// <summary>
        /// If a solid game object (Door) is within 2.5 yards when stuck, blackspot it.
        /// Non-colliding types (Trap/campfire, AreaDamage, etc.) are explicitly excluded —
        /// blackspotting them destroys the navmesh without fixing anything.
        /// </summary>
        private void CheckAndBlackspotNearbyObject()
        {
            const float blockingSearchSqr = 6.25f; // 2.5 yards squared
            var blocker = ObjectManager.GetObjectsOfType<WoWGameObject>()
                .Where(go => go.DistanceSqr < blockingSearchSqr && !_nonCollidingTypes.Contains(go.SubType))
                .OrderBy(go => go.DistanceSqr)
                .FirstOrDefault();

            if (blocker == null)
                return;

            Logging.WriteDebug("[STUCK] Object '{0}' (entry {1}, type {2}) at {3:F2} yards is blocking path — blackspotting at {4}.",
                blocker.Name, blocker.Entry, blocker.SubType, blocker.Distance, blocker.Location);
            BlackspotManager.AddBlackspot(blocker.Location, 2f, 3f);
        }

        private float GetExpectedTravelDistance(WoWUnit mover, TimeSpan timeSpan)
        {
            float speed = mover.MovementInfo.IsSwimming
                ? mover.MovementInfo.SwimSpeed
                : mover.MovementInfo.RunSpeed;
            return speed * (float)timeSpan.TotalSeconds;
        }


    }
}
