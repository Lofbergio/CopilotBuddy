# API Reference Overview

CopilotBuddy provides a comprehensive API for interacting with World of Warcraft 3.3.5a.

!!! warning "WotLK Compatibility"
    This API is based on HonorBuddy 4.3.4 (Cataclysm) but adapted for **WotLK 3.3.5a**. Some features from Cataclysm are not available. See [API Differences](../compatibility/api-differences.md) for details.

## Core Namespaces

### Styx.WoWInternals

Core classes for WoW process interaction:

- **[ObjectManager](styx/objectmanager.md)** - Access game objects (units, players, items)
- **[Memory](styx/memory.md)** - Direct memory reading and writing
- **[Lua](styx/lua.md)** - Execute Lua code and retrieve values

### Styx.WoWInternals.WoWObjects

Game object representations:

- **[WoWObject](wowobjects/wowobject.md)** - Base class for all WoW objects
- **[WoWUnit](wowobjects/wowunit.md)** - NPCs, creatures, and players
- **[WoWPlayer](wowobjects/wowplayer.md)** - Player characters
- **[LocalPlayer](wowobjects/localplayer.md)** - The current player

### Styx.Combat.CombatRoutine

Combat routine system:

- **[CustomClass](combat/customclass.md)** - Base class for combat routines
- **[RoutineManager](combat/routinemanager.md)** - Routine loading and management

### TreeSharp

Behavior tree system for AI logic:

- `Composite` - Base behavior tree node
- `PrioritySelector` - Execute first successful child
- `Sequence` - Execute all children in order
- `Action` - Leaf node that performs an action
- `Decorator` - Modify child behavior

### Tripper.Navigation

Pathfinding and movement:

- `Navigator` - High-level navigation
- `MeshManager` - Recast mesh management
- `PathFinder` - A* pathfinding

## Quick Examples

### Get Current Target

```csharp
using Styx;
using Styx.WoWInternals.WoWObjects;

WoWUnit target = StyxWoW.Me.CurrentTarget;
if (target != null && target.IsValid)
{
    Logger.Write($"Target: {target.Name} ({target.HealthPercent}%)");
}
```

### Execute Lua

```csharp
using Styx;

// Simple execution
Lua.DoString("print('Hello from C#!')");

// Get return value
string playerName = Lua.GetReturnVal<string>("return UnitName('player')", 0);
```

### Check if in Combat

```csharp
using Styx;

if (StyxWoW.Me.Combat)
{
    Logger.Write("We are in combat!");
}
```

### Cast a Spell

```csharp
using Styx;

if (SpellManager.CanCast("Fireball"))
{
    SpellManager.Cast("Fireball");
}
```

## Common Patterns

### Safe Object Access

Always check `IsValid` before accessing properties:

```csharp
WoWUnit target = StyxWoW.Me.CurrentTarget;
if (target != null && target.IsValid && target.IsAlive)
{
    // Safe to use target
    float distance = target.Distance;
}
```

### Memory Safety

Wrap memory access in try-catch:

```csharp
try
{
    ulong guid = Memory.Read<ulong>(address);
}
catch (Exception ex)
{
    Logging.WriteException(ex);
}
```

### Lua Error Handling

```csharp
try
{
    var result = Lua.GetReturnVal<int>("return GetNumRaidMembers()", 0);
}
catch
{
    // Lua error or function doesn't exist
    return 0;
}
```

## Next Steps

- Explore specific API documentation in the sidebar
- Check [WotLK Compatibility](../compatibility/overview.md) for version-specific notes
- Read [Creating Combat Routines](../guides/creating-routines.md) to build your own CC
