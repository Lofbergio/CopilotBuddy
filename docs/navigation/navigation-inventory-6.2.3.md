# Navigation System Inventory — HB WoD 6.2.3

> **Source**: `c:\Users\Texy\Desktop\.Reference\.hb 6.2.3\`
> **Purpose**: Complete catalog of every navigation-related file, class, interface, enum, struct, method signature, and native call in the WoD 6.2.3 decompiled source.

---

## Architecture Overview

Three-layer design:

```
Layer 1 (High-level API)       Styx.Pathing              Navigator, MeshNavigator, Flightor, IPlayerMover
         │ delegates to ↓
Layer 2 (Mid-level engine)     Tripper.Navigation         WowNavigator, IMeshManager, WorldMeshManager
         │ delegates to ↓
Layer 3 (Native wrapper)       Tripper.RecastManaged.Detour   NavMesh, NavMeshQuery, QueryFilter (C++/CLI)
```

**No P/Invoke / DllImport.** The native Detour library is wrapped via **C++/CLI mixed-mode assembly** (`Tripper.RecastManaged.dll`). All native calls go through `<Module>.dt*()` functions (e.g. `<Module>.dtAllocNavMesh()`, `<Module>.dtNavMesh.init()`).

**External DLLs**: `Navigation.dll`, `RecastManaged.dll`, `Tripper.Tools.dll`, `Tripper.XNAMath.dll`

**Coordinate conversion** (WoW ↔ Detour):
```
ToDetour(wow): detour.X = -wow.Y,  detour.Y = wow.Z,  detour.Z = -wow.X
ToWow(detour): wow.X   = -detour.Z, wow.Y  = -detour.X, wow.Z  = detour.Y
```

---

## Layer 1 — `Styx.Pathing` (Honorbuddy\Styx\Pathing\)

### Navigator.cs

**Namespace**: `Styx.Pathing` | **Type**: `static class Navigator`

The public facade. Delegates all work to a pluggable `NavigationProvider` (default: `MeshNavigator`).

| Member | Signature |
|--------|-----------|
| **Properties** | |
| `NavigationProvider` | `NavigationProvider { get; set; }` — default `new MeshNavigator()` |
| `HeightProvider` | `ITerrainHeightProvider { get; set; }` — default `Class1050` |
| `PlayerMover` | `IPlayerMover { get; set; }` — default `ClickToMoveMover` |
| `PathPrecision` | `float { get; set; }` |
| **Methods** | |
| `MoveTo` | `MoveResult MoveTo(WoWPoint location, int mapID = -1)` |
| `Clear` | `bool Clear()` |
| `GeneratePath` | `WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to)` |
| `CanNavigateFully` | `bool CanNavigateFully(WoWPoint from, WoWPoint to)` |
| `CanNavigateWithin` | `bool CanNavigateWithin(WoWPoint from, WoWPoint to, float tolerance)` |
| `PathDistance` | `float? PathDistance(WoWPoint from, WoWPoint to, float maxDist)` |
| `FindHeights` | `List<float> FindHeights(float x, float y)` |
| `FindHeight` | `bool FindHeight(ref Vector3 pt)` |
| `FindHeight` | `bool FindHeight(float x, float y, out float z)` |
| `AtLocation` | `bool AtLocation(WoWPoint pt1, WoWPoint pt2)` |
| `GetRunStatusFromMoveResult` | `RunStatus GetRunStatusFromMoveResult(MoveResult mr)` |
| **Events** | |
| `OnNavigationProviderChanged` | `EventHandler<NavigationProviderChangedEventArgs<NavigationProvider>>` |
| `OnPlayerMoverChanged` | `EventHandler<NavigationProviderChangedEventArgs<IPlayerMover>>` |
| `OnHeightProviderChanged` | `EventHandler<NavigationProviderChangedEventArgs<ITerrainHeightProvider>>` |

---

### NavigationProvider.cs

**Namespace**: `Styx.Pathing` | **Type**: `abstract class NavigationProvider`

| Member | Kind | Signature |
|--------|------|-----------|
| `MoveTo` | abstract | `MoveResult MoveTo(WoWPoint location)` |
| `PathPrecision` | abstract property | `float { get; set; }` |
| `GeneratePath` | abstract | `WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to)` |
| `AtLocation` | abstract | `bool AtLocation(WoWPoint pt1, WoWPoint pt2)` |
| `StuckHandler` | virtual property | `StuckHandler { get; set; }` |
| `Clear` | virtual | `bool Clear()` |
| `CanNavigateWithin` | virtual | `bool CanNavigateWithin(WoWPoint from, WoWPoint to, float tol)` |
| `CanNavigateFully` | virtual | `bool CanNavigateFully(WoWPoint from, WoWPoint to)` |
| `PathDistance` | virtual | `float? PathDistance(WoWPoint from, WoWPoint to, float max)` |
| `OnSetAsCurrent` | virtual | `void OnSetAsCurrent()` |
| `OnRemoveAsCurrent` | virtual | `void OnRemoveAsCurrent()` |

---

### MeshNavigator.cs (1318 lines)

**Namespace**: `Styx.Pathing` | **Type**: `class MeshNavigator : NavigationProvider`

The primary ground navigation implementation. Manages pathfinding, off-mesh connections (elevators, portals, doors), stuck detection, and garrison awareness.

| Member | Signature |
|--------|-----------|
| **Constructor** | Sets `PathPrecision=2f`, creates `Class469` StuckHandler, `Class1039` |
| **Properties** | |
| `Nav` | `WowNavigator` — mid-level navigator instance |
| `CurrentMovePath` | `MeshMovePath` |
| `CurrentHopAbilityFlags` | `AbilityFlags` |
| **Core Methods** | |
| `MoveTo` | `override MoveResult MoveTo(WoWPoint location)` — full impl with garrison handling, door handling, stuck detection, flight path check, path gen |
| `FindPath` | `PathFindResult FindPath(WoWPoint start, WoWPoint end)` — runs on background `Task<PathFindResult>`, combat interruption |
| `GeneratePath` | `override WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to)` |
| `CanNavigateWithin` | `override bool CanNavigateWithin(...)` |
| `CanNavigateFully` | `override bool CanNavigateFully(...)` |
| `PathDistance` | `override float? PathDistance(...)` |
| `Clear` | `override bool Clear()` |
| `UpdateMaps` | `void UpdateMaps()` |
| **Off-mesh handling** | |
| `method_18` | Dispatch by `AreaType`: Elevator→`method_20`, Portal→`method_23`, InteractUnit→`method_22`, InteractObject→`method_21` |
| `method_20` | Elevator: finds Transport GameObjects, waits for elevator, moves in/out |
| `method_23` | Portal: finds Goober/SpellCaster GameObjects, interacts |
| `method_7/8` | Door: opens closed doors via Interact |
| **Query filter** | |
| `method_28` | Sets include/exclude flags based on alive/dead state |
| Faction-aware: sets filter via `SetFactionQueryFilter(isHorde)` |

---

### Flightor.cs (1690 lines)

**Namespace**: `Styx.Pathing` | **Type**: `static class Flightor`

Flying navigation system — aerial pathfinding with blackspot avoidance and indoor detection.

| Member | Signature |
|--------|-----------|
| `CanFly` | `bool` — checks riding skill, flyable area, mount availability |
| `MoveTo` | `void MoveTo(WoWPoint destination, bool checkIndoors)` |
| `MoveTo` | `void MoveTo(WoWPoint dest, float minHeight=40f, bool checkIndoors=false)` |
| `Clear` | `void Clear()` |
| Falls back to `Navigator.MoveTo()` when ground movement is faster |
| Uses `BlackspotManager`, indoor entrance handling, `FlightorAnnotation` |

---

### IPlayerMover.cs

**Namespace**: `Styx.Pathing` | **Type**: `interface IPlayerMover`

| Method | Signature |
|--------|-----------|
| `Move` | `void Move(WoWMovement.MovementDirection direction)` |
| `MoveTowards` | `void MoveTowards(WoWPoint location)` |
| `MoveStop` | `void MoveStop()` |

---

### KeyboardMover.cs

**Namespace**: `Styx.Pathing` | **Type**: `class KeyboardMover : IPlayerMover`

Keyboard-based movement with facing angle calculations. Implements `Move`, `MoveTowards`, `MoveStop`.

---

### StuckHandler.cs

**Namespace**: `Styx.Pathing` | **Type**: `abstract class StuckHandler`

| Method | Signature |
|--------|-----------|
| `IsStuck` | `abstract bool IsStuck()` |
| `Unstick` | `abstract void Unstick()` |
| `Reset` | `virtual void Reset()` |
| `OnSetAsCurrent` | `virtual void OnSetAsCurrent()` |
| `OnRemoveAsCurrent` | `virtual void OnRemoveAsCurrent()` |

---

### MeshMovePath.cs

**Namespace**: `Styx.Pathing` | **Type**: `class MeshMovePath`

| Property | Type |
|----------|------|
| `Path` | `PathFindResult` |
| `Index` | `int` |
| `IsExitingGarrison` | `bool` |

---

### MoveResult.cs

**Namespace**: `Styx.Pathing` | **Type**: `enum MoveResult`

```
Failed = 0, ReachedDestination, PathGenerationFailed, PathGenerated, UnstuckAttempt, Moved
```

---

### MoveResultExtensions.cs

**Namespace**: `Styx.Pathing` | **Type**: `static class MoveResultExtensions`

| Method | Signature |
|--------|-----------|
| `IsSuccessful` | `bool IsSuccessful(this MoveResult mr)` — true unless Failed or PathGenerationFailed |

---

### ITerrainHeightProvider.cs

**Namespace**: `Styx.Pathing` | **Type**: `interface ITerrainHeightProvider`

| Method | Signature |
|--------|-----------|
| `FindHeights` | `List<float> FindHeights(float x, float y)` |

---

### NavigationProviderChangedEventArgs.cs

**Namespace**: `Styx.Pathing` | **Type**: `class NavigationProviderChangedEventArgs<T> : EventArgs`

| Property | Type |
|----------|------|
| `OldProvider` | `T` |
| `NewProvider` | `T` |

---

### BlackspotQueryFlags.cs

**Namespace**: `Styx.Pathing` | **Type**: `[Flags] enum BlackspotQueryFlags`

```
Static = 1, Dynamic = 2, All = 0xFFFFFFFF
```

---

### PathGenerationFailStep.cs

**Namespace**: `Styx.Pathing` | **Type**: `enum PathGenerationFailStep`

```
None = -1, Success = 0, FindStartNode, FindEndNode, FindPath, Mesh
```

---

### NavType.cs (`Styx\NavType.cs`)

**Namespace**: `Styx` | **Type**: `enum NavType`

```
Run = 0, Fly = 1
```

---

### WoWPoint.cs (`Styx\WoWPoint.cs`)

**Namespace**: `Styx` | **Type**: `struct WoWPoint`

| Member | Signature |
|--------|-----------|
| Fields | `float X, Y, Z` |
| `Distance` | `float Distance(WoWPoint other)` |
| `Distance2D` | `float Distance2D(WoWPoint other)` |
| `DistanceSqr` | `float DistanceSqr(WoWPoint other)` |
| Static | `WoWPoint.Zero`, `WoWPoint.Empty` |
| Conversion | Implicit to/from `Vector3` |

---

## Layer 1 — FlightorNavigation Subdir

### BlackspotManager.cs (458 lines)

**Namespace**: `Styx.Pathing.FlightorNavigation` | **Type**: `static class BlackspotManager`

Manages polygon-based aerial blackspots per map/faction.

| Member | Signature |
|--------|-----------|
| `Blackspots` | `IEnumerable<Vector2[]>` — current map+faction blackspots |
| `IsInBlackspot` | `bool IsInBlackspot(WoWPoint point)` |
| `AddBlackspot` | `void AddBlackspot(uint mapId, WoWFactionGroup faction, Vector2[] blackspot)` |
| `AddBlackspots` | `void AddBlackspots(uint mapId, WoWFactionGroup faction, IEnumerable<Vector2[]>)` |
| `RemoveBlackspot` | `void RemoveBlackspot(uint mapId, WoWFactionGroup faction, Vector2[] blackspot)` |
| `RemoveBlackspots` | `void RemoveBlackspots(uint mapId, WoWFactionGroup faction, IEnumerable<Vector2[]>)` |

Hardcoded blackspot polygons for maps 0, 1, 530, 571, 870, 1116, 1464.

---

### PolyNav.cs (265 lines)

**Namespace**: `Styx.Pathing.FlightorNavigation` | **Type**: `class PolyNav`

2D polygon navigation with holes — used for aerial pathfinding around blackspots.

| Member | Signature |
|--------|-----------|
| Constructor | `PolyNav(Vector2[] points, IEnumerable<Vector2[]> holes)` |
| `Points` | `Vector2[] { get; set; }` |
| `Holes` | `Vector2[][] { get; set; }` |
| `GetConnections` | `IEnumerable<Vector2> GetConnections(Vector2 point, out Vector2 closest)` |
| `ContainsLine` | `bool ContainsLine(Vector2 start, Vector2 end)` |
| `ContainsPoint` | `bool ContainsPoint(Vector2 point)` |
| `FindPath` | `Vector2[] FindPath(Vector2 start, Vector2 end)` |
| `GetBounds` | `void GetBounds(out Vector2 min, out Vector2 max)` |

---

### Areas.cs (1145 lines)

**Namespace**: `Styx.Pathing.FlightorNavigation` | **Type**: `static class Areas`

Static data — continent boundary polygons as `Dictionary<uint, Vector2[]> ContinentAreas`. Maps: 0 (Eastern Kingdoms), 1 (Kalimdor), etc. Used by Flightor to determine flyable airspace.

---

## Layer 1 — FlightorAnnotation Subdir

### IndoorEntrance.cs

**Namespace**: `Styx.Pathing.FlightorAnnotation` | **Type**: `class IndoorEntrance`

| Member | Signature |
|--------|-----------|
| Constructor | `IndoorEntrance(WoWPoint location, bool dismount = true, float radius = 4f)` |
| `Dismount` | `bool { get; }` |
| `Location` | `WoWPoint { get; }` |
| `Radius` | `float { get; }` |
| `FromXml` | `static IndoorEntrance FromXml(XElement element)` |

---

## Layer 2 — `Tripper.Navigation` (Honorbuddy\Tripper\Navigation\)

### WowNavigator.cs (809 lines)

**Namespace**: `Tripper.Navigation` | **Type**: `class WowNavigator : IDisposable`

Mid-level navigator managing mesh managers, query filters, and path routing.

| Member | Signature |
|--------|-----------|
| **Constructor** | Creates default/Horde/Alliance/DK query filters, `WorldMeshManager`, `GarrisonMeshManager`, default `Extents=(3,20,3)` |
| **Properties** | |
| `PrimaryMapName` | `string` |
| `MapNames` | `ICollection<string>` |
| `GarbageCollectTime` | `TimeSpan` |
| `QueryFilter` | `WowQueryFilter` |
| `PathPostProcessing` | `PathPostProcessing` |
| `WorldMesh` | `WorldMeshManager` |
| `GarrisonMesh` | `GarrisonMeshManager` |
| `Extents` | `Vector3` |
| **Pathfinding** | |
| `FindPath` | `PathFindResult FindPath(Vector3 start, Vector3 end)` — routes between Garrison or World mesh |
| `ChangeMap` | `void ChangeMap(ICollection<string> mapNames)` |
| `GetManagerFromLocation` | `IMeshManager GetManagerFromLocation(Vector3 loc)` |
| `IsWithinGarrison` | `bool IsWithinGarrison(Vector3 pt)` / `bool IsWithinGarrison(Vector2 pt)` / `bool IsWithinGarrison(float x, float y)` |
| **Query Filters** | |
| `SetFactionQueryFilter` | `void SetFactionQueryFilter(bool isHorde)` |
| `ResetQueryFilter` | `void ResetQueryFilter()` |
| `GetNewDefaultQueryFilter` | `WowQueryFilter GetNewDefaultQueryFilter()` |
| `SetDefaultQueryFilterCosts` | `static void SetDefaultQueryFilterCosts(WowQueryFilter filter)` |
| `GetNewFactionQueryFilter` | `WowQueryFilter GetNewFactionQueryFilter(bool isHorde)` |
| **Events** | |
| `OnPathFindProgress` | `EventHandler<PathFindProgressEventArgs>` |
| `OnMapLoaded` | `EventHandler<MapLoadedEventArgs>` |
| `OnNavigatorLogMessage` | `NavigatorLogMessage` |
| `OnReplacementsLoaded` | `EventHandler<EventArgs>` |

**Default area costs:**
| AreaType | Cost |
|----------|------|
| Road | 1.0 |
| Ground, Gate, KnownBuilding, Alliance, Horde, Portal, InteractObject, InteractUnit | 1.66 |
| Fall | 1.7 |
| Elevator, DefendersPortal | 3.16 |
| Water | 3.33 |
| Lava | 55.0 |
| Blackspot | 60.0 |
| Blocked | 100.0 |

Garrison polygon boundaries stored as `Vector2[]` for Horde/Alliance.

---

### IMeshManager.cs

**Namespace**: `Tripper.Navigation` | **Type**: `interface IMeshManager`

| Member | Signature |
|--------|-----------|
| `Mesh` | `NavMesh { get; }` |
| `MeshQuery` | `NavMeshQuery { get; }` |
| `FindPath` | `PathFindResult FindPath(Vector3 start, Vector3 end)` |
| `LoadTile` | `bool LoadTile(TileIdentifier tile)` |

---

### WorldMeshManager.cs (438 lines)

**Namespace**: `Tripper.Navigation` | **Type**: `class WorldMeshManager : IDisposable, IMeshManager`

| Member | Signature |
|--------|-----------|
| `Nav` | `WowNavigator` |
| `Mesh` | `NavMesh` |
| `MeshQuery` | `NavMeshQuery` |
| `GarbageCollectTime` | `TimeSpan` |
| `LoadTile` | `bool LoadTile(TileIdentifier tile)` |
| `UnloadAllTiles` | `void UnloadAllTiles()` |
| `UpdateTileUsageTime` | `bool UpdateTileUsageTime(TileIdentifier tile)` |
| `FindPath` | `PathFindResult FindPath(Vector3 start, Vector3 end)` |
| **Events** | `TileLoaded`, `SubTileLoaded` (both `EventHandler<TileLoadedEventArgs>`) |

NavMesh initialized with `MeshMapCalculator.Default`: TileSize=133.33f, MaxPolys=4096, MaxTiles=16384. Uses `SetTileLoaderFunction` callback. SubTile grid: 4×4 per ADT.

---

### GarrisonMeshManager.cs

**Namespace**: `Tripper.Navigation` | **Type**: `class GarrisonMeshManager : IDisposable, IMeshManager`

| Member | Signature |
|--------|-----------|
| Static | `HordePartialPathLocation` = (5636.969, 4525.909, 119.71) |
| Static | `AlliancePartialPathLocation` = (1920.831, 294.121, 88.966) |
| `IsLoaded` | `bool` |
| `MapName` | `string` |
| `Nav` | `WowNavigator` |
| `Mesh` | `NavMesh` |
| `MeshQuery` | `NavMeshQuery` |
| `FindPath` | `PathFindResult FindPath(Vector3, Vector3)` |
| `LoadTile` | `bool LoadTile(TileIdentifier)` — always returns true (tiles pre-loaded) |

Supports garrison building replacement system: loads replacement tiles into NavMesh.

---

### PathFindResult.cs

**Namespace**: `Tripper.Navigation` | **Type**: `class PathFindResult`

| Property | Type |
|----------|------|
| `Manager` | `IMeshManager` |
| `Elapsed` | `TimeSpan` |
| `Status` | `Status` (Detour) |
| `Polygons` | `PolygonReference[]` |
| `Flags` | `StraightPathFlags[]` |
| `Points` | `Vector3[]` |
| `AbilityFlags` | `AbilityFlags[]` |
| `PolyTypes` | `AreaType[]` |
| `StartPoly` | `PolygonReference` |
| `EndPoly` | `PolygonReference` |
| `Start` | `Vector3` |
| `End` | `Vector3` |
| `Aborted` | `bool` |
| `Succeeded` | `bool` |
| `IsPartialPath` | `bool` |
| `FailStep` | `PathFindStep` |

---

### WowQueryFilter.cs

**Namespace**: `Tripper.Navigation` | **Type**: `class WowQueryFilter : IDisposable`

Wraps `Tripper.RecastManaged.Detour.QueryFilter`.

| Member | Signature |
|--------|-----------|
| `InternalFilter` | `QueryFilter { get; }` |
| `IncludeFlags` | `AbilityFlags { get; set; }` |
| `ExcludeFlags` | `AbilityFlags { get; set; }` |
| `SetAreaCost` | `void SetAreaCost(AreaType area, float cost)` |
| `GetAreaCost` | `float GetAreaCost(AreaType area)` |

---

### NavHelper.cs

**Namespace**: `Tripper.Navigation` | **Type**: `static class NavHelper`

| Method | Signature |
|--------|-----------|
| `ToNav` | `Vector3 ToNav(Vector3 wow)` — delegates to `GraphicalHelper.ToDetour` |
| `ToWow` | `Vector3 ToWow(Vector3 nav)` — delegates to `GraphicalHelper.ToWow` |

---

### PathPostProcessing.cs

**Namespace**: `Tripper.Navigation` | **Type**: `[Flags] enum PathPostProcessing`

```
None = 0, MoveAwayFromEdges = 1, Randomize = 2
```

---

### PathFindStep.cs

**Namespace**: `Tripper.Navigation` | **Type**: `enum PathFindStep`

```
None, FindStartPoly, FindEndPoly, InitPathFind, UpdatePathFind, FinalizePathFind,
SnapPartialPathToEnd, FindStraightPath
```

---

### PathFindProgressEventArgs.cs

**Namespace**: `Tripper.Navigation` | **Type**: `class PathFindProgressEventArgs : EventArgs`

| Property | Type |
|----------|------|
| `RunTime` | `TimeSpan` |
| `Cancel` | `bool` |
| `PathFindStart` | `DateTime` |

---

### MapLoadedEventArgs.cs

**Namespace**: `Tripper.Navigation` | **Type**: `class MapLoadedEventArgs : EventArgs`

| Property | Type |
|----------|------|
| `Names` | `string[]` |
| `IsTiled` | `bool` |

---

### TileLoadedEventArgs.cs

**Namespace**: `Tripper.Navigation` | **Type**: `class TileLoadedEventArgs : EventArgs`

| Property | Type |
|----------|------|
| `Tile` | `TileIdentifier` |

---

### NavigatorLogMessage.cs

**Namespace**: `Tripper.Navigation` | **Type**: `delegate void NavigatorLogMessage(string msg)`

---

## Layer 2 — `Tripper.MeshMisc` (Honorbuddy\Tripper\MeshMisc\)

### AreaType.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `enum AreaType : byte`

```
Ground=1, Water=2, Lava=3, Road=4, Fall=5, Elevator=6, Gate=7, Portal=8,
DefendersPortal=9, HordePortal=10, AlliancePortal=11, Blocked=12,
InteractUnit=13, InteractObject=14, Horde=15, Alliance=16, Blackspot=17,
KnownBuilding=18, Misc1=20..Misc10=29
```

---

### AbilityFlags.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `[Flags] enum AbilityFlags : ushort`

```
None=0, Run=1, OnlyWhileAlive=2, Swim=4, Jump=8, Unwalkable=16, Teleport=32,
Transport=64, Horde=4096, Alliance=8192, KnownBuilding=16384, All=65535
```

---

### TileIdentifier.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `struct TileIdentifier : IEquatable<TileIdentifier>`

| Member | Signature |
|--------|-----------|
| `X` | `readonly int` |
| `Y` | `readonly int` |
| `GetByPosition` | `static TileIdentifier GetByPosition(float x, float y)` — calculates: `32 - ceil(coord / 533.3333)` |
| Implicit conversion to/from `Vector2i` |

---

### MeshMapCalculator.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `class MeshMapCalculator`

| Member | Signature |
|--------|-----------|
| `SubTilesPerAdt` | `int` |
| `DetourTileSize` | `float` — `533.3333 / SubTilesPerAdt` |
| Static `Default` | `new MeshMapCalculator(4)` → DetourTileSize = 133.33f |
| `GetDetourTile` | `(int x, int y)` |
| `GetWowTile` | `(int x, int y)` |
| `GetBounds` | viewport bounds from tile coords |
| `GetWowBounds` | WoW-space bounds |

---

### MeshManager.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `static class MeshManager`

Tile data file I/O.

| Method | Signature |
|--------|-----------|
| `SaveMeshData` | `void SaveMeshData(Stream s, TileDataHeader h, byte[,][] data)` |
| `ReadHeader` | `TileDataHeader ReadHeader(Stream s)` |
| `LoadMeshData` | `byte[,][] LoadMeshData(Stream s, out TileDataHeader h)` |

File format: Magic `"TDAT"`, version 3, width/height/mapName/tileX/tileY/createTime + tile data blocks.

---

### GraphicalHelper.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `static class GraphicalHelper`

| Method | Signature |
|--------|-----------|
| `ToDetour` | `Vector3 ToDetour(Vector3 wow)` |
| `ToWow` | `Vector3 ToWow(Vector3 detour)` |
| `GetBoundsContaining` | bounds/containment checks |
| `IsPointInTriangle` | triangle point containment |

---

### TileDataHeader.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `class TileDataHeader`

| Property | Type |
|----------|------|
| `Width` | `int` |
| `Height` | `int` |
| `MapName` | `string` |
| `TileX` | `int` |
| `TileY` | `int` |
| `UtcCreateTime` | `DateTime` |

---

### MapConsts.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `static class MapConsts`

```csharp
public const float TileSize = 533.3333f;
```

---

### IoCGate.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `enum IoCGate : byte`

```
HordeWest=20, HordeSouth=21, HordeEast=22, AllianceNorth=23, AllianceWest=24, AllianceEast=25
```

---

### SotAGate.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `enum SotAGate : byte`

```
Green=20, Blue=21, Red=22, Purple=23, Yellow=24
```

---

### TileDataVersionException.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `class TileDataVersionException : ApplicationException`

Message: `"Tile data is wrong version!"`

---

### InvalidTileDataException.cs

**Namespace**: `Tripper.MeshMisc` | **Type**: `class InvalidTileDataException : Exception`

Constructor: `InvalidTileDataException(string message)`

---

## Layer 3 — `Tripper.RecastManaged.Detour` (Tripper.RecastManaged\Detour\)

All classes in this layer are **C++/CLI wrappers** around native `dt*` structs. They implement `IDisposable` and hold raw pointers. Native calls go through `<Module>.dt*()`.

### NavMesh.cs (309 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class NavMesh : IDisposable`

Wraps `dtNavMesh*`.

| Member | Signature |
|--------|-----------|
| **Native** | `<Module>.dtAllocNavMesh()`, `<Module>.dtFreeNavMesh()`, `<Module>.dtNavMesh.*` |
| `Init` | `Status Init(NavMeshData data)` |
| `Init` | `Status Init(NavMeshParams params)` |
| `AddTile` | `Status AddTile(NavMeshData data, out TileReference tileRef)` |
| `RemoveTile` | `Status RemoveTile(TileReference tileRef)` |
| `GetTileAt` | `MeshTile GetTileAt(int x, int y)` |
| `GetTileRefAt` | `TileReference GetTileRefAt(int x, int y)` |
| `GetTileRef` | `TileReference GetTileRef(MeshTile tile)` |
| `GetMaxTiles` | `int GetMaxTiles()` |
| `GetTile` | `MeshTile GetTile(int i)` |
| `SetPolyFlags` | `Status SetPolyFlags(PolygonReference polyRef, ushort flags)` |
| `GetPolyFlags` | `Status GetPolyFlags(PolygonReference polyRef, out ushort flags)` |
| `SetPolyArea` | `Status SetPolyArea(PolygonReference polyRef, byte area)` |
| `GetPolyArea` | `Status GetPolyArea(PolygonReference polyRef, out byte area)` |
| `GetTileAndPolyByRef` | `Status GetTileAndPolyByRef(PolygonReference, out MeshTile, out Poly)` |
| `StoreTileState` | `Status StoreTileState(MeshTile, byte[], int)` |
| `RestoreTileState` | `Status RestoreTileState(MeshTile, byte[], int)` |

---

### NavMeshQuery.cs (451 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class NavMeshQuery : IDisposable`

Wraps `dtNavMeshQuery*`.

| Member | Signature |
|--------|-----------|
| **Native** | `<Module>.dtAllocNavMeshQuery()`, `<Module>.dtFreeNavMeshQuery()`, `<Module>.dtNavMeshQuery.*` |
| `Init` | `Status Init(NavMesh mesh, int maxNodes)` |
| `FindNearestPolygon` | `Status FindNearestPolygon(Vector3 center, Vector3 extents, QueryFilter filter, out Vector3 nearestPt, out PolygonReference result)` |
| `QueryPolygons` | `Status QueryPolygons(Vector3 center, Vector3 extents, QueryFilter, PolygonReference[] polys, out int count, int max)` |
| `FindPath` | `Status FindPath(PolygonReference startRef, PolygonReference endRef, Vector3 startPos, Vector3 endPos, QueryFilter filter, int maxPathSize, out PolygonReference[] result)` |
| `FindStraightPath` | `Status FindStraightPath(Vector3 start, Vector3 end, PolygonReference[] path, int pathSize, out Vector3[] straightPath, out StraightPathFlags[] flags, out PolygonReference[] pathRefs, out int straightPathCount, int maxStraightPath)` |
| `InitSlicedFindPath` | `Status InitSlicedFindPath(PolygonReference start, PolygonReference end, Vector3 startPos, Vector3 endPos, QueryFilter filter)` |
| `UpdateSlicedFindPath` | `Status UpdateSlicedFindPath(int maxIter)` |
| `FinalizeSlicedFindPath` | `Status FinalizeSlicedFindPath(out PolygonReference[] path, out int pathCount, int maxPath)` |
| `FindPolysAroundCircle` | `Status FindPolysAroundCircle(...)` |
| `FindLocalNeighbourhood` | `Status FindLocalNeighbourhood(...)` |
| `GetPolyHeight` | `Status GetPolyHeight(PolygonReference, Vector3, out float)` |
| `ClosestPointOnPoly` | `Status ClosestPointOnPoly(PolygonReference, Vector3, out Vector3)` |
| `ClosestPointOnPolyBoundary` | `Status ClosestPointOnPolyBoundary(PolygonReference, Vector3, out Vector3)` |
| `Raycast` | `Status Raycast(PolygonReference, Vector3, Vector3, QueryFilter, out float, out Vector3, out PolygonReference[], out int, int)` |
| `SetTileLoaderFunction` | `void SetTileLoaderFunction(LoadTileDelegate del)` — lazy tile loading callback |

---

### QueryFilter.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class QueryFilter : IDisposable`

Wraps `dtQueryFilter*`.

| Member | Signature |
|--------|-----------|
| `IncludeFlags` | `ushort { get; set; }` |
| `ExcludeFlags` | `ushort { get; set; }` |
| `GetAreaCost` | `float GetAreaCost(byte areaType)` |
| `SetAreaCost` | `void SetAreaCost(byte areaType, float cost)` |
| Static `Default` | default filter instance |

---

### RandomizedQueryFilter.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class RandomizedQueryFilter : QueryFilter`

| Member | Signature |
|--------|-----------|
| `Clear` | `void Clear()` |
| `MinRandomizationFactor` | `float { get; set; }` |
| `MaxRandomizationFactor` | `float { get; set; }` |
| `SetRandomizationFactors` | `void SetRandomizationFactors(float min, float max)` |

---

### OffMeshConnection.cs (192 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class OffMeshConnection : IDisposable`

Wraps `dtOffMeshConnection*`.

| Property | Type |
|----------|------|
| `Start` | `Vector3` |
| `End` | `Vector3` |
| `Radius` | `float` |

---

### NavMeshData.cs (115 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class NavMeshData : IDisposable`

| Member | Signature |
|--------|-----------|
| Constructor | `NavMeshData(byte[] navData, int startIndex, int length)` |
| Constructor | `NavMeshData(byte* navData, int navDataSize, bool ownsData)` |
| `NavData` | `byte*` |
| `NavDataSize` | `int` |
| `OwnsData` | `bool` |
| `GetNavData` | `byte[] GetNavData()` |
| `GetNavData` | `void GetNavData(byte[] buffer, int start)` |

Uses `<Module>.dtAlloc()` / `<Module>.dtFree()` for native allocation.

---

### NavMeshParams.cs (180 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class NavMeshParams : IDisposable`

Wraps `dtNavMeshParams*`.

| Property | Type |
|----------|------|
| `MaxTiles` | `int` |
| `MaxPolys` | `int` |
| `TileWidth` | `float` |
| `TileHeight` | `float` |
| `Origin` | `Vector3` |

---

### NavMeshCreateParams.cs (547 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class NavMeshCreateParams : IDisposable`

Wraps `dtNavMeshCreateParams*` (136 bytes).

| Property | Type |
|----------|------|
| `Verts` | `ushort*` |
| `VertCount` | `int` |
| `Polys` | `ushort*` |
| `PolyFlags` | `ushort*` |
| `PolyAreas` | `byte*` |
| `PolyCount` | `int` |
| `NVP` | `int` (max verts per poly) |
| `DetailMeshes` | `uint*` |
| `DetailVerts` | `float*` |
| `DetailVertsCount` | `int` |
| `DetailTris` | `byte*` |
| `DetailTriCount` | `int` |
| Off-mesh connection properties | `float* OffMeshConVerts`, `float* OffMeshConRad`, `ushort* OffMeshConFlags`, `byte* OffMeshConAreas`, `byte* OffMeshConDir`, `uint* OffMeshConUserID`, `int OffMeshConCount` |
| `TileX`, `TileY` | `int` |
| `TileLayer` | `int` |
| `BMin`, `BMax` | `Vector3` (bounding box) |
| `WalkableHeight`, `WalkableRadius`, `WalkableClimb` | `float` |
| `CellSize`, `CellHeight` | `float` |
| `BuildBvTree` | `bool` |

---

### Detour.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class Detour`

| Member | Signature |
|--------|-----------|
| `CreateNavMeshData` | `static bool CreateNavMeshData(NavMeshCreateParams params, out NavMeshData data)` |
| `MaxVertsPerPolygon` | `static int` = 6 |

Native: `<Module>.dtCreateNavMeshData()`

---

### MeshTile.cs (523 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class MeshTile : IDisposable`

Wraps `dtMeshTile*` (60 bytes).

| Property | Type |
|----------|------|
| `Salt` | `uint` |
| `LinksFreeList` | `uint` |
| `Header` | `MeshHeader` |
| `Polys` | `dtPoly*` |
| `Verts` | `float*` |
| `Links` | `dtLink*` |
| `DetailMeshes` | `dtPolyDetail*` |
| `DetailVerts` | `float*` |
| `DetailTris` | `byte*` |
| `BvTree` | `dtBVNode*` |
| `OffMeshCons` | `dtOffMeshConnection*` |
| `Data` | `byte*` |
| `DataSize` | `int` |
| `Flags` | `TileFlags` |

---

### MeshHeader.cs (318 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class MeshHeader : IDisposable`

Wraps `dtMeshHeader*` (96 bytes).

| Property | Type |
|----------|------|
| `Magic` | `int` |
| `Version` | `int` |
| `PolyCount`, `VertCount` | `int` |
| `MaxLinkCount` | `int` |
| `DetailMeshCount`, `DetailVertCount`, `DetailTriCount` | `int` |
| `BvNodeCount` | `int` |
| `OffMeshConCount` | `int` |
| `OffMeshBase` | `int` |
| `WalkableHeight`, `WalkableRadius`, `WalkableClimb` | `float` |
| `BMin`, `BMax` | `Vector3` (bounding box) |
| `BvQuantFactor` | `float` |
| `X`, `Y`, `Layer` | `int` (tile coords) |
| `UserId` | `int` |

---

### Poly.cs (248 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class Poly : IDisposable`

Wraps `dtPoly*` (32 bytes).

| Property | Type |
|----------|------|
| `FirstLink` | `uint` |
| `Flags` | `ushort` (AbilityFlags) |
| `VertCount` | `byte` |
| `Verts` | `ushort[]` |
| `Neis` | `ushort[]` (neighbor indices) |
| `Type` | `PolyType` (Ground / OffmeshConnection) |
| `Area` | `byte` (AreaType) |

---

### PolyDetail.cs (153 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class PolyDetail : IDisposable`

Wraps `dtPolyDetail*` (12 bytes).

| Property | Type |
|----------|------|
| `VertBase` | `ushort` |
| `VertCount` | `byte` |
| `TriBase` | `ushort` |
| `TriCount` | `byte` |

---

### Link.cs (185 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `class Link : IDisposable`

Wraps `dtLink*` (12 bytes).

| Property | Type |
|----------|------|
| `Ref` | `PolygonReference` |
| `Next` | `uint` |
| `Edge` | `byte` |
| `Side` | `byte` |
| `BMin`, `BMax` | `byte` |

---

### Status.cs (94 lines)

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `struct Status`

| Member | Signature |
|--------|-----------|
| `Id` | `uint` |
| `Failed` | `bool { get; }` — via `<Module>.dtStatusFailed()` |
| `Succeeded` | `bool { get; }` — via `<Module>.dtStatusSucceed()` |
| `InProgress` | `bool { get; }` — via `<Module>.dtStatusInProgress()` |
| `Flags` | `StatusDetailFlag` |

---

### StatusDetailFlag.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `[Flags] enum StatusDetailFlag : uint`

```
Failure=0x80000000, Success=0x40000000, InProgress=0x20000000,
WrongMagic=1, WrongVersion=2, OutOfMemory=4, InvalidParam=8,
BufferTooSmall=16, OutOfNodes=32, PartialResult=64
```

---

### PolygonReference.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `struct PolygonReference` (Pack=1)

```csharp
public uint Id;
```

---

### TileReference.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `struct TileReference`

```csharp
public uint Id;
```

---

### StraightPathFlags.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `[Flags] enum StraightPathFlags : byte`

```
None=0, Start=1, End=2, OffmeshConnection=4
```

---

### PolyType.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `enum PolyType`

```
Ground = 0, OffmeshConnection = 1
```

---

### DirectionFlags.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `[Flags] enum DirectionFlags`

```
StartToEnd = 0, Bidirectional = 1
```

---

### TileFlags.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `enum TileFlags`

```
DT_TILE_FREE_DATA = 1
```

---

### LoadTileDelegate.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `delegate bool LoadTileDelegate(int x, int y)`

`[UnmanagedFunctionPointer(CallingConvention.Cdecl)]`

---

### NavMeshTileLoader.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `[NativeCppClass] internal struct NavMeshTileLoader`

Empty marker struct for C++/CLI native type.

---

### RandomizedDtQueryFilter.cs

**Namespace**: `Tripper.RecastManaged.Detour` | **Type**: `[NativeCppClass][UnsafeValueType] internal struct RandomizedDtQueryFilter`

Empty marker struct for C++/CLI native type.

---

## Tripper.RecastManaged\Recast\ (14 files)

Lower-level Recast voxelization — used for **mesh generation** (not runtime pathfinding). These are C++/CLI wrappers around native Recast types.

| File | Type |
|------|------|
| `CompactCell.cs` | `class CompactCell : IDisposable` — wraps `rcCompactCell*` |
| `CompactHeightfield.cs` | `class CompactHeightfield : IDisposable` — wraps `rcCompactHeightfield*` |
| `CompactSpan.cs` | `class CompactSpan : IDisposable` — wraps `rcCompactSpan*` |
| `ContourSet.cs` | `class ContourSet : IDisposable` — wraps `rcContourSet*` |
| `DefaultAreaType.cs` | `enum DefaultAreaType` — Null=0, Walkable=63 |
| `Heightfield.cs` | `class Heightfield : IDisposable` — wraps `rcHeightfield*` |
| `PolyMesh.cs` | `class PolyMesh : IDisposable` — wraps `rcPolyMesh*` |
| `PolyMeshDetail.cs` | `class PolyMeshDetail : IDisposable` — wraps `rcPolyMeshDetail*` |
| `rcCustomContext.cs` | `class rcCustomContext : IDisposable` — logging context |
| `Recast.cs` | `static class Recast` — static mesh building functions |
| `RecastContextLogCategory.cs` | `enum RecastContextLogCategory` — Progress, Warning, Error |
| `RecastContextLogEventArgs.cs` | `class RecastContextLogEventArgs : EventArgs` |
| `RecastContextManagedWrapper.cs` | Managed wrapper for rcCustomContext |
| `Span.cs` | `class Span : IDisposable` — wraps `rcSpan*` |

---

## Movement System (`Styx.WoWInternals`)

### WoWMovement.cs (626 lines)

**Namespace**: `Styx.WoWInternals` | **Type**: `static class WoWMovement`

Low-level movement — ClickToMove and keyboard input via ASM injection (GreyMagic Executor).

| Member | Signature |
|--------|-----------|
| `ClickToMoveInfo` | `ClickToMoveInfo { get; }` |
| `ActiveMoverGuid` | `WoWGuid { get; }` |
| `ActiveMover` | `WoWUnit { get; }` |
| `IsFacing` | `bool { get; }` |
| `ActiveInputControl` | `InputControl { get; }` |
| `CalculatePointFrom` | `static WoWPoint CalculatePointFrom(WoWPoint target, float distance)` |
| `ClickToMove` | `void ClickToMove(WoWPoint point)` |
| `ClickToMove` | `void ClickToMove(float x, float y, float z)` |
| `ClickToMove` | `void ClickToMove(WoWPoint point, float distance)` |
| `ClickToMove` | `void ClickToMove(WoWGuid guid, WoWPoint loc, ClickToMoveType type)` |
| `ClickToMove` | `void ClickToMove(WoWGuid guid, ClickToMoveType type)` |
| `Face` | `void Face(WoWGuid guid)` / `void Face()` |
| `StopFace` | `void StopFace()` |
| `ConstantFace` | `void ConstantFace(WoWGuid guid)` |
| `ConstantFaceStop` | `void ConstantFaceStop(WoWGuid guid)` |
| `Move` | `void Move(MovementDirection direction)` |
| `Move` | `void Move(MovementDirection dir, TimeSpan time)` |
| `MoveStop` | `void MoveStop(MovementDirection direction)` |
| `MoveStop` | `void MoveStop()` |
| `GetHeadingDiff` | `void GetHeadingDiff(double curr, double dest, out double diff, out int coeff)` |

**Nested types:**

#### ClickToMoveType (enum)
```
LeftClick=1, Face=2, StopThrowsException=3, Move=4, NpcInteract=5, Loot=6,
ObjInteract=7, FaceOther=8, Skin=9, AttackPosition=10, AttackGuid=11,
ConstantFace=12, None=13
```

#### MovementDirection (flags enum : uint)
```
None=0, RMouse=1, LMouse=2, Forward=16, Backwards=32, StrafeLeft=64,
StrafeRight=128, TurnLeft=256, TurnRight=512, PitchUp=1024, PitchDown=2048,
AutoRun=4096, JumpAscend=8192, Descend=16384, ClickToMove=4194304,
IsCTMing=2097152, ForwardBackMovement=65536, StrafeMovement=131072,
TurnMovement=262144, StrafeMask=131264, TurnMask=262912, MoveMask=69680,
All=463856, AllAllowed=29680
```

#### InputControl (struct)
```csharp
public uint Time;
public MovementDirection Flags;
public MovementControl Movement;
```

#### MovementControl (struct)
Empty wrapper with internal `uint` fields.

**Implementation note**: Uses GreyMagic `Executor` (ASM injection) for `smethod_1` (keyboard moves) and `smethod_4` (ClickToMove). Not P/Invoke — direct x86 ASM generation pushed into wow.exe process.

---

### ClickToMoveInfo.cs

**Namespace**: `Styx.WoWInternals` | **Type**: `class ClickToMoveInfo`

| Property | Type |
|----------|------|
| `IsClickMoving` | `bool` — `Type == ClickToMoveType.Move` |
| `IsUsing` | `bool` — `Type != ClickToMoveType.None` |
| `Type` | `ClickToMoveType` (memory read) |
| `ClickPos` | `WoWPoint` (memory read) |
| `InteractGuid` | `WoWGuid` (memory read) |

---

## Behavior Tree Integration (`CommonBehaviors.Actions`)

### NavigationAction.cs

**Namespace**: `CommonBehaviors.Actions` | **Type**: `class NavigationAction : TreeSharp.Action`

| Constructor | Signature |
|-------------|-----------|
| | `NavigationAction(WoWPoint point)` |
| | `NavigationAction(NavTypeDelegate navType)` |
| | `NavigationAction(GetPointDelegate getPointDel)` |
| | `NavigationAction(GetPointDelegate getPointDel = null, NavTypeDelegate navTypeDel = null)` |

`Run()`: If `navType == NavType.Fly` → `Flightor.MoveTo()`, else → `Navigator.MoveTo()`.

---

### NavigationInfo.cs

**Namespace**: `CommonBehaviors.Actions` | **Type**: `class NavigationInfo`

| Property | Type |
|----------|------|
| `Destination` | `WoWPoint` |
| `Height` | `float` |

---

### NavTypeDelegate.cs

**Namespace**: `CommonBehaviors.Actions` | **Type**: `delegate NavType NavTypeDelegate(object context)`

---

## Complete File Index

### Styx\Pathing\ (16 files + 2 subdirs)

| # | File | Lines | Namespace | Primary Type |
|---|------|-------|-----------|-------------|
| 1 | `Navigator.cs` | ~300 | Styx.Pathing | static class Navigator |
| 2 | `NavigationProvider.cs` | ~100 | Styx.Pathing | abstract class NavigationProvider |
| 3 | `MeshNavigator.cs` | 1318 | Styx.Pathing | class MeshNavigator : NavigationProvider |
| 4 | `Flightor.cs` | 1690 | Styx.Pathing | static class Flightor |
| 5 | `IPlayerMover.cs` | ~20 | Styx.Pathing | interface IPlayerMover |
| 6 | `KeyboardMover.cs` | ~100 | Styx.Pathing | class KeyboardMover : IPlayerMover |
| 7 | `StuckHandler.cs` | ~50 | Styx.Pathing | abstract class StuckHandler |
| 8 | `MeshMovePath.cs` | ~30 | Styx.Pathing | class MeshMovePath |
| 9 | `MoveResult.cs` | ~20 | Styx.Pathing | enum MoveResult |
| 10 | `MoveResultExtensions.cs` | ~15 | Styx.Pathing | static class MoveResultExtensions |
| 11 | `ITerrainHeightProvider.cs` | ~15 | Styx.Pathing | interface ITerrainHeightProvider |
| 12 | `NavigationProviderChangedEventArgs.cs` | ~20 | Styx.Pathing | class NavigationProviderChangedEventArgs\<T\> |
| 13 | `BlackspotQueryFlags.cs` | ~15 | Styx.Pathing | enum BlackspotQueryFlags |
| 14 | `PathGenerationFailStep.cs` | ~15 | Styx.Pathing | enum PathGenerationFailStep |
| 15 | `FlightorAnnotation/IndoorEntrance.cs` | ~55 | Styx.Pathing.FlightorAnnotation | class IndoorEntrance |
| 16 | `FlightorNavigation/BlackspotManager.cs` | 458 | Styx.Pathing.FlightorNavigation | static class BlackspotManager |
| 17 | `FlightorNavigation/PolyNav.cs` | 265 | Styx.Pathing.FlightorNavigation | class PolyNav |
| 18 | `FlightorNavigation/Areas.cs` | 1145 | Styx.Pathing.FlightorNavigation | static class Areas |

### Tripper\Navigation\ (13 files)

| # | File | Lines | Primary Type |
|---|------|-------|-------------|
| 1 | `WowNavigator.cs` | 809 | class WowNavigator : IDisposable |
| 2 | `IMeshManager.cs` | ~20 | interface IMeshManager |
| 3 | `WorldMeshManager.cs` | 438 | class WorldMeshManager : IDisposable, IMeshManager |
| 4 | `GarrisonMeshManager.cs` | ~150 | class GarrisonMeshManager : IDisposable, IMeshManager |
| 5 | `PathFindResult.cs` | ~60 | class PathFindResult |
| 6 | `WowQueryFilter.cs` | ~50 | class WowQueryFilter : IDisposable |
| 7 | `NavHelper.cs` | ~20 | static class NavHelper |
| 8 | `PathPostProcessing.cs` | ~10 | enum PathPostProcessing |
| 9 | `PathFindStep.cs` | ~15 | enum PathFindStep |
| 10 | `PathFindProgressEventArgs.cs` | ~20 | class PathFindProgressEventArgs |
| 11 | `MapLoadedEventArgs.cs` | ~15 | class MapLoadedEventArgs |
| 12 | `TileLoadedEventArgs.cs` | ~15 | class TileLoadedEventArgs |
| 13 | `NavigatorLogMessage.cs` | ~5 | delegate NavigatorLogMessage |

### Tripper\MeshMisc\ (12 files)

| # | File | Primary Type |
|---|------|-------------|
| 1 | `AreaType.cs` | enum AreaType : byte |
| 2 | `AbilityFlags.cs` | enum AbilityFlags : ushort |
| 3 | `TileIdentifier.cs` | struct TileIdentifier |
| 4 | `MeshMapCalculator.cs` | class MeshMapCalculator |
| 5 | `MeshManager.cs` | static class MeshManager |
| 6 | `GraphicalHelper.cs` | static class GraphicalHelper |
| 7 | `TileDataHeader.cs` | class TileDataHeader |
| 8 | `MapConsts.cs` | static class MapConsts |
| 9 | `IoCGate.cs` | enum IoCGate : byte |
| 10 | `SotAGate.cs` | enum SotAGate : byte |
| 11 | `TileDataVersionException.cs` | class TileDataVersionException |
| 12 | `InvalidTileDataException.cs` | class InvalidTileDataException |

### Tripper.RecastManaged\Detour\ (25 files)

| # | File | Primary Type |
|---|------|-------------|
| 1 | `NavMesh.cs` | class NavMesh : IDisposable |
| 2 | `NavMeshQuery.cs` | class NavMeshQuery : IDisposable |
| 3 | `QueryFilter.cs` | class QueryFilter : IDisposable |
| 4 | `RandomizedQueryFilter.cs` | class RandomizedQueryFilter : QueryFilter |
| 5 | `OffMeshConnection.cs` | class OffMeshConnection : IDisposable |
| 6 | `NavMeshData.cs` | class NavMeshData : IDisposable |
| 7 | `NavMeshParams.cs` | class NavMeshParams : IDisposable |
| 8 | `NavMeshCreateParams.cs` | class NavMeshCreateParams : IDisposable |
| 9 | `Detour.cs` | class Detour |
| 10 | `MeshTile.cs` | class MeshTile : IDisposable |
| 11 | `MeshHeader.cs` | class MeshHeader : IDisposable |
| 12 | `Poly.cs` | class Poly : IDisposable |
| 13 | `PolyDetail.cs` | class PolyDetail : IDisposable |
| 14 | `Link.cs` | class Link : IDisposable |
| 15 | `Status.cs` | struct Status |
| 16 | `StatusDetailFlag.cs` | enum StatusDetailFlag : uint |
| 17 | `PolygonReference.cs` | struct PolygonReference |
| 18 | `TileReference.cs` | struct TileReference |
| 19 | `StraightPathFlags.cs` | enum StraightPathFlags : byte |
| 20 | `PolyType.cs` | enum PolyType |
| 21 | `DirectionFlags.cs` | enum DirectionFlags |
| 22 | `TileFlags.cs` | enum TileFlags |
| 23 | `LoadTileDelegate.cs` | delegate LoadTileDelegate |
| 24 | `NavMeshTileLoader.cs` | internal struct NavMeshTileLoader |
| 25 | `RandomizedDtQueryFilter.cs` | internal struct RandomizedDtQueryFilter |

### Tripper.RecastManaged\Recast\ (14 files)

| # | File | Primary Type |
|---|------|-------------|
| 1-14 | CompactCell, CompactHeightfield, CompactSpan, ContourSet, DefaultAreaType, Heightfield, PolyMesh, PolyMeshDetail, rcCustomContext, Recast, RecastContextLogCategory, RecastContextLogEventArgs, RecastContextManagedWrapper, Span | Mesh generation wrappers |

### Other nav-related files

| File | Namespace | Type |
|------|-----------|------|
| `Styx\NavType.cs` | Styx | enum NavType |
| `Styx\WoWPoint.cs` | Styx | struct WoWPoint |
| `Styx\WoWInternals\WoWMovement.cs` | Styx.WoWInternals | static class WoWMovement |
| `Styx\WoWInternals\ClickToMoveInfo.cs` | Styx.WoWInternals | class ClickToMoveInfo |
| `CommonBehaviors\Actions\NavigationAction.cs` | CommonBehaviors.Actions | class NavigationAction |
| `CommonBehaviors\Actions\NavigationInfo.cs` | CommonBehaviors.Actions | class NavigationInfo |
| `CommonBehaviors\Actions\NavTypeDelegate.cs` | CommonBehaviors.Actions | delegate NavTypeDelegate |

---

## Native Call Summary

**No DllImport/P/Invoke anywhere.** All native Detour/Recast functions are called through C++/CLI `<Module>` static methods:

| Native Pattern | Used In | Purpose |
|----------------|---------|---------|
| `<Module>.dtAllocNavMesh()` | NavMesh ctor | Allocate native dtNavMesh |
| `<Module>.dtFreeNavMesh()` | NavMesh.Dispose | Free native dtNavMesh |
| `<Module>.dtAllocNavMeshQuery()` | NavMeshQuery ctor | Allocate native dtNavMeshQuery |
| `<Module>.dtFreeNavMeshQuery()` | NavMeshQuery.Dispose | Free native dtNavMeshQuery |
| `<Module>.dtNavMesh.init()` | NavMesh.Init | Initialize mesh |
| `<Module>.dtNavMesh.addTile()` | NavMesh.AddTile | Add tile data |
| `<Module>.dtNavMesh.removeTile()` | NavMesh.RemoveTile | Remove tile |
| `<Module>.dtNavMeshQuery.init()` | NavMeshQuery.Init | Initialize query |
| `<Module>.dtNavMeshQuery.findNearestPoly()` | NavMeshQuery.FindNearestPolygon | Find nearest polygon |
| `<Module>.dtNavMeshQuery.findPath()` | NavMeshQuery.FindPath | A* pathfinding |
| `<Module>.dtNavMeshQuery.findStraightPath()` | NavMeshQuery.FindStraightPath | String-pull smoothing |
| `<Module>.dtNavMeshQuery.initSlicedFindPath()` | NavMeshQuery.InitSlicedFindPath | Async pathfinding start |
| `<Module>.dtNavMeshQuery.updateSlicedFindPath()` | NavMeshQuery.UpdateSlicedFindPath | Async pathfinding step |
| `<Module>.dtNavMeshQuery.finalizeSlicedFindPath()` | NavMeshQuery.FinalizeSlicedFindPath | Async pathfinding finish |
| `<Module>.dtNavMeshQuery.raycast()` | NavMeshQuery.Raycast | Line-of-sight ray |
| `<Module>.dtCreateNavMeshData()` | Detour.CreateNavMeshData | Build tile from params |
| `<Module>.dtStatusFailed()` | Status.Failed | Check status bits |
| `<Module>.dtStatusSucceed()` | Status.Succeeded | Check status bits |
| `<Module>.dtStatusInProgress()` | Status.InProgress | Check status bits |
| `<Module>.dtAlloc()` / `<Module>.dtFree()` | NavMeshData | Native memory allocation |
| `<Module>.@new()` / `<Module>.delete()` | All C++/CLI wrappers | C++ new/delete |

---

## Data Flow: MoveTo Call

```
Navigator.MoveTo(WoWPoint dest)
  └→ MeshNavigator.MoveTo(dest)
       ├─ Check garrison entry/exit
       ├─ Handle doors (method_7/8: Interact with closed doors)
       ├─ FindPath(start, end)               ← runs on Task<>
       │    ├─ Nav.FindPath(start, end)      ← WowNavigator
       │    │    ├─ GetManagerFromLocation()  ← pick World or Garrison mesh
       │    │    ├─ manager.FindPath()        ← IMeshManager
       │    │    │    ├─ NavMeshQuery.FindNearestPolygon(start)
       │    │    │    ├─ NavMeshQuery.InitSlicedFindPath()
       │    │    │    ├─ NavMeshQuery.UpdateSlicedFindPath() (loop)
       │    │    │    ├─ NavMeshQuery.FinalizeSlicedFindPath()
       │    │    │    └─ NavMeshQuery.FindStraightPath()
       │    │    └─ Return PathFindResult
       │    └─ Return PathFindResult
       ├─ Handle off-mesh: Elevator/Portal/InteractUnit/InteractObject
       ├─ Check stuck (StuckHandler.IsStuck → Unstick)
       └─ PlayerMover.MoveTowards(nextPoint)
            └→ WoWMovement.ClickToMove(point)
                 └→ ASM injection into wow.exe (GreyMagic Executor)
```
