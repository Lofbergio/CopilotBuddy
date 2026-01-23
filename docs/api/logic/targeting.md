# Targeting Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - Advanced target selection with filtering, weighting, and line-of-sight checks.

The `Targeting` class provides an intelligent target selection system with customizable filters and priority scoring.

## Namespace

```csharp
using Styx.Logic;
```

## Overview

`Targeting` is a sophisticated target selection engine that:
- Filters units by combat state, level, distance, and blacklist
- Weights targets by threat, health, distance, and line-of-sight
- Integrates with [Blacklist](blacklist.md), POI, and profiles
- Updates automatically on `Pulse()`

!!! tip "Singleton Pattern"
    Access via `Targeting.Instance` - single instance shared across bot.

---

## Instance Access

```csharp
// Get singleton instance
Targeting targeting = Targeting.Instance;

// Get first target
WoWUnit? firstTarget = targeting.FirstUnit;

// Get all targets
List<WoWUnit> targets = targeting.TargetList;
```

---

## Properties

### Target Lists

| Property | Type | Description |
|----------|------|-------------|
| `FirstUnit` | `WoWUnit?` | First target in priority list (highest score) |
| `TargetList` | `List<WoWUnit>` | All targets in priority order |
| `MaxTargets` | `int` | Maximum targets to return (default: 5) |

```csharp
// Get top 3 targets
Targeting.Instance.MaxTargets = 3;
Targeting.Instance.Pulse();

List<WoWUnit> top3 = Targeting.Instance.TargetList;
foreach (var unit in top3)
{
    Console.WriteLine($"{unit.Name} - Distance: {unit.Distance:F1}");
}
```

### Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeWorldPlayers` | `bool` | `false` | Include other players as targets (always `true` in battlegrounds) |
| `IncludeElites` | `bool` | `true` | Include elite mobs |
| `KillBetweenHotspots` | `bool` | Profile setting | Kill mobs while moving between hotspots |
| `DisplayTargetingExceptions` | `bool` | `true` | Log targeting errors |

```csharp
// Enable PvP targeting
Targeting.Instance.IncludeWorldPlayers = true;

// Disable elite targeting
Targeting.Instance.IncludeElites = false;
```

### Static Properties

| Property | Type | Description |
|----------|------|-------------|
| `PullDistance` | `double` | Pull distance from profile settings |
| `PullDistanceSqr` | `double` | Pull distance squared (for fast distance checks) |
| `CollectionRange` | `double` | Collection range from grind area (default: 100y) |

```csharp
// Check if unit is within pull range
if (unit.DistanceSqr <= Targeting.PullDistanceSqr)
{
    Console.WriteLine("Unit in pull range");
}
```

---

## Methods

### `void Pulse()`

Updates the target list (calls `Update()` internally).

```csharp
// Update targets every bot tick
Targeting.Instance.Pulse();

// Get updated target
WoWUnit? target = Targeting.Instance.FirstUnit;
if (target != null)
{
    target.Target();
}
```

### `void Clear()`

Clears the current target list.

```csharp
// Clear all targets
Targeting.Instance.Clear();
```

### Static Aggro Checks

#### `int GetAggroOnMeWithin(WoWPoint position, float range)`

Counts enemies with aggro on player within range of a position.

```csharp
WoWPoint destination = new WoWPoint(1234, 5678, 100);

int aggroCount = Targeting.GetAggroOnMeWithin(destination, range: 30f);

if (aggroCount > 0)
{
    Console.WriteLine($"{aggroCount} enemies will aggro at destination");
}
```

#### `int GetAggroWithin(WoWPoint position, float range)`

Counts enemies in combat within range of a position (any combat, not just with player).

```csharp
int combatCount = Targeting.GetAggroWithin(destination, range: 20f);
Console.WriteLine($"{combatCount} enemies in combat nearby");
```

---

## Events and Filters

### Event: `OnTargetListUpdateFinished`

Fired after target list is updated.

```csharp
Targeting.Instance.OnTargetListUpdateFinished += (targetNames) =>
{
    Console.WriteLine($"Found {targetNames.Count} targets:");
    foreach (string name in targetNames)
    {
        Console.WriteLine($"  - {name}");
    }
};
```

### Event: `RemoveTargetsFilter`

Filters out unwanted targets **before** scoring.

```csharp
// Remove targets below 50% health
Targeting.Instance.RemoveTargetsFilter += (units) =>
{
    for (int i = units.Count - 1; i >= 0; i--)
    {
        var unit = units[i] as WoWUnit;
        if (unit != null && unit.HealthPercent < 50)
        {
            units.RemoveAt(i);
        }
    }
};
```

### Event: `IncludeTargetsFilter`

Adds specific targets to the list (e.g., quest targets).

```csharp
// Include quest mobs
Targeting.Instance.IncludeTargetsFilter += (incomingUnits, outgoingUnits) =>
{
    foreach (WoWObject obj in incomingUnits)
    {
        var unit = obj as WoWUnit;
        if (unit != null && IsQuestMob(unit))
        {
            outgoingUnits.Add(unit);
        }
    }
};
```

### Event: `WeighTargetsFilter`

Adjusts target priority scores.

```csharp
// Prioritize casters
Targeting.Instance.WeighTargetsFilter += (targets) =>
{
    foreach (var targetPriority in targets)
    {
        var unit = targetPriority.Object as WoWUnit;
        if (unit != null && unit.Class == WoWClass.Mage)
        {
            targetPriority.Score += 100; // High priority for mages
        }
    }
};
```

---

## Default Filter Logic

### DefaultRemoveTargetsFilter

Removes targets that are:
- ❌ Dead
- ❌ Blacklisted
- ❌ Too far (beyond `CollectionRange`)
- ❌ Wrong level (outside grind area level range)
- ❌ Friendly
- ❌ Not attackable
- ❌ On taxi
- ❌ Critter
- ❌ Flight master
- ❌ Flying
- ❌ Tagged by other players (if not in party/raid)
- ❌ In blacklisted entry list (hardcoded NPC IDs)

### DefaultIncludeTargetsFilter

Adds targets that are:
- ✅ In combat with player (`Aggro` or `PetAggro`)
- ✅ Fleeing and tagged by player
- ✅ Current POI (Point of Interest)

### DefaultTargetWeight

Scores targets based on:

**In Combat:**
- ➖ Health percent (lower = higher priority)
- ➕ Mana percent (if current target has mana)
- ➕ Distance bonus (closer = higher priority)

**Out of Combat:**
- ➕ Distance bonus (closer = higher priority)
- ➕ Current target bonus (+150)
- ➕ POI target bonus (+150)
- ➕ Within aggro range bonus (+100)
- ➕ Line-of-sight bonus (+25)
- ➕ Reaction bonus (hostile = higher priority)
- ➖ Elite penalty (-1000 if profile disables elites)
- ➖ Blacklist penalty (-1000)
- ➖ Pet penalty (-50)
- ➖ Line-of-sight penalty (-25 if blocked)
- ➖ Nearby units penalty (-5 per unit within 15y)

---

## Complete Examples

### Example 1: Basic Target Selection

```csharp
using Styx.Logic;

public class SimpleTargeting
{
    public WoWUnit? FindTarget()
    {
        // Update target list
        Targeting.Instance.Pulse();
        
        // Get best target
        WoWUnit? target = Targeting.Instance.FirstUnit;
        
        if (target != null)
        {
            Console.WriteLine($"Best target: {target.Name} (Distance: {target.Distance:F1}y)");
            return target;
        }
        
        Console.WriteLine("No targets available");
        return null;
    }
}
```

### Example 2: Custom Filter - Prioritize Quest Mobs

```csharp
public class QuestTargeting
{
    private HashSet<uint> _questMobIds = new()
    {
        12345, // Quest mob 1
        67890, // Quest mob 2
        11223  // Quest mob 3
    };
    
    public void Initialize()
    {
        // Add quest mobs to target list
        Targeting.Instance.IncludeTargetsFilter += IncludeQuestMobs;
        
        // Give quest mobs high priority
        Targeting.Instance.WeighTargetsFilter += PrioritizeQuestMobs;
    }
    
    private void IncludeQuestMobs(List<WoWObject> incomingUnits, HashSet<WoWObject> outgoingUnits)
    {
        foreach (var obj in incomingUnits)
        {
            var unit = obj as WoWUnit;
            if (unit != null && _questMobIds.Contains(unit.Entry))
            {
                outgoingUnits.Add(unit);
            }
        }
    }
    
    private void PrioritizeQuestMobs(List<Targeting.TargetPriority> targets)
    {
        foreach (var targetPriority in targets)
        {
            var unit = targetPriority.Object as WoWUnit;
            if (unit != null && _questMobIds.Contains(unit.Entry))
            {
                targetPriority.Score += 500; // Very high priority
            }
        }
    }
}
```

### Example 3: Avoid Targeting Specific Mob Types

```csharp
public class AvoidTargeting
{
    public void Initialize()
    {
        // Remove dangerous mobs
        Targeting.Instance.RemoveTargetsFilter += RemoveDangerousMobs;
    }
    
    private void RemoveDangerousMobs(List<WoWObject> units)
    {
        for (int i = units.Count - 1; i >= 0; i--)
        {
            var unit = units[i] as WoWUnit;
            if (unit == null)
                continue;
            
            // Remove elites above our level
            if (unit.Elite && unit.Level > StyxWoW.Me.Level)
            {
                units.RemoveAt(i);
                continue;
            }
            
            // Remove specific dangerous NPCs
            if (unit.Name.Contains("Devilsaur") || unit.Name.Contains("Elite Guard"))
            {
                units.RemoveAt(i);
                continue;
            }
        }
    }
}
```

### Example 4: AoE Target Selection

```csharp
public class AoETargeting
{
    public WoWPoint? FindBestAoELocation(int minTargets = 3, float radius = 10f)
    {
        Targeting.Instance.MaxTargets = 15; // Get more targets
        Targeting.Instance.Pulse();
        
        var targets = Targeting.Instance.TargetList;
        
        if (targets.Count < minTargets)
            return null;
        
        // Find location with most targets
        WoWPoint? bestLocation = null;
        int bestCount = 0;
        
        foreach (var candidate in targets)
        {
            int count = targets.Count(t => t.Location.Distance(candidate.Location) <= radius);
            
            if (count >= minTargets && count > bestCount)
            {
                bestCount = count;
                bestLocation = candidate.Location;
            }
        }
        
        if (bestLocation != null)
        {
            Console.WriteLine($"Found AoE location with {bestCount} targets");
        }
        
        return bestLocation;
    }
}
```

### Example 5: Prioritize Low Health Targets

```csharp
public class ExecuteTargeting
{
    public void Initialize()
    {
        // Prioritize targets below 20% health for Execute
        Targeting.Instance.WeighTargetsFilter += PrioritizeLowHealth;
    }
    
    private void PrioritizeLowHealth(List<Targeting.TargetPriority> targets)
    {
        foreach (var targetPriority in targets)
        {
            var unit = targetPriority.Object as WoWUnit;
            if (unit == null)
                continue;
            
            // High priority for execute range
            if (unit.HealthPercent <= 20)
            {
                targetPriority.Score += 300;
            }
            // Medium priority for low health
            else if (unit.HealthPercent <= 40)
            {
                targetPriority.Score += 100;
            }
        }
    }
}
```

### Example 6: Tank Targeting (Threat Management)

```csharp
public class TankTargeting
{
    public void Initialize()
    {
        // Prioritize mobs not attacking tank
        Targeting.Instance.WeighTargetsFilter += PrioritizeLooseMobs;
    }
    
    private void PrioritizeLooseMobs(List<Targeting.TargetPriority> targets)
    {
        var tank = GetTankUnit();
        if (tank == null)
            return;
        
        foreach (var targetPriority in targets)
        {
            var unit = targetPriority.Object as WoWUnit;
            if (unit == null)
                continue;
            
            // Prioritize mobs attacking non-tanks
            if (unit.CurrentTargetGuid != tank.Guid && unit.Combat)
            {
                targetPriority.Score += 200;
            }
            
            // Extra priority if attacking healer
            if (IsAttackingHealer(unit))
            {
                targetPriority.Score += 500;
            }
        }
    }
    
    private WoWPlayer? GetTankUnit()
    {
        // Implementation to find tank
        return null;
    }
    
    private bool IsAttackingHealer(WoWUnit unit)
    {
        // Implementation to check if targeting healer
        return false;
    }
}
```

---

## Integration with TreeSharp

```csharp
using TreeSharp;

public class TargetingBehavior
{
    public static Composite CreateTargetingBehavior()
    {
        return new PrioritySelector(
            // Update targeting
            new Action(ctx =>
            {
                Targeting.Instance.Pulse();
                return RunStatus.Failure; // Continue to next node
            }),
            
            // Check if we have a target
            new Decorator(
                ctx => Targeting.Instance.FirstUnit == null,
                new Action(ctx =>
                {
                    Console.WriteLine("No targets");
                    return RunStatus.Failure;
                })
            ),
            
            // Target best unit
            new Action(ctx =>
            {
                WoWUnit? target = Targeting.Instance.FirstUnit;
                if (target != null && StyxWoW.Me.CurrentTarget != target)
                {
                    target.Target();
                    Console.WriteLine($"Targeting: {target.Name}");
                }
                return RunStatus.Success;
            })
        );
    }
}
```

---

## TargetPriority Class

```csharp
public sealed class TargetPriority
{
    public WoWObject Object;  // Target object
    public double Score;      // Priority score (higher = better)
}
```

Used internally and in `WeighTargetsFilter` event.

---

## Best Practices

### 1. Call Pulse() Regularly

```csharp
// GOOD - update every bot tick
while (botRunning)
{
    Targeting.Instance.Pulse();
    WoWUnit? target = Targeting.Instance.FirstUnit;
    // ... handle target
}

// BAD - stale targets
Targeting.Instance.Pulse(); // Once at start
while (botRunning)
{
    WoWUnit? target = Targeting.Instance.FirstUnit; // Old data!
}
```

### 2. Use Events for Custom Logic

```csharp
// GOOD - use events for customization
Targeting.Instance.WeighTargetsFilter += MyCustomWeighting;

// BAD - reimplementing entire targeting system
public WoWUnit? MyTargeting() { /* hundreds of lines */ }
```

### 3. Remove Targets in RemoveTargetsFilter

```csharp
// GOOD - efficient removal
Targeting.Instance.RemoveTargetsFilter += (units) =>
{
    for (int i = units.Count - 1; i >= 0; i--)
    {
        if (ShouldRemove(units[i]))
            units.RemoveAt(i);
    }
};

// BAD - modifying score to remove (inefficient)
Targeting.Instance.WeighTargetsFilter += (targets) =>
{
    foreach (var t in targets)
    {
        if (ShouldRemove(t.Object))
            t.Score = -99999; // Still processed
    }
};
```

### 4. Check for Null

```csharp
// GOOD
WoWUnit? target = Targeting.Instance.FirstUnit;
if (target != null && target.IsValid)
{
    target.Target();
}

// BAD - potential NullReferenceException
WoWUnit target = Targeting.Instance.FirstUnit;
target.Target(); // May be null!
```

---

## Blacklisted NPCs (Internal List)

Hardcoded blacklisted entry IDs (cannot target):

```csharp
28093, 22979, 24196, 13358, 23837, 13358, 9157,
17407, 17378, 17408, 24222, 32544, 32522, 24879, 25040
```

These NPCs are permanently excluded from targeting.

---

## See Also

- [Blacklist](blacklist.md) - Temporarily ignore targets
- [ObjectManager](../styx/objectmanager.md) - Get all objects
- [WoWUnit](../wowobjects/wowunit.md) - Unit properties
- [Navigator](../pathing/navigator.md) - Move to targets
- [POI](poi.md) - Point of Interest system
