# Navigator Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - Pathfinding using Tripper/Recast navigation meshes. Direct movement fallback when meshes unavailable.

The `Navigator` class provides pathfinding, movement, and navigation services using the Tripper navigation mesh system.

## Namespace

```csharp
using Styx.Logic.Pathing;
```

## Overview

`Navigator` is a static class that handles all pathfinding and movement operations. It uses the Tripper library (based on Recast navigation) to generate collision-free paths through the game world.

!!! tip "Primary Movement Interface"
    Always use `Navigator.MoveTo()` instead of direct movement commands for safe navigation.

---

## Properties

### Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `PathPrecision` | `float` | 1.6f | Distance in yards to consider "reached destination" |
| `LoadTilesAroundRadius` | `int` | 2 | Radius of navigation tiles to load around player |
| `FlyingMountHeight` | `float` | 25f | Height for flying mount navigation |

```csharp
// Adjust precision for tighter movement
Navigator.PathPrecision = 0.5f;
```

### State

| Property | Type | Description |
|----------|------|-------------|
| `Destination` | `WoWPoint` | Current navigation destination |
| `CurrentPath` | `List<WoWPoint>` | Current path waypoints |
| `AtLocation` | `bool` | True if at destination |
| `IsNavigatorLoaded` | `bool` | True if navigation meshes are loaded |

```csharp
if (Navigator.AtLocation)
{
    Console.WriteLine("Reached destination!");
}
```

### Player Mover

| Property | Type | Description |
|----------|------|-------------|
| `PlayerMover` | `IMover` | The mover implementation (default: `LocalPlayerMover`) |

---

## Basic Movement

### `MoveResult MoveTo(WoWPoint destination)`

Moves to a destination point.

```csharp
WoWPoint destination = new WoWPoint(1234.5f, 5678.9f, 100.0f);
MoveResult result = Navigator.MoveTo(destination);

switch (result)
{
    case MoveResult.ReachedDestination:
        Console.WriteLine("Arrived!");
        break;
    case MoveResult.Moved:
        Console.WriteLine("Moving...");
        break;
    case MoveResult.Failed:
        Console.WriteLine("Movement failed");
        break;
}
```

### `MoveResult MoveTo(WoWPoint destination, string destinationName)`

Moves to a destination with a descriptive name (for logging).

```csharp
WoWGameObject node = FindNearestMiningNode();
Navigator.MoveTo(node.Location, $"Mining Node: {node.Name}");
```

### `MoveResult MoveTo(WoWPoint destination, float precision)`

Moves to a destination with custom precision.

```csharp
// Get very close to quest giver (0.5 yards)
Navigator.MoveTo(questGiver.Location, precision: 0.5f);
```

---

## MoveResult Enum

```csharp
public enum MoveResult
{
    Failed,                  // Movement failed
    ReachedDestination,      // Arrived at destination
    Moved,                   // Currently moving
    PathGenerated,           // Path was successfully generated
    PathGenerationFailed,    // Could not generate path
    UnstuckAttempt          // Attempting to get unstuck
}
```

---

## Path Management

### `void Clear()`

Clears the current path and stops movement.

```csharp
// Cancel current movement
Navigator.Clear();
```

### `WoWPoint[]? GeneratePath(WoWPoint start, WoWPoint destination)`

Generates a path without moving.

```csharp
WoWPoint start = StyxWoW.Me.Location;
WoWPoint end = new WoWPoint(100, 200, 50);

WoWPoint[]? path = Navigator.GeneratePath(start, end);
if (path != null)
{
    Console.WriteLine($"Path has {path.Length} waypoints");
    foreach (var point in path)
    {
        Console.WriteLine($"  -> ({point.X:F1}, {point.Y:F1}, {point.Z:F1})");
    }
}
```

---

## Path Validation

### `bool CanNavigateFully(WoWPoint destination)`

Checks if a complete path can be generated to the destination.

```csharp
WoWGameObject chest = FindNearestChest();

if (Navigator.CanNavigateFully(chest.Location))
{
    Navigator.MoveTo(chest.Location, "Chest");
}
else
{
    Console.WriteLine("Cannot reach chest - path blocked");
}
```

### `bool CanNavigateFully(WoWPoint start, WoWPoint destination)`

Checks if a path exists between two points.

```csharp
if (Navigator.CanNavigateFully(StyxWoW.Me.Location, questLocation))
{
    Console.WriteLine("Quest location is reachable");
}
```

### `bool IsPathSafe(IList<WoWPoint> path)`

Checks if a path is safe (no collisions/falls).

```csharp
var path = Navigator.GeneratePath(start, end);
if (path != null && Navigator.IsPathSafe(path))
{
    Console.WriteLine("Path is safe");
}
```

---

## Height Finding

### `bool FindMeshHeight(float x, float y, out float z)`

Finds the mesh height (Z coordinate) at an XY position.

```csharp
float x = 1234.5f;
float y = 5678.9f;

if (Navigator.FindMeshHeight(x, y, out float z))
{
    Console.WriteLine($"Ground height at ({x}, {y}): {z}");
    var groundPos = new WoWPoint(x, y, z);
}
```

### `bool FindHeight(float x, float y, out float z)`

Alias for `FindMeshHeight`.

### `List<float> FindHeights(float x, float y)`

Finds all heights at an XY position (multi-level areas).

```csharp
List<float> heights = Navigator.FindHeights(x, y);
Console.WriteLine($"Found {heights.Count} levels:");
foreach (float z in heights)
{
    Console.WriteLine($"  Level at Z={z}");
}
```

---

## Navmesh Queries

### `WoWPoint FindNearestPoint(WoWPoint position)`

Finds the nearest navigable point on the mesh.

```csharp
// Player might be slightly off-mesh after teleport
WoWPoint currentPos = StyxWoW.Me.Location;
WoWPoint nearestNavPos = Navigator.FindNearestPoint(currentPos);

if (currentPos.Distance(nearestNavPos) > 5)
{
    Console.WriteLine("Player is off navmesh, moving to nearest point");
    Navigator.MoveTo(nearestNavPos);
}
```

### `WoWPoint FindRandomPoint(WoWPoint center, float radius)`

Finds a random navigable point within radius.

```csharp
// Find random point within 50 yards
WoWPoint randomPos = Navigator.FindRandomPoint(StyxWoW.Me.Location, radius: 50f);
Navigator.MoveTo(randomPos, "Random Wander");
```

---

## Raycasting

### `bool Raycast(WoWPoint start, WoWPoint end, out WoWPoint hitPosition)`

Performs a raycast along the navmesh.

```csharp
WoWPoint playerPos = StyxWoW.Me.Location;
WoWPoint targetPos = enemy.Location;

if (Navigator.Raycast(playerPos, targetPos, out WoWPoint hitPos))
{
    Console.WriteLine($"Line of sight blocked at {hitPos}");
    // Path is NOT clear
}
else
{
    Console.WriteLine("Line of sight is clear");
}
```

**Returns:** `true` if raycast hit an obstacle (path blocked)

---

## Tripper Navigator

### `Tripper.Navigation.Navigator TripperNavigator`

Gets the underlying Tripper navigator instance (advanced usage).

```csharp
var tripper = Navigator.TripperNavigator;
if (tripper.IsLoaded)
{
    Console.WriteLine("Navigation meshes loaded");
}
```

---

## Complete Examples

### Example 1: Move to Quest Objective

```csharp
using Styx;
using Styx.Logic.Pathing;

void MoveToQuestObjective(WoWPoint objective)
{
    var me = StyxWoW.Me;
    
    // Check if already there
    if (me.Location.Distance(objective) < 5f)
    {
        Console.WriteLine("Already at objective");
        return;
    }
    
    // Check if reachable
    if (!Navigator.CanNavigateFully(objective))
    {
        Console.WriteLine("Objective not reachable!");
        return;
    }
    
    // Move to objective
    while (me.Location.Distance(objective) > 5f)
    {
        MoveResult result = Navigator.MoveTo(objective, "Quest Objective");
        
        if (result == MoveResult.Failed || result == MoveResult.PathGenerationFailed)
        {
            Console.WriteLine("Movement failed");
            break;
        }
        
        // Wait a bit before next update
        System.Threading.Thread.Sleep(100);
    }
    
    Console.WriteLine("Reached objective!");
}
```

### Example 2: Mining Node Farming

```csharp
using System.Linq;

void FarmMiningNodes()
{
    var nearbyNodes = ObjectManager.GetObjectsOfType<WoWGameObject>()
        .Where(go => go.CanMine)
        .Where(go => go.Distance < 100)
        .OrderBy(go => go.Distance)
        .ToList();
    
    if (!nearbyNodes.Any())
    {
        Console.WriteLine("No mining nodes nearby");
        return;
    }
    
    foreach (var node in nearbyNodes)
    {
        Console.WriteLine($"Moving to {node.Name} ({node.Distance:F1}y)");
        
        // Check if path exists
        if (!Navigator.CanNavigateFully(node.Location))
        {
            Console.WriteLine($"  Cannot reach {node.Name}, skipping");
            continue;
        }
        
        // Move to node
        while (node.Distance > node.InteractRange && node.IsValid)
        {
            MoveResult result = Navigator.MoveTo(node.Location, node.Name);
            
            if (result == MoveResult.Failed)
            {
                Console.WriteLine($"  Failed to move to {node.Name}");
                break;
            }
            
            System.Threading.Thread.Sleep(100);
        }
        
        // Mine the node
        if (node.Distance <= node.InteractRange)
        {
            Console.WriteLine($"  Mining {node.Name}...");
            node.Interact();
            System.Threading.Thread.Sleep(5000); // Wait for mining
        }
    }
}
```

### Example 3: Safe Position Finding

```csharp
WoWPoint FindSafePosition(WoWPoint dangerZone, float minDistance = 20f)
{
    var me = StyxWoW.Me;
    
    // Try 8 directions
    for (int angle = 0; angle < 360; angle += 45)
    {
        float radians = angle * (float)Math.PI / 180f;
        
        float x = dangerZone.X + minDistance * (float)Math.Cos(radians);
        float y = dangerZone.Y + minDistance * (float)Math.Sin(radians);
        
        // Find ground height
        if (Navigator.FindMeshHeight(x, y, out float z))
        {
            var testPos = new WoWPoint(x, y, z);
            
            // Check if reachable
            if (Navigator.CanNavigateFully(me.Location, testPos))
            {
                Console.WriteLine($"Found safe position at {angle}°");
                return testPos;
            }
        }
    }
    
    Console.WriteLine("No safe position found, using random");
    return Navigator.FindRandomPoint(me.Location, minDistance);
}

// Usage
if (IsInDanger())
{
    WoWPoint safePos = FindSafePosition(dangerousArea, minDistance: 30f);
    Navigator.MoveTo(safePos, "Safe Position");
}
```

### Example 4: Patrol Route

```csharp
class PatrolRoute
{
    private List<WoWPoint> _waypoints = new();
    private int _currentWaypoint = 0;
    
    public PatrolRoute(params WoWPoint[] waypoints)
    {
        _waypoints.AddRange(waypoints);
    }
    
    public void Patrol()
    {
        if (_waypoints.Count == 0)
            return;
        
        WoWPoint destination = _waypoints[_currentWaypoint];
        
        // Move to current waypoint
        MoveResult result = Navigator.MoveTo(destination, $"Waypoint {_currentWaypoint + 1}");
        
        if (result == MoveResult.ReachedDestination)
        {
            Console.WriteLine($"Reached waypoint {_currentWaypoint + 1}");
            
            // Move to next waypoint
            _currentWaypoint = (_currentWaypoint + 1) % _waypoints.Count;
        }
    }
}

// Usage
var route = new PatrolRoute(
    new WoWPoint(100, 200, 50),
    new WoWPoint(150, 250, 52),
    new WoWPoint(200, 200, 51),
    new WoWPoint(150, 150, 50)
);

while (botRunning)
{
    route.Patrol();
    System.Threading.Thread.Sleep(100);
}
```

### Example 5: Unstuck Logic

```csharp
class UnstuckHelper
{
    private WoWPoint _lastPosition;
    private DateTime _lastMovement;
    private int _stuckCount = 0;
    
    public bool IsStuck()
    {
        var me = StyxWoW.Me;
        
        if (me.Location.Distance(_lastPosition) < 1f)
        {
            if ((DateTime.Now - _lastMovement).TotalSeconds > 5)
            {
                _stuckCount++;
                return true;
            }
        }
        else
        {
            _lastPosition = me.Location;
            _lastMovement = DateTime.Now;
            _stuckCount = 0;
        }
        
        return false;
    }
    
    public void Unstuck()
    {
        var me = StyxWoW.Me;
        Console.WriteLine($"Stuck detected (count: {_stuckCount})");
        
        // Try random nearby point
        WoWPoint randomPos = Navigator.FindRandomPoint(me.Location, radius: 10f);
        
        Console.WriteLine("Attempting to unstuck...");
        Navigator.Clear();
        
        // Try to reach random position
        for (int i = 0; i < 50; i++) // 5 seconds max
        {
            Navigator.MoveTo(randomPos, "Unstuck");
            System.Threading.Thread.Sleep(100);
            
            if (me.Location.Distance(_lastPosition) > 5f)
            {
                Console.WriteLine("Unstuck successful!");
                _stuckCount = 0;
                return;
            }
        }
        
        // If still stuck after 5 seconds, try jumping
        Console.WriteLine("Still stuck, trying jump...");
        WoWMovement.Jump();
        System.Threading.Thread.Sleep(500);
    }
}

// Usage
var unstuck = new UnstuckHelper();

while (botRunning)
{
    if (unstuck.IsStuck())
    {
        unstuck.Unstuck();
    }
    
    // Normal movement logic
    Navigator.MoveTo(destination);
    System.Threading.Thread.Sleep(100);
}
```

### Example 6: Path Visualization (Debug)

```csharp
void VisualizePath(WoWPoint destination)
{
    var path = Navigator.GeneratePath(StyxWoW.Me.Location, destination);
    
    if (path == null || path.Length == 0)
    {
        Console.WriteLine("No path found");
        return;
    }
    
    Console.WriteLine($"=== Path to {destination} ===");
    Console.WriteLine($"Total waypoints: {path.Length}");
    
    float totalDistance = 0f;
    for (int i = 0; i < path.Length; i++)
    {
        var point = path[i];
        Console.WriteLine($"Waypoint {i + 1}: ({point.X:F1}, {point.Y:F1}, {point.Z:F1})");
        
        if (i > 0)
        {
            float segmentDist = path[i - 1].Distance(point);
            totalDistance += segmentDist;
            Console.WriteLine($"  Distance from previous: {segmentDist:F1}y");
        }
    }
    
    Console.WriteLine($"Total path distance: {totalDistance:F1} yards");
    
    // Check path safety
    if (Navigator.IsPathSafe(path))
    {
        Console.WriteLine("✓ Path is safe");
    }
    else
    {
        Console.WriteLine("✗ Path may be unsafe");
    }
}
```

---

## Tripper Integration

### Navigation Mesh Loading

Navigation meshes are automatically loaded on bot start. The meshes cover most WoW zones and are stored in the `Tripper/Meshes/` directory.

```csharp
// Check if meshes are loaded
if (Navigator.IsNavigatorLoaded)
{
    Console.WriteLine("Navmesh ready");
}
else
{
    Console.WriteLine("No navmesh - using direct movement");
}
```

### Mesh Format

Tripper uses Recast navigation meshes (`.mesh` files) optimized for WoW's terrain. Each zone has its own mesh file.

---

## Best Practices

### 1. Always Check Path Validity

```csharp
// GOOD
if (Navigator.CanNavigateFully(destination))
{
    Navigator.MoveTo(destination);
}
else
{
    Console.WriteLine("Destination unreachable");
}

// BAD - may get stuck
Navigator.MoveTo(destination);
```

### 2. Use Descriptive Names

```csharp
// GOOD - easy to debug logs
Navigator.MoveTo(quest.Location, $"Quest: {quest.Name}");

// BAD - generic logs
Navigator.MoveTo(quest.Location);
```

### 3. Handle Movement Failures

```csharp
MoveResult result = Navigator.MoveTo(destination);

if (result == MoveResult.Failed || result == MoveResult.PathGenerationFailed)
{
    // Try alternative method or blacklist destination
    HandleMovementFailure(destination);
}
```

### 4. Adjust Precision for Context

```csharp
// Gathering - can be less precise
Navigator.MoveTo(herbNode.Location, precision: 2.5f);

// Quest interaction - need to be close
Navigator.MoveTo(questGiver.Location, precision: 0.5f);
```

### 5. Clear Path When Changing Goals

```csharp
// When combat starts, clear movement
if (enemyDetected)
{
    Navigator.Clear();
    // Handle combat
}
```

---

## Troubleshooting

### Navigation Meshes Not Loading

```csharp
// Check if Tripper meshes exist
if (!Navigator.IsNavigatorLoaded)
{
    Console.WriteLine("Navigation meshes not loaded");
    Console.WriteLine("Check Tripper/Meshes/ directory");
}
```

### Player Stuck in Terrain

```csharp
// Find nearest navigable point
WoWPoint nearestNav = Navigator.FindNearestPoint(StyxWoW.Me.Location);
if (StyxWoW.Me.Location.Distance(nearestNav) > 5)
{
    Navigator.MoveTo(nearestNav);
}
```

---

## See Also

- [WoWMovement](wowmovement.md) - Low-level movement control
- [WoWPoint](../wowobjects/wowpoint.md) - Position representation
- [CustomClass](customclass.md) - Using Navigator in combat routines
- [ObjectManager](../styx/objectmanager.md) - Finding navigation targets
