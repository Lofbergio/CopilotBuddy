// KeyboardMover.cs — Alternative IMover using SetFacing + MoveForward pattern
// P6.11: Fallback for situations where ClickToMove is unreliable (e.g. elevators, slopes)
// Based on HB 6.2.3 KeyboardMover (Styx.Pathing.KeyboardMover)
// Usage: Navigator.PlayerMover = new KeyboardMover();

using System;
using System.Threading;
using Styx.Helpers;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Tripper.XNAMath;

namespace Styx.Logic.Pathing.Interop
{
	/// <summary>
	/// Keyboard-based player mover — uses SetFacing + MoveForward instead of ClickToMove.
	/// Useful as a fallback when CTM is unreliable (elevators, steep terrain, narrow pathways).
	/// HB 6.2.3 pattern: SetFacing toward target, then MoveForwardStart.
	/// </summary>
	public class KeyboardMover : IMover
	{
		private float _lastRotation;

		/// <summary>
		/// Moves toward the target point by adjusting facing and pressing MoveForward.
		/// HB 6.2.3 pattern: smooth facing adjustment with proportional turn speed.
		/// </summary>
		public void MoveTowards(Vector3 point)
		{
			LocalPlayer? me = ObjectManager.Me;
			if (me == null) return;

			WoWPoint target = new WoWPoint(point.X, point.Y, point.Z);
			WoWPoint myLoc = me.Location;

			// If already facing the target, just move forward
			if (WoWMathHelper.IsFacing(myLoc, me.Rotation, target, 2f))
			{
				WoWMovement.Move(WoWMovement.MovementDirection.Forward);
			}
			else
			{
				WoWMovement.MoveStop();
			}

			// Calculate needed facing and smoothly adjust
			float neededFacing = WoWMathHelper.CalculateNeededFacing(myLoc, target);
			float rotation = me.Rotation;
			_lastRotation = (rotation != 0f) ? rotation : _lastRotation;

			float angleDiff = GetAngleDifference(neededFacing);
			float turnStep = Math.Abs(angleDiff) / 6f;
			if (turnStep < 0.1f)
				turnStep = Math.Abs(angleDiff);

			float stepLimit = turnStep + 0.05f;

			// Turn left or right toward target
			if (angleDiff < 0f)
			{
				// Turn right (decrease rotation)
				bool turning = true;
				while (turning)
				{
					me.SetFacing(me.Rotation - turnStep);
					angleDiff = GetAngleDifference(target);
					if ((stepLimit - angleDiff) < stepLimit + 0.1f || (angleDiff > 0f && angleDiff > stepLimit))
						turning = false;
				}
			}
			else if (angleDiff > 0f)
			{
				// Turn left (increase rotation)
				bool turning = true;
				while (turning)
				{
					me.SetFacing(me.Rotation + turnStep);
					angleDiff = GetAngleDifference(target);
					if ((angleDiff - stepLimit) < 0.1f || (angleDiff < 0f && angleDiff < -stepLimit))
						turning = false;
				}
			}
		}

		/// <summary>
		/// Calculates the signed angle difference between current facing and the target point.
		/// Returns negative for right turn, positive for left turn.
		/// </summary>
		private float GetAngleDifference(WoWPoint target)
		{
			LocalPlayer? me = ObjectManager.Me;
			if (me == null) return 0f;

			float neededFacing = WoWMathHelper.CalculateNeededFacing(me.Location, target);
			float rotation = me.Rotation;
			_lastRotation = (rotation != 0f) ? rotation : _lastRotation;

			const float PI = 3.14159274f;
			float diff = neededFacing - _lastRotation;
			if (diff > PI)
				diff = -(PI - (diff - PI));
			else if (diff < -PI)
				diff = PI - (-PI - diff);

			return diff;
		}

		/// <summary>
		/// Overload for initial angle difference calculation using the needed facing value.
		/// </summary>
		private float GetAngleDifference(float neededFacing)
		{
			const float PI = 3.14159274f;
			float diff = neededFacing - _lastRotation;
			if (diff > PI)
				diff = -(PI - (diff - PI));
			else if (diff < -PI)
				diff = PI - (-PI - diff);

			return diff;
		}

		/// <inheritdoc />
		public void MoveInDirection(MoveDirection direction, bool start)
		{
			WoWMovement.MovementDirection movementDir = ConvertDirection(direction);
			if (start)
				WoWMovement.Move(movementDir);
			else
				WoWMovement.MoveStop();
		}

		/// <inheritdoc />
		public void StopMoving() => WoWMovement.MoveStop();

		/// <inheritdoc />
		public void MoveStop() => WoWMovement.MoveStop();

		/// <inheritdoc />
		public void SetFacing(float facing)
		{
			ObjectManager.Me?.SetFacing(facing);
		}

		/// <inheritdoc />
		public void PerformJump()
		{
			Lua.DoString("JumpOrAscendStart()");
			Thread.Sleep(50);
			Lua.DoString("AscendStop()");

			int startTick = Environment.TickCount;
			while (ObjectManager.Me?.IsFalling == true && Environment.TickCount - startTick < 2500)
			{
				Thread.Sleep(50);
			}
		}

		/// <inheritdoc />
		public Vector3 Location
		{
			get
			{
				var me = ObjectManager.Me;
				if (me == null) return Vector3.Zero;
				var loc = me.Location;
				return new Vector3(loc.X, loc.Y, loc.Z);
			}
		}

		/// <inheritdoc />
		public float Facing => ObjectManager.Me?.Rotation ?? 0f;

		/// <inheritdoc />
		public bool IsStuck => StuckDetector.IsStuck;

		private static WoWMovement.MovementDirection ConvertDirection(MoveDirection direction)
		{
			WoWMovement.MovementDirection result = WoWMovement.MovementDirection.None;
			if ((direction & MoveDirection.Forward) != 0)
				result |= WoWMovement.MovementDirection.Forward;
			if ((direction & MoveDirection.Backwards) != 0)
				result |= WoWMovement.MovementDirection.Backwards;
			if ((direction & MoveDirection.StrafeLeft) != 0)
				result |= WoWMovement.MovementDirection.StrafeLeft;
			if ((direction & MoveDirection.StrafeRight) != 0)
				result |= WoWMovement.MovementDirection.StrafeRight;
			if ((direction & MoveDirection.TurnLeft) != 0)
				result |= WoWMovement.MovementDirection.TurnLeft;
			if ((direction & MoveDirection.TurnRight) != 0)
				result |= WoWMovement.MovementDirection.TurnRight;
			return result;
		}
	}
}
