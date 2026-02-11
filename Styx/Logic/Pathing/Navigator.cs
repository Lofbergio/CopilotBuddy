using System;
using System.Collections.Generic;
using System.Linq;
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
		private static IStuckHandler? _stuckHandler;
		private static WaitTimer _stuckCheckTimer = new WaitTimer(TimeSpan.FromSeconds(2));
		private static WaitTimer _doorInteractTimer = new WaitTimer(TimeSpan.FromSeconds(2)); // Cooldown between door interactions
		private static WaitTimer _pathRegenThrottle = new WaitTimer(TimeSpan.FromMilliseconds(500)); // Prevent path regen spam

		// Path metadata — stored alongside _currentPath to support off-mesh, terrain type, and ability checks
		private static TripperNav.StraightPathFlags[]? _currentFlags;
		private static TripperNav.AreaType[]? _currentPolyTypes;
		private static TripperNav.AbilityFlags[]? _currentAbilityFlags;
		private static bool _ridingElevator; // True while actively riding an elevator (blocks mount-up)
		private static int _unstickAttempts; // AUDIT FIX: Max retry counter to prevent infinite unstick loops
		private const int MaxUnstickAttempts = 5; // After 5 failed unsticks, force path regeneration

		// Path drift suppression after an initial node skip.
		// We may set _currentPathIndex > 0 immediately after generating a new path (HB 6.2.3 method_14).
		// Our drift detection (P6.7) would otherwise see the player "far from segment" and regen endlessly.
		private static bool _suppressDriftCheck;
		private static int _suppressDriftCheckIndex;

		// Elevator sequence tracking (HB 6.2.3: once method_18 enters method_20, the elevator
		// handler keeps being called every tick via the path index — no re-checking player
		// position vs offmesh start). This flag locks the bot into elevator mode.
		private static bool _inElevatorSequence;
		private static WoWPoint _elevatorTarget = WoWPoint.Zero;
		// HB 4.3.4 MeshNavigator.bool_0: state flag used by elevator off-mesh logic
		private static bool _hbElevatorFlag;
		// Track whether the current path is partial (navmesh doesn't fully connect start→dest).
		// HB 6.2.3 method_24: returns MoveResult.Failed when reaching end of a partial path,
		// instead of ReachedDestination, so the behavior tree knows the bot isn't at the goal.
		private static bool _isPartialPath;

		// WotLK no-fly zone IDs — areas where flying is forbidden or problematic
		private static readonly HashSet<uint> _noFlyZoneIds = new HashSet<uint>
		{
			4395, // Dalaran city (no flying allowed)
			4613, // The Pit of Saron (indoor dungeon entrance area)
			4820, // Halls of Reflection
		};

		public static float PathPrecision { get; set; } = 2.0f;
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

		/// <summary>
		/// Gets or sets the stuck handler used for stuck detection and recovery.
		/// </summary>
		public static IStuckHandler StuckHandler
		{
			get
			{
				_stuckHandler ??= new StuckHandler();
				return _stuckHandler;
			}
			set => _stuckHandler = value;
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
			BotEvents.Player.OnMapChanged += OnMapChanged;
			BotEvents.OnBotStart += OnBotStart;
			BotEvents.OnBotStop += OnBotStop;

			// HB 6.2.3 pattern: cancel mount-up while riding elevator
			Mount.OnMountUp += OnMountUpDuringElevator;
		}

		/// <summary>
		/// Prevents mounting while riding an elevator (HB 6.2.3 MeshNavigator.method_17).
		/// </summary>
		private static void OnMountUpDuringElevator(object? sender, MountUpEventArgs e)
		{
			if (_ridingElevator)
			{
				e.Cancel = true;
			}
		}

		/// <summary>
		/// Checks if the current zone is a no-fly zone (P6.10 — Dalaran etc.).
		/// </summary>
		public static bool IsInNoFlyZone
		{
			get
			{
				var me = ObjectManager.Me;
				if (me == null) return false;
				return _noFlyZoneIds.Contains(me.ZoneId);
			}
		}

		/// <summary>
		/// Gets whether the player is currently riding an elevator (blocks mount-up).
		/// HB 6.2.3 pattern: MeshNavigator.method_17 cancels mount while on transport.
		/// </summary>
		public static bool IsRidingElevator => _ridingElevator;

		private static void OnMapChanged(BotEvents.Player.MapChangedEventArgs args)
		{
			Clear();
		}

		private static void OnBotStart(EventArgs args)
		{
			Clear();
			// Load navigation meshes on bot start
			if (_navigator == null)
			{
				_navigator = new TripperNav.Navigator();
			}
			if (!_navigator.IsLoaded)
			{
				try
				{
					if (_navigator.LoadMeshes())
					{
					}
					else
					{
					}
				}
				catch (Exception ex)
				{
					_ = ex;
				}
			}

			// Set faction-aware query filter (HB 6.2.3 pattern: OnBotStarted → SetFactionQueryFilter)
			// Excludes opposite faction's paths and applies 50x cost penalty on their areas
			if (IsNavigatorLoaded && _navigator != null)
			{
				try
				{
					var me = StyxWoW.Me;
					if (me != null)
					{
						_navigator.SetFactionQueryFilter(me.IsHorde);
					}
				}
				catch (Exception ex)
				{
					_ = ex;
				}
			}

			// Initialize flight path system (HB 4.3.4 Class448 startup pattern):
			// Loads saved XmlFlightNode database and attaches TAXIMAP_OPENED Lua event handler.
			// Without this call, P6.9 flight path auto-detection is non-functional.
			try
			{
				FlightPaths.Initialize();
			}
			catch (Exception ex)
			{
				_ = ex;
			}
		}

		private static void OnBotStop(EventArgs args)
		{
			Clear();
		}

		public static void Clear()
		{
			// When we clear navigation (e.g. after an unstick), also reset stuck detection state.
			// Otherwise, the stuck logic can immediately re-trigger on the next MoveTo call.
			try
			{
				StuckHandler.Reset();
			}
			catch
			{
				// Ignore
			}

			_destination = WoWPoint.Zero;
			_currentPath.Clear();
			_currentPathIndex = 0;
			_currentFlags = null;
			_currentPolyTypes = null;
			_currentAbilityFlags = null;
			_ridingElevator = false;
			_inElevatorSequence = false;
			_elevatorTarget = WoWPoint.Zero;
			_unstickAttempts = 0;
			_doorCenterTarget = WoWPoint.Zero;
			_pathRegenThrottle = new WaitTimer(TimeSpan.FromMilliseconds(500)); // Reset so next path gen is immediate
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
			{
				return MoveResult.Failed;
			}

			// Check for NaN coordinates - would crash WoW with INT_DIVIDE_BY_ZERO
			if (float.IsNaN(destination.X) || float.IsNaN(destination.Y) || float.IsNaN(destination.Z))
			{
				return MoveResult.Failed;
			}

			LocalPlayer? me = ObjectManager.Me;
			if (me == null)
			{
				return MoveResult.Failed;
			}

			// Check if we're already at the destination
			float distance = me.Location.Distance(destination);
			if (distance < precision)
			{
				_destination = WoWPoint.Zero;
				_currentPath.Clear();
				return MoveResult.ReachedDestination;
			}

			// Auto-mount check (HB 6.2.3 MeshNavigator L231):
			// If distance >= MountDistance and we can mount, mount up before path following.
			// This ensures all callers (quest behaviors, plugins, etc.) get auto-mounting.
			try
			{
				if (Mount.ShouldMount(destination))
				{
					WoWPoint dest = destination;
					Mount.StateMount(() => dest);
				}
			}
			catch (System.Exception ex)
			{
				_ = ex;
			}

			// Auto-dismount near destination (HB pattern):
			// Dismount when close to interaction POIs (loot, vendor, quest NPC, etc.)
			// to avoid running into NPCs mounted and failing to interact.
			try
			{
				if (Mount.ShouldDismount(destination))
				{
					Mount.Dismount("Near destination");
				}
			}
			catch (System.Exception ex)
			{
				_ = ex;
			}

			// P6.14 — Combat abort: If we're in combat, skip expensive pathfinding.
			// In synchronous mode we can't abort mid-pathfind, but we CAN short-circuit
			// before calling FindPath() and use direct click-to-move instead.
			// This lets the combat routine take over faster (HB 6.2.3 equivalent of
			// the 4-second combat abort timeout in method_16).
			if (me.Combat && _currentPath.Count == 0)
			{
				// In combat with no existing path — move directly toward target
				// to close distance or flee, without wasting time on pathfinding.
				WoWMovement.ClickToMove(destination);
				return MoveResult.Moved;
			}

			// If destination changed significantly, generate new path (with throttle to prevent regen spam)
			// BUG FIX: Was using exact equality (_destination != destination) which triggers on float
			// jitter every tick, resetting stuck state mid-unstick sequence ("saute une fois puis rien").
			// Now uses distance-based comparison: only regen path if destination moved >1 yard.
			bool destinationChanged = _destination == WoWPoint.Zero || _destination.DistanceSqr(destination) > 1.0f;
			// Also regen if path was cleared (drift, combat, MaxUnstick) but destination hasn't changed.
			// In that case we do NOT reset stuck state — the unstick sequence continues.
			bool needsPathRegen = !destinationChanged && _currentPath.Count == 0;
			if (destinationChanged || needsPathRegen)
			{
				if (!_pathRegenThrottle.IsFinished)
				{
					// Too soon since last path generation — wait (HB pattern: never blindly ClickToMove)
					return MoveResult.Moved;
				}
				_pathRegenThrottle.Reset();

				_destination = destination;
				_currentPath.Clear();

				// Only reset stuck state when the destination truly changed.
				// If we're just regenerating after drift/combat/MaxUnstick, keep stuck state intact
				// so the unstick sequence (jump 1→2→3→strafe→...) can continue.
				if (destinationChanged)
				{
					_stuckCheckTimer.Reset();
					_unstickAttempts = 0;

					try
					{
						StuckHandler.Reset();
					}
					catch
					{
						// Ignore
					}

					// Clear stale elevator state: if bot died during elevator ride, the sequence
					// flag persists and blocks all subsequent navigation (e.g., corpse run).
					// Resetting here ensures a fresh start when destination changes.
					_inElevatorSequence = false;
					_elevatorTarget = WoWPoint.Zero;
					_ridingElevator = false;
				}

				// P6.14 — Combat abort: Skip pathfinding entirely if in combat.
				// Direct click-to-move is sufficient for combat movement (kiting, chasing).
				if (me.Combat)
				{
					_currentPath.Add(me.Location);
					_currentPath.Add(destination);
					_currentFlags = null;
					_currentPolyTypes = null;
					_currentAbilityFlags = null;
					_currentPathIndex = 0;
					WoWMovement.ClickToMove(destination);
					return MoveResult.Moved;
				}

				// P6.9 — Flight path auto-detection (HB 6.2.3 method_10):
				// For long distances (>400 yards), check if taking a taxi would be faster than running.
				// ShouldTakeFlightpath compares run time vs flight+walk time, needs >30s savings.
				// SetFlightPathUsage sets up a BotPoi(PoiType.Fly) to walk to the flight master.
				float distanceSqr = me.Location.DistanceSqr(destination);
				if (distanceSqr > 160000f) // 400² yards
				{
					if (FlightPaths.ShouldTakeFlightpath(me.Location, destination, me.MovementInfo.RunSpeed))
					{
						if (FlightPaths.SetFlightPathUsage(me.Location, destination, out _, out _))
						{
							return MoveResult.PathGenerated;
						}
					}
				}

				// Try to use Tripper for pathfinding
				if (IsNavigatorLoaded)
				{
					uint mapId = (uint)(GetCurrentMapId());
					var start = new Vector3(me.Location.X, me.Location.Y, me.Location.Z);
					var end = new Vector3(destination.X, destination.Y, destination.Z);

					// HB-style tile streaming: ensure navmesh tiles are loaded at BOTH
					// start AND destination before pathfinding. Without this, the A*
					// search can't discover offmesh connections (elevators, portals) that
					// sit in tiles far from the player, causing the pathfinder to generate
					// a long detour path or return a partial path.
					// NOTE: HB's WowNavigator loads tiles on-demand during A* via MeshProvider.
					// We can't do that, so we pre-load tiles explicitly instead.
					// TEST: Commenté pour isoler crash - EnsureTilesAroundPosition suspect
					// try
					// {
					// 	TripperNavigator.EnsureTilesAroundPosition(mapId, start, LoadTilesAroundRadius);
					// 	TripperNavigator.EnsureTilesAroundPosition(mapId, end, LoadTilesAroundRadius);
					// 
					// 	// COPILOTBUDDY ENHANCEMENT (not in HB): also load tiles at the midpoint
					// 	// to bridge large gaps. HB doesn't need this because it loads tiles
					// 	// on-demand during A*; we pre-load so need to cover the corridor.
					// 	float distSqr = Vector3.DistanceSquared(start, end);
					// 	if (distSqr > 40000f) // > 200 yards
					// 	{
					// 		var mid = (start + end) * 0.5f;
					// 		TripperNavigator.EnsureTilesAroundPosition(mapId, mid, LoadTilesAroundRadius);
					// 	}
					// }
					// catch
					// {
					// 	// Non-fatal: pathfinding still works with whatever tiles are loaded
					// }

					// Ensure blackspots are marked on navmesh before pathfinding
					// This is HB 4.3.4's OnTileLoaded workaround
					BlackspotManager.EnsureBlackspotsMarked();

					var sw = System.Diagnostics.Stopwatch.StartNew();
					var result = TripperNavigator.FindPath(mapId, start, end, true);
					sw.Stop();
					LogPathResult(result, me.Location, destination, mapId);
					if (result.Status.Succeeded && result.Points != null && result.Points.Length > 0)
					{
						// HB 4.3.4: if the path is partial (navmesh doesn't fully connect
						// start to destination), log it and consider flight paths.
						if (result.IsPartialPath)
						{
							Logging.WriteDebug("Could not generate full path from {0} to {1}", me.Location, destination);

							// COPILOTBUDDY ENHANCEMENT (not in HB): try flight paths on partial
							// paths regardless of distance. HB only checks flights BEFORE
							// pathfinding (distance > threshold). We add this because our
							// pre-loaded tiles may miss corridors that HB's on-demand loading
							// would find, resulting in partial paths where HB gets full paths.
							if (FlightPaths.ShouldTakeFlightpath(me.Location, destination, me.MovementInfo.RunSpeed))
							{
								if (FlightPaths.SetFlightPathUsage(me.Location, destination, out _, out _))
								{
									return MoveResult.PathGenerated;
								}
							}
							// No flight path available — follow the partial path anyway
							// (HB 4.3.4 logs warning but still follows partial paths).
						}

						foreach (var point in result.Points)
						{
							_currentPath.Add(new WoWPoint(point.X, point.Y, point.Z));
						}

						// Store path metadata for off-mesh/terrain handling (P4.1 fix)
						_currentFlags = result.Flags;
						_currentPolyTypes = result.PolyTypes;
						_currentAbilityFlags = result.AbilityFlags;
						_isPartialPath = result.IsPartialPath;
					}
					else
					{
						// HB pattern: path generation failed — stop and return failure.
						// Never blindly ClickToMove (causes walking into walls, off cliffs,
						// climbing impossible hills, and "random walking" behavior).
						WoWMovement.MoveStop();
						return MoveResult.PathGenerationFailed;
					}
				}
				else
				{
					// HB pattern: no navmesh available — stop and return failure.
					// Never blindly ClickToMove without navigation data.
					WoWMovement.MoveStop();
					return MoveResult.PathGenerationFailed;
				}

				_currentPathIndex = 0;
				_suppressDriftCheck = false;
				_suppressDriftCheckIndex = 0;

				// Path start skip (HB 6.2.3 method_14): skip early visible waypoints via raycast.
				// Walk forward through path and raycast from player position — if we can see
				// waypoint N directly, skip to it for smoother movement (avoids zigzag on flat terrain).
				// Stop before any off-mesh connection.
				if (_currentPath.Count > 2 && _currentFlags != null)
				{
					uint skipMapId = (uint)(GetCurrentMapId());
					var playerVec = new Vector3(me.Location.X, me.Location.Y, me.Location.Z);
					int lastVisible = 0;
					for (int i = 1; i < Math.Min(_currentPath.Count - 1, 6); i++) // Check up to 5 waypoints ahead
					{
						// Don't skip past off-mesh connections
						if (i < _currentFlags.Length &&
						    (_currentFlags[i] & TripperNav.StraightPathFlags.OffMeshConnection) != 0)
							break;

						var wp = new Vector3(_currentPath[i].X, _currentPath[i].Y, _currentPath[i].Z);
						var status = TripperNavigator.Raycast(skipMapId, playerVec, wp, out float hitT, out _);
						if (status.Succeeded && hitT >= 1.0f)
						{
							lastVisible = i;
						}
						else
						{
							break; // No point checking further if this one is blocked
						}
					}
					if (lastVisible > 0)
					{
						// HB behavior: advance the path index instead of mutating the path.
						_currentPathIndex = lastVisible;
						_suppressDriftCheck = true;
						_suppressDriftCheckIndex = lastVisible;

						// HB 6.2.3 logs only when skipping 2+ nodes.
						if (lastVisible >= 2)
							Logging.WriteDebug("Skipped {0} path nodes", lastVisible);
					}
				}
			}

			// Move along path
			// HB 4.3.4: Check both 2D distance and Z difference, push waypoint ahead
			if (_currentPath.Count > 0 && _currentPathIndex < _currentPath.Count)
			{
				// P6.14 — Combat abort during path following: when combat starts mid-path,
				// abandon the current path and let the behavior tree's combat routine take over.
				// This is the synchronous equivalent of HB 6.2.3's 4-second combat abort timeout.
				// We clear the path so next MoveTo() call will use the direct-movement shortcut above.
				if (me.Combat)
				{
					// BUG FIX: Don't set _destination = WoWPoint.Zero here.
					// That caused the next MoveTo() call to see destinationChanged=true,
					// which reset StuckHandler mid-unstick sequence.
					// Instead, just clear the path — the destination stays so we don't
					// reset stuck state when combat ends and we resume the same path.
					_currentPath.Clear();
					_currentPathIndex = 0;
						_suppressDriftCheck = false;
						_suppressDriftCheckIndex = 0;
					_currentFlags = null;
					_currentPolyTypes = null;
					_currentAbilityFlags = null;
					_pathRegenThrottle = new WaitTimer(TimeSpan.FromMilliseconds(500)); // Allow immediate regen after combat
					return MoveResult.Moved;
				}

				// HB 6.2.3: Once in elevator mode (method_20), keep calling the elevator handler
				// every tick regardless of player position relative to offmesh start.
				// This prevents the offmesh precision check from pulling the bot back to the gate.
				if (_inElevatorSequence)
				{
					var elevResult = HandleElevator(me, _elevatorTarget);
					if (elevResult != null)
						return elevResult.Value;
					// HandleElevator returned null = ride complete.
					// Force path regeneration from current position: the old path's
					// remaining waypoints were rooted at the offmesh endpoint, which may
					// be offset from the actual elevator exit (mmap-extractor tiles).
					_inElevatorSequence = false;
					_elevatorTarget = WoWPoint.Zero;
					_stuckCheckTimer.Reset();
					_unstickAttempts = 0;
					try { StuckHandler.Reset(); } catch { }
					_currentPath.Clear();
					_currentPathIndex = 0;
					_suppressDriftCheck = false;
					_suppressDriftCheckIndex = 0;
					_currentFlags = null;
					_currentPolyTypes = null;
					_currentAbilityFlags = null;
					return MoveResult.Moved;
				}

				// Door handling (HB 6.2.3 method_7): auto-detect and interact with closed doors on path
				var doorResult = HandleDoors(me);
				if (doorResult != null)
					return doorResult.Value;

				// Stuck detection — integrated directly in MoveTo() to cover ALL callers
				// (ActionMoveToPoi, corpse run, loot, hotspot, plugins, etc.)
				// Suppress stuck detection when at an off-mesh connection point (elevator, portal):
				// the bot may be intentionally stopped, waiting for elevator to arrive.
				bool isAtOffMesh = _currentFlags != null && _currentPathIndex < _currentFlags.Length
					&& (_currentFlags[_currentPathIndex] & TripperNav.StraightPathFlags.OffMeshConnection) != 0;
				if (_stuckCheckTimer.IsFinished && !isAtOffMesh && !_ridingElevator && !_inElevatorSequence)
				{
					_stuckCheckTimer.Reset();
					if (StuckHandler.IsStuck())
					{
						_unstickAttempts++;
						if (_unstickAttempts >= MaxUnstickAttempts)
						{
							// AUDIT FIX: After MaxUnstickAttempts failed attempts, force path regeneration
							_unstickAttempts = 0;
							// Force path regen by clearing path. Keep _destination so we don't
							// accidentally trigger a full StuckHandler.Reset() on next call.
							// StuckHandler.Reset() IS appropriate here since we're starting fresh.
							_currentPath.Clear();
							_currentPathIndex = 0;
							_suppressDriftCheck = false;
							_suppressDriftCheckIndex = 0;
							_pathRegenThrottle = new WaitTimer(TimeSpan.FromMilliseconds(500)); // Allow immediate regen
							try { StuckHandler.Reset(); } catch { }
							return MoveResult.Failed;
						}
						StuckHandler.Unstick();
						return MoveResult.UnstuckAttempt;
					}
				}

				WoWPoint nextPoint = _currentPath[_currentPathIndex];

				// Path validity check (P6.7): if player has drifted far from the current path segment
				// (knockback, teleport, fear, etc.), force path regeneration instead of following stale path
				if (_currentPathIndex > 0 && !_ridingElevator && !_inElevatorSequence
				    && !(_suppressDriftCheck && _currentPathIndex == _suppressDriftCheckIndex))
				{
					WoWPoint prevPoint = _currentPath[_currentPathIndex - 1];
					float distToSegment = DistanceToLineSegment(me.Location, prevPoint, nextPoint);
					if (distToSegment > PathPrecision * 5f) // >10 yards off path = stale
					{
						// BUG FIX: Don't set _destination = WoWPoint.Zero here.
						// That caused destinationChanged=true on next MoveTo(), resetting
						// StuckHandler mid-unstick sequence. Instead, just clear the path
						// so it gets regenerated, but keep _destination intact.
						_currentPath.Clear();
						_currentPathIndex = 0;
						_suppressDriftCheck = false;
						_suppressDriftCheckIndex = 0;
						_pathRegenThrottle = new WaitTimer(TimeSpan.FromMilliseconds(500)); // Allow immediate regen
						return MoveResult.Moved;
					}
				}
				
				// HB 6.2.3 method_27: waypoint reached = 2D distance² ≤ precision² AND |ΔZ| < 4.5
				bool isFinalPoint = (_currentPathIndex == _currentPath.Count - 1);
				float waypointPrecision = isFinalPoint ? precision : PathPrecision;
				float distance2DSqr = me.Location.Distance2DSqr(nextPoint);
				float zDiff = Math.Abs(me.Location.Z - nextPoint.Z);
				bool reachedWaypoint = distance2DSqr <= waypointPrecision * waypointPrecision && zDiff < 4.5f;
				
				if (reachedWaypoint)
				{
					// Off-mesh guard: if this waypoint is an off-mesh entry point (elevator, portal),
					// require tighter precision before advancing — don't skip critical transition points
					if (_currentFlags != null && _currentPathIndex < _currentFlags.Length &&
					    (_currentFlags[_currentPathIndex] & TripperNav.StraightPathFlags.OffMeshConnection) != 0)
					{
						const float offMeshPrecision = 1.0f; // 1 yard tight precision for off-mesh points
						if (distance2DSqr > offMeshPrecision * offMeshPrecision)
						{
							// Not close enough to off-mesh connection point yet, click directly to it
							WoWMovement.ClickToMove(nextPoint);
							return MoveResult.Moved;
						}
						
						// Dispatch off-mesh connection based on AreaType (ported from HB 4.3.4 method_4)
						// The offmesh START is at _currentPathIndex, the offmesh END is at _currentPathIndex+1.
						// We pass the END point as the target for elevator/portal handling — that's where
						// the bot needs to go. The START point is where the bot already is.
						// Get offmesh end point (next waypoint after this connection)
						WoWPoint offMeshEnd = (_currentPathIndex + 1 < _currentPath.Count)
							? _currentPath[_currentPathIndex + 1]
							: nextPoint; // Fallback to start if no next point

						if (_currentPolyTypes != null && _currentPathIndex < _currentPolyTypes.Length)
						{
							var offMeshResult = HandleOffMeshConnection(me, offMeshEnd, _currentPolyTypes[_currentPathIndex]);
							if (offMeshResult != null)
								return offMeshResult.Value;
						}
						else
						{
							// PolyTypes not available (mmap-extractor paths don't return them).
							// Use geometry-based fallback: HandleOffMeshConnection detects
							// elevators via Z-delta regardless of AreaType.
							var offMeshResult = HandleOffMeshConnection(me, offMeshEnd, TripperNav.AreaType.Ground);
							if (offMeshResult != null)
								return offMeshResult.Value;
						}
					}

					// Reset stuck timer and unstick counter on successful waypoint advance
					_stuckCheckTimer.Reset();
					_unstickAttempts = 0;
					try { StuckHandler.Reset(); } catch { }

					_currentPathIndex++;
					if (_suppressDriftCheck && _currentPathIndex > _suppressDriftCheckIndex)
					{
						_suppressDriftCheck = false;
						_suppressDriftCheckIndex = 0;
					}
					if (_currentPathIndex >= _currentPath.Count)
					{
						// HB 6.2.3 method_24: if we reached the end of a partial path,
						// return Failed (not ReachedDestination) so the behavior tree
						// knows the bot didn't actually arrive at the goal.
						if (_isPartialPath)
							return MoveResult.Failed;
						return MoveResult.ReachedDestination;
					}
					nextPoint = _currentPath[_currentPathIndex];
				}

				// HB 6.2.3 method_26: For CTM mover, push waypoint slightly ahead in movement
				// direction to prevent character from stopping at each intermediate waypoint.
				// Validate with a single navmesh raycast (if extended point is off-mesh, use exact waypoint).
				WoWPoint clickPoint = nextPoint;

				// HB 4.3.4 method_6 / HB 6.2.3 method_25: add +2f to waypoint Z when
				// traversing Water or Lava polygons. Without this, the bot tries to walk on
				// the water surface mesh and can sink/drown or take lava damage.
				if (_currentPolyTypes != null && _currentPathIndex < _currentPolyTypes.Length)
				{
					var polyType = _currentPolyTypes[_currentPathIndex];
					if (polyType == TripperNav.AreaType.Water || polyType == TripperNav.AreaType.Lava)
					{
						clickPoint = new WoWPoint(clickPoint.X, clickPoint.Y, clickPoint.Z + 2f);
					}
				}

				if (!isFinalPoint)
				{
					WoWPoint direction = nextPoint - me.Location;
					float length = (float)Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
					if (length > 0.01f)
					{
						direction = new WoWPoint(direction.X / length, direction.Y / length, direction.Z / length);
						WoWPoint pushedPoint = nextPoint + direction * PathPrecision;

						// Single raycast validation: ensure our direct line from the player to the pushed point
						// remains on the navmesh. Validating only nextPoint->pushedPoint allows corner-cutting.
						if (!Raycast(me.Location, pushedPoint, out _))
						{
							clickPoint = pushedPoint;
						}
					}
				}

				WoWMovement.ClickToMove(clickPoint);
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

			// TEST: Commenté pour isoler crash - EnsureTilesAroundPosition suspect
			// try
			// {
			// 	TripperNavigator.EnsureTilesAroundPosition(mapId, startVec, LoadTilesAroundRadius);
			// 	TripperNavigator.EnsureTilesAroundPosition(mapId, endVec, LoadTilesAroundRadius);
			// }
			// catch { }

			BlackspotManager.EnsureBlackspotsMarked();

			var result = TripperNavigator.FindPath(mapId, startVec, endVec, true);
			
			// HB 4.3.4 MeshNavigator.CanNavigateFully:
			// path.Succeeded && !path.IsPartialPath
			if (result.Status.Succeeded && !result.IsPartialPath)
			{
				return true;
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

			// TEST: Commenté pour isoler crash - EnsureTilesAroundPosition suspect
			// try
			// {
			// 	TripperNavigator.EnsureTilesAroundPosition(mapId, startVec, LoadTilesAroundRadius);
			// 	TripperNavigator.EnsureTilesAroundPosition(mapId, endVec, LoadTilesAroundRadius);
			// }
			// catch { }

			BlackspotManager.EnsureBlackspotsMarked();

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

				var status = TripperNavigator.Raycast(mapId, start, end, out float hitT, out _);
				if (status.Succeeded)
				{
					// Hit something before reaching next waypoint (hitT < 1.0 means hit)
					if (hitT < 0.99f)
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

			var status = TripperNavigator.Raycast(mapId, startVec, endVec, out float hitT, out _);
			if (status.Succeeded && hitT < 1.0f)
			{
				// Calculate hit position along the ray
				var direction = endVec - startVec;
				var hitVec = startVec + direction * hitT;
				hitPosition = new WoWPoint(hitVec.X, hitVec.Y, hitVec.Z);
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

		/// <summary>
		/// Calculates the shortest distance from a point to a line segment (2D, XY plane).
		/// Used for path validity checking — detects when player has drifted off the current path.
		/// </summary>
		private static float DistanceToLineSegment(WoWPoint point, WoWPoint segA, WoWPoint segB)
		{
			float dx = segB.X - segA.X;
			float dy = segB.Y - segA.Y;
			float lenSqr = dx * dx + dy * dy;

			if (lenSqr < 0.0001f)
			{
				// Segment is essentially a point
				return point.Distance(segA);
			}

			// Project point onto segment, clamped to [0, 1]
			float t = ((point.X - segA.X) * dx + (point.Y - segA.Y) * dy) / lenSqr;
			t = Math.Max(0f, Math.Min(1f, t));

			// Closest point on segment
			float closestX = segA.X + t * dx;
			float closestY = segA.Y + t * dy;
			float closestZ = segA.Z + t * (segB.Z - segA.Z);

			float ex = point.X - closestX;
			float ey = point.Y - closestY;
			float ez = point.Z - closestZ;
			return (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
		}

		/// <summary>
		/// Handles off-mesh connection dispatch based on AreaType.
		/// Ported from HB 4.3.4 MeshNavigator.method_4 / HB 6.2.3 method_18.
		/// Returns null to continue normal waypoint advancement, or a MoveResult to return immediately.
		/// </summary>
		/// <remarks>
		/// MaNGOS mmap-extractor bakes offmesh connections with Recast default area type 63
		/// (RC_WALKABLE_AREA). Additionally, the Detour straightPathPolys array sometimes maps
		/// the offmesh start vertex to the adjacent GROUND poly (AreaType=1) rather than the
		/// offmesh poly itself, depending on path geometry. Therefore we detect elevator by
		/// Z-delta FIRST, regardless of AreaType — this is the only reliable indicator.
		/// </remarks>
		private static MoveResult? HandleOffMeshConnection(LocalPlayer me, WoWPoint targetPoint, TripperNav.AreaType areaType)
		{
			// Detect elevator by Z-delta geometry FIRST, regardless of AreaType.
			// This handles both RC_WALKABLE_AREA=63 (mmap-extractor default) and Ground=1
			// (when Detour assigns the ground poly at the offmesh start vertex).
			float zDelta = Math.Abs(me.Z - targetPoint.Z);
			if (zDelta > 10f)
			{
				return HandleElevator(me, targetPoint);
			}
			switch (areaType)
			{
				case TripperNav.AreaType.Elevator:
					return HandleElevator(me, targetPoint);

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
					// Standard run/jump offmesh — walk to target, advance path
					WoWMovement.ClickToMove(targetPoint);
					return null;
			}
		}

		/// <summary>
		/// Standard off-mesh connection handler for Run/Jump connections.
		/// Ported from HB 6.2.3 MeshNavigator.method_19.
		/// NOTE: MaNGOS mmap-extractor sets default flags (255) on all offmesh connections.
		/// These don't match HB's custom flag convention, so we only check ability flags
		/// when the area type was set by HB's custom mesh builder (not Recast default 63).
		/// For mmap-extractor connections, HandleOffMeshConnection auto-detects by geometry.
		/// </summary>
		private static MoveResult? HandleStandardOffMesh(LocalPlayer me, WoWPoint targetPoint)
		{
			// Check ability flags — only meaningful for HB-format meshes where
			// the area type was explicitly set (not RC_WALKABLE_AREA = 63).
			// mmap-extractor offmesh connections are handled in HandleOffMeshConnection
			// before reaching here, so this code path is only for HB-compatible tiles.
			if (_currentAbilityFlags != null && _currentPathIndex < _currentAbilityFlags.Length)
			{
				var abilityFlags = _currentAbilityFlags[_currentPathIndex];

				// Jump connection — dismount first if mounted (can't jump mounted in WotLK)
				if ((abilityFlags & TripperNav.AbilityFlags.Jump) != 0)
				{
					if (me.Mounted)
					{
						Mount.Dismount();
						return MoveResult.Moved;
					}
				}
			}

			// Standard Run/Jump — click to the target point and advance
			WoWMovement.ClickToMove(targetPoint);
			return null; // Advance to next waypoint normally
		}

		/// <summary>
		/// Elevator handling — matches HB 4.3.4 MeshNavigator (AreaType.Elevator).
		/// Called every tick while at an elevator offmesh connection.
		/// </summary>
		private static MoveResult? HandleElevator(LocalPlayer me, WoWPoint targetPoint)
		{
			static WoWPoint GetTransportWorldLocation(WoWGameObject transportGo)
			{
				if (transportGo.SubType == WoWGameObjectType.Transport
				    || transportGo.SubType == WoWGameObjectType.MapObjectTransport)
				{
					try
					{
						Tripper.Tools.Math.Matrix worldMatrix = transportGo.GetWorldMatrix();
						Matrix4x4 mat = worldMatrix;
						Vector3 translation = mat.Translation;
						return new WoWPoint(translation.X, translation.Y, translation.Z);
					}
					catch
					{
						// Fall back to base position if matrix read fails
					}
				}
				return transportGo.Location;
			}

			// HB 4.3.4: reset stuck handler every tick
			try { StuckHandler.Reset(); } catch { }
			_stuckCheckTimer.Reset();

			// HB 6.2.3-style lock: keep calling elevator handler every tick once entered
			if (!_inElevatorSequence)
			{
				_inElevatorSequence = true;
				_elevatorTarget = targetPoint;
				_hbElevatorFlag = false;
			}

			WoWPoint playerPos = me.Location;

			// HB 4.3.4: find nearest transport GO
			var transport = ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
				.Where(go => go.SubType == WoWGameObjectType.Transport
				              && go.Entry != 20657 && go.Entry != 20656)
				.OrderBy(go => GetTransportWorldLocation(go).Distance2DSqr(playerPos))
				.FirstOrDefault();

			WoWPoint endPoint = targetPoint; // offmesh END
			WoWPoint startPoint = (_currentPathIndex >= 0 && _currentPathIndex < _currentPath.Count)
				? _currentPath[_currentPathIndex]
				: playerPos; // offmesh START (waiting spot)

			if (transport == null)
			{
				Logging.WriteDiagnostic("There is no elevator around. Something is wrong");
				return MoveResult.Failed;
			}

			WoWPoint transportWorldLocation = GetTransportWorldLocation(transport);

			// HB 4.3.4: if at endpoint, complete offmesh and reset flag
			if (playerPos.DistanceSqr(endPoint) < 4f)
			{
				_hbElevatorFlag = false;
				return null;
			}

			if (me.IsOnTransport)
			{
				_ridingElevator = true;
				if (Math.Abs(playerPos.Z - endPoint.Z) > 2f)
				{
					Styx.Logic.BehaviorTree.TreeRoot.StatusText = "Waiting for elevator to reach end point";
					return MoveResult.Moved;
				}
				if (!_hbElevatorFlag)
					_hbElevatorFlag = true;
				Logging.WriteDiagnostic("Moving out of elevator");
				WoWMovement.ClickToMove(endPoint);
				return MoveResult.Moved;
			}

			// NOT on transport
			_ridingElevator = false;
			if (Math.Abs(transportWorldLocation.Z - playerPos.Z) > 2f)
			{
				if (me.Location.DistanceSqr(startPoint) > 4f)
				{
					Logging.WriteDiagnostic("Woops, we are not in the waiting spot");
					WoWMovement.ClickToMove(startPoint);
					return MoveResult.Moved;
				}
				Styx.Logic.BehaviorTree.TreeRoot.StatusText = "Waiting for the elevator";
				return MoveResult.Moved;
			}

			if (!_hbElevatorFlag)
			{
				Logging.WriteDiagnostic("Moving inside elevator");
				if (me.Mounted)
					Mount.Dismount();
				// Center on the platform like HB: move toward transport.Location (world-space)
				WoWMovement.ClickToMove(transportWorldLocation);
				return MoveResult.Moved;
			}

			return MoveResult.Failed;
		}

		/// <summary>
		/// Portal handling: find nearest Goober or SpellCaster game object and interact.
		/// HB 4.3.4 pattern from MeshNavigator.method_4.
		/// </summary>
		private static MoveResult? HandlePortal(LocalPlayer me)
		{
			const float maxSearchDistSqr = 900f; // 30 yards squared
			WoWGameObject? bestPortal = null;
			float bestDistSqr = maxSearchDistSqr;

			foreach (var go in ObjectManager.GetObjectsOfType<WoWGameObject>())
			{
				if (go.SubType == WoWGameObjectType.Goober || go.SubType == WoWGameObjectType.SpellCaster)
				{
					float distSqr = go.Location.DistanceSqr(me.Location);
					if (distSqr < bestDistSqr)
					{
						bestDistSqr = distSqr;
						bestPortal = go;
					}
				}
			}

			if (bestPortal == null)
			{
				Logging.WriteDiagnostic("Could not find portal to take.");
				return MoveResult.Failed;
			}

			if (bestPortal.WithinInteractRange)
			{
				Logging.WriteDiagnostic("Interacting with:{0}", bestPortal.Name);
				bestPortal.Interact();
				return MoveResult.Moved;
			}

			WoWMovement.ClickToMove(bestPortal.Location);
			return MoveResult.Moved;
		}

		/// <summary>
		/// InteractUnit handling: find nearest non-hostile, alive NPC and interact.
		/// HB 4.3.4 pattern from MeshNavigator.method_4.
		/// AUDIT FIX: Filter out hostile units, dead units, and player-controlled units
		/// to avoid targeting enemy mobs during off-mesh traversal.
		/// </summary>
		private static MoveResult? HandleInteractUnit(LocalPlayer me)
		{
			// AUDIT FIX: Add 30yd range limit to prevent interacting with distant NPCs
			const float maxSearchDistSqr = 900f; // 30 yards squared
			var unit = ObjectManager.GetObjectsOfType<WoWUnit>(false, false)
				.Where(u => !u.IsDead && !u.IsHostile && !u.PlayerControlled && !u.IsPlayer
				            && u.Location.DistanceSqr(me.Location) < maxSearchDistSqr)
				.OrderBy(u => u.Location.DistanceSqr(me.Location))
				.FirstOrDefault();

			if (unit == null)
			{
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
			return MoveResult.Moved;
		}

		/// <summary>
		/// InteractObject handling: find nearest interactable game object and interact.
		/// HB 4.3.4 pattern from MeshNavigator.method_4.
		/// </summary>
		private static MoveResult? HandleInteractObject(LocalPlayer me)
		{
			// AUDIT FIX: Add 30yd range limit + move to object if in range but not interact range
			const float maxSearchDistSqr = 900f; // 30 yards squared
			var gameObject = ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
				.Where(go => go.Location.DistanceSqr(me.Location) < maxSearchDistSqr)
				.OrderBy(go => go.Location.DistanceSqr(me.Location))
				.FirstOrDefault();

			if (gameObject == null)
			{
				// No interactable object in range — skip this off-mesh point
				return null;
			}

			if (!gameObject.WithinInteractRange)
			{
				WoWMovement.ClickToMove(gameObject.Location);
				return MoveResult.Moved;
			}

			if (me.Mounted)
				Mount.Dismount("InteractObject in path");
			gameObject.Interact();
			return MoveResult.Moved;
		}

		/// <summary>
		/// Logs the result of a pathfinding operation.
		/// Matches HB 6.2.3 MeshNavigator.method_12 logging pattern.
		/// </summary>
		private static void LogPathResult(Tripper.Navigation.PathFindResult result, WoWPoint start, WoWPoint end, uint mapId)
		{
			_ = result;
			_ = start;
			_ = end;
			_ = mapId;
		}

		/// <summary>
		/// Door handling: auto-detect closed doors on the path and interact to open them.
		/// Ported from HB 6.2.3 MeshNavigator.method_7/method_8.
		/// Improvement: steers through the door center point before resuming normal path,
		/// so the bot passes cleanly through the doorframe like HB does.
		/// Returns null if no door action needed, MoveResult.Moved if interacting with a door.
		/// </summary>
		private static WoWPoint _doorCenterTarget = WoWPoint.Zero;
		
		private static MoveResult? HandleDoors(LocalPlayer me)
		{
			// If we're actively steering through a door center, continue until we pass through
			if (_doorCenterTarget != WoWPoint.Zero)
			{
				float distToDoorCenter = me.Location.Distance(_doorCenterTarget);
				if (distToDoorCenter < 1.5f)
				{
					// Passed through door center — resume normal path following
					_doorCenterTarget = WoWPoint.Zero;
					return null;
				}
				// Keep steering toward door center
				WoWMovement.ClickToMove(_doorCenterTarget);
				return MoveResult.Moved;
			}
			
			// Cooldown between door interactions to avoid spam
			if (!_doorInteractTimer.IsFinished)
				return null;

			// Find closed/ready Door-type GameObjects within 10 yards
			const float doorSearchDistSqr = 100f; // 10 yards squared
			WoWGameObject? closestDoor = null;
			float closestDistSqr = doorSearchDistSqr;

			foreach (var go in ObjectManager.GetObjectsOfType<WoWGameObject>(false, false))
			{
				if (go.SubType != WoWGameObjectType.Door)
					continue;

				// State.Ready = closed and interactable; Active = already open
				if (go.State != WoWGameObjectState.Ready)
					continue;

				// Skip locked doors (can't open without key)
				if (go.Locked)
					continue;

				float distSqr = go.Location.DistanceSqr(me.Location);
				if (distSqr < closestDistSqr)
				{
					// Verify the door is actually on our path direction
					// (avoid opening random doors that are beside us but not blocking)
					if (_currentPathIndex < _currentPath.Count)
					{
						WoWPoint nextWp = _currentPath[_currentPathIndex];
						// Door must be roughly between us and the next waypoint
						float distToNext = go.Location.DistanceSqr(nextWp);
						float playerToNext = me.Location.DistanceSqr(nextWp);
						if (distToNext > playerToNext)
							continue; // Door is behind us relative to movement direction
					}

					closestDistSqr = distSqr;
					closestDoor = go;
				}
			}

			if (closestDoor == null)
				return null;

			// Move toward door center if not in interact range
			if (!closestDoor.WithinInteractRange)
			{
				// Steer to door center (not nearby waypoint) to pass through cleanly
				WoWMovement.ClickToMove(closestDoor.Location);
				return MoveResult.Moved;
			}

			// HB 6.2.3 pattern: stop before interacting
			if (me.IsMoving)
			{
				WoWMovement.MoveStop();
				return MoveResult.Moved;
			}

			// Interact to open the door
			closestDoor.Interact();
			_doorInteractTimer.Reset();
			
			// Set door center as steering target — bot will walk through the center
			// of the doorframe before resuming normal waypoint following.
			// Calculate a point slightly past the door in our movement direction.
			if (_currentPathIndex < _currentPath.Count)
			{
				WoWPoint doorCenter = closestDoor.Location;
				WoWPoint nextWp = _currentPath[_currentPathIndex];
				WoWPoint dir = nextWp - doorCenter;
				float dirLen = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
				if (dirLen > 0.01f)
				{
					// Target is 2 yards past the door center in path direction
					dir = new WoWPoint(dir.X / dirLen, dir.Y / dirLen, dir.Z / dirLen);
					_doorCenterTarget = doorCenter + dir * 2.0f;
				}
				else
				{
					_doorCenterTarget = doorCenter;
				}
			}
			else
			{
				_doorCenterTarget = closestDoor.Location;
			}

			return MoveResult.Moved;
		}
	}
}
