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

		// Elevator sequence tracking (HB 6.2.3: once method_18 enters method_20, the elevator
		// handler keeps being called every tick via the path index — no re-checking player
		// position vs offmesh start). This flag locks the bot into elevator mode.
		private static bool _inElevatorSequence;
		private static WoWPoint _elevatorTarget = WoWPoint.Zero;

		// Elevator movement tracking (HB 6.2.3 method_20: poll every 400ms to detect moving/stopped)
		private static WoWPoint _lastElevatorPos = WoWPoint.Zero;
		private static WaitTimer _elevatorPollTimer = new WaitTimer(TimeSpan.FromMilliseconds(400));
		private static bool _isElevatorMoving;
		private static DateTime _elevatorWaitStart = DateTime.MinValue; // Timeout for elevator wait
		// Elevator ride direction tracking — used to detect stop at destination level
		private static float _lastRideZ;           // playerZ from previous tick while riding
		private static bool _elevatorHasMoved;     // True once playerZ changes significantly from boarding
		private static bool _elevatorExitRequested; // True once direction reversal detected → walk off

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
				Logging.WriteDebug("[Navigator] Cancelled mount-up while riding elevator");
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
			Logging.WriteDebug("[Navigator] Changed map(s) from {0} to {1}. Path cleared.", args.OldMapId, args.NewMapId);
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
					Logging.WriteDebug("[Navigator] Failed to set faction filter: {0}", ex.Message);
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
				Logging.WriteDebug("[Navigator] Failed to initialize FlightPaths: {0}", ex.Message);
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
			_lastElevatorPos = WoWPoint.Zero;
			_elevatorPollTimer = new WaitTimer(TimeSpan.FromMilliseconds(400));
			_isElevatorMoving = false;
			_lastRideZ = 0f;
			_elevatorHasMoved = false;
			_elevatorExitRequested = false;
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
				Logging.WriteDebug("[Navigator] MoveTo: destination is WoWPoint.Zero — returning Failed");
				return MoveResult.Failed;
			}

			// Check for NaN coordinates - would crash WoW with INT_DIVIDE_BY_ZERO
			if (float.IsNaN(destination.X) || float.IsNaN(destination.Y) || float.IsNaN(destination.Z))
			{
				Logging.WriteDebug("[Navigator] ERROR: Destination contains NaN coordinates, aborting movement");
				return MoveResult.Failed;
			}

			LocalPlayer? me = ObjectManager.Me;
			if (me == null)
			{
				Logging.WriteDebug("[Navigator] MoveTo: ObjectManager.Me is null — returning Failed");
				return MoveResult.Failed;
			}

			// Check if we're already at the destination
			float distance = me.Location.Distance(destination);
			if (distance < precision)
			{
				Logging.WriteDebug("[Navigator] MoveTo: Already at destination (dist={0:F1} < precision={1:F1})", distance, precision);
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
				Logging.WriteDebug("[Navigator] Mount.ShouldMount/StateMount exception: {0}", ex.Message);
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
				Logging.WriteDebug("[Navigator] Mount.ShouldDismount/Dismount exception: {0}", ex.Message);
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
			Logging.WriteDebug("[Navigator] MoveTo: dest={0}, destChanged={1}, needsRegen={2}, pathCount={3}",
				destination, destinationChanged, needsPathRegen, _currentPath.Count);
			if (destinationChanged || needsPathRegen)
			{
				if (!_pathRegenThrottle.IsFinished)
				{
					// Too soon since last path generation — wait (HB pattern: never blindly ClickToMove)
					Logging.WriteDebug("[Navigator] MoveTo: pathRegenThrottle not finished — returning Moved (silent wait)");
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
					_lastElevatorPos = WoWPoint.Zero;
					_isElevatorMoving = false;
					_lastRideZ = 0f;
					_elevatorHasMoved = false;
					_elevatorExitRequested = false;
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
					Logging.WriteDebug("[Navigator] In combat — using direct movement to: {0}", destinationName);
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
							Logging.Write("[Navigator] Flight path would be faster — setting taxi POI");
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

					// Ensure blackspots are marked on navmesh before pathfinding
					// This is HB 4.3.4's OnTileLoaded workaround
					BlackspotManager.EnsureBlackspotsMarked();

					Logging.WriteDebug("[Navigator] FindPath: ({0:F0},{1:F0},{2:F0}) -> ({3:F0},{4:F0},{5:F0}) map={6}",
						start.X, start.Y, start.Z, end.X, end.Y, end.Z, mapId);
					var sw = System.Diagnostics.Stopwatch.StartNew();
					var result = TripperNavigator.FindPath(mapId, start, end, true);
					sw.Stop();
					Logging.WriteDebug("[Navigator] FindPath returned in {0}ms — Succeeded={1}, Points={2}, Partial={3}",
						sw.ElapsedMilliseconds, result.Succeeded,
						result.Points?.Length ?? 0, result.IsPartialPath);
					LogPathResult(result, me.Location, destination, mapId);
					if (result.Status.Succeeded && result.Points != null && result.Points.Length > 0)
					{
						foreach (var point in result.Points)
						{
							_currentPath.Add(new WoWPoint(point.X, point.Y, point.Z));
						}

						// Store path metadata for off-mesh/terrain handling (P4.1 fix)
						_currentFlags = result.Flags;
						_currentPolyTypes = result.PolyTypes;
						_currentAbilityFlags = result.AbilityFlags;
					}
					else
					{
						// HB pattern: path generation failed — stop and return failure.
						// Never blindly ClickToMove (causes walking into walls, off cliffs,
						// climbing impossible hills, and "random walking" behavior).
						WoWMovement.MoveStop();
						Logging.Write("[Navigator] Path generation failed — staying still");
						return MoveResult.PathGenerationFailed;
					}
				}
				else
				{
					// HB pattern: no navmesh available — stop and return failure.
					// Never blindly ClickToMove without navigation data.
					WoWMovement.MoveStop();
					Logging.Write("[Navigator] No navmesh loaded — staying still (destination: {0})", destinationName);
					return MoveResult.PathGenerationFailed;
				}

				_currentPathIndex = 0;

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
						// Remove skipped waypoints from the path instead of advancing _currentPathIndex.
						// This keeps _currentPathIndex = 0, preventing the drift detection from comparing
						// player position against a segment that is far ahead (which caused infinite
						// path regeneration loops — drift detected → regen → skip → drift → regen).
						_currentPath.RemoveRange(0, lastVisible);
						if (_currentFlags != null && _currentFlags.Length > lastVisible)
							_currentFlags = _currentFlags[lastVisible..];
						if (_currentPolyTypes != null && _currentPolyTypes.Length > lastVisible)
							_currentPolyTypes = _currentPolyTypes[lastVisible..];
						if (_currentAbilityFlags != null && _currentAbilityFlags.Length > lastVisible)
							_currentAbilityFlags = _currentAbilityFlags[lastVisible..];
						Logging.WriteDebug("[Navigator] Skipped {0} visible early waypoints", lastVisible);
					}
				}
			}

			// Move along path
			// HB 4.3.4: Check both 2D distance and Z difference, push waypoint ahead
			if (_currentPath.Count > 0 && _currentPathIndex < _currentPath.Count)
			{
				Logging.WriteDebug("[Navigator] Path following: pathCount={0}, index={1}, nextWP={2}",
					_currentPath.Count, _currentPathIndex, _currentPath[_currentPathIndex]);
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
					_currentFlags = null;
					_currentPolyTypes = null;
					_currentAbilityFlags = null;
					_pathRegenThrottle = new WaitTimer(TimeSpan.FromMilliseconds(500)); // Allow immediate regen after combat
					Logging.WriteDebug("[Navigator] Combat detected — aborting path to let combat routine take over");
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
							Logging.Write("[Navigator] {0} unstick attempts failed — forcing path regeneration", _unstickAttempts);
							_unstickAttempts = 0;
							// Force path regen by clearing path. Keep _destination so we don't
							// accidentally trigger a full StuckHandler.Reset() on next call.
							// StuckHandler.Reset() IS appropriate here since we're starting fresh.
							_currentPath.Clear();
							_currentPathIndex = 0;
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
				if (_currentPathIndex > 0 && !_ridingElevator && !_inElevatorSequence)
				{
					WoWPoint prevPoint = _currentPath[_currentPathIndex - 1];
					float distToSegment = DistanceToLineSegment(me.Location, prevPoint, nextPoint);
					if (distToSegment > PathPrecision * 5f) // >10 yards off path = stale
					{
						Logging.WriteDebug("[Navigator] Player drifted {0:F1}yd from path — regenerating", distToSegment);
						// BUG FIX: Don't set _destination = WoWPoint.Zero here.
						// That caused destinationChanged=true on next MoveTo(), resetting
						// StuckHandler mid-unstick sequence. Instead, just clear the path
						// so it gets regenerated, but keep _destination intact.
						_currentPath.Clear();
						_currentPathIndex = 0;
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
						Logging.WriteDebug("[Navigator] Reached off-mesh connection at waypoint {0}", _currentPathIndex);
						
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
							Logging.WriteDebug("[Navigator] Off-mesh dispatch: PolyTypes unavailable, using geometry fallback");
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
					if (_currentPathIndex >= _currentPath.Count)
					{
						return MoveResult.ReachedDestination;
					}
					nextPoint = _currentPath[_currentPathIndex];
				}

				// HB 6.2.3 method_26: For CTM mover, push waypoint slightly ahead in movement
				// direction to prevent character from stopping at each intermediate waypoint.
				// Validate with a single navmesh raycast (if extended point is off-mesh, use exact waypoint).
				WoWPoint clickPoint = nextPoint;

				if (!isFinalPoint)
				{
					WoWPoint direction = nextPoint - me.Location;
					float length = (float)Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
					if (length > 0.01f)
					{
						direction = new WoWPoint(direction.X / length, direction.Y / length, direction.Z / length);
						WoWPoint pushedPoint = nextPoint + direction * PathPrecision;

						// Single raycast validation: check if pushed point is reachable on navmesh
						if (!Raycast(nextPoint, pushedPoint, out _))
						{
							clickPoint = pushedPoint;
						}
					}
				}

				WoWMovement.ClickToMove(clickPoint);
				return MoveResult.Moved;
			}

			Logging.WriteDebug("[Navigator] MoveTo: No path to follow (pathCount={0}, index={1}) — returning PathGenerationFailed",
				_currentPath.Count, _currentPathIndex);
			return MoveResult.PathGenerationFailed;
		}

		public static bool CanNavigateFully(WoWPoint start, WoWPoint destination)
		{
			if (!IsNavigatorLoaded)
				return true; // Assume we can if no mesh loaded

			uint mapId = (uint)(GetCurrentMapId());
			var startVec = new Vector3(start.X, start.Y, start.Z);
			var endVec = new Vector3(destination.X, destination.Y, destination.Z);

			// Ensure blackspots are marked before pathfinding
			BlackspotManager.EnsureBlackspotsMarked();

			Logging.WriteDebug("[Navigator] CanNavigateFully: ({0:F0},{1:F0},{2:F0}) -> ({3:F0},{4:F0},{5:F0})",
				start.X, start.Y, start.Z, destination.X, destination.Y, destination.Z);
			var result = TripperNavigator.FindPath(mapId, startVec, endVec, true);
			Logging.WriteDebug("[Navigator] CanNavigateFully result: Succeeded={0}, Points={1}, Partial={2}",
				result.Succeeded, result.Points?.Length ?? 0, result.IsPartialPath);
			
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

			// Ensure blackspots are marked before pathfinding
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
				Logging.WriteDebug("[Navigator] Off-mesh dispatch: AreaType={0}, Z-delta={1:F1} — treating as Elevator, target={2}",
					(byte)areaType, zDelta, targetPoint);
				return HandleElevator(me, targetPoint);
			}

			// Small Z-delta: dispatch by AreaType for HB-format meshes
			Logging.WriteDebug("[Navigator] Off-mesh dispatch: AreaType={0}, Z-delta={1:F1}, target={2}",
				(byte)areaType, zDelta, targetPoint);
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
		/// Elevator handling — based on HB 6.2.3 MeshNavigator.method_20.
		/// Called every tick while at an elevator offmesh connection.
		/// Boat entries {20656, 20657, 205080} excluded (HB 6.2.3 uint_0 blacklist).
		///
		/// FIX: WoWGameObject.Location for Transport-type GOs reads the spawn/origin position
		/// (offsets 0xE8/0xEC/0xF0), not the current interpolated position. This means
		/// _isElevatorMoving is always false and |elevZ - playerZ| is always large.
		/// Workaround: use IsOnTransport for boarding detection, player Z for ride tracking,
		/// and a probe-walk approach for waiting instead of relying on transport Z.
		/// NOTE: Ghosts ride elevators normally in WoW 3.3.5a — no ghost bypass needed.
		/// ClickToMove cannot path through floors, so ghosts must take the elevator.
		/// </summary>
		private static MoveResult? HandleElevator(LocalPlayer me, WoWPoint targetPoint)
		{
			// HB 6.2.3 method_20 line 1: Reset stuck handler every tick.
			try { StuckHandler.Reset(); } catch { }
			_stuckCheckTimer.Reset();

			// Lock into elevator sequence (our equivalent of HB's areaType != Elevator check in method_18)
			if (!_inElevatorSequence)
			{
				_inElevatorSequence = true;
				_elevatorTarget = targetPoint;
				_elevatorWaitStart = DateTime.Now;
			}

			WoWPoint playerPos = me.Location;

			// HB 6.2.3 method_20: find closest Transport, exclude blacklist.
			// Door transports excluded (UC/TB animated doors are Transport type but not elevators).
			var transport = ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
				.Where(go => go.SubType == WoWGameObjectType.Transport
				              && go.Entry != 20656 && go.Entry != 20657 && go.Entry != 205080
				              && !go.Name.Contains("door", StringComparison.OrdinalIgnoreCase))
				.OrderBy(go => go.Location.Distance2DSqr(playerPos))
				.FirstOrDefault();

			// HB: vector = path.Points[Index] (offmesh END), vector2 = path.Points[Index-1] (offmesh START)
			WoWPoint endPoint = targetPoint;
			WoWPoint startPoint = _currentPath[_currentPathIndex];

			// HB: if (woWGameObject == null) → "There is no elevator around. Something is wrong" → Failed
			if (transport == null)
			{
				// Timeout: don't wait forever if no transport found
				if ((DateTime.Now - _elevatorWaitStart).TotalSeconds > 30)
				{
					Logging.Write("[Navigator] No elevator found after 30s — forcing path regeneration");
					_ridingElevator = false;
					_inElevatorSequence = false;
					_elevatorTarget = WoWPoint.Zero;
					_lastRideZ = 0f;
					_elevatorHasMoved = false;
					_elevatorExitRequested = false;
					return null;
				}
				Logging.WriteDiagnostic("There is no elevator around — waiting...");
				return MoveResult.Moved;
			}

			// HB: if at endPoint && !IsOnTransport && !IsFalling → advance path
			if (playerPos.DistanceSqr(endPoint) < 4f && !me.IsOnTransport && !me.IsFalling)
			{
				_ridingElevator = false;
				_lastRideZ = 0f;
				_elevatorHasMoved = false;
				_elevatorExitRequested = false;
				Logging.WriteNavigator("Elevator ride complete — at end point.");
				return null; // Advance past offmesh
			}

			// mmap-extractor adaptation: offmesh endpoint Z can be far from the actual
			// elevator stop level (e.g., endPoint.Z = -40.8 but elevator bottom is Z=16.4).
			// If player is at destination Z level, off transport, and closer to end than start
			// → consider the ride complete. Path will be regenerated from current position.
			if (!me.IsOnTransport && !me.IsFalling
			    && Math.Abs(playerPos.Z - endPoint.Z) <= 5f
			    && Math.Abs(startPoint.Z - playerPos.Z) > Math.Abs(endPoint.Z - playerPos.Z))
			{
				_ridingElevator = false;
				Logging.WriteNavigator("Elevator ride complete — at destination level.");
				return null;
			}

			// FIX: After successfully riding the elevator and stepping off the platform,
			// consider the ride complete. The offmesh endpoint Z from mmap-extractor may
			// be far from the actual elevator stop (e.g., -40.8 vs 16.4), so the Z-level
			// checks above won't match. Instead, trust that the elevator brought us to the
			// correct level and let the pathfinder regenerate from current position.
			if (_ridingElevator && !me.IsOnTransport && !me.IsFalling)
			{
				Logging.WriteNavigator("Elevator ride complete — stepped off after riding (playerZ={0:F1}).",
					playerPos.Z);
				_ridingElevator = false;
				_inElevatorSequence = false;
				_elevatorTarget = WoWPoint.Zero;
				_lastRideZ = 0f;
				_elevatorHasMoved = false;
				_elevatorExitRequested = false;
				return null;
			}

			WoWPoint elevatorLoc = transport.Location;

			// HB: poll elevator movement every 400ms (waitTimer_2)
			// NOTE: For Transport-type GOs, Location may return spawn position (static).
			// _isElevatorMoving will be false if transport position doesn't update in memory.
			if (_elevatorPollTimer.IsFinished)
			{
				_isElevatorMoving = _lastElevatorPos != WoWPoint.Zero
					&& _lastElevatorPos.DistanceSqr(elevatorLoc) > 0.0001f;
				_lastElevatorPos = elevatorLoc;
				_elevatorPollTimer.Reset();
			}

			// HB: if (activeMover.IsOnTransport)
			if (me.IsOnTransport)
			{
				_ridingElevator = true;
				_elevatorWaitStart = DateTime.Now; // Reset timeout while riding

				// If we've already decided to exit, keep walking toward exit point
				// until the bot steps off the platform (IsOnTransport becomes false).
				if (_elevatorExitRequested)
				{
					WoWPoint exitPoint = new WoWPoint(endPoint.X, endPoint.Y, playerPos.Z);
					WoWMovement.ClickToMove(exitPoint);
					Logging.WriteDebug("[Navigator] Walking off elevator (playerZ={0:F1})", playerPos.Z);
					return MoveResult.Moved;
				}

				// Direction-reversal exit detection:
				// The mmap-extractor offmesh endpoint Z is unreliable (may be far from the
				// actual elevator stop). Instead, we track the player Z while riding:
				// 1. Once playerZ changes significantly from boarding Z → _elevatorHasMoved
				// 2. Detect the elevator reversing direction (was descending, now ascending
				//    for a "going down" trip, or vice versa) → request exit
				if (_lastRideZ == 0f)
				{
					// First tick on transport — initialize tracking
					_lastRideZ = playerPos.Z;
					_elevatorHasMoved = false;
					Logging.WriteDebug("[Navigator] Riding elevator (playerZ={0:F1}, boarded)", playerPos.Z);
					WoWMovement.MoveStop();
					return MoveResult.Moved;
				}

				float deltaZ = playerPos.Z - _lastRideZ;

				// Mark as having moved once Z changes significantly from where we boarded
				if (Math.Abs(deltaZ) > 3f)
					_elevatorHasMoved = true;

				// Determine desired direction: if endPoint is below startPoint, we want bottom
				bool goingDown = endPoint.Z < startPoint.Z;

				// Detect direction reversal after the elevator has moved:
				// Going down: deltaZ was negative (descending), now positive (ascending)
				//   + playerZ is well below startPoint (we actually went down, not a jitter at top)
				// Going up:   deltaZ was positive (ascending), now negative (descending)
				//   + playerZ is well above startPoint
				if (_elevatorHasMoved)
				{
					bool shouldExit = false;
					if (goingDown && deltaZ > 3f && playerPos.Z < startPoint.Z - 10f)
					{
						// Was descending, now ascending — bottom reached
						shouldExit = true;
					}
					else if (!goingDown && deltaZ < -3f && playerPos.Z > startPoint.Z + 10f)
					{
						// Was ascending, now descending — top reached
						shouldExit = true;
					}

					if (shouldExit)
					{
						_elevatorExitRequested = true;
						Logging.WriteDiagnostic("Elevator reached destination — walking off (playerZ={0:F1}, boardedZ={1:F1})",
							playerPos.Z, startPoint.Z);
						WoWPoint exitPoint = new WoWPoint(endPoint.X, endPoint.Y, playerPos.Z);
						WoWMovement.ClickToMove(exitPoint);
						_lastRideZ = playerPos.Z;
						return MoveResult.Moved;
					}
				}

				_lastRideZ = playerPos.Z;

				// Riding — wait for elevator to reach destination level
				Logging.WriteDebug("[Navigator] Riding elevator (playerZ={0:F1}, targetZ={1:F1}, moved={2})",
					playerPos.Z, endPoint.Z, _elevatorHasMoved);
				WoWMovement.MoveStop();
				return MoveResult.Moved;
			}

			// NOT on transport
			bool closerToEnd = Math.Abs(startPoint.Z - playerPos.Z) > Math.Abs(endPoint.Z - playerPos.Z);

			if (!closerToEnd)
			{
				_ridingElevator = false;

				if (me.Mounted)
					Mount.Dismount("Moving to transport.");

				// Walk to the elevator shaft center and wait there for the platform.
				// transport.Location gives the spawn/origin position — the XY is the
				// physical shaft center. Use that XY at the player's current Z so
				// ClickToMove can path there along the corridor/floor.
				// When the elevator platform arrives, player steps on → IsOnTransport.
				// If the platform isn't there, ClickToMove stops at the shaft edge
				// (WoW's built-in pathfinding won't walk off ledges).
				WoWPoint shaftTarget = new WoWPoint(elevatorLoc.X, elevatorLoc.Y, playerPos.Z);
				float distToShaft = playerPos.Distance2D(shaftTarget);

				if (distToShaft > 3f)
				{
					Logging.WriteDiagnostic("Walking toward elevator shaft (dist={0:F1})", distToShaft);
					WoWMovement.ClickToMove(shaftTarget);
					return MoveResult.Moved;
				}

				// Close to shaft center — stop and wait for elevator platform
				WoWMovement.MoveStop();

				// Timeout: after 60 seconds of waiting, force path regeneration.
				if ((DateTime.Now - _elevatorWaitStart).TotalSeconds > 60)
				{
					Logging.Write("[Navigator] Elevator wait timeout (60s) — forcing path regeneration");
					_ridingElevator = false;
					_inElevatorSequence = false;
					_elevatorTarget = WoWPoint.Zero;
					_lastRideZ = 0f;
					_elevatorHasMoved = false;
					_elevatorExitRequested = false;
					_currentPath.Clear();
					_currentPathIndex = 0;
					return MoveResult.Failed;
				}

				Logging.WriteDebug("[Navigator] Waiting for elevator at shaft (dist={0:F1}, playerZ={1:F1}, transport={2})",
					distToShaft, playerPos.Z, transport.Name);
				return MoveResult.Moved;
			}

			// closerToEnd — player already past start level (fell/jumped past start point)
			_ridingElevator = true;
			return MoveResult.Moved;
		}

		/// <summary>
		/// Portal handling: find nearest Goober or SpellCaster game object and interact.
		/// HB 4.3.4 pattern from MeshNavigator.method_4.
		/// AUDIT FIX: Added 30-yard range limit to avoid interacting with distant random objects.
		/// </summary>
		private static MoveResult? HandlePortal(LocalPlayer me)
		{
			// Find nearest portal-type game object within 30 yards (Goober or SpellCaster)
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
				Logging.WriteNavigator("Could not find portal to interact with.");
				return MoveResult.Failed;
			}

			if (bestPortal.WithinInteractRange)
			{
				Logging.WriteDebug("[Navigator] Interacting with portal: {0}", bestPortal.Name);
				bestPortal.Interact();
				return MoveResult.Moved;
			}

			// Move closer to the portal
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
				Logging.WriteNavigator("Could not find unit to interact with.");
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
			if (!result.Succeeded)
			{
				if (result.Aborted)
				{
					Logging.Write("[Navigator] Path search from {0} to {1} was aborted due to {2} (time used: {3:F0}ms)",
						start, end,
						Styx.Logic.BehaviorTree.TreeRoot.IsRunning ? "combat" : "bot stopping",
						result.Elapsed.TotalMilliseconds);
					return;
				}
				Logging.Write("[Navigator] Could not generate path from {0} to {1} on map {2} (time used: {3:F0}ms) @ {4}",
					start, end, mapId, result.Elapsed.TotalMilliseconds, result.FailStep);
				return;
			}

			if (result.IsPartialPath)
			{
				Logging.Write("[Navigator] Could not generate full path from {0} to {1} (time used: {2:F0}ms)",
					start, end, result.Elapsed.TotalMilliseconds);
				return;
			}

			if (result.Elapsed.TotalMilliseconds > 50.0)
			{
				Logging.WriteDebug("[Navigator] Successfully generated path from {0} to {1} in {2:F0}ms ({3} points)",
					start, end, result.Elapsed.TotalMilliseconds, result.PathLength);
			}
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
			Logging.WriteDebug("[Navigator] Opening door: {0} (Entry: {1})", closestDoor.Name, closestDoor.Entry);
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
