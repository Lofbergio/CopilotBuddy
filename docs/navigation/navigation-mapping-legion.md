# HonorBuddy Legion — Comprehensive Navigation File Mapping

> **Source:** `c:\Users\Texy\Desktop\hb legion\Honorbuddy\`
> **Expansion:** Legion (7.x)
> **Purpose:** Navigation & UI reference for CopilotBuddy (per instructions.md)

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Styx\Pathing — Core Navigation API](#2-styxpathing--core-navigation-api)
3. [Styx\Pathing\FlightorNavigation — Aerial Navigation](#3-styxpathingflightornavigation--aerial-navigation)
4. [Styx\Pathing\FlightorAnnotation — Indoor/Entrance Data](#4-styxpathingflightorannotation--indoorentrance-data)
5. [Tripper\Navigation — Navmesh Engine](#5-trippernavigation--navmesh-engine)
6. [Tripper\MeshMisc — Mesh Infrastructure](#6-trippermeshmisc--mesh-infrastructure)
7. [Tripper\LZMACompression — Tile Compression](#7-tripperlzmacompression--tile-compression)
8. [CommonBehaviors\Actions — Navigation Actions](#8-commonbehaviorsactions--navigation-actions)
9. [Bots — Bot-Specific Navigation](#9-bots--bot-specific-navigation)
10. [Styx\CommonBot — Flight Path (Taxi) System](#10-styxcommonbot--flight-path-taxi-system)
11. [Obfuscated Files (ns\*) — Navigation References](#11-obfuscated-files-ns--navigation-references)
12. [Missing Class Definitions (Obfuscated)](#12-missing-class-definitions-obfuscated)
13. [DLL References](#13-dll-references)
14. [NEW Features vs WoD 6.2.3](#14-new-features-vs-wod-623)
15. [Architecture Diagram](#15-architecture-diagram)

---

## 1. Architecture Overview

Legion HB uses the same navigation architecture as WoD 6.2.3:

```
Navigator (static facade)
  ├── NavigationProvider  →  MeshNavigator : NavigationProvider (default)
  │                           └── AvoidanceNavigationProvider : MeshNavigator (DungeonBuddy)
  ├── PlayerMover         →  IPlayerMover (KeyboardMover / ClickToMoveMover)
  └── StuckHandler        →  StuckHandler (abstract)

MeshNavigator
  └── WowNavigator (Tripper.Navigation)
        ├── WorldMeshManager → NavMesh + NavMeshQuery (Detour)
        └── GarrisonMeshManager → NavMesh + NavMeshQuery (Garrison-specific, NEW)

Flightor (static facade)
  └── BlackspotManager (aerial no-fly polygon zones)
  └── PolyNav (A* over 2D visibility graph)
  └── IndoorEntrance[] (dismount points from profiles)
```

**Key pattern:** `Navigator.MoveTo()` delegates to `NavigationProvider.MoveTo()` → `MeshNavigator.MoveTo()` → `WowNavigator.FindPath()` → Detour `NavMeshQuery`.

---

## 2. Styx\Pathing — Core Navigation API

### MeshNavigator.cs
- **Path:** `Styx\Pathing\MeshNavigator.cs`
- **Lines:** 1340
- **Class:** `MeshNavigator : NavigationProvider`
- **Namespace:** `Styx.Pathing`
- **Functionality:** Main navigation provider — wraps Tripper's WowNavigator, generates paths, moves along them, handles off-mesh connections (elevators, portals, interact objects/units, doors, garrison exits, flight paths).

| Member | Kind | Description |
|--------|------|-------------|
| `MoveTo(WoWPoint)` | override method | Main move-to method, returns `MoveResult` |
| `MovePath(MeshMovePath)` | method | Move along a pre-built path |
| `FindPath(WoWPoint, WoWPoint)` | method | Generate a path between two points |
| `GeneratePath(WoWPoint, WoWPoint)` | override method | Abstract implementation — generates path via WowNavigator |
| `CanNavigateWithin(WoWPoint, WoWPoint, float)` | override method | Quick navigability check |
| `CanNavigateFully(WoWPoint, WoWPoint)` | override method | Full navigability check |
| `PathDistance(WoWPoint, WoWPoint)` | override method | Calculate path distance |
| `Clear()` | override method | Clear current path and movement state |
| `UpdateMaps()` | method | Reload navmesh maps |
| `Nav` | property | `WowNavigator` instance |
| `CurrentMovePath` | property | Active `MeshMovePath` being followed |
| `PathPrecision` | override property | How close to waypoint before advancing |
| `CurrentHopAbilityFlags` | property | `AbilityFlags` of current path segment |
| `method_5` | private | Get `AreaType` from polygon reference |
| `method_7` | private | Door handling (interact with game objects) |
| `method_10` | private | Flight path detection and usage |
| `method_11` | private | Path generation with garrison exit support |
| `method_14` | private | Path start skip via navmesh raycast |
| `method_18` | private | Off-mesh connection dispatcher by `AreaType` |
| `method_20` | private | Elevator handler |
| `method_21` | private | InteractObject handler (portals, etc.) |
| `method_22` | private | InteractUnit handler |
| `method_23` | private | Portal handler (DefendersPortal, HordePortal, AlliancePortal) |
| `method_24` | private | Normal movement along path points |
| `method_28` | private | Toggle alive/dead query filter |

**Events hooked:** `BotEvents.OnBotStarted`, `OnBotStopped`, `OnPulse`; `Mount.OnMountUp` (blocks mount during elevator).

**References to obfuscated types:** `ns36`, `ns71.Class1039` (map updater), `ns72`, `ns81`.

---

### NavigationProvider.cs
- **Path:** `Styx\Pathing\NavigationProvider.cs`
- **Lines:** 125
- **Class:** `NavigationProvider` (abstract)
- **Namespace:** `Styx.Pathing`
- **Functionality:** Abstract base class for all navigation providers.

| Member | Kind | Description |
|--------|------|-------------|
| `MoveTo(WoWPoint)` | abstract method | Navigate to a point, returns `MoveResult` |
| `PathPrecision` | abstract property | Distance threshold for waypoint arrival |
| `GeneratePath(WoWPoint, WoWPoint)` | abstract method | Generate `MeshMovePath` |
| `AtLocation(WoWPoint, WoWPoint)` | abstract method | Check if location matches |
| `StuckHandler` | virtual property | Get/set `StuckHandler` |
| `Clear()` | virtual method | Clear navigation state |
| `CanNavigateWithin(WoWPoint, WoWPoint, float)` | virtual method | Quick check |
| `CanNavigateFully(WoWPoint, WoWPoint)` | virtual method | Full check |
| `PathDistance(WoWPoint, WoWPoint)` | virtual method | Path distance calculation |
| `OnSetAsCurrent()` | virtual method | Called when set as active provider |
| `OnRemoveAsCurrent()` | virtual method | Called when removed as active provider |
| `IsCurrent` | property | `Navigator.NavigationProvider == this` |

---

### IPlayerMover.cs
- **Path:** `Styx\Pathing\IPlayerMover.cs`
- **Lines:** 19
- **Interface:** `IPlayerMover`
- **Namespace:** `Styx.Pathing`
- **Functionality:** Interface for low-level player movement.

| Member | Kind | Description |
|--------|------|-------------|
| `Move(WoWMovement.MovementDirection)` | method | Move in a direction |
| `MoveTowards(WoWPoint)` | method | Move towards a point |
| `MoveStop()` | method | Stop movement |

---

### StuckHandler.cs
- **Path:** `Styx\Pathing\StuckHandler.cs`
- **Lines:** 34
- **Class:** `StuckHandler` (abstract)
- **Namespace:** `Styx.Pathing`
- **Functionality:** Abstract base for stuck detection and recovery.

| Member | Kind | Description |
|--------|------|-------------|
| `IsStuck()` | abstract method | Check if bot is stuck |
| `Unstick()` | abstract method | Attempt to get unstuck |
| `Reset()` | virtual method | Reset stuck detection state |
| `OnSetAsCurrent()` | virtual method | Called when activated |
| `OnRemoveAsCurrent()` | virtual method | Called when deactivated |

---

### KeyboardMover.cs
- **Path:** `Styx\Pathing\KeyboardMover.cs`
- **Lines:** 95
- **Class:** `KeyboardMover : IPlayerMover`
- **Namespace:** `Styx.Pathing`
- **Functionality:** Moves player by facing target and pressing forward key.

| Member | Kind | Description |
|--------|------|-------------|
| `Move(MovementDirection)` | method | Send movement keys |
| `MoveTowards(WoWPoint)` | method | Face point and move forward |
| `MoveStop()` | method | Stop all movement keys |

---

### BlackspotQueryFlags.cs
- **Path:** `Styx\Pathing\BlackspotQueryFlags.cs`
- **Lines:** 16
- **Enum:** `BlackspotQueryFlags` (Flags)
- **Namespace:** `Styx.Pathing`

| Value | Integer |
|-------|---------|
| `Static` | 1 |
| `Dynamic` | 2 |
| `All` | 0xFFFFFFFF |

---

### NavType.cs
- **Path:** `Styx\NavType.cs`
- **Lines:** 12
- **Enum:** `NavType`
- **Namespace:** `Styx`

| Value | Description |
|-------|-------------|
| `Run` | Ground navigation |
| `Fly` | Aerial navigation (Flightor) |

---

## 3. Styx\Pathing\FlightorNavigation — Aerial Navigation

### BlackspotManager.cs
- **Path:** `Styx\Pathing\FlightorNavigation\BlackspotManager.cs`
- **Lines:** 473
- **Class:** `BlackspotManager` (static)
- **Namespace:** `Styx.Pathing.FlightorNavigation`
- **Functionality:** Manages aerial blackspots (polygon-based no-fly zones). Integrates with profile system via `AerialBlackspots` per map+faction. Supports static and dynamic blackspots.

| Member | Kind | Description |
|--------|------|-------------|
| `Blackspots` | static property | Current blackspot collection |
| `IsInBlackspot(WoWPoint)` | static method | Check if point is in a no-fly zone |
| `AddBlackspots(...)` | static method | Add blackspot polygons |
| `RemoveBlackspots(...)` | static method | Remove blackspot polygons |
| `GetBlackspots(BlackspotQueryFlags)` | static method | Query blackspots by type |
| Methods for profile loading | private | Parse XML profile blackspot definitions |

---

### PolyNav.cs
- **Path:** `Styx\Pathing\FlightorNavigation\PolyNav.cs`
- **Lines:** 272
- **Class:** `PolyNav`
- **Namespace:** `Styx.Pathing.FlightorNavigation`
- **Functionality:** Polygon-based 2D aerial navigation using A* over a visibility graph with polygon holes (blackspots as obstacles).

| Member | Kind | Description |
|--------|------|-------------|
| `GetConnections(Vector2, out Vector2)` | method | Find visible neighbor points from polygon mesh |
| A* pathfinding logic | internal | Navigates around blackspot polygons in 2D |

---

## 4. Styx\Pathing\FlightorAnnotation — Indoor/Entrance Data

### IndoorEntrance.cs
- **Path:** `Styx\Pathing\FlightorAnnotation\IndoorEntrance.cs`
- **Lines:** 67
- **Class:** `IndoorEntrance`
- **Namespace:** `Styx.Pathing.FlightorAnnotation`
- **Functionality:** Data class representing a dismount/entrance point for Flightor. Parsed from XML profiles.

| Member | Kind | Description |
|--------|------|-------------|
| `Location` | property | `WoWPoint` — entrance position |
| `Dismount` | property | `bool` — whether to dismount |
| `Radius` | property | `float` — activation radius |
| `FromXml(XElement)` | static method | Parse from XML profile element |

---

## 5. Tripper\Navigation — Navmesh Engine

### WowNavigator.cs
- **Path:** `Tripper\Navigation\WowNavigator.cs`
- **Lines:** 809
- **Class:** `WowNavigator : IDisposable`
- **Namespace:** `Tripper.Navigation`
- **Functionality:** Core navmesh navigator wrapping Recast/Detour. Manages world and garrison meshes, query filters, map loading, path finding.

| Member | Kind | Description |
|--------|------|-------------|
| `WorldMesh` | property | `WorldMeshManager` — main world navmesh |
| `GarrisonMesh` | property | `GarrisonMeshManager` — garrison-specific navmesh (NEW) |
| `QueryFilter` | property | `WowQueryFilter` — current Detour query filter |
| `Extents` | property | `Vector3` — search extents for poly lookup |
| `PathPostProcessing` | property | `PathPostProcessing` — path smoothing mode |
| `MapNames` | property | `ICollection<string>` — loaded map names |
| `PrimaryMapName` | property | `string` — primary map |
| `FindPath(Vector3, Vector3)` | method | Core pathfinding via Detour, returns `PathFindResult` |
| `ChangeMap(ICollection<string>)` | method | Switch active map(s) |
| `SetFactionQueryFilter(bool)` | method | Set Horde/Alliance filter |
| `ResetQueryFilter()` | method | Reset to default filter |
| `IsWithinGarrison()` | method | Check if in garrison bounds (NEW) |
| `GetManagerFromLocation()` | method | Return WorldMesh or GarrisonMesh based on location |
| `Dispose()` | method | Cleanup meshes |
| `GetNewDefaultQueryFilter()` | static method | Create default `WowQueryFilter` |
| `SetDefaultQueryFilterCosts()` | static method | Configure area type costs |
| `GetNewFactionQueryFilter(bool)` | static method | Create faction-specific filter |
| `OnPathFindProgress` | event | Path find progress callback |
| `OnMapLoaded` | event | Map loaded callback |
| `OnNavigatorLogMessage` | event | Log messages from navigator |
| `OnReplacementsLoaded` | event | Mesh replacement data loaded |

**Stored query filters:** "Default", "Horde", "Alliance", "Horde_DeathKnightStart", "Alliance_DeathKnightStart".

**Garrison boundaries:** `vector2_1` (Alliance polygon), `vector2_2` (Horde polygon) — used by `IsWithinGarrison()`.

---

### WorldMeshManager.cs
- **Path:** `Tripper\Navigation\WorldMeshManager.cs`
- **Lines:** 441
- **Class:** `WorldMeshManager : IDisposable, IMeshManager`
- **Namespace:** `Tripper.Navigation`
- **Functionality:** Manages the main world navmesh. Loads tiles on demand, provides `NavMesh` and `NavMeshQuery` to WowNavigator.

| Member | Kind | Description |
|--------|------|-------------|
| `Nav` | property | Parent `WowNavigator` |
| `Mesh` | property | `NavMesh` (Detour) |
| `MeshQuery` | property | `NavMeshQuery` (Detour) |
| `FindPath(Vector3, Vector3)` | method | Delegate to NavMeshQuery |
| `LoadTile(TileIdentifier)` | method | Load a map tile |
| `TileLoaded` | event | Fired when a tile is loaded |
| `SubTileLoaded` | event | Fired when a sub-tile is loaded |

Contains internal class `Class1458` for mesh file management.

---

### GarrisonMeshManager.cs
- **Path:** `Tripper\Navigation\GarrisonMeshManager.cs`
- **Lines:** 238
- **Class:** `GarrisonMeshManager : IDisposable, IMeshManager`
- **Namespace:** `Tripper.Navigation`
- **Functionality:** **NEW in WoD/Legion.** Manages garrison-specific navmesh, separate from world mesh. Supports partial path fallback locations per faction.

| Member | Kind | Description |
|--------|------|-------------|
| `IsLoaded` | property | Whether garrison mesh is loaded |
| `MapName` | property | Garrison map name |
| `Nav` | property | Parent `WowNavigator` |
| `Mesh` | property | `NavMesh` (Detour) |
| `MeshQuery` | property | `NavMeshQuery` (Detour) |
| `FindPath(Vector3, Vector3)` | method | Pathfind within garrison |
| `LoadTile(TileIdentifier)` | method | Load garrison tile |
| `HordePartialPathLocation` | static property | `(5636.969, 4525.909, 119.7096)` — fallback exit for Horde garrison |
| `AlliancePartialPathLocation` | static property | `(1920.831, 294.1213, 88.966)` — fallback exit for Alliance garrison |

---

### IMeshManager.cs
- **Path:** `Tripper\Navigation\IMeshManager.cs`
- **Lines:** 22
- **Interface:** `IMeshManager`
- **Namespace:** `Tripper.Navigation`
- **Functionality:** Common interface for mesh managers.

| Member | Kind | Description |
|--------|------|-------------|
| `Mesh` | property | `NavMesh` |
| `MeshQuery` | property | `NavMeshQuery` |
| `FindPath(Vector3, Vector3)` | method | Pathfind between two points |
| `LoadTile(TileIdentifier)` | method | Load a tile |

---

### PathFindResult.cs
- **Path:** `Tripper\Navigation\PathFindResult.cs`
- **Lines:** 158
- **Class:** `PathFindResult`
- **Namespace:** `Tripper.Navigation`
- **Functionality:** Result of a navmesh pathfind operation.

| Member | Kind | Description |
|--------|------|-------------|
| `Manager` | property | `IMeshManager` that produced the path |
| `Elapsed` | property | `TimeSpan` — pathfind duration |
| `Status` | property | `Status` (Detour status code) |
| `Polygons` | property | `PolygonReference[]` — path polygon corridor |
| `Flags` | property | `StraightPathFlags[]` — per-point flags |
| `Points` | property | `Vector3[]` — path waypoints |
| `AbilityFlags` | property | `AbilityFlags[]` — per-segment ability flags |
| `PolyTypes` | property | `AreaType[]` — per-segment area types |
| `StartPoly` | property | `PolygonReference` — start polygon |
| `EndPoly` | property | `PolygonReference` — end polygon |
| `Start` | property | `Vector3` — start position |
| `End` | property | `Vector3` — end position |
| `Aborted` | property | `bool` — pathfind was aborted |
| `Succeeded` | property | `bool` — pathfind succeeded |
| `IsPartialPath` | property | `bool` — only partial path found |
| `FailStep` | property | `PathFindStep` — which step failed |

---

### WowQueryFilter.cs
- **Path:** `Tripper\Navigation\WowQueryFilter.cs`
- **Lines:** 92
- **Class:** `WowQueryFilter`
- **Namespace:** `Tripper.Navigation`
- **Functionality:** Wraps Detour `QueryFilter` with `AbilityFlags` (include/exclude) and per-`AreaType` costs.

| Member | Kind | Description |
|--------|------|-------------|
| `Filter` | property | Underlying Detour `QueryFilter` |
| `IncludeFlags` | property | `AbilityFlags` — which abilities to include |
| `ExcludeFlags` | property | `AbilityFlags` — which abilities to exclude |
| `GetCost(AreaType)` | method | Get traversal cost for area type |
| `SetCost(AreaType, float)` | method | Set traversal cost for area type |

---

### NavHelper.cs
- **Path:** `Tripper\Navigation\NavHelper.cs`
- **Lines:** 42
- **Class:** `NavHelper` (static)
- **Namespace:** `Tripper.Navigation`
- **Functionality:** Coordinate conversion between WoW and Detour coordinate systems.

| Member | Kind | Description |
|--------|------|-------------|
| `ToNav(Vector3)` | static method | WoW coords → Detour coords (via `GraphicalHelper.ToDetour`) |
| `ToWow(Vector3)` | static method | Detour coords → WoW coords (via `GraphicalHelper.ToWow`) |

---

### PathPostProcessing.cs
- **Path:** `Tripper\Navigation\PathPostProcessing.cs`
- **Lines:** 11
- **Enum:** `PathPostProcessing`
- **Namespace:** `Tripper.Navigation`

| Value | Description |
|-------|-------------|
| `None` | No post-processing |
| `MoveAwayFromEdges` | Push waypoints away from mesh edges |
| `Randomize` | Randomize path slightly (used by DungeonBuddy) |

---

### PathFindStep.cs
- **Path:** `Tripper\Navigation\PathFindStep.cs`
- **Lines:** 25
- **Enum:** `PathFindStep`
- **Namespace:** `Tripper.Navigation`

| Value | Description |
|-------|-------------|
| `None` | No step |
| `FindStartPoly` | Finding start polygon |
| `FindEndPoly` | Finding end polygon |
| `InitPathFind` | Initializing pathfind |
| `UpdatePathFind` | Running pathfind iterations |
| `FinalizePathFind` | Finalizing path |
| `SnapPartialPathToEnd` | Snapping partial path endpoint |
| `FindStraightPath` | Converting polygon corridor to straight path |

---

### NavigatorLogMessage.cs
- **Path:** `Tripper\Navigation\NavigatorLogMessage.cs`
- **Lines:** 8
- **Delegate:** `NavigatorLogMessage(string msg)`
- **Namespace:** `Tripper.Navigation`
- **Functionality:** Callback delegate for navigator log messages.

---

## 6. Tripper\MeshMisc — Mesh Infrastructure

### AreaType.cs
- **Path:** `Tripper\MeshMisc\AreaType.cs`
- **Lines:** 40
- **Enum:** `AreaType` (byte)
- **Namespace:** `Tripper.MeshMisc`

| Value | Byte | Description |
|-------|------|-------------|
| `Ground` | 1 | Normal ground |
| `Water` | 2 | Swimmable water |
| `Lava` | 3 | Lava (avoid) |
| `Road` | 4 | Road (preferred) |
| `Fall` | 5 | Fall connection |
| `Elevator` | 6 | Elevator off-mesh connection |
| `Gate` | 7 | Battleground gate |
| `Portal` | 8 | Generic portal |
| `DefendersPortal` | 9 | Defenders portal (BG) |
| `HordePortal` | 10 | Horde-only portal |
| `AlliancePortal` | 11 | Alliance-only portal |
| `Blocked` | 12 | Blocked area |
| `InteractUnit` | 13 | NPC interaction point |
| `InteractObject` | 14 | Game object interaction point |
| `Horde` | 15 | Horde-only area |
| `Alliance` | 16 | Alliance-only area |
| `Blackspot` | 17 | Blackspot (avoid) |
| `KnownBuilding` | 18 | Known building (NEW) |
| `Misc1`–`Misc10` | 20–29 | Reserved miscellaneous |

---

### AbilityFlags.cs
- **Path:** `Tripper\MeshMisc\AbilityFlags.cs`
- **Lines:** 20
- **Enum:** `AbilityFlags` (ushort, Flags)
- **Namespace:** `Tripper.MeshMisc`

| Value | Bits | Description |
|-------|------|-------------|
| `None` | 0 | No abilities |
| `Run` | 1 | Can run (ground movement) |
| `OnlyWhileAlive` | 2 | Only usable while alive |
| `Swim` | 4 | Can swim |
| `Jump` | 8 | Can jump |
| `Unwalkable` | 16 | Not walkable |
| `Teleport` | 32 | Teleport connection |
| `Transport` | 64 | Transport (ship/zeppelin) |
| `Horde` | 4096 | Horde only |
| `Alliance` | 8192 | Alliance only |
| `KnownBuilding` | 16384 | Known building (NEW) |
| `All` | 65535 | All flags |

---

### GraphicalHelper.cs
- **Path:** `Tripper\MeshMisc\GraphicalHelper.cs`
- **Lines:** 170
- **Class:** `GraphicalHelper` (static)
- **Namespace:** `Tripper.MeshMisc`
- **Functionality:** Coordinate conversion between WoW and Detour (Recast) coordinate systems. Also provides bounds checking and triangle containment tests.

| Member | Kind | Description |
|--------|------|-------------|
| `ToWow(Vector3)` | static method | Detour → WoW: `wow.Y = -detour.X; wow.X = -detour.Z; wow.Z = detour.Y` |
| `ToDetour(Vector3)` | static method | WoW → Detour: `detour.X = -wow.Y; detour.Y = wow.Z; detour.Z = -wow.X` |
| `IsVectorContained(...)` | static method | AABB containment check (2D and 3D variants) |
| `IsVectorContained2D(...)` | static method | 2D containment ignoring Z |
| `TriangleContainsPoint(...)` | static method | Barycentric triangle containment |
| `NormalizeBounds(...)` | static method | Ensure min < max for bounding box |
| `Swap<T>(...)` | static method | Generic swap |

---

### MeshManager.cs
- **Path:** `Tripper\MeshMisc\MeshManager.cs`
- **Lines:** 137
- **Class:** `MeshManager` (static)
- **Namespace:** `Tripper.MeshMisc`
- **Functionality:** Tile data serialization — save and load navmesh tile binary data files.

| Member | Kind | Description |
|--------|------|-------------|
| `SaveMeshData(Stream, TileDataHeader, byte[,][])` | static method | Save tile data to binary stream |
| `ReadHeader(Stream)` | static method | Read tile data header |
| `LoadMeshData(Stream, out TileDataHeader)` | static method | Load full tile data from stream |
| `CurrentVersion` | const | `3` — tile data format version |

Supports version 2 (4×4 sub-tiles) and version 3 (variable sub-tiles).

---

### MeshMapCalculator.cs
- **Path:** `Tripper\MeshMisc\MeshMapCalculator.cs`
- **Lines:** 156
- **Class:** `MeshMapCalculator`
- **Namespace:** `Tripper.MeshMisc`
- **Functionality:** Converts between WoW ADT tile coordinates and Detour tile coordinates. Uses `TileSize = 533.3333f` (WoW's ADT size).

| Member | Kind | Description |
|--------|------|-------------|
| `SubTilesPerAdt` | property | Number of Detour sub-tiles per WoW ADT |
| `DetourTileSize` | property | `533.3333f / SubTilesPerAdt` |
| `GetDetourTile(TileIdentifier, int, int)` | method | WoW tile + sub-tile → Detour tile |
| `GetDetourTile(Vector3)` | method | WoW coordinates → Detour tile |
| `GetDetourTileFractional(Vector3)` | method | WoW coordinates → fractional Detour tile |
| `GetBounds(TileIdentifier, int, int)` | method | Get world bounds for a sub-tile |
| `GetWowTile(int, int)` | method | Detour tile → WoW tile identifier |
| `GetWowBounds(TileIdentifier)` | static method | Get WoW-space bounds for an ADT |
| `Default` | static property | Default calculator (4 sub-tiles per ADT) |

---

### TileIdentifier.cs
- **Path:** `Tripper\MeshMisc\TileIdentifier.cs`
- **Lines:** 97
- **Struct:** `TileIdentifier : IEquatable<TileIdentifier>`
- **Namespace:** `Tripper.MeshMisc`
- **Functionality:** WoW ADT tile coordinate pair (X, Y).

| Member | Kind | Description |
|--------|------|-------------|
| `X` | readonly field | Tile X coordinate |
| `Y` | readonly field | Tile Y coordinate |
| `GetByPosition(float, float)` | static method | World position → tile identifier |
| `GetHashCode()` | override | `Y * 64 + X` |
| Implicit conversions | operators | `TileIdentifier` ↔ `Vector2i` |

---

### TileDataHeader.cs
- **Path:** `Tripper\MeshMisc\TileDataHeader.cs`
- **Lines:** 64
- **Class:** `TileDataHeader`
- **Namespace:** `Tripper.MeshMisc`
- **Functionality:** Header for serialized tile data files.

| Member | Kind | Description |
|--------|------|-------------|
| `Width` | property | Sub-tile grid width |
| `Height` | property | Sub-tile grid height |
| `MapName` | property | WoW map name |
| `TileX` | property | ADT X coordinate |
| `TileY` | property | ADT Y coordinate |
| `UtcCreateTime` | property | Creation timestamp |

---

### MapConsts.cs
- **Path:** `Tripper\MeshMisc\MapConsts.cs`
- **Lines:** 11
- **Class:** `MapConsts` (static)
- **Namespace:** `Tripper.MeshMisc`

| Member | Kind | Description |
|--------|------|-------------|
| `TileSize` | const | `533.3333f` — WoW ADT tile size in world units |

---

### IoCGate.cs
- **Path:** `Tripper\MeshMisc\IoCGate.cs`
- **Lines:** 15
- **Enum:** `IoCGate` (byte)
- **Namespace:** `Tripper.MeshMisc`
- **Functionality:** Isle of Conquest battleground gate identifiers (maps to `AreaType.Misc1+`).

| Value | Byte | Description |
|-------|------|-------------|
| `HordeWest` | 20 | Horde western gate |
| `HordeSouth` | 21 | Horde southern gate |
| `HordeEast` | 22 | Horde eastern gate |
| `AllianceNorth` | 23 | Alliance northern gate |
| `AllianceWest` | 24 | Alliance western gate |
| `AllianceEast` | 25 | Alliance eastern gate |

---

### SotAGate.cs
- **Path:** `Tripper\MeshMisc\SotAGate.cs`
- **Lines:** 15
- **Enum:** `SotAGate` (byte)
- **Namespace:** `Tripper.MeshMisc`
- **Functionality:** Strand of the Ancients battleground gate identifiers.

| Value | Byte | Description |
|-------|------|-------------|
| `Green` | 20 | Green gate |
| `Blue` | 21 | Blue gate |
| `Red` | 22 | Red gate |
| `Purple` | 23 | Purple gate |
| `Yellow` | 24 | Yellow gate |

---

### InvalidTileDataException.cs
- **Path:** `Tripper\MeshMisc\InvalidTileDataException.cs`
- **Lines:** 14
- **Class:** `InvalidTileDataException : Exception`
- **Functionality:** Thrown when tile data stream has invalid magic header.

### TileDataVersionException.cs
- **Path:** `Tripper\MeshMisc\TileDataVersionException.cs`
- **Lines:** 11
- **Class:** `TileDataVersionException : ApplicationException`
- **Functionality:** Thrown when tile data version is unsupported.

---

## 7. Tripper\LZMACompression — Tile Compression

### Lzma.cs
- **Path:** `Tripper\LZMACompression\Lzma.cs`
- **Lines:** ~50 (estimated)
- **Class:** `Lzma`
- **Namespace:** `Tripper.LZMACompression`
- **Functionality:** LZMA compression/decompression for navmesh tile data files.

---

## 8. CommonBehaviors\Actions — Navigation Actions

### NavigationAction.cs
- **Path:** `CommonBehaviors\Actions\NavigationAction.cs`
- **Lines:** ~250 (estimated)
- **Class:** `NavigationAction`
- **Namespace:** `Styx.CommonBot.Actions` / `CommonBehaviors.Actions`
- **Functionality:** TreeSharp composite that navigates to a location using `NavTypeDelegate` to switch between ground (`Navigator.MoveTo()`) and flying (`Flightor.MoveTo()`).

| Member | Kind | Description |
|--------|------|-------------|
| `NavTypeDelegate` | property | Delegate returns `NavType.Run` or `NavType.Fly` |
| Uses `Navigator.GetRunStatusFromMoveResult()` | | Converts `MoveResult` to `RunStatus` |
| Switches between `Navigator.MoveTo()` and `Flightor.MoveTo()` based on NavType | | |

---

### NavigationInfo.cs
- **Path:** `CommonBehaviors\Actions\NavigationInfo.cs`
- **Lines:** 35
- **Class:** `NavigationInfo`
- **Namespace:** `Styx.CommonBot.Actions`
- **Functionality:** Data class holding navigation destination info.

| Member | Kind | Description |
|--------|------|-------------|
| `Destination` | property | `WoWPoint` — target point |
| `Height` | property | `float` — flight height |

---

### NavTypeDelegate.cs
- **Path:** `CommonBehaviors\Actions\NavTypeDelegate.cs`
- **Lines:** 10
- **Delegate:** `NavType NavTypeDelegate(object context)`
- **Namespace:** `Styx.CommonBot.Actions`
- **Functionality:** Delegate for runtime Run/Fly decision in NavigationAction.

---

## 9. Bots — Bot-Specific Navigation

### AvoidanceNavigationProvider.cs (DungeonBuddy)
- **Path:** `Bots\DungeonBuddy\AvoidanceNavigationProvider.cs`
- **Lines:** 117
- **Class:** `AvoidanceNavigationProvider : MeshNavigator`
- **Namespace:** `Bots.DungeonBuddy`
- **Functionality:** Extends MeshNavigator with avoidance overlay for dungeon boss mechanics. Checks avoidance zones and adjusts paths around them.

| Member | Kind | Description |
|--------|------|-------------|
| `MoveTo(WoWPoint)` | override | Checks avoidance before delegating to base |
| `method_29` | private | Avoidance path overlay logic |
| References `AvoidanceManager` | | Uses DungeonBuddy's avoidance system |

---

### DynamicBlackspot.cs + DynamicBlackspotManager.cs (DungeonBuddy)
- **Path:** `Bots\DungeonBuddy\DynamicBlackspot.cs` and `DynamicBlackspotManager.cs`
- **Functionality:** Dynamic runtime blackspots for dungeon avoidance. Unlike static profile blackspots, these are created/removed programmatically during boss encounters.

---

### ns17\Class164.cs (BGBuddy — obfuscated)
- **Path:** `ns17\Class164.cs`
- **Lines:** 186
- **Class:** `Class164 : MeshNavigator`
- **Namespace:** `ns17`
- **Functionality:** BGBuddy's navigation provider. Extends MeshNavigator with battleground-specific tile-loaded blackspot handling.

---

### ns18\Class165.cs (DungeonBuddy — obfuscated)
- **Path:** `ns18\Class165.cs`
- **Lines:** 384
- **Class:** `Class165 : AvoidanceNavigationProvider`
- **Namespace:** `ns18`
- **Functionality:** DungeonBuddy's actual navigation provider. Extends AvoidanceNavigationProvider, sets `PathPostProcessing.Randomize`, handles dungeon-specific path adjustments.

---

## 10. Styx\CommonBot — Flight Path (Taxi) System

### FlightPaths.cs
- **Path:** `Styx\CommonBot\FlightPaths.cs`
- **Lines:** 717
- **Class:** `FlightPaths` (static)
- **Namespace:** `Styx.CommonBot`
- **Functionality:** Flight path (taxi) system — automatic flight master discovery, route calculation, and usage. Uses `Navigator.CanNavigateFully()` to check reachability.

| Member | Kind | Description |
|--------|------|-------------|
| `IsIgnored(uint)` / `IsIgnored(WoWPoint)` | static method | Check if FP entry/location is ignored |
| `IgnoreFp(uint)` / `IgnoreFp(WoWPoint)` | static method | Add FP to ignore list |
| `NearestFlightMerchant` | static property | Closest reachable flight master |
| Multiple route-related methods | | Route planning, DBC integration |

---

### FlightPathReason.cs
- **Path:** `Styx\CommonBot\FlightPathReason.cs`
- **Lines:** 14
- **Enum:** `FlightPathReason`
- **Namespace:** `Styx.CommonBot`

| Value | Description |
|-------|-------------|
| `None` | No reason |
| `Learn` | Discover new flight point |
| `Use` | Take a flight |
| `Update` | Update flight path data |

---

## 11. Obfuscated Files (ns\*) — Navigation References

These files are in obfuscated namespaces but contain significant navigation logic:

| File | Lines | Description |
|------|-------|-------------|
| `ns17\Class164.cs` | 186 | `Class164 : MeshNavigator` — BGBuddy's nav provider |
| `ns18\Class165.cs` | 384 | `Class165 : AvoidanceNavigationProvider` — DungeonBuddy's nav provider |
| `ns18\Class339.cs` | ~200 | Uses `Navigator.FindHeights()` for positioning |
| `ns42\Class565.cs` | 248 | Garrison navigation helpers, uses `Navigator.FindHeight()` |
| `ns76\Class1080.cs` | 146 | Abstract Flightor annotation base class (IndoorEntrance[] parsing) |
| `ns76\Class1081.cs` | 97 | Bounding box annotation (Min/Max parsed from XML) |
| `ns76\Class1082.cs` | 106 | Flightor annotation variant |
| `ns76\Class1083.cs` | 183 | Flightor annotation variant |
| `ns76\Class1084.cs` | 155 | Flightor annotation variant |
| `ns76\Class1085.cs` | 120 | Flightor annotation variant |
| `ns76\Class1086.cs` | 83 | Flightor annotation variant |
| `ns76\Interface15.cs` | 13 | Interface for annotation bounding box access |
| `ns76\Struct429-431.cs` | ~200 | Supporting math structs for annotations |
| `ns82\Class1109.cs` | 158 | Initialization: calls `Navigator.smethod_0()` and `Flightor.smethod_0()` |
| `ns87\Class1196.cs` | ~300 | XML parsing helpers (used for navigation profile elements) |
| `ns96\Class1397.cs` | ~50 | Uses `Navigator.FindHeights()` |

---

## 12. Missing Class Definitions (Obfuscated)

The following types are **used extensively** throughout the codebase but their **class/enum definitions** could not be found in any named `.cs` file. They are embedded in obfuscated files (possibly with Unicode characters in filenames, or inlined by the decompiler):

### Navigator (static class)
- **Namespace:** `Styx.Pathing`
- **Used in:** 50+ files
- **Mapped API surface (via usage analysis):**

| Member | Kind | Description |
|--------|------|-------------|
| `NavigationProvider` | static property | Get/set active `NavigationProvider` |
| `PlayerMover` | static property | Get/set active `IPlayerMover` |
| `MoveTo(WoWPoint)` | static method | Delegate to `NavigationProvider.MoveTo()` |
| `Clear()` | static method | Clear navigation state |
| `FindHeights(float, float)` | static method | Get navmesh heights at (X, Y), returns `List<float>` |
| `FindHeight(float, float, out float)` | static method | Get single height at (X, Y) |
| `CanNavigateFully(WoWPoint, WoWPoint)` | static method | Full navigability check |
| `CanNavigateWithin(WoWPoint, WoWPoint, float)` | static method | Quick navigability check |
| `PathPrecision` | static property | Distance for waypoint arrival |
| `GeneratePath(WoWPoint, WoWPoint)` | static method | Generate path |
| `PathDistance(WoWPoint, WoWPoint)` | static method | Calculate path distance |
| `AtLocation(WoWPoint, WoWPoint)` | static method | Check if at location |
| `GetRunStatusFromMoveResult(MoveResult)` | static method | Convert `MoveResult` → `RunStatus` |
| `UpdateMaps()` | static method | Reload navmesh maps |
| `CurrentMovePath` | static property | Active `MeshMovePath` |
| `Nav` | static property | `WowNavigator` instance |
| `smethod_0()` | static method | Initialization (called during startup) |
| `OnNavigationProviderChanged` | static event | Fires when provider changes |

---

### Flightor (static class)
- **Namespace:** `Styx.Pathing`
- **Used in:** 15+ files
- **Mapped API (via usage analysis):**

| Member | Kind | Description |
|--------|------|-------------|
| `MoveTo(WoWPoint)` | static method | Aerial navigation to point |
| `smethod_0()` | static method | Initialization (called during startup) |
| Uses `BlackspotManager`, `PolyNav`, `IndoorEntrance` | | |

---

### MoveResult (enum)
- **Namespace:** `Styx.Pathing` (likely)
- **Used in:** 30+ files as return type of `MoveTo()` methods
- **Known values (via usage):** `Moved`, `Failed`, `PathGenerating`, `PathGenerated`, `ReachedDestination`

---

### MeshMovePath (class)
- **Used in:** `MeshNavigator.CurrentMovePath`, `GeneratePath()` return type
- **Known members (via usage):** `Path` (WoWPoint[]), `IsExitingGarrison`, indexer, path traversal methods

---

### ClickToMoveMover (class)
- **Implements:** `IPlayerMover`
- **Used by:** `ScriptHelpers.cs` in DungeonBuddy — `Navigator.PlayerMover = new ClickToMoveMover()`
- **Functionality:** Click-to-move based player movement (uses CTM API instead of keyboard)

---

## 13. DLL References

The Legion HB navigation system references these assemblies:

| Reference | Namespace | Usage |
|-----------|-----------|-------|
| **Tripper.RecastManaged** | `Tripper.RecastManaged.Detour` | `NavMesh`, `NavMeshQuery`, `QueryFilter`, `PolygonReference`, `StraightPathFlags`, `Status` |
| **Tripper.Tools** | `Tripper.Tools.Math` | `Vector2`, `Vector3`, `Vector2i`, `BoundingBox2` |
| **Tripper.Tools** | `Tripper.Tools` | `MagicHelper` (file magic numbers) |

These are the **Tripper.RecastManaged equivalent** — the Detour navmesh library is wrapped by Tripper's managed C# layer and accessed through `WowNavigator` → `WorldMeshManager.MeshQuery` / `GarrisonMeshManager.MeshQuery`.

---

## 14. NEW Features vs WoD 6.2.3

| Feature | Status | Details |
|---------|--------|---------|
| **GarrisonMeshManager** | NEW | Separate navmesh for WoD garrisons with boundary polygon detection and per-faction partial-path fallback locations |
| **MeshMovePath.IsExitingGarrison** | NEW | Flag for paths that exit garrison bounds |
| **KnownBuilding AreaType (#18)** | NEW | New area type for buildings known to navigation |
| **KnownBuilding AbilityFlag** | NEW | Corresponding ability flag (16384) |
| **PathPostProcessing.Randomize** | NEW | Path randomization (used by DungeonBuddy) |
| **AvoidanceNavigationProvider** | NEW | DungeonBuddy navigation with boss mechanic avoidance |
| **DynamicBlackspotManager** | NEW | Runtime blackspots for dungeon encounters |
| **NavigationInfo class** | NEW | Data class for destination + flight height |
| **NavTypeDelegate** | NEW | Runtime Run/Fly switching delegate |
| **NavType enum** | NEW | `Run` / `Fly` enum |
| **OnReplacementsLoaded event** | NEW | Mesh replacement data callback |
| **Faction query filters** | EXPANDED | "Horde_DeathKnightStart", "Alliance_DeathKnightStart" filters |
| **TileData v3** | UPDATED | Variable sub-tile dimensions (vs fixed 4×4 in v2) |

---

## 15. Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    Bot Layer (Consumers)                      │
│  LevelBot, QuestBot, BGBuddy, DungeonBuddy, GarrisonBuddy  │
└──────────────┬──────────────────────┬───────────────────────┘
               │                      │
               ▼                      ▼
┌──────────────────────┐ ┌──────────────────────────┐
│  Navigator (static)  │ │   Flightor (static)      │
│  .MoveTo()           │ │   .MoveTo()              │
│  .FindHeights()      │ │   .smethod_0() (init)    │
│  .CanNavigateFully() │ │                          │
│  .PlayerMover        │ │   ┌──────────────────┐   │
│  .NavigationProvider │ │   │ BlackspotManager │   │
└──────┬───────────────┘ │   │ PolyNav          │   │
       │                 │   │ IndoorEntrance    │   │
       ▼                 └───┴──────────────────┘   │
┌──────────────────────────────┐                     │
│  NavigationProvider (abstract)│                     │
│  ├─ MeshNavigator            │                     │
│  │  ├─ AvoidNavProv (Dungeon)│                     │
│  │  │  └─ Class165           │                     │
│  │  └─ Class164 (BG)         │                     │
│  └─ (custom providers)       │                     │
└──────┬───────────────────────┘                     │
       │                                             │
       ▼                                             │
┌──────────────────────────────────────────────────┐ │
│  WowNavigator (Tripper.Navigation)               │ │
│  ├── WorldMeshManager ──► NavMesh + NavMeshQuery  │ │
│  ├── GarrisonMeshManager ► NavMesh + NavMeshQuery │ │
│  ├── WowQueryFilter (AreaType costs, AbilityFlags)│ │
│  └── MeshMapCalculator (ADT ↔ Detour coords)     │ │
└──────┬───────────────────────────────────────────┘ │
       │                                             │
       ▼                                             │
┌──────────────────────────────────────────────────┐ │
│  Tripper.RecastManaged.Detour                    │ │
│  (NavMesh, NavMeshQuery, QueryFilter,            │ │
│   PolygonReference, StraightPathFlags, Status)   │ │
└──────────────────────────────────────────────────┘ │
                                                     │
┌──────────────────────┐  ┌──────────────────────────┘
│  IPlayerMover        │  │
│  ├─ KeyboardMover    │  │
│  └─ ClickToMoveMover │  │
└──────────────────────┘  │
                          │
┌──────────────────────┐  │
│  StuckHandler        │  │
│  (abstract)          │  │
└──────────────────────┘  │

┌──────────────────────────────────────────────────┐
│  Support Infrastructure                          │
│  ├── GraphicalHelper (WoW ↔ Detour coords)       │
│  ├── NavHelper (higher-level coord conversion)    │
│  ├── MeshManager (tile data serialization)        │
│  ├── TileIdentifier (ADT coordinates)             │
│  ├── TileDataHeader (tile file header)            │
│  ├── MapConsts (TileSize = 533.3333)             │
│  ├── Lzma (tile compression)                      │
│  ├── PathFindResult (pathfind output)             │
│  ├── PathFindStep (pathfind progress enum)        │
│  ├── PathPostProcessing (None/EdgeAvoid/Random)   │
│  ├── AreaType (29 polygon area types)             │
│  ├── AbilityFlags (11 movement ability flags)     │
│  ├── IoCGate / SotAGate (BG gate enums)          │
│  └── FlightPaths (taxi route system)              │
└──────────────────────────────────────────────────┘
```

---

## Summary Statistics

| Category | Files | Total Lines |
|----------|-------|-------------|
| Styx\Pathing (core) | 6 | ~1,629 |
| Styx\Pathing\FlightorNavigation | 2 | ~745 |
| Styx\Pathing\FlightorAnnotation | 1 | 67 |
| Tripper\Navigation | 8 | ~1,581 |
| Tripper\MeshMisc | 12 | ~605 |
| Tripper\LZMACompression | 1 | ~50 |
| CommonBehaviors\Actions (nav-related) | 3 | ~295 |
| Bots (nav-specific) | 4 | ~650 |
| Styx\CommonBot (flight paths) | 2 | ~731 |
| Obfuscated ns\* (nav-related) | 16+ | ~2,500+ |
| **TOTAL** | **55+** | **~8,850+** |
