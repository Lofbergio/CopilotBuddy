using System;
using System.Threading;
using Styx.Helpers;
using Styx.WoWInternals;
using Tripper.XNAMath;

namespace Styx.Logic.Pathing.Interop
{
    /// <summary>
    /// Movement directions for player movement control.
    /// </summary>
    [Flags]
    public enum MoveDirection
    {
        None = 0,
        Forward = 1,
        Backwards = 2,
        StrafeLeft = 4,
        StrafeRight = 8,
        TurnLeft = 16,
        TurnRight = 32
    }

    /// <summary>
    /// Interface for player movement control.
    /// </summary>
    public interface IMover
    {
        void MoveTowards(Vector3 point);
        void MoveInDirection(MoveDirection direction, bool start);
        void StopMoving();
        void MoveStop();
        void SetFacing(float facing);
        void PerformJump();
        Vector3 Location { get; }
        float Facing { get; }
        bool IsStuck { get; }
    }

    /// <summary>
    /// Implements player movement control via WoW API calls.
    /// </summary>
    public class LocalPlayerMover : IMover
    {
        /// <summary>
        /// Moves the player towards a specific point using click-to-move.
        /// </summary>
        public void MoveTowards(Vector3 point)
        {
            if (ObjectManager.Me == null)
                return;

            WoWPoint targetPoint = new WoWPoint(point.X, point.Y, point.Z);
            
            // Limit click distance to avoid clicking through walls
            // Like HB 3.3.5a, click on intermediate point if target is far
            float distance = ObjectManager.Me.Location.Distance(targetPoint);
            if (distance > 10f)
            {
                // Click on a point 8 yards away in the direction of target
                Vector3 direction = point - new Vector3(ObjectManager.Me.Location.X, ObjectManager.Me.Location.Y, ObjectManager.Me.Location.Z);
                direction.Normalize();
                targetPoint = new WoWPoint(
                    ObjectManager.Me.Location.X + direction.X * 8f,
                    ObjectManager.Me.Location.Y + direction.Y * 8f,
                    ObjectManager.Me.Location.Z + direction.Z * 8f
                );
            }

            WoWMovement.ClickToMove(targetPoint);
        }

        /// <summary>
        /// Moves the player in a specific direction (strafe, turn, etc).
        /// </summary>
        public void MoveInDirection(MoveDirection direction, bool start)
        {
            WoWMovement.MovementDirection movementDir = ConvertDirection(direction);

            if (start)
                WoWMovement.Move(movementDir);
            else
                WoWMovement.MoveStop();
        }

        /// <summary>
        /// Stops all player movement.
        /// </summary>
        public void StopMoving()
        {
            WoWMovement.MoveStop();
        }

        /// <summary>
        /// Stops all player movement (alias for StopMoving).
        /// </summary>
        public void MoveStop()
        {
            WoWMovement.MoveStop();
        }

		/// <summary>
		/// Sets the player's facing direction.
		/// </summary>
		public void SetFacing(float facing)
		{
			ObjectManager.Me?.SetFacing(facing);
		}

		/// <summary>
		/// Performs a jump action, waiting for the fall to complete.
		/// </summary>
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

		/// <summary>
		/// Gets the player's current location.
		/// </summary>
		public Vector3 Location
		{
			get
			{
				var me = ObjectManager.Me;
				if (me == null)
					return Vector3.Zero;
				
				var loc = me.Location;
				return new Vector3(loc.X, loc.Y, loc.Z);
			}
		}

		/// <summary>
		/// Gets the player's current facing angle.
		/// </summary>
		public float Facing => ObjectManager.Me?.Rotation ?? 0f;

		/// <summary>
		/// Gets a value indicating whether the player is stuck (not moving).
		/// </summary>
		public bool IsStuck => StuckDetector.IsStuck;

		/// <summary>
		/// Converts a MoveDirection enum to WoWMovement.MovementDirection.
		/// </summary>
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
