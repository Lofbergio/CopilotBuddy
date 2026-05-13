using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing.Interop;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.World;
using TripperNav = Tripper.Navigation;

namespace Styx.Logic.Pathing
{
	/// <summary>
	/// Concrete NavigationProvider that wraps Tripper.Navigator (Navigation.dll).
	/// Direct port of HB 6.2.3 MeshNavigator (Styx.Pathing.MeshNavigator).
	///
	/// Responsibilities (matching HB WoD):
	/// - Navmesh path generation (FindPath + EnsureTiles + Blackspot sync)
	/// - Path following with push-ahead (method_25/26)
	/// - Start-index skip (method_14)
	/// - Off-mesh connection dispatch (elevator, portal, interact, jump)
	/// - Door detection and interaction (method_7/8)
	/// - Stuck detection and recovery (Class469)
	/// - Drift detection (method_15)
	/// - Alive/ghost query filter (method_28)
	/// - PathPrecision-based waypoint advance (method_24/27)
	///
	/// Not here (stays in Navigator facade):
	/// - Flightor routing, mount/dismount, avoidance wiring, bot lifecycle
	/// </summary>
	public class MeshNavigator : NavigationProvider
	{
		#region Fields — path state (HB 6.2.3 MeshNavigator fields)

		// HB 6.2.3 bool_0 guard: prevents double-registration of event handlers.
		// OnSetAsCurrent throws InvalidOperationException if already registered.
		// OnRemoveAsCurrent throws if not registered. Prevents BotEvents.OnPulse double-hook.
		private bool _isCurrent;

		private WoWPoint _destination;
		private readonly List<WoWPoint> _currentPath = new List<WoWPoint>();

		private int _currentPathIndex;
		private TripperNav.StraightPathFlags[]? _currentFlags;
		private TripperNav.AreaType[]? _currentPolyTypes;
		private TripperNav.AbilityFlags[]? _currentAbilityFlags;
		private bool _isPartialPath;
		private bool _ridingElevator;

		// Path drift suppression after start-index skip
		private bool _suppressDriftCheck;
		private int _suppressDriftCheckIndex;

		// Push-ahead cache (HB 6.2.3 method_25/26)
		private int _cachedPushAheadIndex = -1;
		private WoWPoint _cachedClickPoint = WoWPoint.Zero;

		// Timers
		private WaitTimer _doorScanTimer = new WaitTimer(TimeSpan.FromSeconds(1));
		private WaitTimer _doorInteractTimer = new WaitTimer(TimeSpan.FromSeconds(1));
		private WaitTimer _pathRegenThrottle = new WaitTimer(TimeSpan.FromMilliseconds(500));
		private WaitTimer _interactTimer = new WaitTimer(TimeSpan.FromMilliseconds(2000));

		// Elevator motion detection (HB 6.2.3 bool_2, woWPoint_0, waitTimer_2)
		private bool _elevatorMoving;
		private WoWPoint _lastElevatorPos = WoWPoint.Zero;
		private WaitTimer _elevatorMotionTimer = new WaitTimer(TimeSpan.FromMilliseconds(400));

		// HB 6.2.3 areaType_0: faction area type for RaycastBlocked
		private TripperNav.AreaType _factionAreaType = TripperNav.AreaType.Ground;

		// Avoidance path (set by Navigator facade from NavAvoidWaypointProvider)
		private WoWPoint[]? _currentAvoidPath;
		private int _currentAvoidPathIndex;

		private StuckHandler _stuckHandler;

		#endregion

		#region Constructor

		public MeshNavigator()
		{
			PathPrecision = 2f;
			// HB 6.2.3: StuckHandler is assigned via base setter which checks IsCurrent.
			// At construction time IsCurrent is false → OnSetAsCurrent not called here.
			// It will be called later through NavigationProvider.OnSetAsCurrent() when
			// Navigator.NavigationProvider = this is executed.
			_stuckHandler = new DefaultStuckHandler();
		}

		#endregion

		#region NavigationProvider implementation

		/// <summary>HB 6.2.3 MeshNavigator.PathPrecision — default 2.0 yards.</summary>
		public override float PathPrecision { get; set; }

		/// <summary>
		/// HB 6.2.3 MeshNavigator.OnSetAsCurrent — hooks BotEvents.OnPulse for per-pulse
		/// faction update + tile streaming (Class1039.method_0 equivalent).
		/// Guard mirrors HB 6.2.3 bool_0: throws if already current.
		/// Source: .hb 6.2.3 MeshNavigator.OnSetAsCurrent
		/// </summary>
		public override void OnSetAsCurrent()
		{
			if (_isCurrent)
				throw new InvalidOperationException("This MeshNavigator instance is already in use");
			base.OnSetAsCurrent(); // StuckHandler.OnSetAsCurrent()
			BotEvents.OnPulse += OnPulse;
			Navigator.TripperNavigator.OnNavigatorLogMessage += OnNavigatorLog;
			_isCurrent = true;
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.OnRemoveAsCurrent — unhooks BotEvents.OnPulse.
		/// Guard mirrors HB 6.2.3 bool_0: throws if not currently registered.
		/// Source: .hb 6.2.3 MeshNavigator.OnRemoveAsCurrent
		/// </summary>
		public override void OnRemoveAsCurrent()
		{
			if (!_isCurrent)
				throw new InvalidOperationException("This MeshNavigator instance is not in use");
			BotEvents.OnPulse -= OnPulse;
			Navigator.TripperNavigator.OnNavigatorLogMessage -= OnNavigatorLog;
			base.OnRemoveAsCurrent(); // StuckHandler.OnRemoveAsCurrent()
			_isCurrent = false;
		}

		/// <summary>
		/// HB 6.2.3 method_1 bound to BotEvents.OnPulse:
		/// Updates faction area type + calls UpdateMaps() each pulse.
		/// </summary>
		private void OnPulse(object sender, EventArgs e)
		{
			var me = ObjectManager.Me;
			if (me != null)
				_factionAreaType = me.IsHorde ? TripperNav.AreaType.Horde : TripperNav.AreaType.Alliance;

			UpdateMaps();
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.UpdateMaps():
		/// Preloads navmesh tiles around player position each pulse (Class1039.method_0).
		/// </summary>
		public void UpdateMaps()
		{
			var me = ObjectManager.Me;
			if (me == null || !Navigator.IsNavigatorLoaded)
				return;

			uint mapId = Navigator.TripperNavigator.CurrentMapId;
			var pos = new Vector3(me.Location.X, me.Location.Y, me.Location.Z);
			try
			{
				TripperNavigator.EnsureTilesAroundPosition(mapId, pos, 1);
			}
			catch { }
		}

		/// <summary>HB 6.2.3 MeshNavigator.StuckHandler — Class469 instance.
		/// Lifecycle (OnSetAsCurrent/OnRemoveAsCurrent) is handled by the base class setter
		/// which guards on IsCurrent per HB NavigationProvider pattern.
		/// MeshNavigator uses _stuckHandler directly for Reset() calls.
		/// </summary>
		public override StuckHandler StuckHandler
		{
			get => _stuckHandler;
			set
			{
				if (ReferenceEquals(value, _stuckHandler))
					return;
				// HB pattern: only call lifecycle if this provider is current.
				if (IsCurrent)
				{
					value?.OnSetAsCurrent();
					_stuckHandler?.OnRemoveAsCurrent();
				}
				_stuckHandler = value;
			}
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.MoveTo — main navigation method.
		/// Generates path if needed, follows path with push-ahead, handles offmesh/doors/stuck.
		/// </summary>
		public override MoveResult MoveTo(WoWPoint destination)
		{
			return MoveTo(destination, PathPrecision, "Navigation");
		}

		public MoveResult MoveTo(WoWPoint destination, string destinationName)
		{
			return MoveTo(destination, PathPrecision, destinationName);
		}

		public MoveResult MoveTo(WoWPoint destination, float precision)
		{
			return MoveTo(destination, precision, "Navigation");
		}

		public MoveResult MoveTo(WoWPoint destination, float precision, string destinationName)
		{
			if (destination == WoWPoint.Zero)
				return MoveResult.Failed;

			if (float.IsNaN(destination.X) || float.IsNaN(destination.Y) || float.IsNaN(destination.Z))
				return MoveResult.Failed;

			LocalPlayer? me = ObjectManager.Me;
			if (me == null)
				return MoveResult.Failed;

			ApplyAliveQueryFilter(me.IsAlive);

			// Check if already at destination
			float distance = me.Location.Distance(destination);
			if (distance < precision)
			{
				_destination = WoWPoint.Zero;
				_currentPath.Clear();
				return MoveResult.ReachedDestination;
			}

			// HB 6.2.3 method_9: compare new destination against PATH ENDPOINT
			float destThresholdSqr = PathPrecision * PathPrecision;
			WoWPoint pathEndpoint = _currentPath.Count > 0 ? _currentPath[_currentPath.Count - 1] : _destination;
			bool destinationChanged = _destination == WoWPoint.Zero
				|| (pathEndpoint != WoWPoint.Zero
					? pathEndpoint.DistanceSqr(destination) > destThresholdSqr
					: _destination.DistanceSqr(destination) > destThresholdSqr);
			bool needsPathRegen = !destinationChanged && _currentPath.Count == 0;

			if (destinationChanged || needsPathRegen)
			{
				if (!_pathRegenThrottle.IsFinished)
					return MoveResult.Moved;

				_pathRegenThrottle.Reset();
				_destination = destination;
				_currentPath.Clear();

				if (destinationChanged)
				{
					try { StuckHandler.Reset(); } catch { }
					_ridingElevator = false;
				}

				if (!GeneratePathInternal(me, destination))
					return MoveResult.PathGenerationFailed;

				_currentPathIndex = 0;
				_suppressDriftCheck = false;
				_suppressDriftCheckIndex = 0;
				_cachedPushAheadIndex = -1;

				// Detour always outputs path[0] = the snapped start position, which is within
				// PathPrecision of the player. If we leave _currentPathIndex = 0, the waypoint
				// advance loop will immediately "reach" it on the first tick, call Reset(), and
				// wipe _tried* flags from any in-progress Unstick sequence.
				// Fix: skip the trivial start point when it's within PathPrecision of the player.
				if (_currentPath.Count > 1
				    && me.Location.Distance2DSqr(_currentPath[0]) <= PathPrecision * PathPrecision)
				{
					_currentPathIndex = 1;
				}

				// HB 6.2.3 method_14: skip waypoints the player has already passed
				SkipPassedWaypoints(me);

				if (_currentPathIndex > 0)
				{
					_suppressDriftCheck = true;
					_suppressDriftCheckIndex = _currentPathIndex;
				}
			}

			// Follow existing path
			if (_currentPath.Count > 0 && _currentPathIndex < _currentPath.Count)
			{
				// Off-mesh segment re-dispatch (HB 6.2.3 method_18)
				if (_currentPathIndex > 0 && _currentFlags != null
				    && (_currentPathIndex - 1) < _currentFlags.Length
				    && (_currentFlags[_currentPathIndex - 1] & TripperNav.StraightPathFlags.OffMeshConnection) != 0)
				{
					var offMeshAreaType = (_currentPolyTypes != null && (_currentPathIndex - 1) < _currentPolyTypes.Length)
						? _currentPolyTypes[_currentPathIndex - 1]
						: TripperNav.AreaType.Ground;

					WoWPoint offMeshEndPt = _currentPath[_currentPathIndex];
					WoWPoint offMeshStartPt = _currentPath[_currentPathIndex - 1];
					if (IsAtPoint(me.Location, offMeshEndPt) && offMeshAreaType != TripperNav.AreaType.Elevator)
					{
						_currentPathIndex++;
						_ridingElevator = false;
						if (_currentPathIndex >= _currentPath.Count)
							return _isPartialPath ? MoveResult.Failed : MoveResult.ReachedDestination;
						return MoveResult.Moved;
					}

					return DispatchOffMesh(me, offMeshEndPt, offMeshStartPt, offMeshAreaType);
				}

				// Door handling (HB 6.2.3 method_7)
				var doorResult = HandleDoors(me);
				if (doorResult != null)
					return doorResult.Value;

				// Stuck detection (HB 6.2.3 Class469)
				bool isAtOffMesh = _currentFlags != null && _currentPathIndex > 0
					&& (_currentPathIndex - 1) < _currentFlags.Length
					&& (_currentFlags[_currentPathIndex - 1] & TripperNav.StraightPathFlags.OffMeshConnection) != 0;
				// Stuck detection — exact HB 6.2.3 method_24: IsStuck() → Unstick() per tick.
				// IsStuck() manages its own 500ms throttle internally (stopwatch in StuckHandler).
				if (!isAtOffMesh && !_ridingElevator)
				{
					if (StuckHandler.IsStuck())
					{
						StuckHandler.Unstick();
						return MoveResult.UnstuckAttempt;
					}
				}

				// Avoidance path (HB 6.2.3 AvoidanceNavigationProvider pattern)
				if (Navigator.NavAvoidWaypointProvider != null)
				{
					bool hadAvoidPath = _currentAvoidPath != null;
					var avoidPoints = Navigator.NavAvoidWaypointProvider(_destination);
					if (avoidPoints != null && avoidPoints.Length > 0)
					{
						bool avoidEndpointChanged = _currentAvoidPath == null || _currentAvoidPath.Length == 0
							|| _currentAvoidPath[_currentAvoidPath.Length - 1].DistanceSqr(avoidPoints[avoidPoints.Length - 1]) > PathPrecision * PathPrecision;
						if (avoidEndpointChanged)
						{
							_currentAvoidPath = avoidPoints;
							_currentAvoidPathIndex = 0;
						}

						while (_currentAvoidPathIndex < _currentAvoidPath.Length
						       && me.Location.Distance2DSqr(_currentAvoidPath[_currentAvoidPathIndex]) <= PathPrecision * PathPrecision)
						{
							_currentAvoidPathIndex++;
						}

						if (_currentAvoidPathIndex < _currentAvoidPath.Length)
						{
							var avoidWp = _currentAvoidPath[_currentAvoidPathIndex];
							Navigator.PlayerMover.MoveTowards(avoidWp);
							return MoveResult.Moved;
						}

						_currentAvoidPath = null;
						_currentAvoidPathIndex = 0;
					}
					else
					{
						_currentAvoidPath = null;
						_currentAvoidPathIndex = 0;

						if (hadAvoidPath)
						{
							var refreshedPath = Navigator.ComputeRawPath(me.Location, _destination);
							if (refreshedPath == null || refreshedPath.Length == 0)
								return MoveResult.PathGenerationFailed;

							OverrideCurrentPath(refreshedPath);
							try { StuckHandler.Reset(); } catch { }
						}
					}
				}

				// Drift detection (HB 6.2.3 method_15)
				// Guard: if the player is falling (dismount/drop) the check would always fire
				// and regenerate the path mid-air. HB method_15 returns true (= still on path)
				// whenever IsFalling. Port: (.hb 6.2.3/Styx/Pathing/MeshNavigator.cs method_15 line 1)
				if (!_suppressDriftCheck && _currentPathIndex > 0 && _currentPathIndex < _currentPath.Count
				    && !(WoWMovement.ActiveMover ?? StyxWoW.Me).IsFalling)
				{
					WoWPoint segA = _currentPath[_currentPathIndex - 1];
					WoWPoint segB = _currentPath[_currentPathIndex];
					float driftDist = DistanceToLineSegment2D(me.Location, segA, segB);
					if (driftDist * driftDist > PathPrecision * PathPrecision)
					{
						Logging.WriteDiagnostic("Generating new path because we are not on the old path anymore!");
						_currentPath.Clear();
						_currentPathIndex = 0;
						_cachedPushAheadIndex = -1;
						return MoveTo(destination, precision, destinationName);
					}
				}

				WoWPoint nextPoint = _currentPath[_currentPathIndex];

				// Advance through reached waypoints (HB 6.2.3 method_24)
				while (_currentPathIndex < _currentPath.Count)
				{
					nextPoint = _currentPath[_currentPathIndex];
					bool isFinalPoint = (_currentPathIndex == _currentPath.Count - 1);
					float waypointPrecision = isFinalPoint ? precision : PathPrecision;
					float distance2DSqr = me.Location.Distance2DSqr(nextPoint);
					float zDiff = Math.Abs(me.Location.Z - nextPoint.Z);
					bool reachedWaypoint = distance2DSqr <= waypointPrecision * waypointPrecision && zDiff < 4.5f;

					if (!reachedWaypoint)
						break;

					_currentPathIndex++;
					if (_suppressDriftCheck && _currentPathIndex > _suppressDriftCheckIndex)
					{
						_suppressDriftCheck = false;
						_suppressDriftCheckIndex = 0;
					}
					if (_currentPathIndex >= _currentPath.Count)
						return _isPartialPath ? MoveResult.Failed : MoveResult.ReachedDestination;

					if (_currentFlags != null && (_currentPathIndex - 1) < _currentFlags.Length
					    && (_currentFlags[_currentPathIndex - 1] & TripperNav.StraightPathFlags.OffMeshConnection) != 0)
						return MoveResult.Moved;

					try { StuckHandler.Reset(); } catch { }
				}

				// Move toward current waypoint with push-ahead
				nextPoint = _currentPath[_currentPathIndex];

				// Water/lava Z+2f lift (HB 6.2.3 step 15)
				if (_currentPolyTypes != null && _currentPathIndex < _currentPolyTypes.Length)
				{
					var polyType = _currentPolyTypes[_currentPathIndex];
					if (polyType == TripperNav.AreaType.Water || polyType == TripperNav.AreaType.Lava)
						nextPoint = new WoWPoint(nextPoint.X, nextPoint.Y, nextPoint.Z + 2f);
				}

				// Push-ahead (HB 6.2.3 method_25/26)
				WoWPoint clickPoint = ComputeClickPoint(me, nextPoint);
				Navigator.PlayerMover.MoveTowards(clickPoint);
				return MoveResult.Moved;
			}

			return MoveResult.PathGenerationFailed;
		}

		/// <summary>
		/// Clears all navigation state. HB 6.2.3 MeshNavigator.Clear().
		/// </summary>
		public override bool Clear()
		{
			try { StuckHandler.Reset(); } catch { }

			_destination = WoWPoint.Zero;
			_currentPath.Clear();
			_currentPathIndex = 0;
			_currentFlags = null;
			_currentPolyTypes = null;
			_currentAbilityFlags = null;
			_ridingElevator = false;
			_currentAvoidPath = null;
			_currentAvoidPathIndex = 0;
			_cachedPushAheadIndex = -1;
			_pathRegenThrottle = new WaitTimer(TimeSpan.FromMilliseconds(500));
			_suppressDriftCheck = false;
			_suppressDriftCheckIndex = 0;
			return true;
		}

		/// <summary>
		/// Generates a navmesh path from player position to destination.
		/// HB 6.2.3 MeshNavigator.GeneratePath.
		/// </summary>
		public override WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to)
		{
			if (!Navigator.IsNavigatorLoaded)
				return Array.Empty<WoWPoint>();

			uint mapId = Navigator.TripperNavigator.CurrentMapId;
			var start = new Vector3(from.X, from.Y, from.Z);
			var end = new Vector3(to.X, to.Y, to.Z);

			try
			{
				TripperNavigator.EnsureTilesAroundPosition(mapId, start, Navigator.LoadTilesAroundRadius);
				TripperNavigator.EnsureTilesAroundPosition(mapId, end, Navigator.LoadTilesAroundRadius);
			}
			catch { }

			BlackspotManager.EnsureBlackspotsMarked();
			ApplyAliveQueryFilter(ObjectManager.Me?.IsAlive ?? true);

			var result = TripperNavigator.FindPath(mapId, start, end, true);
			if (result.Status.Succeeded && result.Points != null && result.Points.Length > 0)
			{
				var path = new WoWPoint[result.Points.Length];
				for (int i = 0; i < result.Points.Length; i++)
					path[i] = new WoWPoint(result.Points[i].X, result.Points[i].Y, result.Points[i].Z);
				return path;
			}

			return Array.Empty<WoWPoint>();
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.CanNavigateWithin — uses the full PathFindResult endpoint.
		/// </summary>
		public override bool CanNavigateWithin(WoWPoint from, WoWPoint to, float distanceTolerancy)
		{
			TripperNav.PathFindResult? result = FindPathResult(from, to);
			return result != null
			       && result.Succeeded
			       && result.Points != null
			       && result.Points.Length != 0
			       && Vector3.DistanceSquared(result.Points[result.Points.Length - 1], new Vector3(to.X, to.Y, to.Z)) < distanceTolerancy * distanceTolerancy;
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.CanNavigateFully — partial paths are not fully navigable.
		/// </summary>
		public override bool CanNavigateFully(WoWPoint from, WoWPoint to)
		{
			TripperNav.PathFindResult? result = FindPathResult(from, to);
			return result != null && result.Succeeded && !result.IsPartialPath;
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.PathDistance — returns null for failed or partial paths.
		/// </summary>
		public override float? PathDistance(WoWPoint from, WoWPoint to, float maxDistance = float.MaxValue)
		{
			TripperNav.PathFindResult? result = FindPathResult(from, to);
			if (result == null || !result.Succeeded || result.IsPartialPath || result.Points == null || result.Points.Length == 0)
				return null;

			Vector3 start = new Vector3(from.X, from.Y, from.Z);
			Vector3 end = new Vector3(to.X, to.Y, to.Z);
			Vector3[] points = result.Points;
			float distance = Vector3.Distance(start, points[0]);
			distance += Vector3.Distance(points[points.Length - 1], end);

			for (int i = 0; i < points.Length - 1; i++)
			{
				if (distance > maxDistance)
					return maxDistance;
				distance += Vector3.Distance(points[i], points[i + 1]);
			}

			return distance;
		}

		/// <summary>
		/// HB 6.2.3 MeshNavigator.AtLocation — checks if two points are within PathPrecision.
		/// </summary>
		public override bool AtLocation(WoWPoint point1, WoWPoint point2)
		{
			return point1.Distance2DSqr(point2) <= PathPrecision * PathPrecision
			       && Math.Abs(point1.Z - point2.Z) < 4.5f;
		}

		#endregion

		#region Public properties/accessors

		/// <summary>Current navmesh destination.</summary>
		public WoWPoint Destination => _destination;

		/// <summary>Current path waypoints.</summary>
		public List<WoWPoint> CurrentPath => _currentPath;

		/// <summary>Current waypoint index in path.</summary>
		public int CurrentPathIndex => _currentPathIndex;

		/// <summary>true when there are unvisited waypoints.</summary>
		public bool HasActivePath => _currentPath.Count > 0 && _currentPathIndex < _currentPath.Count;

		/// <summary>true while riding an elevator.</summary>
		public bool IsRidingElevator => _ridingElevator;

		/// <summary>
		/// Returns the remaining unvisited navmesh waypoints.
		/// Used by Bots.DungeonBuddy.Avoidance.Helpers.GetAvoidPath().
		/// </summary>
		public WoWPoint[] GetRemainingNavPath()
		{
			if (_currentPath.Count == 0 || _currentPathIndex >= _currentPath.Count)
				return Array.Empty<WoWPoint>();
			return _currentPath.Skip(_currentPathIndex).ToArray();
		}

		/// <summary>
		/// Replaces the active navmesh path. Called by Helpers.GetAvoidPath().
		/// HB 6.2.3: CurrentMovePath.Path = FindPath(from, to); Index = 0.
		/// </summary>
		public void OverrideCurrentPath(WoWPoint[] points)
		{
			_currentPath.Clear();
			if (points != null)
				foreach (var p in points)
					_currentPath.Add(p);
			_currentPathIndex = 0;
			_currentFlags = null;
			_currentPolyTypes = null;
			_currentAbilityFlags = null;
			_cachedPushAheadIndex = -1;
		}

		/// <summary>
		/// Sets faction area type. Called by Navigator on bot start.
		/// </summary>
		public void SetFactionAreaType(TripperNav.AreaType areaType)
		{
			_factionAreaType = areaType;
		}

		#endregion

		#region Internal — path generation

		private TripperNav.Navigator TripperNavigator => Navigator.TripperNavigator;

		// HB 6.2.3 MeshNavigator.smethod_0: route navigator internal messages to the log.
		// Source: .hb 6.2.3 MeshNavigator.cs line 38
		private static void OnNavigatorLog(string msg) => Logging.WriteDiagnostic(System.Windows.Media.Colors.LightBlue, msg);

		private TripperNav.PathFindResult? FindPathResult(WoWPoint from, WoWPoint to)
		{
			if (!Navigator.IsNavigatorLoaded)
				return null;

			uint mapId = (uint)(ObjectManager.Me?.MapId ?? 0);
			if (mapId == 0)
				return null;

			var start = new Vector3(from.X, from.Y, from.Z);
			var end = new Vector3(to.X, to.Y, to.Z);

			try
			{
				TripperNavigator.EnsureTilesAroundPosition(mapId, start, Navigator.LoadTilesAroundRadius);
				TripperNavigator.EnsureTilesAroundPosition(mapId, end, Navigator.LoadTilesAroundRadius);
			}
			catch { }

			BlackspotManager.EnsureBlackspotsMarked();
			ApplyAliveQueryFilter(ObjectManager.Me?.IsAlive ?? true);

			return TripperNavigator.FindPath(mapId, start, end, true);
		}

		/// <summary>
		/// Generates path via navmesh. Returns true on success.
		/// HB 6.2.3 MeshNavigator method — uses Task.Run for tile loading (non-blocking).
		/// </summary>
		private bool GeneratePathInternal(LocalPlayer me, WoWPoint destination)
		{
			if (!Navigator.IsNavigatorLoaded)
				return false;

			uint mapId = (uint)(me.MapId);
			var start = new Vector3(me.Location.X, me.Location.Y, me.Location.Z);
			var end = new Vector3(destination.X, destination.Y, destination.Z);

			BlackspotManager.EnsureBlackspotsMarked();

			float distSqr = Vector3.DistanceSquared(start, end);
			bool hasMiddle = distSqr > 40000f;
			var capturedStart = start;
			var capturedEnd = end;
			uint capturedMapId = mapId;
			int capturedRadius = Navigator.LoadTilesAroundRadius;
			var capturedMid = hasMiddle ? (start + end) * 0.5f : Vector3.Zero;

			var navTask = System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					TripperNavigator.EnsureTilesAroundPosition(capturedMapId, capturedStart, capturedRadius);
					TripperNavigator.EnsureTilesAroundPosition(capturedMapId, capturedEnd, capturedRadius);
					if (hasMiddle)
						TripperNavigator.EnsureTilesAroundPosition(capturedMapId, capturedMid, capturedRadius);
				}
				catch { }
				return TripperNavigator.FindPath(capturedMapId, capturedStart, capturedEnd, true);
			});

			TripperNav.PathFindResult result;
			try
			{
				if (navTask.Wait(10))
				{
					result = navTask.Result;
				}
				else
				{
					using (StyxWoW.Memory.ReleaseFrame(true))
					{
						int waitSlice = Math.Max(10, 1000 / Math.Max(1, (int)TreeRoot.TicksPerSecond));
						while (!navTask.Wait(waitSlice))
						{
							try
							{
								StyxWoW.Memory.ClearCache();
								using (StyxWoW.Memory.AcquireFrame())
								{
									ObjectManager.Update();
									WoWMovement.Pulse();
								}
							}
							catch { }
						}
					}
					result = navTask.Result;
				}
			}
			catch
			{
				result = null!;
			}
			finally
			{
				navTask.Dispose();
			}

			if (result == null || result.Points == null || result.Points.Length == 0)
			{
				Logging.WriteDiagnostic(System.Windows.Media.Colors.Red,
					"Could not generate path from {0} to {1} on map {2} (status: {3})",
					me.Location, destination, me.MapId, result?.Status);
				return false;
			}

			if (result.IsPartialPath)
				Logging.WriteDiagnostic(System.Windows.Media.Colors.Orange,
					"Could not generate full path from {0} to {1} (time used: {2})",
					me.Location, destination, result.Elapsed);
			else if (result.Elapsed.TotalMilliseconds > 50.0)
				Logging.WriteDiagnostic("Successfully generated path from {0} to {1} in {2}",
					me.Location, destination, result.Elapsed);

			foreach (var point in result.Points)
				_currentPath.Add(new WoWPoint(point.X, point.Y, point.Z));

			_currentFlags = result.Flags;
			_currentPolyTypes = result.PolyTypes;
			_currentAbilityFlags = result.AbilityFlags;
			_isPartialPath = result.IsPartialPath;
			return true;
		}

		#endregion

		#region Internal — push-ahead (HB 6.2.3 method_25/26)

		/// <summary>
		/// HB 6.2.3 MeshNavigator.method_26: push-ahead click target computation.
		/// Extends the click target PathPrecision yards beyond the current waypoint along the
		/// player→waypoint direction. Raycasts to verify the extension is unobstructed.
		/// Called only for intermediate waypoints (not first, not last) — HB method_25 guard.
		/// </summary>
		private WoWPoint ComputeClickPoint(LocalPlayer me, WoWPoint waypoint)
		{
			if (Navigator.PlayerMover is not ClickToMoveMover)
				return waypoint;

			// HB 6.2.3 method_25: Index > 0 && Index < Path.Points.Length - 1
			if (_currentPathIndex == 0 || _currentPathIndex >= _currentPath.Count - 1)
				return waypoint;

			// Forward = direction from player TO waypoint (HB: vector2 = vector3_0 - activeMover.Location)
			float fdx = waypoint.X - me.Location.X;
			float fdy = waypoint.Y - me.Location.Y;
			float fdz = waypoint.Z - me.Location.Z;
			float flen = (float)Math.Sqrt(fdx * fdx + fdy * fdy + fdz * fdz);
			if (flen < 0.01f)
				return waypoint;

			fdx /= flen; fdy /= flen; fdz /= flen;

			// Lookahead = waypoint + forward * PathPrecision (HB: vector3 = vector3_0 + vector2 * PathPrecision)
			WoWPoint lookahead = new WoWPoint(
				waypoint.X + fdx * PathPrecision,
				waypoint.Y + fdy * PathPrecision,
				waypoint.Z + fdz * PathPrecision);

			uint mapId = (uint)me.MapId;
			var waypointVec = new Vector3(waypoint.X, waypoint.Y, waypoint.Z);
			var lookaheadVec = new Vector3(lookahead.X, lookahead.Y, lookahead.Z);

			try
			{
				TripperNavigator.EnsureTilesAroundPosition(mapId, waypointVec, 0);
				TripperNavigator.EnsureTilesAroundPosition(mapId, lookaheadVec, 0);
			}
			catch { }

			// HB: FindNearestPoly at waypoint with tight extents (0.5,3,0.5), then Raycast to lookahead.
			// Clear = hitT >= 1.0f (Detour float.MaxValue clamped to 1.0f by our DLL).
			var tightExtents = new Vector3(0.5f, 3f, 0.5f);
			var status = TripperNavigator.RaycastWithExtents(mapId, waypointVec, lookaheadVec, tightExtents,
				out float hitT, out _, out _, out _);

			return (status.Succeeded && hitT >= 1.0f) ? lookahead : waypoint;
		}

		#endregion

		#region Internal — start-index skip (HB 6.2.3 method_14)

		/// <summary>
		/// HB 6.2.3 MeshNavigator.method_14: skips waypoints the player has already passed.
		/// </summary>
		private void SkipPassedWaypoints(LocalPlayer me)
		{
			if (_currentPath.Count < 2 || me == null)
				return;

			WoWPoint playerPos = me.Location;
			int skipTo = 0;

			for (int i = 0; i < _currentPath.Count - 1; i++)
			{
				WoWPoint wp = _currentPath[i];
				WoWPoint nextWp = _currentPath[i + 1];

				float sx = nextWp.X - wp.X;
				float sy = nextWp.Y - wp.Y;
				float segLenSqr = sx * sx + sy * sy;
				if (segLenSqr < 0.01f)
					continue;

				float px = playerPos.X - wp.X;
				float py = playerPos.Y - wp.Y;
				float t = (px * sx + py * sy) / segLenSqr;
				float perpDist = DistanceToLineSegment2D(playerPos, wp, nextWp);

				if (t > 0.8f && perpDist < 8f)
					skipTo = i + 1;
				else
					break;
			}

			if (skipTo > 0)
				_currentPathIndex = skipTo;
		}

		#endregion

		#region Internal — alive query filter (HB 6.2.3 method_28)

		private void ApplyAliveQueryFilter(bool isAlive)
		{
			try
			{
				if (!Navigator.IsNavigatorLoaded)
					return;

				ushort onlyWhileAlive = (ushort)TripperNav.AbilityFlags.OnlyWhileAlive;
				ushort include = TripperNavigator.GetIncludeFlags();
				ushort exclude = TripperNavigator.GetExcludeFlags();

				if (isAlive)
				{
					exclude = (ushort)(exclude & ~onlyWhileAlive);
					include = (ushort)(include | onlyWhileAlive);
				}
				else
				{
					include = (ushort)(include & ~onlyWhileAlive);
					exclude = (ushort)(exclude | onlyWhileAlive);
				}

				TripperNavigator.SetIncludeFlags(include);
				TripperNavigator.SetExcludeFlags(exclude);
			}
			catch { }
		}

		#endregion

		#region Internal — off-mesh dispatch (HB 6.2.3 method_18)

		private MoveResult DispatchOffMesh(LocalPlayer me, WoWPoint endPoint, WoWPoint startPoint, TripperNav.AreaType areaType)
		{
			switch (areaType)
			{
				case TripperNav.AreaType.Elevator:
					return HandleElevator(me, endPoint, startPoint);
				case TripperNav.AreaType.Portal:
				case TripperNav.AreaType.DefendersPortal:
				case TripperNav.AreaType.HordePortal:
				case TripperNav.AreaType.AlliancePortal:
					return HandlePortal(me);
				case TripperNav.AreaType.InteractUnit:
					return HandleInteractUnit(me);
				case TripperNav.AreaType.InteractObject:
					return HandleInteractObject(me);
				default:
					return HandleStandardOffMesh(me, endPoint);
			}
		}

		private MoveResult HandleStandardOffMesh(LocalPlayer me, WoWPoint targetPoint)
		{
			if (_currentAbilityFlags != null && (_currentPathIndex - 1) >= 0
			    && (_currentPathIndex - 1) < _currentAbilityFlags.Length)
			{
				var abilityFlags = _currentAbilityFlags[_currentPathIndex - 1];
				if (abilityFlags != 0 && (abilityFlags & (TripperNav.AbilityFlags.Run | TripperNav.AbilityFlags.Jump)) == 0)
				{
					Logging.WriteDiagnostic("Invalid offmesh connection encountered at {0}", me.Location);
					return MoveResult.Failed;
				}
			}

			WoWMovement.ClickToMove(targetPoint);
			return MoveResult.Moved;
		}

		#endregion

		#region Internal — elevator (HB 6.2.3 method_20)

		private MoveResult HandleElevator(LocalPlayer me, WoWPoint endPoint, WoWPoint startPoint)
		{
			try { StuckHandler.Reset(); } catch { }

			WoWPoint playerPos = me.Location;

			var transport = ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
				.Where(go => go.SubType == WoWGameObjectType.Transport
				              && go.Entry != 20657 && go.Entry != 20656)
				.OrderBy(go => go.Location.Distance2DSqr(playerPos))
				.FirstOrDefault();

			if (transport == null)
			{
				Logging.WriteDiagnostic("There is no elevator around. Something is wrong");
				return MoveResult.Failed;
			}

			if (playerPos.DistanceSqr(endPoint) < 4f && !me.IsOnTransport && !me.IsFalling)
			{
				_ridingElevator = false;
				_currentPathIndex++;
				if (_currentPathIndex >= _currentPath.Count)
					return _isPartialPath ? MoveResult.Failed : MoveResult.ReachedDestination;
				return MoveResult.Moved;
			}

			WoWPoint transportLocation = transport.Location;

			if (_elevatorMotionTimer.IsFinished)
			{
				_elevatorMoving = _lastElevatorPos.DistanceSqr(transportLocation) > 0.0001f;
				_lastElevatorPos = transportLocation;
				_elevatorMotionTimer.Reset();
			}

			if (me.IsOnTransport)
			{
				_ridingElevator = true;
				if (Math.Abs(playerPos.Z - endPoint.Z) <= 2f && !_elevatorMoving)
				{
					Logging.WriteDiagnostic("Moving out of elevator");
					Navigator.PlayerMover.MoveTowards(endPoint);
					return MoveResult.Moved;
				}
				TreeRoot.StatusText = "Waiting for elevator to reach end point";
				return MoveResult.Moved;
			}

			_ridingElevator = false;
			bool closerToEnd = Math.Abs(startPoint.Z - playerPos.Z) > Math.Abs(endPoint.Z - playerPos.Z);

			if (!closerToEnd && (Math.Abs(transportLocation.Z - playerPos.Z) > 2f || _elevatorMoving))
			{
				if (playerPos.DistanceSqr(startPoint) > 4f)
				{
					Logging.WriteDiagnostic("Woops, we are not in the waiting spot");
					Navigator.PlayerMover.MoveTowards(startPoint);
					return MoveResult.Moved;
				}
				if (!me.IsSafelyFacing(transport, 15f))
				{
					WoWMovement.MoveStop();
					me.SetFacing(transport.Location);
				}
				TreeRoot.StatusText = "Waiting for the elevator";
				return MoveResult.Moved;
			}

			if (!closerToEnd)
			{
				Logging.WriteDiagnostic("Moving inside elevator");
				if (me.Mounted)
					Mount.Dismount();
				Navigator.PlayerMover.MoveTowards(transportLocation);
				return MoveResult.Moved;
			}

			return MoveResult.Moved;
		}

		#endregion

		#region Internal — portal/interact (HB 6.2.3 method_21/22/23)

		private MoveResult HandlePortal(LocalPlayer me)
		{
			try { StuckHandler.Reset(); } catch { }

			float bestDistSqr = float.MaxValue;
			WoWGameObject? bestPortal = null;

			foreach (var go in ObjectManager.GetObjectsOfType<WoWGameObject>(false, false))
			{
				if (go.SubType == WoWGameObjectType.Goober
				    || go.SubType == WoWGameObjectType.SpellCaster)
				{
					float distSqr = go.Location.DistanceSqr(me.Location);
					if (distSqr < bestDistSqr)
					{
						bestDistSqr = distSqr;
						bestPortal = go;
					}
				}
			}

			if (bestPortal != null && bestPortal.WithinInteractRange)
			{
				Logging.WriteDiagnostic("Interacting with:{0}", bestPortal.Name);
				bestPortal.Interact();
				return MoveResult.Moved;
			}

			Logging.WriteDiagnostic("Could not find portal to take.");
			return MoveResult.Failed;
		}

		private MoveResult HandleInteractUnit(LocalPlayer me)
		{
			try { StuckHandler.Reset(); } catch { }

			if (!_interactTimer.IsFinished)
				return MoveResult.Moved;

			// Sort by distance to offmesh entry point (HB method_22: meshMovePath_0.Path.Points[Index-1])
			WoWPoint offMeshEntry = _currentPathIndex > 0 ? _currentPath[_currentPathIndex - 1] : me.Location;

			var unit = ObjectManager.CachedUnits
				.Where(u => !u.IsDead && !u.IsHostile && !u.PlayerControlled && !u.IsPlayer)
				.OrderBy(u => u.Location.DistanceSqr(offMeshEntry))
				.FirstOrDefault();

			if (unit == null)
			{
				Logging.WriteDiagnostic("Could not find unit to interact with.");
				return MoveResult.Failed;
			}

			if (!unit.WithinInteractRange)
			{
				WoWMovement.ClickToMove(unit.Location);
				return MoveResult.Moved;
			}

			if (me.Mounted)
				Mount.Dismount("InteractUnit in path");
			unit.Interact();
			_interactTimer.Reset();
			return MoveResult.Moved;
		}

		private MoveResult HandleInteractObject(LocalPlayer me)
		{
			try { StuckHandler.Reset(); } catch { }

			if (!_interactTimer.IsFinished)
				return MoveResult.Moved;

			// Sort by distance to offmesh entry point (HB method_21: meshMovePath_0.Path.Points[Index-1])
			WoWPoint offMeshEntry = _currentPathIndex > 0 ? _currentPath[_currentPathIndex - 1] : me.Location;

			var gameObject = ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
				.OrderBy(go => go.Location.DistanceSqr(offMeshEntry))
				.FirstOrDefault();

			if (gameObject == null)
			{
				_currentPathIndex++;
				return MoveResult.Moved;
			}

			if (!gameObject.WithinInteractRange)
			{
				WoWMovement.ClickToMove(gameObject.Location);
				return MoveResult.Moved;
			}

			if (me.Mounted)
				Mount.Dismount("InteractObject in path");
			gameObject.Interact();
			_interactTimer.Reset();
			return MoveResult.Moved;
		}

		#endregion

		#region Internal — door handling (HB 6.2.3 method_7/8)

		private MoveResult? HandleDoors(LocalPlayer me)
		{
			if (!_doorScanTimer.IsFinished)
				return null;

			WoWGameObject? closestDoor = ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
				.FirstOrDefault(IsDoorUsable);

			if (closestDoor == null)
			{
				_doorScanTimer.Reset();
				return null;
			}

			if (me.IsMoving)
			{
				WoWMovement.MoveStop();
			}
			else if (!me.IsCasting && _doorInteractTimer.IsFinished)
			{
				Logging.WriteDiagnostic("Opening Closed Door {0} (Id: {1})", closestDoor.Name, closestDoor.Entry);
				closestDoor.Interact();
				_doorInteractTimer.Reset();
			}

			return MoveResult.Moved;
		}

		private bool IsDoorUsable(WoWGameObject gameObject)
		{
			if (gameObject.SubType != WoWGameObjectType.Door)
				return false;

			if (gameObject.SubObj is not WoWDoor door || !door.IsClosed)
				return false;

			if (gameObject.Locked || !gameObject.WithinInteractRange || !gameObject.CanUse() || !gameObject.CanUseNow())
				return false;

			LockEntry? lockEntry = gameObject.LockRecord;
			if (lockEntry == null)
				return true;

			for (int i = 0; i < lockEntry.Value.LockProperties.Length && i < lockEntry.Value.Type.Length; i++)
			{
				uint itemId = lockEntry.Value.LockProperties[i];
				if (itemId == 0 || lockEntry.Value.Type[i] != 1)
					continue;

				if (ObjectManager.Me?.CarriedItems.Any(item => item.Entry == itemId) != true)
					return false;
			}

			return true;
		}

		#endregion

		#region Internal — geometry helpers

		private bool IsAtPoint(WoWPoint playerPos, WoWPoint target)
		{
			return playerPos.Distance2DSqr(target) <= PathPrecision * PathPrecision
			       && Math.Abs(playerPos.Z - target.Z) < 4.5f;
		}

		/// <summary>
		/// 2D distance from a point to a line segment (Z ignored).
		/// HB 6.2.3 method_15/smethod_1.
		/// </summary>
		private static float DistanceToLineSegment2D(WoWPoint point, WoWPoint segA, WoWPoint segB)
		{
			float dx = segB.X - segA.X;
			float dy = segB.Y - segA.Y;
			float lenSqr = dx * dx + dy * dy;

			if (lenSqr < 0.0001f)
			{
				float px = point.X - segA.X;
				float py = point.Y - segA.Y;
				return (float)Math.Sqrt(px * px + py * py);
			}

			float t = ((point.X - segA.X) * dx + (point.Y - segA.Y) * dy) / lenSqr;
			t = Math.Max(0f, Math.Min(1f, t));

			float closestX = segA.X + t * dx;
			float closestY = segA.Y + t * dy;

			float ex = point.X - closestX;
			float ey = point.Y - closestY;
			return (float)Math.Sqrt(ex * ex + ey * ey);
		}

		#endregion
	}
}
