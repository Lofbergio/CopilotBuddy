using System;
using System.Collections.Generic;
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
    /// Stuck detection: HB 4.3.4 Class485.method_0() — WaitTimer(2s) + straight-line RunSpeed/2 threshold.
    /// Unstick sequence: Dismount → Jump(1x, LOS-gated) → StrafeFwdL → StrafeFwdR → Dismount2 → StrafeL → StrafeR → Blackspot+Reverse
    /// </summary>
    internal class DefaultStuckHandler : StuckHandler
    {
        private const float UnstickResetDistanceSqr = 100f;
        private const float DismountRaycastDistance = 3f;
        private const float JumpRaycastDistance = 2f;

        private readonly WaitTimer _mountUpBlockTimer = new WaitTimer(TimeSpan.FromSeconds(10.0));
        private readonly WaitTimer _stuckCheckTimer = new WaitTimer(TimeSpan.FromSeconds(2.0));

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

        public DefaultStuckHandler()
        {
            _lastCheckLocation = WoWPoint.Empty;
            _stuckCheckTimer.Reset();
        }

        public override void OnSetAsCurrent()
        {
            if (_isCurrent)
                return;

            Mount.OnMountUp += OnMountUp;
            WoWMovement.OnMovementFlagsChanged += OnMovementFlagsChanged;
            _isCurrent = true;
        }

        public override void OnRemoveAsCurrent()
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
            // HB 4.3.4 Class485: no timer reset on movement stop — the 2-second
            // stuck check timer runs continuously and independently of movement events.
            // Resetting it here would prevent detection during waypoint-to-waypoint navigation.
        }

        public override bool IsStuck()
        {
            var me = ObjectManager.Me;
            if (me == null)
                return false;

            // HB 4.3.4 Class485.IsStuck(): never fire stuck detection during combat —
            // ranged classes (Balance Druid, Mage, etc.) stand still intentionally.
            if (me.Combat)
                return false;

            if (me.Stunned || me.Fleeing || me.Dazed || me.Rooted)
            {
                Reset();
                return false;
            }

            WoWUnit? activeMover = WoWMovement.ActiveMover;
            if (activeMover == null || !activeMover.IsMe)
                return false;

            // HB 4.3.4 Class485.method_0(): check only every 2 seconds.
            if (!_stuckCheckTimer.IsFinished)
                return false;

            _stuckCheckTimer.Reset();

            WoWPoint currentLocation = activeMover.Location;
            if (_lastCheckLocation == WoWPoint.Empty)
            {
                _lastCheckLocation = currentLocation;
                return false;
            }

            // Straight-line distance — no PathDistance / navmesh query (HB 4.3.4 exact pattern).
            // Threshold: RunSpeed / 2 ≈ 3.5 yards in 2 seconds at base run speed.
            bool movedEnough = currentLocation.Distance(_lastCheckLocation) > activeMover.MovementInfo.RunSpeed / 2f;
            if (movedEnough)
            {
                _lastCheckLocation = currentLocation;
                return false;
            }

            Logging.WriteVerbose(Colors.Red, "We are stuck! (TPS: {0:F1}, Latency: {1}, loc: {2})!",
                GameStats.TicksPerSecond,
                StyxWoW.WoWClient.Latency,
                currentLocation);
            LogNearestGameObject();
            // HB 4.3.4: do NOT update _lastCheckLocation when stuck.
            // Next 2-second tick will still compare from the same reference point,
            // keeping stuck detection active until Unstick() actually moves the bot.
            return true;
        }

        public override void Unstick()
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

            _stuckCheckTimer.Reset();
        }

        public override void Reset()
        {
            _stuckCheckTimer.Reset();
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

        private void MoveInDirection(WoWMovement.MovementDirection direction, int milliseconds)
        {
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



    }
}
