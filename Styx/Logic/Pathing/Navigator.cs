using System;
using System.Collections.Generic;
using System.Numerics;
using Styx.Helpers;
using Styx.Logic.Pathing.Interop;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TripperNav = Tripper.Navigation;

namespace Styx.Logic.Pathing
{
	public static class Navigator
	{
		private static WoWPoint _destination = WoWPoint.Zero;
		private static readonly List<WoWPoint> _currentPath = new List<WoWPoint>();
		private static int _currentPathIndex;
		private static TripperNav.Navigator? _navigator;
		private static IMover? _playerMover;

		public static float PathPrecision { get; set; } = 1.6f;
		public static int LoadTilesAroundRadius { get; set; } = 2;
		public static float FlyingMountHeight { get; set; } = 25f;

		/// <summary>
		/// Gets or sets the player mover used for movement control.
		/// </summary>
		public static IMover PlayerMover
		{
			get
			{
				_playerMover ??= new LocalPlayerMover();
				return _playerMover;
			}
			set => _playerMover = value;
		}

		public static WoWPoint Destination => _destination;

		public static List<WoWPoint> CurrentPath => _currentPath;

		public static bool AtLocation => _destination != WoWPoint.Zero &&
			ObjectManager.Me != null &&
			ObjectManager.Me.Location.Distance(_destination) < PathPrecision;

		/// <summary>
		/// Gets the current map ID from the local player.
		/// </summary>
		private static uint GetCurrentMapId()
		{
			LocalPlayer? me = ObjectManager.Me;
			return me?.MapId ?? 0;
		}

		/// <summary>
		/// Gets or creates the Tripper navigator instance.
		/// </summary>
		public static TripperNav.Navigator TripperNavigator
		{
			get
			{
				if (_navigator == null)
				{
					_navigator = new TripperNav.Navigator();
					_navigator.LogMessage += msg => Logging.WriteDebug("[Tripper] {0}", msg);
				}
				return _navigator;
			}
		}

		/// <summary>
		/// Indicates if navigation meshes are loaded and ready.
		/// </summary>
		public static bool IsNavigatorLoaded => _navigator?.IsLoaded ?? false;

		static Navigator()
		{
			Logging.WriteDebug("[Navigator] Static constructor called - subscribing to events");
			BotEvents.Player.OnMapChanged += OnMapChanged;
			BotEvents.OnBotStart += OnBotStart;
			BotEvents.OnBotStop += OnBotStop;
		}

		private static void OnMapChanged(BotEvents.Player.MapChangedEventArgs args)
		{
			Clear();
			Logging.WriteDebug("[Navigator] Map changed. Path cleared.");
		}

		private static void OnBotStart(EventArgs args)
		{
			Logging.WriteDebug("[Navigator] OnBotStart event received");
			Clear();
			// Load navigation meshes on bot start
			if (_navigator == null)
			{
				Logging.WriteDebug("[Navigator] Creating new Tripper.Navigator instance");
				_navigator = new TripperNav.Navigator();
				_navigator.LogMessage += msg => Logging.WriteDebug("[Tripper] {0}", msg);
			}
			if (!_navigator.IsLoaded)
			{
				Logging.Write("[Navigator] Loading navigation meshes...");
				try
				{
					if (_navigator.LoadMeshes())
					{
						Logging.Write("[Navigator] Navigation meshes loaded successfully.");
					}
					else
					{
						Logging.Write(LogLevel.Quiet, "[Navigator] Failed to load navigation meshes. Using direct movement.");
					}
				}
				catch (Exception ex)
				{
					Logging.Write(LogLevel.Quiet, "[Navigator] Exception loading navigation meshes: {0}", ex.Message);
				}
			}
			else
			{
				Logging.WriteDebug("[Navigator] Navigation meshes already loaded");
			}
		}

		private static void OnBotStop(EventArgs args)
		{
			Clear();
		}

		public static void Clear()
		{
			_destination = WoWPoint.Zero;
			_currentPath.Clear();
			_currentPathIndex = 0;
			WoWMovement.MoveStop();
		}

		public static MoveResult MoveTo(WoWPoint destination)
		{
			return MoveTo(destination, "Navigation");
		}

		public static MoveResult MoveTo(WoWPoint destination, string destinationName)
		{
			return MoveTo(destination, PathPrecision, destinationName);
		}

		public static MoveResult MoveTo(WoWPoint destination, float precision)
		{
			return MoveTo(destination, precision, "Navigation");
		}

		public static MoveResult MoveTo(WoWPoint destination, float precision, string destinationName)
		{
			if (destination == WoWPoint.Zero)
				return MoveResult.Failed;

			LocalPlayer? me = ObjectManager.Me;
			if (me == null)
				return MoveResult.Failed;

			// Check if we're already at the destination
			float distance = me.Location.Distance(destination);
			if (distance < precision)
			{
				_destination = WoWPoint.Zero;
				_currentPath.Clear();
				return MoveResult.ReachedDestination;
			}

			// If destination changed, generate new path
			if (_destination != destination)
			{
				_destination = destination;
				_currentPath.Clear();

				// Try to use Tripper for pathfinding
				if (IsNavigatorLoaded)
				{
					uint mapId = (uint)(GetCurrentMapId());
					var start = new Vector3(me.Location.X, me.Location.Y, me.Location.Z);
					var end = new Vector3(destination.X, destination.Y, destination.Z);

					var result = TripperNavigator.FindPath(mapId, start, end, true);
					if (result.Status.Succeeded && result.Points != null && result.Points.Length > 0)
					{
						foreach (var point in result.Points)
						{
							_currentPath.Add(new WoWPoint(point.X, point.Y, point.Z));
						}
						Logging.WriteDebug("[Navigator] Path generated with {0} points to: {1} ({2})", 
							_currentPath.Count, destinationName, destination);
					}
					else
					{
						// Fallback to direct movement
						_currentPath.Add(me.Location);
						_currentPath.Add(destination);
						Logging.WriteDebug("[Navigator] Pathfinding failed, using direct movement to: {0} ({1})", 
							destinationName, destination);
					}
				}
				else
				{
					// No navmesh, use direct path
					_currentPath.Add(me.Location);
					_currentPath.Add(destination);
					Logging.WriteDebug("[Navigator] Moving directly to: {0} ({1})", destinationName, destination);
				}

				_currentPathIndex = 0;
			}

			// Move along path
			// Use smaller precision for intermediate waypoints, larger for final destination
			if (_currentPath.Count > 0 && _currentPathIndex < _currentPath.Count)
			{
				WoWPoint nextPoint = _currentPath[_currentPathIndex];
				
				// For intermediate waypoints, use PathPrecision (1.6)
				// For final waypoint, use requested precision
				bool isFinalPoint = (_currentPathIndex == _currentPath.Count - 1);
				float waypointPrecision = isFinalPoint ? precision : PathPrecision;

				if (me.Location.Distance(nextPoint) < waypointPrecision)
				{
					_currentPathIndex++;
					if (_currentPathIndex >= _currentPath.Count)
					{
						return MoveResult.ReachedDestination;
					}
					nextPoint = _currentPath[_currentPathIndex];
				}

				// HB 3.3.5a: Simply call ClickToMove - anti-spam is handled there
				// WoWMovement.ClickToMove already checks if we're moving to the same destination
				WoWMovement.ClickToMove(nextPoint);
				
				return MoveResult.Moved;
			}

			return MoveResult.PathGenerationFailed;
		}

		public static bool CanNavigateFully(WoWPoint start, WoWPoint destination)
		{
			if (!IsNavigatorLoaded)
				return true; // Assume we can if no mesh loaded

			uint mapId = (uint)(GetCurrentMapId());
			var startVec = new Vector3(start.X, start.Y, start.Z);
			var endVec = new Vector3(destination.X, destination.Y, destination.Z);

			var result = TripperNavigator.FindPath(mapId, startVec, endVec, true);
			
			// Check if path is complete (reached destination)
			if (result.Status.Succeeded && result.Points != null && result.Points.Length > 0)
			{
				var lastPoint = result.Points[^1];
				float distToEnd = Vector3.Distance(lastPoint, endVec);
				return distToEnd < PathPrecision;
			}

			return false;
		}

		public static bool CanNavigateFully(WoWPoint destination)
		{
			LocalPlayer? me = ObjectManager.Me;
			if (me == null) return false;
			return CanNavigateFully(me.Location, destination);
		}

		public static WoWPoint[]? GeneratePath(WoWPoint start, WoWPoint destination)
		{
			if (!IsNavigatorLoaded)
				return null;

			uint mapId = (uint)(GetCurrentMapId());
			var startVec = new Vector3(start.X, start.Y, start.Z);
			var endVec = new Vector3(destination.X, destination.Y, destination.Z);

			var result = TripperNavigator.FindPath(mapId, startVec, endVec, true);
			if (result.Status.Succeeded && result.Points != null && result.Points.Length > 0)
			{
				var path = new WoWPoint[result.Points.Length];
				for (int i = 0; i < result.Points.Length; i++)
				{
					var point = result.Points[i];
					path[i] = new WoWPoint(point.X, point.Y, point.Z);
				}
				return path;
			}

			return null;
		}

		public static bool IsPathSafe(IList<WoWPoint> path)
		{
			if (path == null || path.Count < 2)
				return false;

			if (!IsNavigatorLoaded)
				return true; // Assume safe if no mesh

			uint mapId = (uint)(GetCurrentMapId());

			// Check each segment of the path with raycast
			for (int i = 0; i < path.Count - 1; i++)
			{
				var start = new Vector3(path[i].X, path[i].Y, path[i].Z);
				var end = new Vector3(path[i + 1].X, path[i + 1].Y, path[i + 1].Z);

				if (TripperNavigator.Raycast(mapId, start, end, out _, out float hitDistance))
				{
					// Hit something before reaching next waypoint
					if (hitDistance < 0.99f)
						return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Finds the mesh height at a given XY position.
		/// </summary>
		/// <param name="x">X coordinate.</param>
		/// <param name="y">Y coordinate.</param>
		/// <param name="z">Output Z coordinate (height).</param>
		/// <returns>True if a valid height was found.</returns>
		public static bool FindMeshHeight(float x, float y, out float z)
		{
			z = 0f;

			if (!IsNavigatorLoaded)
				return false;

			uint mapId = (uint)(GetCurrentMapId());
			var position = new Vector3(x, y, 10000f); // Start from high up

			if (TripperNavigator.FindNearestPoint(mapId, position, out Vector3 nearestPoint))
			{
				z = nearestPoint.Z;
				return true;
			}

			return false;
		}

		/// <summary>
		/// Finds the mesh height at a given position.
		/// </summary>
		/// <param name="pos">The position to check (Z will be modified).</param>
		/// <returns>True if a valid height was found.</returns>
		public static bool FindMeshHeight(ref Tripper.XNAMath.Vector3 pos)
		{
			if (FindMeshHeight(pos.X, pos.Y, out float z))
			{
				pos.Z = z;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Finds the mesh height at a given position (alias for FindMeshHeight).
		/// </summary>
		/// <param name="v">The position to check (Z will be modified).</param>
		/// <returns>True if a valid height was found.</returns>
		public static bool FindHeight(ref Tripper.XNAMath.Vector3 v)
		{
			return FindMeshHeight(ref v);
		}

		/// <summary>
		/// Finds the mesh height at a given XY position.
		/// </summary>
		/// <param name="x">X coordinate.</param>
		/// <param name="y">Y coordinate.</param>
		/// <param name="z">Output Z coordinate (height).</param>
		/// <returns>True if a valid height was found.</returns>
		public static bool FindHeight(float x, float y, out float z)
		{
			return FindMeshHeight(x, y, out z);
		}

		/// <summary>
		/// Finds all mesh heights at a given XY position.
		/// </summary>
		/// <param name="x">X coordinate.</param>
		/// <param name="y">Y coordinate.</param>
		/// <returns>List of heights found at the position.</returns>
		public static List<float> FindHeights(float x, float y)
		{
			var heights = new List<float>();

			if (!IsNavigatorLoaded)
				return heights;

			// FindNearestPoint returns single height - for multi-level we'd need additional API
			if (FindMeshHeight(x, y, out float z))
			{
				heights.Add(z);
			}

			return heights;
		}

		/// <summary>
		/// Finds a random navigable point within radius of center position.
		/// </summary>
		/// <param name="center">Center position.</param>
		/// <param name="radius">Search radius in yards.</param>
		/// <returns>Random navigable point, or center if none found.</returns>
		public static WoWPoint FindRandomPoint(WoWPoint center, float radius)
		{
			if (!IsNavigatorLoaded)
				return center;

			uint mapId = (uint)(GetCurrentMapId());
			var centerVec = new Vector3(center.X, center.Y, center.Z);

			if (TripperNavigator.FindRandomPoint(mapId, centerVec, radius, out Vector3 randomPoint))
			{
				return new WoWPoint(randomPoint.X, randomPoint.Y, randomPoint.Z);
			}

			return center;
		}

		/// <summary>
		/// Finds the nearest navigable point to a given position.
		/// </summary>
		/// <param name="position">Position to search from.</param>
		/// <returns>Nearest navigable point, or original position if none found.</returns>
		public static WoWPoint FindNearestPoint(WoWPoint position)
		{
			if (!IsNavigatorLoaded)
				return position;

			uint mapId = (uint)(GetCurrentMapId());
			var posVec = new Vector3(position.X, position.Y, position.Z);

			if (TripperNavigator.FindNearestPoint(mapId, posVec, out Vector3 nearestPoint))
			{
				return new WoWPoint(nearestPoint.X, nearestPoint.Y, nearestPoint.Z);
			}

			return position;
		}

		/// <summary>
		/// Performs a raycast from start to end position on the navmesh.
		/// </summary>
		/// <param name="start">Ray start position.</param>
		/// <param name="end">Ray end position.</param>
		/// <param name="hitPosition">Output hit position if raycast hits.</param>
		/// <returns>True if raycast hit a navmesh boundary (path is NOT clear).</returns>
		public static bool Raycast(WoWPoint start, WoWPoint end, out WoWPoint hitPosition)
		{
			hitPosition = end;

			if (!IsNavigatorLoaded)
				return false;

			uint mapId = (uint)(GetCurrentMapId());
			var startVec = new Vector3(start.X, start.Y, start.Z);
			var endVec = new Vector3(end.X, end.Y, end.Z);

			if (TripperNavigator.Raycast(mapId, startVec, endVec, out Vector3 hit, out _))
			{
				hitPosition = new WoWPoint(hit.X, hit.Y, hit.Z);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Disposes the navigator and releases resources.
		/// </summary>
		public static void Dispose()
		{
			if (_navigator != null)
			{
				_navigator.Dispose();
				_navigator = null;
			}
		}

		/// <summary>
		/// Converts a MoveResult to a TreeSharp RunStatus.
		/// </summary>
		public static TreeSharp.RunStatus GetRunStatusFromMoveResult(MoveResult moveResult)
		{
			switch (moveResult)
			{
				case MoveResult.Failed:
				case MoveResult.PathGenerationFailed:
					return TreeSharp.RunStatus.Failure;
				case MoveResult.ReachedDestination:
				case MoveResult.PathGenerated:
				case MoveResult.UnstuckAttempt:
				case MoveResult.Moved:
					return TreeSharp.RunStatus.Success;
				default:
					throw new ArgumentOutOfRangeException(nameof(moveResult));
			}
		}
	}
}
