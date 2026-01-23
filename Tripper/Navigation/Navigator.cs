using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Tripper.Navigation
{
    /// <summary>
    /// Main navigation system for WoW pathfinding using Detour navmesh.
    /// Provides mesh loading, path calculation, and navigation queries.
    /// Adapted from Honorbuddy 5.4.8 WowNavigator for Trinity navmesh format.
    /// </summary>
    public sealed class Navigator : IDisposable
    {
        #region Fields

        private readonly object _meshLock = new object();
        private readonly Dictionary<string, QueryFilter> _queryFilters = new Dictionary<string, QueryFilter>();
        private QueryFilter _currentQueryFilter = null!;
        private bool _isDisposed;
        private DateTime _lastGarbageCollect = DateTime.UtcNow;

        #endregion

        #region Events

        /// <summary>
        /// Raised when a tile is loaded into the navmesh.
        /// </summary>
        public event EventHandler<TileLoadedEventArgs>? TileLoaded;

        /// <summary>
        /// Raised when a map is fully loaded.
        /// </summary>
        public event EventHandler<MapLoadedEventArgs>? MapLoaded;

        /// <summary>
        /// Raised during pathfinding progress.
        /// </summary>
        public event EventHandler<PathProgressEventArgs>? PathProgress;

        /// <summary>
        /// Raised when a navigation log message is generated.
        /// </summary>
        public event Action<string>? LogMessage;

        #endregion

        #region Properties

        /// <summary>
        /// Lock object for thread-safe mesh operations.
        /// </summary>
        public object MeshLock => _meshLock;

        /// <summary>
        /// Time interval for garbage collection of unused tiles.
        /// </summary>
        public TimeSpan GarbageCollectTime { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Search extents for navmesh queries (X, Y, Z).
        /// Default: 3, 20, 3 yards.
        /// </summary>
        public Vector3 Extents { get; set; } = new Vector3(3f, 20f, 3f);

        /// <summary>
        /// Current query filter used for pathfinding operations.
        /// </summary>
        public QueryFilter QueryFilter
        {
            get => _currentQueryFilter;
            set => _currentQueryFilter = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Indicates if the navigation system is initialized and ready.
        /// </summary>
        public bool IsLoaded { get; private set; }

        /// <summary>
        /// Current map ID that the navigator is operating on.
        /// </summary>
        public uint CurrentMapId { get; private set; }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes a new instance of the Navigator class.
        /// </summary>
        public Navigator()
        {
            InitializeQueryFilters();
            ResetQueryFilter();
        }

        /// <summary>
        /// Initializes default query filters for different movement scenarios.
        /// </summary>
        private void InitializeQueryFilters()
        {
            // Default filter - standard ground/water movement
            _queryFilters["Default"] = new QueryFilter
            {
                IncludeFlags = AbilityFlags.Run | AbilityFlags.Swim,
                ExcludeFlags = AbilityFlags.Unwalkable,
                AreaCosts = new Dictionary<AreaType, float>
                {
                    { AreaType.Ground, 1.0f },
                    { AreaType.Water, 2.0f },
                    { AreaType.Road, 0.5f }
                }
            };

            // Swimming filter - prioritize water movement
            _queryFilters["Swimming"] = new QueryFilter
            {
                IncludeFlags = AbilityFlags.Swim | AbilityFlags.Run,
                ExcludeFlags = AbilityFlags.Unwalkable,
                AreaCosts = new Dictionary<AreaType, float>
                {
                    { AreaType.Water, 1.0f },
                    { AreaType.Ground, 1.5f }
                }
            };

            // Flying filter - allow all movement types
            _queryFilters["Flying"] = new QueryFilter
            {
                IncludeFlags = AbilityFlags.Run | AbilityFlags.Swim | AbilityFlags.Jump | AbilityFlags.Transport,
                ExcludeFlags = AbilityFlags.None,
                AreaCosts = new Dictionary<AreaType, float>
                {
                    { AreaType.Ground, 1.0f },
                    { AreaType.Water, 1.0f },
                    { AreaType.Elevator, 1.0f }
                }
            };

            // Transport filter - include elevators, boats, etc.
            _queryFilters["Transport"] = new QueryFilter
            {
                IncludeFlags = AbilityFlags.Run | AbilityFlags.Swim | AbilityFlags.Transport,
                ExcludeFlags = AbilityFlags.Unwalkable,
                AreaCosts = new Dictionary<AreaType, float>
                {
                    { AreaType.Ground, 1.0f },
                    { AreaType.Elevator, 0.5f },
                    { AreaType.Portal, 0.5f }
                }
            };
        }

        /// <summary>
        /// Resets query filter to default settings.
        /// </summary>
        public void ResetQueryFilter()
        {
            _currentQueryFilter = _queryFilters["Default"];
        }

        /// <summary>
        /// Loads navigation meshes from the mmaps directory.
        /// </summary>
        /// <returns>True if meshes were loaded successfully.</returns>
        public bool LoadMeshes()
        {
            lock (_meshLock)
            {
                try
                {
                    bool success = NativeMethods.Nav_LoadMaps();
                    if (success)
                    {
                        IsLoaded = true;
                        Log("Navigation meshes loaded successfully");
                    }
                    else
                    {
                        Log("Failed to load navigation meshes");
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    Log($"Exception loading meshes: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Unloads all navigation meshes and releases resources.
        /// </summary>
        public void UnloadMeshes()
        {
            lock (_meshLock)
            {
                try
                {
                    NativeMethods.Nav_UnloadMaps();
                    IsLoaded = false;
                    Log("Navigation meshes unloaded");
                }
                catch (Exception ex)
                {
                    Log($"Exception unloading meshes: {ex.Message}");
                }
            }
        }

        #endregion

        #region Pathfinding

        /// <summary>
        /// Calculates a path from start to end position.
        /// Uses CalculatePathEx for complete path information including flags and area types.
        /// </summary>
        /// <param name="mapId">Map ID to pathfind on.</param>
        /// <param name="start">Starting position.</param>
        /// <param name="end">Destination position.</param>
        /// <param name="straightPath">If true, returns straight path; otherwise returns polygon corridor.</param>
        /// <returns>PathFindResult containing the calculated path.</returns>
        public PathFindResult FindPath(uint mapId, Vector3 start, Vector3 end, bool straightPath = true)
        {
            if (!IsLoaded)
            {
                Log("Cannot pathfind - meshes not loaded");
                return PathFindResult.CreateFailed(PathFindStep.None);
            }

            lock (_meshLock)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    CurrentMapId = mapId;

                    var startC = new NativeMethods.XYZ_C(start);
                    var endC = new NativeMethods.XYZ_C(end);

                    // Use CalculatePathEx for complete path data
                    IntPtr resultPtr = NativeMethods.CalculatePathEx(mapId, startC, endC, straightPath);

                    if (resultPtr == IntPtr.Zero)
                    {
                        return PathFindResult.CreateFailed(PathFindStep.InitPathFind);
                    }

                    try
                    {
                        // Marshal the PathResult structure
                        var nativeResult = Marshal.PtrToStructure<NativeMethods.PathResult_C>(resultPtr);
                        
                        stopwatch.Stop();

                        // Check status
                        var status = new Status(nativeResult.Status);
                        if (status.Failed || nativeResult.Length == 0)
                        {
                            return new PathFindResult
                            {
                                Elapsed = stopwatch.Elapsed,
                                Status = status,
                                FailStep = (PathFindStep)nativeResult.FailStep,
                                Start = start,
                                End = end
                            };
                        }

                        int pathLength = nativeResult.Length;

                        // Marshal arrays from native memory
                        Vector3[] points = new Vector3[pathLength];
                        StraightPathFlags[] flags = new StraightPathFlags[pathLength];
                        AreaType[] polyTypes = new AreaType[pathLength];
                        AbilityFlags[] abilityFlags = new AbilityFlags[pathLength];

                        unsafe
                        {
                            // Points
                            NativeMethods.XYZ_C* pointsPtr = (NativeMethods.XYZ_C*)nativeResult.Points.ToPointer();
                            for (int i = 0; i < pathLength; i++)
                            {
                                points[i] = pointsPtr[i].ToVector3();
                            }

                            // StraightPathFlags
                            if (nativeResult.StraightPathFlags != IntPtr.Zero)
                            {
                                byte* flagsPtr = (byte*)nativeResult.StraightPathFlags.ToPointer();
                                for (int i = 0; i < pathLength; i++)
                                {
                                    flags[i] = (StraightPathFlags)flagsPtr[i];
                                }
                            }

                            // PolyTypes (AreaType)
                            if (nativeResult.PolyTypes != IntPtr.Zero)
                            {
                                byte* polyTypesPtr = (byte*)nativeResult.PolyTypes.ToPointer();
                                for (int i = 0; i < pathLength; i++)
                                {
                                    polyTypes[i] = (AreaType)polyTypesPtr[i];
                                }
                            }

                            // AbilityFlags
                            if (nativeResult.AbilityFlags != IntPtr.Zero)
                            {
                                byte* abilityPtr = (byte*)nativeResult.AbilityFlags.ToPointer();
                                for (int i = 0; i < pathLength; i++)
                                {
                                    abilityFlags[i] = (AbilityFlags)abilityPtr[i];
                                }
                            }
                        }

                        // Create full result
                        var result = new PathFindResult
                        {
                            Elapsed = stopwatch.Elapsed,
                            Status = status,
                            Points = points,
                            Flags = flags,
                            Polygons = new PolygonReference[pathLength], // Not returned by C++ yet
                            AbilityFlags = abilityFlags,
                            PolyTypes = polyTypes,
                            StartPoly = PolygonReference.Invalid,
                            EndPoly = PolygonReference.Invalid,
                            Start = points.Length > 0 ? points[0] : start,
                            End = points.Length > 0 ? points[^1] : end,
                            Aborted = false,
                            IsPartialPath = false, // TODO: detect from status flags
                            FailStep = PathFindStep.None
                        };

                        RaisePathProgress(result);
                        return result;
                    }
                    finally
                    {
                        NativeMethods.FreePathResult(resultPtr);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Pathfinding exception: {ex.Message}");
                    return PathFindResult.CreateFailed(PathFindStep.None);
                }
            }
        }

        /// <summary>
        /// Finds the nearest valid navmesh point to a given position.
        /// </summary>
        /// <param name="mapId">Map ID.</param>
        /// <param name="position">Position to search from.</param>
        /// <param name="nearestPoint">Output nearest valid point.</param>
        /// <returns>True if a valid point was found.</returns>
        public bool FindNearestPoint(uint mapId, Vector3 position, out Vector3 nearestPoint)
        {
            nearestPoint = Vector3.Zero;

            if (!IsLoaded)
                return false;

            lock (_meshLock)
            {
                try
                {
                    var posC = new NativeMethods.XYZ_C(position);
                    NativeMethods.XYZ_C nearestC;

                    bool success = NativeMethods.FindNearestPoint_C(mapId, posC, out nearestC);
                    if (success)
                    {
                        nearestPoint = nearestC.ToVector3();
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    Log($"FindNearestPoint exception: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Finds a random navigable point within radius of center position.
        /// </summary>
        /// <param name="mapId">Map ID.</param>
        /// <param name="center">Center position.</param>
        /// <param name="radius">Search radius in yards.</param>
        /// <param name="randomPoint">Output random valid point.</param>
        /// <returns>True if a random point was found.</returns>
        public bool FindRandomPoint(uint mapId, Vector3 center, float radius, out Vector3 randomPoint)
        {
            randomPoint = Vector3.Zero;

            if (!IsLoaded)
                return false;

            lock (_meshLock)
            {
                try
                {
                    var centerC = new NativeMethods.XYZ_C(center);
                    NativeMethods.XYZ_C randomC;

                    bool success = NativeMethods.FindRandomPoint_C(mapId, centerC, radius, out randomC);
                    if (success)
                    {
                        randomPoint = randomC.ToVector3();
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    Log($"FindRandomPoint exception: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Performs a raycast from start to end position.
        /// </summary>
        /// <param name="mapId">Map ID.</param>
        /// <param name="start">Ray start position.</param>
        /// <param name="end">Ray end position.</param>
        /// <param name="hitPosition">Output hit position if raycast hits.</param>
        /// <param name="hitDistance">Output normalized hit distance (0-1).</param>
        /// <returns>True if raycast hit a navmesh boundary.</returns>
        public bool Raycast(uint mapId, Vector3 start, Vector3 end, out Vector3 hitPosition, out float hitDistance)
        {
            hitPosition = Vector3.Zero;
            hitDistance = 0f;

            if (!IsLoaded)
                return false;

            lock (_meshLock)
            {
                try
                {
                    var startC = new NativeMethods.XYZ_C(start);
                    var endC = new NativeMethods.XYZ_C(end);
                    NativeMethods.XYZ_C hitC;

                    bool hit = NativeMethods.Raycast_C(mapId, startC, endC, out hitC, out hitDistance);
                    if (hit)
                    {
                        hitPosition = hitC.ToVector3();
                    }
                    return hit;
                }
                catch (Exception ex)
                {
                    Log($"Raycast exception: {ex.Message}");
                    return false;
                }
            }
        }

        #endregion

        #region OffMesh Connections

        /// <summary>
        /// Adds a custom offmesh connection at runtime.
        /// </summary>
        /// <param name="mapId">Map ID.</param>
        /// <param name="start">Connection start position.</param>
        /// <param name="end">Connection end position.</param>
        /// <param name="radius">Connection radius.</param>
        /// <param name="flags">Connection flags.</param>
        /// <param name="type">Connection type (0=normal, 1=elevator, 2=portal).</param>
        /// <param name="interactId">Object ID to interact with (for elevators/portals).</param>
        public void AddOffMeshConnection(uint mapId, Vector3 start, Vector3 end, float radius = 1.0f, 
            byte flags = 1, byte type = 0, uint interactId = 0)
        {
            if (!IsLoaded)
                return;

            lock (_meshLock)
            {
                try
                {
                    var startC = new NativeMethods.XYZ_C(start);
                    var endC = new NativeMethods.XYZ_C(end);

                    NativeMethods.AddOffMeshConnection_C(mapId, startC, endC, radius, flags, type, interactId);
                    Log($"Added offmesh connection on map {mapId}: {start} -> {end}");
                }
                catch (Exception ex)
                {
                    Log($"AddOffMeshConnection exception: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Loads offmesh connections for a specific tile from .offmesh file.
        /// </summary>
        /// <param name="mapId">Map ID.</param>
        /// <param name="tileX">Tile X coordinate.</param>
        /// <param name="tileY">Tile Y coordinate.</param>
        /// <returns>True if offmesh data was loaded successfully.</returns>
        public bool LoadTileOffMesh(uint mapId, int tileX, int tileY)
        {
            if (!IsLoaded)
                return false;

            lock (_meshLock)
            {
                try
                {
                    bool success = NativeMethods.LoadTileOffMesh_C(mapId, tileX, tileY);
                    if (success)
                    {
                        Log($"Loaded offmesh data for tile ({tileX}, {tileY}) on map {mapId}");
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    Log($"LoadTileOffMesh exception: {ex.Message}");
                    return false;
                }
            }
        }

        #endregion

        #region Tile Management

        /// <summary>
        /// Ensures tiles within specified ring distance are loaded.
        /// </summary>
        /// <param name="mapId">Map ID.</param>
        /// <param name="position">Center position.</param>
        /// <param name="ring">Ring distance (number of tiles in each direction).</param>
        public void EnsureTiles(uint mapId, Vector3 position, int ring = 2)
        {
            if (!IsLoaded)
                return;

            lock (_meshLock)
            {
                try
                {
                    var posC = new NativeMethods.XYZ_C(position);
                    NativeMethods.EnsureTiles_C(mapId, posC, ring);
                }
                catch (Exception ex)
                {
                    Log($"EnsureTiles exception: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets tile coordinates for a world position.
        /// </summary>
        /// <param name="position">World position.</param>
        /// <returns>TileIdentifier for the position.</returns>
        public TileIdentifier GetTileByPosition(Vector3 position)
        {
            return TileIdentifier.GetByPosition(position.X, position.Y);
        }

        #endregion

        #region Event Helpers

        private void RaisePathProgress(PathFindResult result)
        {
            PathProgress?.Invoke(this, new PathProgressEventArgs(result));
        }

        private void RaiseTileLoaded(uint mapId, int tileX, int tileY)
        {
            TileLoaded?.Invoke(this, new TileLoadedEventArgs(mapId, tileX, tileY));
        }

        private void RaiseMapLoaded(uint mapId)
        {
            MapLoaded?.Invoke(this, new MapLoadedEventArgs(mapId));
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(message);
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Releases all resources used by the Navigator.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            lock (_meshLock)
            {
                UnloadMeshes();
                _queryFilters.Clear();
                _isDisposed = true;
            }

            GC.SuppressFinalize(this);
        }

        #endregion
    }

    #region Event Args

    /// <summary>
    /// Event arguments for path progress events.
    /// </summary>
    public class PathProgressEventArgs : EventArgs
    {
        public PathFindResult Result { get; }

        public PathProgressEventArgs(PathFindResult result)
        {
            Result = result;
        }
    }

    /// <summary>
    /// Event arguments for tile loaded events.
    /// </summary>
    public class TileLoadedEventArgs : EventArgs
    {
        public uint MapId { get; }
        public int TileX { get; }
        public int TileY { get; }

        public TileLoadedEventArgs(uint mapId, int tileX, int tileY)
        {
            MapId = mapId;
            TileX = tileX;
            TileY = tileY;
        }
    }

    /// <summary>
    /// Event arguments for map loaded events.
    /// </summary>
    public class MapLoadedEventArgs : EventArgs
    {
        public uint MapId { get; }

        public MapLoadedEventArgs(uint mapId)
        {
            MapId = mapId;
        }
    }

    #endregion

    #region Query Filter

    /// <summary>
    /// Defines query filtering rules for pathfinding operations.
    /// Controls which polygon types are traversable and their costs.
    /// </summary>
    public class QueryFilter
    {
        /// <summary>
        /// Flags that must be present on a polygon for it to be traversable.
        /// </summary>
        public AbilityFlags IncludeFlags { get; set; } = AbilityFlags.Run | AbilityFlags.Swim;

        /// <summary>
        /// Flags that prevent a polygon from being traversable.
        /// </summary>
        public AbilityFlags ExcludeFlags { get; set; } = AbilityFlags.Unwalkable;

        /// <summary>
        /// Cost multipliers for different area types.
        /// Higher cost = less preferred path.
        /// </summary>
        public Dictionary<AreaType, float> AreaCosts { get; set; } = new Dictionary<AreaType, float>();

        /// <summary>
        /// Creates a copy of this query filter.
        /// </summary>
        public QueryFilter Clone()
        {
            return new QueryFilter
            {
                IncludeFlags = IncludeFlags,
                ExcludeFlags = ExcludeFlags,
                AreaCosts = new Dictionary<AreaType, float>(AreaCosts)
            };
        }
    }

    #endregion
}
