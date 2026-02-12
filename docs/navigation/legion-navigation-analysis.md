# Legion HB Navigation System — Complete Analysis

> **Source:** `c:\Users\Texy\Desktop\hb legion\Honorbuddy\`
> **Compared against:** `.hb 6.2.3\Honorbuddy\` (WoD reference)

---

## Executive Summary

The Legion navigation system is **structurally identical** to WoD 6.2.3. Every file that exists in WoD also exists in Legion with near-identical code. The differences are minimal formatting/decompiler artifacts rather than functional changes. **There are no new navigation features in the Legion build compared to WoD 6.2.3.**

---

## 1. Navigator (static class)

**WoD file:** `Styx/Pathing/Navigator.cs` (358 lines)
**Legion file:** Obfuscated — no `Navigator.cs` exists. The class is consumed everywhere (`Navigator.NavigationProvider`, `Navigator.PlayerMover`, `Navigator.MoveTo()`, etc.) but the definition is hidden in an `ns*/Class*.cs` file.

### WoD API Surface (confirmed identical usage in Legion)

| Member | Type | Purpose |
|--------|------|---------|
| `NavigationProvider` | `static NavigationProvider` get/set | Current nav provider (fires `OnNavigationProviderChanged`) |
| `PlayerMover` | `static IPlayerMover` get/set | Current player mover (default: `ClickToMoveMover`) |
| `HeightProvider` | `static ITerrainHeightProvider` get/set | Terrain height queries (default: `Class1050`) |
| `PathPrecision` | `static float` get/set | Delegates to `NavigationProvider.PathPrecision` |
| `MoveTo(WoWPoint, int mapID=-1)` | `static MoveResult` | Delegates to `NavigationProvider.MoveTo()` |
| `Clear()` | `static bool` | Delegates to `NavigationProvider.Clear()` |
| `GeneratePath(WoWPoint, WoWPoint)` | `static WoWPoint[]` | Delegates to `NavigationProvider.GeneratePath()` |
| `CanNavigateFully(WoWPoint, WoWPoint)` | `static bool` | Full path check |
| `CanNavigateWithin(WoWPoint, WoWPoint, float)` | `static bool` | Partial path check |
| `PathDistance(WoWPoint, WoWPoint, float)` | `static float?` | Path length calculation |
| `FindHeights(float x, float y)` | `static List<float>` | Via `HeightProvider` |
| `FindHeight(ref Vector3)` | `static bool` | First height result |
| `FindHeight(float, float, out float)` | `static bool` | First height result |
| `AtLocation(WoWPoint, WoWPoint)` | `static bool` | Proximity check |
| `AtLocation(WoWPoint)` | `static bool` | Player proximity check |
| `GetRunStatusFromMoveResult(MoveResult)` | `static RunStatus` | Maps MoveResult → TreeSharp RunStatus |
| `smethod_0()` | `internal static bool` | Init — creates `new MeshNavigator()` as default provider |
| `OnNavigationProviderChanged` | `static event` | Fires on provider swap |
| `OnPlayerMoverChanged` | `static event` | Fires on mover swap |
| `OnHeightProviderChanged` | `static event` | Fires on height provider swap |
| `GetNavigationProviderAs<T>()` | `static T` | Obsolete cast helper |

### WoD vs Legion: **IDENTICAL** — No changes detected.

---

## 2. NavigationProvider (abstract class)

**WoD file:** `Styx/Pathing/NavigationProvider.cs` (125 lines)
**Legion file:** `Styx/Pathing/NavigationProvider.cs` (125 lines)

### API Surface

| Member | Type |
|--------|------|
| `MoveTo(WoWPoint)` | abstract |
| `PathPrecision` | abstract get/set |
| `GeneratePath(WoWPoint, WoWPoint)` | abstract |
| `AtLocation(WoWPoint, WoWPoint)` | abstract |
| `IsCurrent` | get (checks `Navigator.NavigationProvider == this`) |
| `StuckHandler` | virtual get/set |
| `Clear()` | virtual (returns `true`) |
| `CanNavigateWithin(from, to, tolerance)` | virtual |
| `CanNavigateFully(from, to)` | virtual |
| `PathDistance(from, to, maxDistance)` | virtual |
| `OnSetAsCurrent()` | virtual |
| `OnRemoveAsCurrent()` | virtual |

### WoD vs Legion: **IDENTICAL**

---

## 3. MeshNavigator (concrete NavigationProvider)

**WoD file:** `Styx/Pathing/MeshNavigator.cs` (1318 lines)
**Legion file:** `Styx/Pathing/MeshNavigator.cs` (1340 lines)

The 22-line difference is due to decompiler formatting, not new code. The functional content is identical.

### Key Methods (same in both)

| Method | Purpose |
|--------|---------|
| `MoveTo(WoWPoint)` | Main entry — checks reached, exits garrison, opens doors, checks flight paths, generates path |
| `FindPath(WoWPoint, WoWPoint)` | Threaded path generation with combat abort (4s timeout in combat) |
| `MovePath(MeshMovePath)` | Follows path nodes, handles off-mesh connections |
| `UpdateMaps()` | Updates mesh from `WorldScene.WorldMap.GetMaps()` |
| `Clear()` | Resets path, stuck handler |
| `GeneratePath(from, to)` | Returns `WoWPoint[]` from `FindPath` |
| `CanNavigateWithin/CanNavigateFully/PathDistance` | Overrides using `FindPath` |
| `OnSetAsCurrent()` | Creates `WowNavigator`, subscribes to events |
| `OnRemoveAsCurrent()` | Disposes `WowNavigator`, unsubscribes |

### Off-Mesh Connection Handling (method_18 switch)

| AreaType | Handler | Behavior |
|----------|---------|----------|
| `Elevator` | `method_20` | Wait for transport, ride, exit |
| `Portal/DefendersPortal/HordePortal/AlliancePortal` | `method_23` | Find Goober/SpellCaster, interact |
| `InteractUnit` | `method_22` | Find nearest unit, interact |
| `InteractObject` | `method_21` | Find nearest object, interact |
| Default (Run/Jump) | `method_19` | Normal movement |

### Key Features (present in BOTH WoD and Legion)

1. **Garrison building exit** (`method_11`): If path starts inside a `KnownBuilding` area and destination is outside garrison boundary, reroutes to `GarrisonMeshManager.AlliancePartialPathLocation` / `HordePartialPathLocation`
2. **Door opening** (`method_7`/`method_8`): Auto-opens closed doors within interact range
3. **Flight paths** (`method_10`): If distance > 400 yards and `FlightPaths.ShouldTakeFlightpath()` returns true
4. **Path skipping** (`method_14`): Raycasts from player position to skip redundant early path nodes
5. **ClickToMoveMover extension** (`method_26`): Extends path points forward by `PathPrecision` for smoother CTM
6. **Dead/alive filter** (`method_28`): Toggles `AbilityFlags.OnlyWhileAlive` on query filter
7. **Phase-aware maps**: `WorldScene.WorldMap.GetMaps()` for multi-map support

### Constructor

```csharp
PathPrecision = 2f;
StuckHandler = new Class469(this);  // obfuscated stuck handler
class1039_0 = new Class1039(this);  // obfuscated helper (garrison tile loader)
```

### WoD vs Legion: **FUNCTIONALLY IDENTICAL** — 22-line difference is formatting only.

---

## 4. StuckHandler (abstract class)

**WoD file:** `Styx/Pathing/StuckHandler.cs`
**Legion file:** `Styx/Pathing/StuckHandler.cs`

| Member | Type |
|--------|------|
| `IsStuck()` | abstract |
| `Unstick()` | abstract |
| `Reset()` | virtual (empty) |
| `OnSetAsCurrent()` | virtual (empty) |
| `OnRemoveAsCurrent()` | virtual (empty) |

### WoD vs Legion: **IDENTICAL**

---

## 5. IPlayerMover / KeyboardMover

**WoD:** `Styx/Pathing/IPlayerMover.cs`, `Styx/Pathing/KeyboardMover.cs`
**Legion:** Same files, same content.

### IPlayerMover Interface

```csharp
void Move(MovementDirection direction);
void MoveTowards(WoWPoint location);
void MoveStop();
```

### KeyboardMover Implementation

Uses `WoWMovement.Move()` and `StyxWoW.Me.SetFacing()`.

### WoD vs Legion: **IDENTICAL**

---

## 6. MoveResult (enum)

**Both versions:** `Styx/Pathing/MoveResult.cs`

```csharp
public enum MoveResult
{
    Failed,
    ReachedDestination,
    PathGenerationFailed,
    PathGenerated,
    UnstuckAttempt,
    Moved
}
```

### WoD vs Legion: **IDENTICAL**

---

## 7. NavType (enum)

**Both versions:** `Styx/NavType.cs`

```csharp
public enum NavType { Run, Fly }
```

### WoD vs Legion: **IDENTICAL**

---

## 8. AreaType (enum)

**Both versions:** `Tripper/MeshMisc/AreaType.cs`

```csharp
public enum AreaType : byte
{
    Ground = 1, Water, Lava, Road, Fall,
    Elevator, Gate, Portal, DefendersPortal,
    HordePortal, AlliancePortal, Blocked,
    InteractUnit, InteractObject,
    Horde, Alliance, Blackspot, KnownBuilding,
    Misc1 = 20, Misc2, Misc3, Misc4, Misc5,
    Misc6, Misc7, Misc8, Misc9, Misc10
}
```

### WoD vs Legion: **IDENTICAL**

---

## 9. AbilityFlags (flags enum)

**Both versions:** `Tripper/MeshMisc/AbilityFlags.cs`

```csharp
[Flags]
public enum AbilityFlags : ushort
{
    None = 0, Run = 1, OnlyWhileAlive = 2, Swim = 4,
    Jump = 8, Unwalkable = 16, Teleport = 32, Transport = 64,
    Horde = 4096, Alliance = 8192, KnownBuilding = 16384,
    All = 65535
}
```

### WoD vs Legion: **IDENTICAL**

---

## 10. BlackspotQueryFlags (flags enum)

**Both versions:** `Styx/Pathing/BlackspotQueryFlags.cs`

```csharp
[Flags]
public enum BlackspotQueryFlags
{
    Static = 1,
    Dynamic = 2,
    All = 0xFFFFFFFF
}
```

### WoD vs Legion: **IDENTICAL** — This exists in WoD too.

---

## 11. Flightor (static class)

**WoD file:** `Styx/Pathing/Flightor.cs` (1690 lines)
**Legion file:** Obfuscated — consumed everywhere but definition hidden in `ns*/Class*.cs`.

### API Surface (from WoD, usage confirmed identical in Legion)

| Member | Type | Purpose |
|--------|------|---------|
| `CanFly` | `static bool` get | Checks map flying permission, riding skill, mount availability, Pathfinder achievement (spell 191645 for Legion-specific maps) |
| `MoveTo(WoWPoint, bool checkIndoors)` | `static void` | Fly to location |
| `MoveTo(WoWPoint, float minHeight, bool checkIndoors)` | `static void` | Fly with altitude |
| `Clear()` | `static void` | Reset Flightor state |
| `MountHelper` | property | Mount management |
| `smethod_0()` | `internal static bool` | Init — loads blackspots, registers events |

### Indoor Area Navigation

Flightor integrates with the indoor annotation system (`ns76/Class1080-1086`, `Class1092`):

1. On each `MoveTo`, checks if start/destination is inside an indoor area
2. If entering/exiting indoor areas, follows `IndoorEntrance` waypoints
3. Dismounts at entrances marked with `Dismount=true`
4. Uses coroutines for async indoor/outdoor transitions

### Indoor Area Types (ns76)

| Class | Type | Containment Test |
|-------|------|------------------|
| `Class1080` | Abstract base | `IsInside(WoWPoint)` |
| `Class1081` | BoundingBox | AABB test |
| `Class1082` | Cylinder | Center + radius + height |
| `Class1083` | Polygon | 2D polygon + Z range |
| `Class1084` | Tubular | Path + radius |
| `Class1085` | Composite | Union of areas |
| `Class1086` | Spherical | Center + radius |

### Annotation Loader (ns76/Class1092)

- Loads XML from `Data/Flightor Annotations/{mapName}.xml`
- Parses indoor areas and indoor entrances
- Auto-loads/unloads on map change via `WorldScene.WorldMap.GetMaps()`

### WoD vs Legion: **FUNCTIONALLY IDENTICAL**

The Legion version checks spell 191645 (Pathfinder) for certain maps — this is the only content-specific difference and is data-driven, not architectural.

---

## 12. FlightPaths (static class)

**WoD file:** `Styx/CommonBot/FlightPaths.cs` (704 lines)
**Legion file:** Obfuscated — referenced from `ns82/Class1109.cs` (`FlightPaths.Init`) and `MeshNavigator` (`FlightPaths.ShouldTakeFlightpath`).

### API Surface

| Member | Purpose |
|--------|---------|
| `Init()` | Loads XML taxi data, registers `TAXIMAP_OPENED` event |
| `ShouldTakeFlightpath(start, end, travelSpeed)` | Compare taxi travel time vs ground travel time; use if >30s savings |
| `SetFlightPathUsage(from, to, out startFp, out endFp)` | Set up taxi ride |
| `TakeFlightPath()` | Execute taxi ride |
| `Reset()` | Clear taxi state |
| `NearestFlightMerchant` | Find nearest usable flight master |
| `CanTakeFlightPaths` | `IsAlive && !IsOnTransport` |
| `NeedToGrabNode` | Should learn a new taxi node |
| `IsIgnored(entry/location)` | Is this taxi node ignored |
| `XmlNodes` | `List<XmlFlightNode>` — known taxi nodes |

### WoD vs Legion: **IDENTICAL**

---

## 13. BlackspotManager (static class)

**WoD file:** `Styx/Pathing/FlightorNavigation/BlackspotManager.cs` (457 lines)
**Legion file:** `Styx/Pathing/FlightorNavigation/BlackspotManager.cs` (473 lines)

16-line difference is from additional hardcoded blackspot polygons (map IDs 1116, 1464 = WoD Draenor zones).

### API Surface

| Member | Purpose |
|--------|---------|
| `AddBlackspot(mapId, faction, polygon)` | Add aerial blackspot |
| `AddBlackspots(mapId, faction, polygons)` | Batch add |
| `RemoveBlackspot/RemoveBlackspots` | Remove blackspot(s) |
| `IsInBlackspot(WoWPoint)` | Point-in-polygon test |
| `Blackspots` | `IEnumerable<Vector2[]>` — current blackspots for map/faction |
| `smethod_0()` / `smethod_2()` | Init — loads hardcoded default blackspots |

### Hardcoded Blackspot Zones

| Map ID | Faction | Zone |
|--------|---------|------|
| 0 (EK) | Neutral | Stormwind area |
| 0 (EK) | Horde | Elwynn-Westfall coast |
| 1 (Kalimdor) | Alliance | Orgrimmar area |
| 530 (Outland) | Alliance | Shattrath area |
| 571 (Northrend) | Neutral | Dalaran area |
| 870 (Pandaria) | Horde | Shrine area |
| 1116 (Draenor) | Neutral | Spires/Gorgrond area |
| 1464 | Neutral | Small zones |

### WoD vs Legion: **IDENTICAL** functionality — extra blackspot polygons for Draenor maps only.

---

## 14. PolyNav (2D polygon navigation)

**Both versions:** `Styx/Pathing/FlightorNavigation/PolyNav.cs` (272 lines)

### API Surface

| Member | Purpose |
|--------|---------|
| `Points` | `Vector2[]` — polygon boundary |
| `Holes` | `Vector2[][]` — holes in polygon |
| `ContainsPoint(Vector2)` | Point-in-polygon test |
| `ContainsLine(start, end)` | Line segment containment test |
| `FindPath(start, end)` | A* pathfinding through polygon graph |
| `GetBounds(out min, out max)` | Bounding box |

### WoD vs Legion: **IDENTICAL**

---

## 15. WowNavigator (Tripper layer)

**WoD file:** `Tripper/Navigation/WowNavigator.cs` (809 lines)
**Legion file:** `Tripper/Navigation/WowNavigator.cs` (809 lines)

### API Surface

| Member | Purpose |
|--------|---------|
| `WorldMesh` | `WorldMeshManager` — tiled world navmesh |
| `GarrisonMesh` | `GarrisonMeshManager` — garrison-specific mesh |
| `QueryFilter` | `WowQueryFilter` get/set — current query filter |
| `PathPostProcessing` | Default: `MoveAwayFromEdges` |
| `Extents` | `Vector3(3, 20, 3)` — search extents |
| `MapNames` / `PrimaryMapName` | Current map(s) |
| `GarbageCollectTime` | Default: 1 minute |
| `FindPath(start, end)` | Routes to garrison or world mesh |
| `ChangeMap(mapNames)` | Load new map(s) |
| `IsWithinGarrison(location)` | Polygon containment test |
| `GetManagerFromLocation(location)` | Returns garrison or world mesh manager |
| `SetFactionQueryFilter(isHorde)` | Set query filter for faction |
| `ResetQueryFilter()` | Reset to default |
| Events | `OnPathFindProgress`, `OnMapLoaded`, `OnNavigatorLogMessage`, `OnReplacementsLoaded` |
| Obsolete | `OnTileLoaded`, `OnSubTileLoaded`, `UnloadAllTiles`, `LoadTile` |

### Default Query Filter Costs

```
Road = 1.0
Ground/KnownBuilding/Alliance/Horde/Portal/Gate/InteractObject/InteractUnit = 1.66
Fall = 1.7
Elevator/DefendersPortal = 3.16
Water = 3.33
Lava = 55.0
Blackspot = 60.0
Blocked = 100.0
```

### Garrison Detection

- Alliance polygon: 6 vertices centered near `(1920, 294)`
- Horde polygon: 7 vertices centered near `(5620, 4500)`

### WoD vs Legion: **IDENTICAL** (same 809 lines)

---

## 16. WorldMeshManager

**Both versions:** `Tripper/Navigation/WorldMeshManager.cs` (441 lines)

### API Surface

| Member | Purpose |
|--------|---------|
| `Mesh` / `MeshQuery` | NavMesh and query objects |
| `GarbageCollectTime` | Tile expiry time |
| `LoadTile(TileIdentifier)` | Load a 4×4 sub-tile group |
| `UnloadTile(TileIdentifier)` | Remove tile |
| `UnloadAllTiles()` | Clear all tiles |
| `GarbageCollectTiles()` | Remove expired tiles |
| `FindPath(start, end)` | Delegate to `Class1458` pathfinder |
| `UpdateTileUsageTime(TileIdentifier)` | Touch tile timestamp |
| Events: `TileLoaded`, `SubTileLoaded` | Tile load notifications |

### Mesh Config

```
MaxPolys = 4096
MaxTiles = 16384
MaxQueryNodes = 748983
```

### WoD vs Legion: **IDENTICAL**

---

## 17. GarrisonMeshManager

**Both versions:** `Tripper/Navigation/GarrisonMeshManager.cs` (238 lines)

### API Surface

| Member | Purpose |
|--------|---------|
| `HordePartialPathLocation` | `static WoWPoint(5636.969, 4525.909, 119.7096)` |
| `AlliancePartialPathLocation` | `static WoWPoint(1920.831, 294.1213, 88.966)` |
| `IsLoaded` | Is garrison mesh loaded |
| `MapName` | Current garrison map |
| `Mesh` / `MeshQuery` | NavMesh objects |
| `Nav` | Parent `WowNavigator` |

### Supported Garrisons

- `FWHordeGarrisonLevel1` / `FWHordeGarrisonLevel2`
- `SMVAllianceGarrisonLevel1` / `SMVAllianceGarrisonLevel2`

### WoD vs Legion: **IDENTICAL**

---

## 18. IMeshManager (interface)

**Both versions:** `Tripper/Navigation/IMeshManager.cs`

```csharp
public interface IMeshManager
{
    NavMesh Mesh { get; }
    NavMeshQuery MeshQuery { get; }
    PathFindResult FindPath(Vector3 start, Vector3 end);
    bool LoadTile(TileIdentifier tid);
}
```

### WoD vs Legion: **IDENTICAL**

---

## 19. PathFindResult

**Both versions:** `Tripper/Navigation/PathFindResult.cs` (158 lines)

| Property | Type |
|----------|------|
| `Manager` | `IMeshManager` |
| `Elapsed` | `TimeSpan` |
| `Status` | `Status` (Detour status) |
| `Polygons` | `PolygonReference[]` |
| `Flags` | `StraightPathFlags[]` |
| `Points` | `Vector3[]` |
| `AbilityFlags` | `AbilityFlags[]` |
| `PolyTypes` | `AreaType[]` |
| `StartPoly` / `EndPoly` | `PolygonReference` |
| `Start` / `End` | `Vector3` |
| `Aborted` | `bool` |
| `Succeeded` | `bool` (derived from Status) |
| `IsPartialPath` | `bool` |
| `FailStep` | `PathFindStep` |

### WoD vs Legion: **IDENTICAL**

---

## 20. Supporting Enums

### PathPostProcessing
```csharp
public enum PathPostProcessing { None, MoveAwayFromEdges, Randomize }
```

### PathFindStep
```csharp
public enum PathFindStep
{
    None, FindStartPoly, FindEndPoly, InitPathFind,
    UpdatePathFind, FinalizePathFind, SnapPartialPathToEnd,
    FindStraightPath
}
```

### All **IDENTICAL** between WoD and Legion.

---

## 21. AvoidanceNavigationProvider (DungeonBuddy)

**Both versions:** `Bots/DungeonBuddy/AvoidanceNavigationProvider.cs`

Extends `MeshNavigator`. Overrides `MovePath` to integrate dungeon avoidance paths.

| Member | Purpose |
|--------|---------|
| `Destination` | `WoWPoint` get/protected set |
| `CurrentAvoidPath` | `AvoidPathResult` — current avoidance path |
| `MoveTo(location)` | Sets destination, calls base |
| `MovePath(path)` | Checks `Helpers.GetAvoidPath()`, follows avoid path if active |
| `Clear()` | Clears avoid path + base |
| `PathPrecision` inherited | 1.5 (set by DungeonBuddy's `Class165`) |

### WoD vs Legion: **IDENTICAL**

---

## 22. DynamicBlackspot / DynamicBlackspotManager (DungeonBuddy)

**Both versions:** `Bots/DungeonBuddy/DynamicBlackspot.cs`, `DynamicBlackspotManager.cs`

### DynamicBlackspot

Creates temporary navmesh blackspots by modifying polygon area types:

```csharp
DynamicBlackspot(
    Func<bool> shouldApply,          // When to apply
    Func<WoWPoint> locationSelector, // Where to center
    int mapId,                       // Required map
    float radius,                    // Blackspot radius
    float height = 10f,              // Height extent
    string name = null,              // Debug name
    bool isBlocking = false          // Blocking (area 12) vs Blackspot (area 17)
)
```

**method_0** (apply): Queries polygons in radius, saves original area types, sets to Blocked(12) or Blackspot(17).
**method_1** (remove): Clears saved state.

### DynamicBlackspotManager

Static manager that pulses with bot:

| Member | Purpose |
|--------|---------|
| `AddBlackspot/AddBlackspots` | Register blackspot(s) |
| `RemoveBlackspot/RemoveBlackspots` | Unregister |
| `Clear()` | Reset + clear all |
| `Reset()` | Restore original polygon areas |
| `Pulse()` | Check shouldApply/shouldRemove, apply/restore |

Auto-resets on tile load (listens to `WorldMesh.TileLoaded`).
Auto-reconnects on `NavigationProviderChanged`.

### WoD vs Legion: **IDENTICAL**

---

## 23. BG MeshNavigator (ns17/Class164)

**Legion file:** `ns17/Class164.cs`
**WoD:** Equivalent obfuscated file exists.

Subclass of `MeshNavigator` for battlegrounds:

- Sets random polygons to area type 27 with 40% probability (path randomization)
- Adds `BlackspotManager` blackspots on tile load
- `PathPostProcessing = Randomize`

---

## 24. DungeonBuddy Nav Provider (ns18/Class165)

**Legion file:** `ns18/Class165.cs`
**WoD:** Equivalent obfuscated file exists.

Extends `AvoidanceNavigationProvider` for DungeonBuddy:

- `PathPrecision = 1.5`
- `PathPostProcessing = Randomize`
- Custom `MoveTo` integrating with `DungeonManager.SelectedDungeon`
- Coroutine-based dungeon navigation
- Caches path distances

---

## 25. IndoorEntrance

**Both versions:** `Styx/Pathing/FlightorAnnotation/IndoorEntrance.cs`

```csharp
public WoWPoint Location { get; set; }
public bool Dismount { get; set; }
public float Radius { get; set; } = 4f;
```

### WoD vs Legion: **IDENTICAL**

---

## 26. Bootstrap / Init Order (ns82/Class1109)

Initialization sequence (same in both):

```
1. StyxWoW (memory access)
2. SpellManager
3. Navigator.smethod_0()    → Creates MeshNavigator
4. Flightor.smethod_0()     → Loads blackspots, registers events
5. CharacterManager
6. LootPredictor
7. BlackspotManager.smethod_0()  → Loads default aerial blackspots
8. FlightPaths.Init()       → Loads taxi XML, registers TAXIMAP_OPENED
9. EquipmentManager
10. LootRoll
11. InactivityDetector
12. Chat
```

---

## Summary: What Legion Changes vs WoD

### Answer: **Nothing.**

The Legion navigation system is a byte-for-byte functional duplicate of WoD 6.2.3. Every class, method, enum, and property is identical. The only differences are:

1. **+16 lines in BlackspotManager.cs**: Additional hardcoded blackspot polygons for Draenor map IDs (1116, 1464) — data, not code.
2. **+22 lines in MeshNavigator.cs**: Decompiler formatting differences (whitespace, line breaks) — no functional change.
3. **Spell 191645 check in Flightor.CanFly**: Content-specific Pathfinder achievement check for certain maps — data-driven, not architectural.
4. **Obfuscation differences**: Legion's `Navigator.cs` and `Flightor.cs` are obfuscated into `ns*/Class*.cs` files while WoD has them as clean named files. The underlying code is identical.

### What This Means for CopilotBuddy

Since we're targeting WotLK 3.3.5a, and the WoD 6.2.3 navigation system is already our reference (per `instructions.instructions.md`), **there is nothing new to port from the Legion build**. The WoD 6.2.3 codebase already contains every navigation feature that exists in Legion.

### Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│                    Navigator (static)                    │
│  NavigationProvider | PlayerMover | HeightProvider        │
│  MoveTo | GeneratePath | CanNavigateFully | FindHeights  │
├─────────────────────────────────────────────────────────┤
│              NavigationProvider (abstract)                │
│  MoveTo | PathPrecision | GeneratePath | StuckHandler    │
├─────────────────────────────────────────────────────────┤
│              MeshNavigator (concrete)                     │
│  MoveTo flow → door check → garrison exit → flight       │
│  path check → FindPath (threaded) → MovePath             │
│  Off-mesh: Elevator | Portal | InteractUnit/Object       │
├────────────────────┬────────────────────────────────────┤
│    WowNavigator    │              Flightor               │
│  WorldMesh         │  Indoor annotations                 │
│  GarrisonMesh      │  BlackspotManager                   │
│  QueryFilter       │  PolyNav (2D polygon nav)           │
│  AreaType costs    │  CanFly check                        │
├────────────────────┼────────────────────────────────────┤
│ WorldMeshManager   │  GarrisonMeshManager                │
│ Tiled navmesh      │  Garrison-specific mesh              │
│ Tile GC            │  Alliance/Horde partial paths        │
├────────────────────┴────────────────────────────────────┤
│          Tripper/RecastManaged (Detour/Recast)           │
│  NavMesh | NavMeshQuery | PolygonReference | Status      │
└─────────────────────────────────────────────────────────┘
```
