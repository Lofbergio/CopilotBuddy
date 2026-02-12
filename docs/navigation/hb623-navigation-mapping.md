# HB 6.2.3 — Comprehensive Navigation File Mapping

> Source: `c:\Users\Texy\Desktop\.Reference\.hb 6.2.3\`
> Generated for critical comparison analysis against CopilotBuddy's navigation port.

---

## Architecture Overview

```
Navigator (static facade)
 └─► NavigationProvider (abstract)
      └─► MeshNavigator (concrete, 1318 lines)
           ├─► WowNavigator (pathfinding orchestrator)
           │    ├─► WorldMeshManager : IMeshManager
           │    │    ├─► NavMesh (C++/CLI wrapper)
           │    │    └─► NavMeshQuery (C++/CLI wrapper)
           │    └─► GarrisonMeshManager : IMeshManager
           ├─► FlightPaths (taxi system)
           ├─► Flightor (aerial navigation)
           └─► StuckHandler (abstract, default = Class469)

IPlayerMover (interface)
 ├─► ClickToMoveMover (default, obfuscated ns*)
 └─► KeyboardMover

ITerrainHeightProvider (interface)
 └─► Class1050 (default, obfuscated ns*)

Coordinate conversion:
  GraphicalHelper.ToDetour():  detour.X = -wow.Y; detour.Y = wow.Z; detour.Z = -wow.X
  GraphicalHelper.ToWow():     wow.Y = -detour.X; wow.X = -detour.Z; wow.Z = detour.Y
```

---

## 1. Styx/Pathing/ — Core Navigation Layer

### 1.1 Navigator.cs
- **Path:** `Honorbuddy\Styx\Pathing\Navigator.cs`
- **Lines:** 358
- **Namespace:** `Styx.Pathing`
- **Class:** `Navigator` (public static)
- **Description:** Static facade for all navigation — single entry point for bot code to request movement, path generation, and height queries.
- **Public Members:**
  - `static NavigationProvider NavigationProvider { get; set; }` — default: `MeshNavigator`
  - `static IPlayerMover PlayerMover { get; set; }` — default: `ClickToMoveMover`
  - `static ITerrainHeightProvider HeightProvider { get; set; }` — default: `Class1050`
  - `static float PathPrecision { get; set; }`
  - `static MoveResult MoveTo(WoWPoint destination, int mapID = -1)`
  - `static void Clear()`
  - `static WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to)`
  - `static bool CanNavigateFully(WoWPoint from, WoWPoint to, float tolerance)`
  - `static bool CanNavigateWithin(WoWPoint from, WoWPoint to, float tolerance)`
  - `static float PathDistance(WoWPoint from, WoWPoint to)`
  - `static List<float> FindHeights(float x, float y)`
  - `static bool FindHeight(float x, float y, out float z)`
  - `static bool AtLocation(WoWPoint from, WoWPoint to)`
  - `static RunStatus GetRunStatusFromMoveResult(MoveResult result)`
  - `static event ... OnNavigationProviderChanged`
  - `static event ... OnPlayerMoverChanged`
  - `static event ... OnHeightProviderChanged`
- **Dependencies:** `MeshNavigator`, `IPlayerMover`, `ITerrainHeightProvider`, `WoWMovement`, `TreeSharp.RunStatus`

---

### 1.2 NavigationProvider.cs
- **Path:** `Honorbuddy\Styx\Pathing\NavigationProvider.cs`
- **Lines:** ~120
- **Namespace:** `Styx.Pathing`
- **Class:** `NavigationProvider` (public abstract)
- **Description:** Abstract base class that all navigation providers must implement. Defines the contract for pathfinding and movement.
- **Public Members:**
  - `abstract MoveResult MoveTo(WoWPoint location)`
  - `abstract WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to)`
  - `abstract bool AtLocation(WoWPoint a, WoWPoint b)`
  - `abstract float PathPrecision { get; set; }`
  - `virtual StuckHandler StuckHandler { get; set; }`
  - `virtual void Clear()`
  - `virtual bool CanNavigateWithin(WoWPoint from, WoWPoint to, float tolerance)`
  - `virtual bool CanNavigateFully(WoWPoint from, WoWPoint to, float tolerance)`
  - `virtual float PathDistance(WoWPoint from, WoWPoint to)`
  - `virtual void OnSetAsCurrent()`
  - `virtual void OnRemoveAsCurrent()`
  - `bool IsCurrent { get; }` — checks `this == Navigator.NavigationProvider`
- **Dependencies:** `StuckHandler`, `WoWPoint`, `MoveResult`

---

### 1.3 MeshNavigator.cs
- **Path:** `Honorbuddy\Styx\Pathing\MeshNavigator.cs`
- **Lines:** 1318
- **Namespace:** `Styx.Pathing`
- **Class:** `MeshNavigator : NavigationProvider`
- **Description:** Primary concrete navigation implementation. Uses Detour navmesh for pathfinding, handles flight paths, off-mesh connections (elevators, portals, gates, interact objects/units), stuck detection, and garrison logic.
- **Public Members:**
  - `MeshNavigator()` — sets PathPrecision=2f, StuckHandler=Class469
  - `WowNavigator Nav { get; }`
  - `MeshMovePath CurrentMovePath { get; }`
  - `AbilityFlags CurrentHopAbilityFlags { get; }`
  - `override MoveResult MoveTo(WoWPoint location)`
  - `override WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to)`
  - `override bool CanNavigateWithin(WoWPoint from, WoWPoint to, float tolerance)`
  - `override bool CanNavigateFully(WoWPoint from, WoWPoint to, float tolerance)`
  - `override float PathDistance(WoWPoint from, WoWPoint to)`
  - `override bool AtLocation(WoWPoint a, WoWPoint b)`
  - `override void Clear()`
  - `override float PathPrecision { get; set; }`
  - `override void OnSetAsCurrent()` / `OnRemoveAsCurrent()`
  - `PathFindResult FindPath(WoWPoint start, WoWPoint end)`
  - `MoveResult MovePath(MeshMovePath path)`
  - `void UpdateMaps()`
- **Key Internal Methods (obfuscated):**
  - `method_5` — GetAreaType at position (returns AreaType enum from navmesh)
  - `method_6` — Garrison exit check
  - `method_7` — Door opening logic
  - `method_10` — Flight path trigger (distance² > 160,000 = 400+ yards)
  - `method_11` — Path generation routing (garrison vs world)
  - `method_18` — OffMeshConnection handling dispatch:
    - `AreaType.Elevator` → method_20 (elevator/transport)
    - `AreaType.InteractObject` → method_21
    - `AreaType.InteractUnit` → method_22
    - `AreaType.Portal/Gate/*Portal` → method_23
  - `method_20` — Elevator handling: finds nearest Transport, waits, moves in/out, dismounts
  - `method_24` — Normal movement + stuck detection
  - `method_28` — Alive/dead query filter setup
- **Dependencies:** `WowNavigator`, `WoWMovement`, `ObjectManager`, `FlightPaths`, `Mount`, `BotEvents`, `AreaType`, `AbilityFlags`, `StraightPathFlags`, `PathFindResult`

---

### 1.4 IPlayerMover.cs
- **Path:** `Honorbuddy\Styx\Pathing\IPlayerMover.cs`
- **Lines:** ~20
- **Namespace:** `Styx.Pathing`
- **Interface:** `IPlayerMover`
- **Description:** Interface for movement execution. Default implementation is `ClickToMoveMover` (obfuscated).
- **Members:**
  - `void Move(MovementDirection direction)`
  - `void MoveTowards(WoWPoint location)`
  - `void MoveStop()`

---

### 1.5 ITerrainHeightProvider.cs
- **Path:** `Honorbuddy\Styx\Pathing\ITerrainHeightProvider.cs`
- **Lines:** ~12
- **Namespace:** `Styx.Pathing`
- **Interface:** `ITerrainHeightProvider`
- **Description:** Interface for querying terrain heights. Default: Class1050 (obfuscated).
- **Members:**
  - `List<float> FindHeights(float x, float y)`

---

### 1.6 StuckHandler.cs
- **Path:** `Honorbuddy\Styx\Pathing\StuckHandler.cs`
- **Lines:** ~30
- **Namespace:** `Styx.Pathing`
- **Class:** `StuckHandler` (public abstract)
- **Description:** Abstract base for stuck detection. Default implementation is `Class469` (obfuscated).
- **Members:**
  - `abstract bool IsStuck()`
  - `abstract void Unstick()`
  - `virtual void Reset()`
  - `virtual void OnSetAsCurrent()`
  - `virtual void OnRemoveAsCurrent()`

---

### 1.7 Flightor.cs
- **Path:** `Honorbuddy\Styx\Pathing\Flightor.cs`
- **Lines:** 1690
- **Namespace:** `Styx.Pathing`
- **Class:** `Flightor` (public static)
- **Description:** Complete aerial/flying navigation system. Handles mount checks, flight level checks, indoor/outdoor transitions, blackspot avoidance, and coroutine-based movement.
- **Public Members:**
  - `static bool CanFly { get; }`
  - `static MoveResult MoveTo(WoWPoint destination, float minHeight = 0, bool checkIndoors = true)`
  - `static void Clear()`
- **Dependencies:** `BlackspotManager`, `IndoorEntrance`, `Navigator`, `Mount`, `SpellManager`, `GameWorld`, `Coroutine`

---

### 1.8 KeyboardMover.cs
- **Path:** `Honorbuddy\Styx\Pathing\KeyboardMover.cs`
- **Lines:** ~90
- **Namespace:** `Styx.Pathing`
- **Class:** `KeyboardMover : IPlayerMover`
- **Description:** Alternative `IPlayerMover` implementation using keyboard-style movement with facing calculations.
- **Members:**
  - `void Move(MovementDirection direction)`
  - `void MoveTowards(WoWPoint location)`
  - `void MoveStop()`

---

### 1.9 MeshMovePath.cs
- **Path:** `Honorbuddy\Styx\Pathing\MeshMovePath.cs`
- **Lines:** ~45
- **Namespace:** `Styx.Pathing`
- **Class:** `MeshMovePath`
- **Description:** Wrapper around a PathFindResult with traversal index tracking.
- **Members:**
  - `PathFindResult Path { get; }`
  - `int Index { get; set; }`
  - `bool IsExitingGarrison { get; set; }`

---

### 1.10 MoveResult.cs
- **Path:** `Honorbuddy\Styx\Pathing\MoveResult.cs`
- **Lines:** ~20
- **Namespace:** `Styx.Pathing`
- **Enum:** `MoveResult`
- **Values:** `Failed=0, ReachedDestination=1, PathGenerationFailed=2, PathGenerated=3, UnstuckAttempt=4, Moved=5`

---

### 1.11 MoveResultExtensions.cs
- **Path:** `Honorbuddy\Styx\Pathing\MoveResultExtensions.cs`
- **Lines:** ~15
- **Namespace:** `Styx.Pathing`
- **Class:** `MoveResultExtensions` (static)
- **Members:**
  - `static bool IsSuccessful(this MoveResult)` — true unless `Failed` or `PathGenerationFailed`

---

### 1.12 NavigationProviderChangedEventArgs.cs
- **Path:** `Honorbuddy\Styx\Pathing\NavigationProviderChangedEventArgs.cs`
- **Lines:** ~30
- **Namespace:** `Styx.Pathing`
- **Class:** `NavigationProviderChangedEventArgs<T> : EventArgs`
- **Members:**
  - `T OldProvider { get; }`
  - `T NewProvider { get; }`

---

### 1.13 PathGenerationFailStep.cs
- **Path:** `Honorbuddy\Styx\Pathing\PathGenerationFailStep.cs`
- **Lines:** ~20
- **Namespace:** `Styx.Pathing`
- **Enum:** `PathGenerationFailStep`
- **Values:** `None=-1, Success=0, FindStartNode, FindEndNode, FindPath, Mesh`

---

### 1.14 BlackspotQueryFlags.cs
- **Path:** `Honorbuddy\Styx\Pathing\BlackspotQueryFlags.cs`
- **Lines:** ~15
- **Namespace:** `Styx.Pathing`
- **Enum (Flags):** `BlackspotQueryFlags`
- **Values:** `Static=1, Dynamic=2, All=0xFFFFFFFF`

---

### 1.15 NavType.cs
- **Path:** `Honorbuddy\Styx\NavType.cs`
- **Lines:** 14
- **Namespace:** `Styx`
- **Enum:** `NavType`
- **Values:** `Run, Fly`

---

## 2. Styx/Pathing/FlightorAnnotation/ — Indoor Area Detection

### 2.1 IndoorEntrance.cs
- **Path:** `Honorbuddy\Styx\Pathing\FlightorAnnotation\IndoorEntrance.cs`
- **Lines:** ~65
- **Namespace:** `Styx.Pathing`
- **Class:** `IndoorEntrance`
- **Description:** Data descriptor for indoor area entrances. Used by Flightor to detect when flying must transition to ground movement.
- **Members:**
  - `WoWPoint Location { get; set; }`
  - `bool Dismount { get; set; }`
  - `float Radius { get; set; }`
  - `static IndoorEntrance FromXml(XElement element)`

---

## 3. Styx/Pathing/FlightorNavigation/ — Aerial Navigation Support

### 3.1 BlackspotManager.cs
- **Path:** `Honorbuddy\Styx\Pathing\FlightorNavigation\BlackspotManager.cs`
- **Lines:** 458
- **Namespace:** `Styx.Pathing`
- **Class:** `BlackspotManager` (public static)
- **Description:** Manages aerial blackspot polygons per map/faction. Prevents flying through restricted airspace.
- **Members:**
  - `static IReadOnlyList<...> Blackspots { get; }`
  - `static bool IsInBlackspot(WoWPoint location)`
  - `static void AddBlackspots(IEnumerable<...>)`
  - `static void RemoveBlackspots(IEnumerable<...>)`
- **Default Blackspots:** Hardcoded for maps 0, 1, 530, 571, 870

---

### 3.2 PolyNav.cs
- **Path:** `Honorbuddy\Styx\Pathing\FlightorNavigation\PolyNav.cs`
- **Lines:** 265
- **Namespace:** `Styx.Pathing`
- **Class:** `PolyNav`
- **Description:** 2D polygon-based pathfinding for Flightor. Finds routes around blackspot holes in continent boundary polys.
- **Members:**
  - `PolyNav(Vector2[] boundary, Vector2[][] holes)`
  - `List<Vector2> GetConnections(Vector2 from, Vector2 to)`

---

### 3.3 Areas.cs
- **Path:** `Honorbuddy\Styx\Pathing\FlightorNavigation\Areas.cs`
- **Lines:** 1145
- **Namespace:** `Styx.Pathing`
- **Class:** `Areas` (public static)
- **Description:** Continent boundary polygons for aerial navigation. Defines flyable areas per map ID.
- **Members:**
  - `static Dictionary<int, Vector2[]> ContinentAreas`

---

## 4. Tripper/Navigation/ — Pathfinding Engine

### 4.1 WowNavigator.cs
- **Path:** `Honorbuddy\Tripper\Navigation\WowNavigator.cs`
- **Lines:** 809
- **Namespace:** `Tripper.Navigation`
- **Class:** `WowNavigator : IDisposable`
- **Description:** Core pathfinding orchestrator. Creates and manages NavMesh/NavMeshQuery for world and garrison. Routes pathfinding to correct mesh manager, manages query filters per faction.
- **Public Members:**
  - `WowNavigator()`
  - `string PrimaryMapName { get; }`
  - `ICollection<string> MapNames { get; }`
  - `TimeSpan GarbageCollectTime { get; set; }`
  - `WowQueryFilter QueryFilter { get; set; }`
  - `PathPostProcessing PathPostProcessing { get; set; }`
  - `Vector3 Extents { get; set; }` — default `(3, 20, 3)`
  - `WorldMeshManager WorldMesh { get; }`
  - `GarrisonMeshManager GarrisonMesh { get; }`
  - `PathFindResult FindPath(Vector3 start, Vector3 end)` — routes to garrison or world mesh
  - `void ChangeMap(ICollection<string> mapNames)`
  - `void SetFactionQueryFilter(bool isHorde)`
  - `void ResetQueryFilter()`
  - `bool IsWithinGarrison(Vector3 location)` — point-in-polygon test
  - `bool HasQueryFilter()`
  - `void StoreQueryFilter()` / `WowQueryFilter GetStoredQueryFilter()`
  - `IMeshManager GetManagerFromLocation(Vector3 location)`
  - `static WowQueryFilter GetNewDefaultQueryFilter()`
  - `static WowQueryFilter GetNewFactionQueryFilter(bool horde)`
  - `static void SetDefaultQueryFilterCosts(WowQueryFilter filter)`
  - `void Dispose()`
  - `event OnPathFindProgress, OnMapLoaded, OnNavigatorLogMessage, OnReplacementsLoaded`
- **Default Area Costs:**
  - Road=1.0, Ground/Portal/Gate/KnownBuilding/Alliance/Horde/InteractObject/InteractUnit=1.66
  - Fall=1.7, Water=3.33, Elevator/DefendersPortal=3.16, Lava=55, Blackspot=60, Blocked=100
- **Dependencies:** `WorldMeshManager`, `GarrisonMeshManager`, `NavMesh`, `NavMeshQuery`, `WowQueryFilter`, `AbilityFlags`, `AreaType`, `PathFindResult`, `GraphicalHelper`

---

### 4.2 WorldMeshManager.cs
- **Path:** `Honorbuddy\Tripper\Navigation\WorldMeshManager.cs`
- **Lines:** 438
- **Namespace:** `Tripper.Navigation`
- **Class:** `WorldMeshManager : IDisposable, IMeshManager`
- **Description:** Manages the world's Detour navmesh — tile loading, garbage collection, lazy initialization.
- **Public Members:**
  - `WowNavigator Nav { get; }`
  - `NavMesh Mesh { get; }`
  - `NavMeshQuery MeshQuery { get; }`
  - `TimeSpan GarbageCollectTime { get; set; }`
  - `event TileLoaded, SubTileLoaded`
- **Init Params:** MaxPolys=4096, MaxTiles=16384, maxNodes=748983

---

### 4.3 GarrisonMeshManager.cs
- **Path:** `Honorbuddy\Tripper\Navigation\GarrisonMeshManager.cs`
- **Lines:** 238
- **Namespace:** `Tripper.Navigation`
- **Class:** `GarrisonMeshManager : IDisposable, IMeshManager`
- **Description:** Manages garrison-specific navmesh — separate mesh for instanced garrison maps.
- **Public Members:**
  - `static Vector3 HordePartialPathLocation` — `(5636.969, 4525.909, 119.7096)`
  - `static Vector3 AlliancePartialPathLocation` — `(1920.831, 294.1213, 88.966)`
  - `bool IsLoaded { get; }`
  - `string MapName { get; }`
  - `WowNavigator Nav { get; }`
  - `NavMesh Mesh { get; }`
  - `NavMeshQuery MeshQuery { get; }`

---

### 4.4 IMeshManager.cs
- **Path:** `Honorbuddy\Tripper\Navigation\IMeshManager.cs`
- **Lines:** ~20
- **Namespace:** `Tripper.Navigation`
- **Interface:** `IMeshManager`
- **Members:**
  - `NavMesh Mesh { get; }`
  - `NavMeshQuery MeshQuery { get; }`
  - `PathFindResult FindPath(Vector3 start, Vector3 end)`
  - `void LoadTile(TileIdentifier tile)`

---

### 4.5 PathFindResult.cs
- **Path:** `Honorbuddy\Tripper\Navigation\PathFindResult.cs`
- **Lines:** ~120
- **Namespace:** `Tripper.Navigation`
- **Class:** `PathFindResult`
- **Description:** Complete pathfinding result with path points, polygon data, area types, and ability flags per path segment.
- **Members:**
  - `IMeshManager Manager { get; }`
  - `TimeSpan Elapsed { get; }`
  - `Status Status { get; }`
  - `PolygonReference[] Polygons { get; }`
  - `StraightPathFlags[] Flags { get; }`
  - `Vector3[] Points { get; }`
  - `AbilityFlags[] AbilityFlags { get; }`
  - `AreaType[] PolyTypes { get; }`
  - `PolygonReference StartPoly { get; }` / `EndPoly { get; }`
  - `Vector3 Start { get; }` / `End { get; }`
  - `bool Aborted { get; }` / `Succeeded { get; }` / `IsPartialPath { get; }`
  - `PathGenerationFailStep FailStep { get; }`

---

### 4.6 WowQueryFilter.cs
- **Path:** `Honorbuddy\Tripper\Navigation\WowQueryFilter.cs`
- **Lines:** ~70
- **Namespace:** `Tripper.Navigation`
- **Class:** `WowQueryFilter`
- **Description:** Managed wrapper around Detour QueryFilter, exposing include/exclude as `AbilityFlags` and area costs as `AreaType`.
- **Members:**
  - `WowQueryFilter(AbilityFlags include, AbilityFlags exclude)`
  - `AbilityFlags IncludeFlags { get; set; }`
  - `AbilityFlags ExcludeFlags { get; set; }`
  - `void SetAreaCost(AreaType area, float cost)`
  - `float GetAreaCost(AreaType area)`
  - `QueryFilter InternalFilter { get; }` — native Detour filter

---

### 4.7 NavHelper.cs
- **Path:** `Honorbuddy\Tripper\Navigation\NavHelper.cs`
- **Lines:** ~40
- **Namespace:** `Tripper.Navigation`
- **Class:** `NavHelper` (public static)
- **Description:** Coordinate conversion shorthands.
- **Members:**
  - `static Vector3 ToNav(Vector3 wowCoord)` → calls `GraphicalHelper.ToDetour()`
  - `static Vector3 ToWow(Vector3 detourCoord)` → calls `GraphicalHelper.ToWow()`

---

### 4.8 PathPostProcessing.cs
- **Path:** `Honorbuddy\Tripper\Navigation\PathPostProcessing.cs`
- **Lines:** ~10
- **Namespace:** `Tripper.Navigation`
- **Enum:** `PathPostProcessing`
- **Values:** `None=0, MoveAwayFromEdges=1, Randomize=2`

---

### 4.9 PathFindStep.cs
- **Path:** `Honorbuddy\Tripper\Navigation\PathFindStep.cs`
- **Lines:** ~20
- **Namespace:** `Tripper.Navigation`
- **Enum:** `PathFindStep`
- **Values:** `None, FindStartPoly, FindEndPoly, InitPathFind, UpdatePathFind, FinalizePathFind, SnapPartialPathToEnd, FindStraightPath`

---

### 4.10 PathFindProgressEventArgs.cs
- **Path:** `Honorbuddy\Tripper\Navigation\PathFindProgressEventArgs.cs`
- **Lines:** ~35
- **Namespace:** `Tripper.Navigation`
- **Class:** `PathFindProgressEventArgs : EventArgs`
- **Members:**
  - `TimeSpan Elapsed { get; }`
  - `bool Cancel { get; set; }`

---

### 4.11 NavigatorLogMessage.cs
- **Path:** `Honorbuddy\Tripper\Navigation\NavigatorLogMessage.cs`
- **Lines:** ~5
- **Namespace:** `Tripper.Navigation`
- **Delegate:** `NavigatorLogMessage`

---

### 4.12 MapLoadedEventArgs.cs
- **Path:** `Honorbuddy\Tripper\Navigation\MapLoadedEventArgs.cs`
- **Lines:** ~20
- **Namespace:** `Tripper.Navigation`
- **Class:** `MapLoadedEventArgs : EventArgs`

---

### 4.13 TileLoadedEventArgs.cs
- **Path:** `Honorbuddy\Tripper\Navigation\TileLoadedEventArgs.cs`
- **Lines:** ~15
- **Namespace:** `Tripper.Navigation`
- **Class:** `TileLoadedEventArgs : EventArgs`

---

## 5. Tripper/MeshMisc/ — Tile Data & Enums

### 5.1 AreaType.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\AreaType.cs`
- **Lines:** ~40
- **Namespace:** `Tripper.MeshMisc`
- **Enum (byte):** `AreaType`
- **Values:** `Ground=1, Water=2, Lava=3, Road=4, Fall=5, Elevator=6, Gate=7, Portal=8, DefendersPortal=9, HordePortal=10, AlliancePortal=11, Blocked=12, InteractUnit=13, InteractObject=14, Horde=15, Alliance=16, Blackspot=17, KnownBuilding=18, Misc1=20..Misc10=29`
- **Description:** Area type tags painted onto navmesh polygons. Controls pathfinding costs and special handling (elevators, portals, gates).

---

### 5.2 AbilityFlags.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\AbilityFlags.cs`
- **Lines:** ~30
- **Namespace:** `Tripper.MeshMisc`
- **Enum (ushort, Flags):** `AbilityFlags`
- **Values:** `None=0, Run=1, OnlyWhileAlive=2, Swim=4, Jump=8, Unwalkable=16, Teleport=32, Transport=64, Horde=4096, Alliance=8192, KnownBuilding=16384, All=65535`
- **Description:** Polygon capability flags — used in query filter include/exclude to determine which polygons are traversable.

---

### 5.3 GraphicalHelper.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\GraphicalHelper.cs`
- **Lines:** ~170
- **Namespace:** `Tripper.MeshMisc`
- **Class:** `GraphicalHelper` (public static)
- **Description:** Coordinate system conversion between WoW and Detour (axis swaps).
- **Members:**
  - `static Vector3 ToWow(Vector3 detour)` — `Y=-detour.X, X=-detour.Z, Z=detour.Y`
  - `static Vector3 ToDetour(Vector3 wow)` — `X=-wow.Y, Y=wow.Z, Z=-wow.X`
  - `static bool IsVectorContained(Vector3 v, Vector3 min, Vector3 max)`
  - `static bool TriangleContainsPoint(Vector3 a, Vector3 b, Vector3 c, Vector3 p)`
  - `static void NormalizeBounds(ref Vector3 min, ref Vector3 max)`
  - `static void Swap<T>(ref T a, ref T b)`

---

### 5.4 MeshManager.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\MeshManager.cs`
- **Lines:** ~160
- **Namespace:** `Tripper.MeshMisc`
- **Class:** `MeshManager` (public static)
- **Description:** Tile data I/O — saves and loads binary navmesh tile data from disk.
- **Members:**
  - `static void SaveMeshData(string path, TileDataHeader header, NavMeshData data)`
  - `static TileDataHeader ReadHeader(string path)`
  - `static NavMeshData LoadMeshData(string path, TileDataHeader header)`
- **Binary Format:** Magic `"TDAT"`, Version 3

---

### 5.5 MeshMapCalculator.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\MeshMapCalculator.cs`
- **Lines:** ~160
- **Namespace:** `Tripper.MeshMisc`
- **Class:** `MeshMapCalculator` (public static)
- **Description:** WoW ADT-to-Detour tile math. Converts WoW world coordinates to tile indices and back.
- **Members:**
  - `static int SubTilesPerAdt { get; set; }` — default=4
  - `static float DetourTileSize` — `533.3333 / SubTilesPerAdt`
  - `static TileIdentifier GetDetourTile(Vector3 worldPos)`
  - `static TileIdentifier GetWowTile(Vector3 worldPos)`
  - `static BoundingBox GetBounds(TileIdentifier tile)`

---

### 5.6 TileIdentifier.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\TileIdentifier.cs`
- **Lines:** ~100
- **Namespace:** `Tripper.MeshMisc`
- **Struct:** `TileIdentifier`
- **Members:**
  - `int X, Y`
  - `static TileIdentifier GetByPosition(float x, float y)` — `X = 32 - ceil(y/533.3333), Y = 32 - ceil(x/533.3333)`
- **Description:** Identifies a tile in the 64×64 WoW ADT grid. Used to determine which mesh tiles to load.

---

### 5.7 TileDataHeader.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\TileDataHeader.cs`
- **Lines:** ~50
- **Namespace:** `Tripper.MeshMisc`
- **Class:** `TileDataHeader`
- **Members:**
  - `int Width, Height`
  - `string MapName`
  - `int TileX, TileY`
  - `DateTime UtcCreateTime`

---

### 5.8 MapConsts.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\MapConsts.cs`
- **Lines:** 12
- **Namespace:** `Tripper.MeshMisc`
- **Class:** `MapConsts` (public static)
- **Members:**
  - `const float TileSize = 533.3333f`

---

### 5.9 IoCGate.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\IoCGate.cs`
- **Lines:** 16
- **Namespace:** `Tripper.MeshMisc`
- **Enum (byte):** `IoCGate` — Isle of Conquest gate identifiers
- **Values:** `HordeWest=20, HordeSouth=21, HordeEast=22, AllianceNorth=23, AllianceWest=24, AllianceEast=25`

---

### 5.10 SotAGate.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\SotAGate.cs`
- **Lines:** 14
- **Namespace:** `Tripper.MeshMisc`
- **Enum (byte):** `SotAGate` — Strand of the Ancients gate identifiers
- **Values:** `Green=20, Blue=21, Red=22, Purple=23, Yellow=24`

---

### 5.11 InvalidTileDataException.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\InvalidTileDataException.cs`
- **Lines:** 14
- **Namespace:** `Tripper.MeshMisc`
- **Class:** `InvalidTileDataException : Exception`

---

### 5.12 TileDataVersionException.cs
- **Path:** `Honorbuddy\Tripper\MeshMisc\TileDataVersionException.cs`
- **Lines:** 13
- **Namespace:** `Tripper.MeshMisc`
- **Class:** `TileDataVersionException : ApplicationException`
- **Message:** `"Tile data is wrong version!"`

---

## 6. Styx/CommonBot/ — Flight Path System

### 6.1 FlightPaths.cs
- **Path:** `Honorbuddy\Styx\CommonBot\FlightPaths.cs`
- **Lines:** 704
- **Namespace:** `Styx.CommonBot`
- **Class:** `FlightPaths` (public static)
- **Description:** Taxi flight path management. Determines when to use flight masters based on distance and known routes.
- **Members:**
  - `static WoWUnit NearestFlightMerchant { get; }`
  - `static bool NeedFlightPath { get; }`
  - `static FlightPathReason Reason { get; }`
  - `static IList<XmlFlightNode> XmlNodes { get; }`
  - `static string TakingPathTo { get; }` / `TakingPathFrom { get; }`
  - `static bool CanTakeFlightPaths { get; }`
  - `static bool IsAtStart { get; }` / `IsAtEnd { get; }`
  - `static bool IsIgnored(string node)`
  - `static void IgnoreFp(string node)`
  - `static void Reset()`
  - `static void SetFlightPathUsage(bool enabled)`
  - `static bool ShouldTakeFlightpath(WoWPoint from, WoWPoint to)`
  - `static void SetPoi(...)`
- **Trigger:** Distance² > 160,000 (= 400 yards straight-line)

---

### 6.2 FlightPathReason.cs
- **Path:** `Honorbuddy\Styx\CommonBot\FlightPathReason.cs`
- **Lines:** ~15
- **Namespace:** `Styx.CommonBot`
- **Enum:** `FlightPathReason`

---

### 6.3 XmlFlightNode.cs
- **Path:** `Honorbuddy\Styx\CommonBot\XmlFlightNode.cs`
- **Lines:** 183
- **Namespace:** `Styx.CommonBot`
- **Class:** `XmlFlightNode`
- **Description:** XML-serializable flight node data (name, continent, location, connections).
- **Members:**
  - `XmlFlightNode(string name, uint continent, WoWPoint location)`
  - `XmlFlightNode(uint masterEntry, string name, uint continent, WoWPoint location)`
  - `XmlFlightNode(XElement xml)`
  - `List<string> Connections { get; set; }`
  - `uint Continent { get; set; }`
  - `uint MasterEntry { get; set; }`
  - `string Name { get; set; }`
  - `WoWPoint Location { get; set; }`
  - `float X, Y, Z { get; set; }`
  - `void Connect(string to)`
  - `XElement ToXml()`

---

## 7. Styx/ — Core Types Used by Navigation

### 7.1 WoWPoint.cs
- **Path:** `Honorbuddy\Styx\WoWPoint.cs`
- **Lines:** 370
- **Namespace:** `Styx`
- **Struct:** `WoWPoint` — `[StructLayout(Explicit, Size=12)]`, fields at offsets 0, 4, 8
- **Description:** Core 3D coordinate struct for WoW world positions. Implicit conversion to/from `Vector3`.
- **Members:**
  - `float X, Y, Z`
  - `static readonly WoWPoint Empty, Zero`
  - `float Distance(WoWPoint)` / `DistanceSqr(WoWPoint)`
  - `float Distance2D(WoWPoint)` / `Distance2DSqr(WoWPoint)`
  - `WoWPoint Normalize()`
  - `static WoWPoint Add(WoWPoint, WoWPoint)`
  - `static WoWPoint RayCast(WoWPoint start, float direction, float distance)`
  - `float GetDirection(WoWPoint other)`
  - Operators: `+, -, *, implicit Vector3↔WoWPoint`

---

### 7.2 WoWMovement.cs
- **Path:** `Honorbuddy\Styx\WoWInternals\WoWMovement.cs`
- **Lines:** 626
- **Namespace:** `Styx.WoWInternals`
- **Class:** `WoWMovement` (public static)
- **Description:** Low-level movement commands — ClickToMove, facing, keyboard move, stop.
- **Members:**
  - `static void Pulse()`
  - `static void ClickToMove(WoWPoint)` (multiple overloads)
  - `static void Face(WoWPoint)` / `Face(float heading)`
  - `static void StopFace()`
  - `static void Move(MovementDirection)` / `MoveStop()`
  - `static void ConstantFace(ulong guid)`
  - `static WoWUnit ActiveMover { get; }`
  - `static ulong ActiveMoverGuid { get; }`
  - `static ClickToMoveInfo ClickToMoveInfo { get; }`
  - `static bool IsFacing { get; }`
  - Nested: `MovementDirection` (flags enum), `ClickToMoveType`, `InputControl`

---

## 8. Tripper.RecastManaged/Detour/ — Native C++/CLI Wrappers

### 8.1 NavMesh.cs
- **Path:** `Tripper.RecastManaged\Detour\NavMesh.cs`
- **Lines:** 309
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `NavMesh : IDisposable`
- **Description:** Managed wrapper for `dtNavMesh`. Core mesh storage — add/remove tiles, query polygon flags/areas.
- **Members:**
  - `Status Init(NavMeshData data)` / `Init(NavMeshParams params)`
  - `Status AddTile(NavMeshData, TileFlags, TileReference, out TileReference)` / `RemoveTile(TileReference)`
  - `MeshTile GetTileAt(int x, int y, int layer)` / `int GetMaxTiles()`
  - `Status SetPolyFlags(PolygonReference, ushort)` / `GetPolyFlags(PolygonReference, out ushort)`
  - `Status SetPolyArea(PolygonReference, byte)` / `GetPolyArea(PolygonReference, out byte)`
  - `Status GetTileAndPolyByRef(PolygonReference, out MeshTile, out Poly)`
  - `void SetTileLoaderFunction(LoadTileDelegate)`

---

### 8.2 NavMeshQuery.cs
- **Path:** `Tripper.RecastManaged\Detour\NavMeshQuery.cs`
- **Lines:** 451
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `NavMeshQuery : IDisposable`
- **Description:** Managed wrapper for `dtNavMeshQuery`. All pathfinding queries.
- **Members:**
  - `Status Init(NavMesh mesh, int maxNodes)`
  - `Status FindNearestPolygon(Vector3 center, Vector3 extents, QueryFilter, out PolygonReference)`
  - `Status FindPath(PolygonReference start, PolygonReference end, Vector3 startPos, Vector3 endPos, QueryFilter, PolygonReference[] path, out int count, int maxPath)`
  - `Status InitSlicedFindPath(PolygonReference start, PolygonReference end, Vector3 startPos, Vector3 endPos, QueryFilter)`
  - `Status UpdateSlicedFindPath(int maxIter, out int actualIter)`
  - `Status FinalizeSlicedFindPath(PolygonReference[] path, out int count, int maxPath)`
  - `Status FinalizeSlicedFindPathPartial(PolygonReference[] existing, int existingSize, PolygonReference[] path, out int count, int maxPath)`
  - `Status FindStraightPath(Vector3 start, Vector3 end, PolygonReference[] path, int pathSize, Vector3[] straightPath, StraightPathFlags[] flags, PolygonReference[] straightPathPolys, out int count, int maxStraightPath)`
  - `Status QueryPolygons(Vector3 center, Vector3 extents, QueryFilter, PolygonReference[] polys, out int count, int maxPolys)`
  - `Status FindPolysAroundCircle(...)` / `FindLocalNeighbourhood(...)`
  - `Status GetPolyHeight(PolygonReference, Vector3, out float)`
  - `Status ClosestPointOnPoly(PolygonReference, Vector3, out Vector3)`
  - `Status ClosestPointOnPolyBoundary(PolygonReference, Vector3, out Vector3)`
  - `Status Raycast(PolygonReference start, Vector3 startPos, Vector3 endPos, QueryFilter, out float t, out Vector3 hitNormal, PolygonReference[] path, out int count, int maxPath)`
  - `void SetTileLoaderFunction(LoadTileDelegate)`

---

### 8.3 QueryFilter.cs
- **Path:** `Tripper.RecastManaged\Detour\QueryFilter.cs`
- **Lines:** 180
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `QueryFilter : IDisposable`
- **Description:** Native wrapper for `dtQueryFilter`. Controls which polygons are traversable and their traversal costs.
- **Members:**
  - `ushort IncludeFlags { get; set; }`
  - `ushort ExcludeFlags { get; set; }`
  - `void SetAreaCost(int areaId, float cost)`
  - `float GetAreaCost(int areaId)`

---

### 8.4 OffMeshConnection.cs
- **Path:** `Tripper.RecastManaged\Detour\OffMeshConnection.cs`
- **Lines:** 192
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `OffMeshConnection : IDisposable`
- **Description:** Native wrapper for `dtOffMeshConnection`. Represents connections between discontinuous mesh regions (elevators, portals, etc.).
- **Members:**
  - `Vector3 Start { get; set; }` / `End { get; set; }`
  - `float Radius { get; set; }`
  - `PolygonReference Poly { get; set; }`
  - `ushort Flags { get; set; }`
  - `byte Side { get; set; }`

---

### 8.5 RandomizedQueryFilter.cs
- **Path:** `Tripper.RecastManaged\Detour\RandomizedQueryFilter.cs`
- **Lines:** ~80
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `RandomizedQueryFilter : QueryFilter`
- **Description:** Query filter with randomized path costs for more natural-looking movement.
- **Members:**
  - `void Clear()`
  - `float MinRandomizationFactor { get; set; }`
  - `float MaxRandomizationFactor { get; set; }`
  - `void SetRandomizationFactors(float min, float max)`

---

### 8.6 Status.cs
- **Path:** `Tripper.RecastManaged\Detour\Status.cs`
- **Lines:** ~80
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Struct:** `Status`
- **Members:**
  - `uint Id`
  - `bool Failed { get; }` / `Succeeded { get; }` / `InProgress { get; }`
  - `StatusDetailFlag Flags { get; }`
  - `bool HasFlag(StatusDetailFlag flag)`
  - `static readonly Status Failure` (0x80000000) / `Success` (0x40000000)

---

### 8.7 StatusDetailFlag.cs
- **Path:** `Tripper.RecastManaged\Detour\StatusDetailFlag.cs`
- **Lines:** ~20
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Enum (uint, Flags):** `StatusDetailFlag`
- **Values:** `Failure=0x80000000, Success=0x40000000, InProgress=0x20000000, WrongMagic=1, WrongVersion=2, OutOfMemory=4, InvalidParam=8, BufferTooSmall=16, OutOfNodes=32, PartialResult=64`

---

### 8.8 StraightPathFlags.cs
- **Path:** `Tripper.RecastManaged\Detour\StraightPathFlags.cs`
- **Lines:** ~15
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Enum (byte, Flags):** `StraightPathFlags`
- **Values:** `None=0, Start=1, End=2, OffmeshConnection=4`

---

### 8.9 Detour.cs
- **Path:** `Tripper.RecastManaged\Detour\Detour.cs`
- **Lines:** ~25
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `Detour`
- **Members:**
  - `static bool CreateNavMeshData(NavMeshCreateParams params, out NavMeshData data)`
  - `static int MaxVertsPerPolygon = 6`

---

### 8.10 NavMeshData.cs
- **Path:** `Tripper.RecastManaged\Detour\NavMeshData.cs`
- **Lines:** 115
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `NavMeshData : IDisposable`
- **Description:** Raw navmesh tile data buffer. Wraps native memory for transfer to/from `dtNavMesh`.
- **Members:**
  - `NavMeshData(byte[] navData, int startIndex, int length)`
  - `byte[] GetNavData()`
  - `void GetNavData(byte[] buffer, int start)`

---

### 8.11 NavMeshCreateParams.cs
- **Path:** `Tripper.RecastManaged\Detour\NavMeshCreateParams.cs`
- **Lines:** 547
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `NavMeshCreateParams : IDisposable`
- **Description:** Parameters for creating navmesh tile data. Wraps `dtNavMeshCreateParams`.
- **Members:** `Verts`, `VertCount`, `Polys`, `PolyFlags`, `PolyAreas`, `PolyCount`, `NVP`, `DetailMeshes`, `DetailVerts`, `DetailVertsCount`, `DetailTris`, `DetailTriCount`, `OffMeshConVerts`, `OffMeshConRad`, `OffMeshConFlags`, `OffMeshConAreas`, `OffMeshConDir`, `OffMeshConUserID`, `OffMeshConCount`, `UserId`, `TileX`, `TileY`, `TileLayer`, `BMin`, `BMax`, `WalkableHeight`, `WalkableRadius`, `WalkableClimb`, `CellSize`, `CellHeight`, `BuildBvTree`

---

### 8.12 NavMeshParams.cs
- **Path:** `Tripper.RecastManaged\Detour\NavMeshParams.cs`
- **Lines:** 180
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `NavMeshParams : IDisposable`
- **Description:** Initialization parameters for `dtNavMesh`.
- **Members:**
  - `int MaxTiles { get; set; }`
  - `int MaxPolys { get; set; }`
  - `float TileWidth { get; set; }` / `TileHeight { get; set; }`
  - `Vector3 Origin { get; set; }`

---

### 8.13 MeshTile.cs
- **Path:** `Tripper.RecastManaged\Detour\MeshTile.cs`
- **Lines:** 523
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `MeshTile : IDisposable`
- **Description:** Native wrapper for `dtMeshTile`. Contains per-tile header, polygon data, detail meshes, off-mesh connections.
- **Members:** `Salt`, `LinksFreeList`, `Header` (MeshHeader), `Polys`, `Verts`, `Links`, `DetailMeshes`, `DetailVerts`, `DetailTris`, `BVTree`, `OffMeshCons`, `DataSize`, `Flags`

---

### 8.14 MeshHeader.cs
- **Path:** `Tripper.RecastManaged\Detour\MeshHeader.cs`
- **Lines:** 318
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `MeshHeader : IDisposable`
- **Description:** Per-tile header data. Contains polygon counts, vertex counts, bounds.
- **Members:** `Magic`, `Version`, `X`, `Y`, `Layer`, `PolyCount`, `VertCount`, `MaxLinkCount`, `DetailMeshCount`, `DetailVertCount`, `DetailTriCount`, `BVNodeCount`, `OffMeshConCount`, `OffMeshBase`, `WalkableHeight`, `WalkableRadius`, `WalkableClimb`, `BMin`, `BMax`, `BVQuantFactor`

---

### 8.15 Poly.cs
- **Path:** `Tripper.RecastManaged\Detour\Poly.cs`
- **Lines:** 248
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `Poly : IDisposable`
- **Members:** `FirstLink`, `Flags` (ushort), `VertCount`, `Verts` (ushort[]), `Neis` (ushort[]), `Area` (byte), `Type` (PolyType)

---

### 8.16 PolyDetail.cs
- **Path:** `Tripper.RecastManaged\Detour\PolyDetail.cs`
- **Lines:** 153
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `PolyDetail : IDisposable`
- **Members:** `VertBase`, `VertCount`, `TriBase`, `TriCount`

---

### 8.17 Link.cs
- **Path:** `Tripper.RecastManaged\Detour\Link.cs`
- **Lines:** 185
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Class:** `Link : IDisposable`
- **Members:** `Ref` (PolygonReference), `Next` (uint), `Edge` (byte), `Side` (byte), `BMin` (byte), `BMax` (byte)

---

### 8.18 PolygonReference.cs
- **Path:** `Tripper.RecastManaged\Detour\PolygonReference.cs`
- **Lines:** ~30
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Struct:** `PolygonReference`
- **Members:** `uint Id`

---

### 8.19 TileReference.cs
- **Path:** `Tripper.RecastManaged\Detour\TileReference.cs`
- **Lines:** ~30
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Struct:** `TileReference`
- **Members:** `uint Id`

---

### 8.20 PolyType.cs
- **Path:** `Tripper.RecastManaged\Detour\PolyType.cs`
- **Lines:** ~10
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Enum:** `PolyType`
- **Values:** `Ground=0, OffmeshConnection=1`

---

### 8.21 DirectionFlags.cs
- **Path:** `Tripper.RecastManaged\Detour\DirectionFlags.cs`
- **Lines:** ~12
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Enum (Flags):** `DirectionFlags`
- **Values:** `StartToEnd=0, Bidirectional=1`

---

### 8.22 TileFlags.cs
- **Path:** `Tripper.RecastManaged\Detour\TileFlags.cs`
- **Lines:** ~10
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Enum:** `TileFlags`
- **Values:** `DT_TILE_FREE_DATA=1`

---

### 8.23 LoadTileDelegate.cs
- **Path:** `Tripper.RecastManaged\Detour\LoadTileDelegate.cs`
- **Lines:** ~10
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Delegate:** `bool LoadTileDelegate(int x, int y)`

---

### 8.24 NavMeshTileLoader.cs
- **Path:** `Tripper.RecastManaged\Detour\NavMeshTileLoader.cs`
- **Lines:** ~10
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Struct (internal, NativeCppClass):** `NavMeshTileLoader`

---

### 8.25 RandomizedDtQueryFilter.cs
- **Path:** `Tripper.RecastManaged\Detour\RandomizedDtQueryFilter.cs`
- **Lines:** ~10
- **Namespace:** `Tripper.RecastManaged.Detour`
- **Struct (internal, NativeCppClass):** `RandomizedDtQueryFilter`

---

## 9. Tripper.RecastManaged/Recast/ — Navigation Mesh Generation

### 9.1 Recast.cs
- **Path:** `Tripper.RecastManaged\Recast\Recast.cs`
- **Lines:** 326
- **Namespace:** `Tripper.RecastManaged.Recast`
- **Class:** `Recast`
- **Description:** Static methods wrapping Recast mesh generation pipeline (heightfield creation, voxelization, polygon mesh generation).
- **Public Members:**
  - `static RecastContextManagedWrapper Context { get; }`
  - `static void CalcBounds(float[] verts / Vector3[] verts, out bmin, out bmax)`
  - `static void CalcGridSize(bmin, bmax, cs, out w, out h)`
  - `static bool CreateHeightfield(width, height, bmin, bmax, cs, ch, out Heightfield)`
  - `static void MarkWalkableTriangles(slopeAngle, verts, tris, flags)`
  - (+ more Recast pipeline methods: RasterizeTriangles, FilterLowHangingWalkableObstacles, FilterLedgeSpans, FilterWalkableLowHeightSpans, BuildCompactHeightfield, ErodeWalkableArea, BuildDistanceField, BuildRegions, BuildContours, BuildPolyMesh, BuildPolyMeshDetail, MergePolyMeshes, MergePolyMeshDetails)

---

### 9.2 DefaultAreaType.cs
- **Path:** `Tripper.RecastManaged\Recast\DefaultAreaType.cs`
- **Lines:** ~12
- **Namespace:** `Tripper.RecastManaged.Recast`
- **Enum (byte, Flags):** `DefaultAreaType`
- **Values:** `RC_NULL_AREA=0, RC_WALKABLE_AREA=63`

---

### 9.3 Heightfield.cs
- **Path:** `Tripper.RecastManaged\Recast\Heightfield.cs`
- **Namespace:** `Tripper.RecastManaged.Recast`
- **Class:** `Heightfield : IDisposable` — Wrapper for `rcHeightfield`

### 9.4 CompactHeightfield.cs
- **Namespace:** `Tripper.RecastManaged.Recast`
- **Class:** `CompactHeightfield : IDisposable` — Wrapper for `rcCompactHeightfield`

### 9.5 CompactCell.cs / CompactSpan.cs
- **Structs wrapping `rcCompactCell` / `rcCompactSpan`**

### 9.6 ContourSet.cs
- **Class:** `ContourSet : IDisposable` — Wrapper for `rcContourSet`

### 9.7 PolyMesh.cs
- **Path:** `Tripper.RecastManaged\Recast\PolyMesh.cs`
- **Lines:** 280
- **Class:** `PolyMesh : IDisposable` — Wrapper for `rcPolyMesh`
- **Members:** `Verts`, `Polys`, `Regs`, `Flags`, `Areas`, `NVerts`, `NPolys`, `MaxPolys`, `NVP`, `BMin`, `BMax`, `CS`, `CH`, `BorderSize`

### 9.8 PolyMeshDetail.cs
- **Class:** `PolyMeshDetail : IDisposable` — Wrapper for `rcPolyMeshDetail`

### 9.9 Span.cs
- **Struct wrapping `rcSpan`**

### 9.10 RecastContextManagedWrapper.cs
- **Class wrapping `rcContext` for logging**

### 9.11 RecastContextLogCategory.cs / RecastContextLogEventArgs.cs
- **Enum/EventArgs for Recast logging**

### 9.12 rcCustomContext.cs
- **Internal NativeCppClass struct**

---

## 10. Specific Feature Search Results

### IMover / IPlayerMover
- **Interface:** `Styx.Pathing.IPlayerMover` — `Move()`, `MoveTowards()`, `MoveStop()`
- **Default impl:** `ClickToMoveMover` (obfuscated, in ns* folders)
- **Alt impl:** `KeyboardMover` (Styx/Pathing/KeyboardMover.cs)

### IStuckHandler / StuckHandler
- **Abstract:** `Styx.Pathing.StuckHandler` — `IsStuck()`, `Unstick()`, `Reset()`
- **Default impl:** `Class469` (obfuscated, set in MeshNavigator constructor)

### OffMeshConnection Handling
- **Native type:** `Tripper.RecastManaged.Detour.OffMeshConnection` — Start, End, Radius, Poly, Flags
- **Detection:** `StraightPathFlags.OffmeshConnection` (value=4) in path flags array
- **Dispatch (MeshNavigator.method_18):** reads `AreaType` of the off-mesh polygon, routes to:
  - Elevator → method_20 (Transport GameObjects)
  - InteractObject → method_21
  - InteractUnit → method_22
  - Portal/Gate → method_23

### Elevator/Transport Handling
- **AreaType.Elevator** (value=6), **AbilityFlags.Transport** (value=64)
- **MeshNavigator.method_20:** Finds nearest Transport within 100 yards, waits for arrival, boards, waits for destination, dismounts
- Cost: 3.16 (higher than ground 1.66)

### FlightPaths
- **FlightPaths.cs** (704 lines) — Static class managing taxi/flight master routes
- **XmlFlightNode.cs** (183 lines) — Serializable node data with connection graph
- **Trigger:** distance² > 160,000 (400 yards)

### MeshHeightHelper / Height Finding
- **ITerrainHeightProvider** interface: `FindHeights(float x, float y) → List<float>`
- **Navigator.FindHeight(x, y, out z)** / **FindHeights(x, y)**
- Default impl: Class1050 (obfuscated)
- NavMeshQuery also provides: `GetPolyHeight(PolygonReference, Vector3, out float)`

### PathPostProcessing
- **Enum:** `None=0, MoveAwayFromEdges=1, Randomize=2`
- Set on `WowNavigator.PathPostProcessing` property

### AreaType Enums
- **Primary:** `Tripper.MeshMisc.AreaType` (byte) — 29 values (Ground through Misc10)
- **Recast default:** `Tripper.RecastManaged.Recast.DefaultAreaType` — RC_NULL_AREA=0, RC_WALKABLE_AREA=63

### AbilityFlags
- `Tripper.MeshMisc.AbilityFlags` (ushort, Flags) — 12 flag values
- Used as polygon traversal capability flags in query filters

### StraightPathFlags
- `Tripper.RecastManaged.Detour.StraightPathFlags` (byte, Flags) — Start=1, End=2, OffmeshConnection=4
- Used to identify off-mesh connection points in straight paths

---

## File Count Summary

| Location | File Count | Total Lines (approx) |
|----------|-----------|---------------------|
| Styx/Pathing/ (core) | 13 | ~3,700 |
| Styx/Pathing/FlightorAnnotation/ | 1 | ~65 |
| Styx/Pathing/FlightorNavigation/ | 3 | ~1,870 |
| Tripper/Navigation/ | 13 | ~1,870 |
| Tripper/MeshMisc/ | 12 | ~900 |
| Styx/CommonBot/ (flight) | 3 | ~900 |
| Styx/ (WoWPoint, NavType) | 2 | ~385 |
| Styx/WoWInternals/ (WoWMovement) | 1 | ~625 |
| Tripper.RecastManaged/Detour/ | 25 | ~3,800 |
| Tripper.RecastManaged/Recast/ | 14 | ~1,200 |
| **TOTAL** | **87** | **~15,300** |
