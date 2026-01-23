# Blacklist Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - Time-based blacklist system for objects and mobs. Automatically cleans expired entries.

The `Blacklist` class provides a global blacklist system to temporarily ignore objects, mobs, or nodes.

## Namespace

```csharp
using Styx.Logic;
```

## Overview

`Blacklist` is a static class that maintains a global dictionary of blacklisted GUIDs with expiration times. Use it to avoid repeatedly interacting with problematic targets.

!!! tip "Common Use Cases"
    - Blacklist unreachable gathering nodes
    - Ignore mobs that repeatedly evade
    - Skip bugged quest objects
    - Avoid players in PvP

---

## Core Methods

### `void Add(ulong guid, TimeSpan duration)`

Adds a GUID to the blacklist for the specified duration.

```csharp
// Blacklist for 5 minutes
Blacklist.Add(enemy.Guid, TimeSpan.FromMinutes(5));

// Blacklist for 30 seconds
Blacklist.Add(node.Guid, TimeSpan.FromSeconds(30));

// Blacklist permanently (until bot restart)
Blacklist.Add(mob.Guid, TimeSpan.MaxValue);
```

### `void Add(WoWObject obj, TimeSpan duration)`

Adds a `WoWObject` to the blacklist for the specified duration.

```csharp
WoWUnit enemy = StyxWoW.Me.CurrentTarget;
Blacklist.Add(enemy, TimeSpan.FromMinutes(2));
```

---

### `bool Contains(ulong guid)`

Checks if a GUID is currently blacklisted.

```csharp
if (Blacklist.Contains(enemy.Guid))
{
    Console.WriteLine("Target is blacklisted, finding new target");
    return;
}
```

### `bool Contains(WoWObject obj)`

Checks if a `WoWObject` is currently blacklisted.

```csharp
WoWGameObject node = FindNearestOreNode();

if (Blacklist.Contains(node))
{
    Console.WriteLine($"{node.Name} is blacklisted");
    return;
}
```

### `bool Contains(ulong guid, bool flush)`

Checks if a GUID is blacklisted, optionally flushing expired entries first.

```csharp
// Check without flushing (faster)
bool isBlacklisted = Blacklist.Contains(guid, flush: false);

// Check with flushing (more accurate)
bool isBlacklisted = Blacklist.Contains(guid, flush: true);
```

**Parameters:**
- `guid` - GUID to check
- `flush` - If `true`, removes expired entries before checking

---

### `void Flush()`

Removes all expired blacklist entries.

```csharp
// Manually flush expired entries
Blacklist.Flush();
```

!!! note "Auto-Flushing"
    `Contains()` automatically flushes by default, so manual flushing is rarely needed.

---

## Complete Examples

### Example 1: Gathering with Blacklist

```csharp
using Styx;
using Styx.Logic;
using Styx.WoWInternals.WoWObjects;
using System.Linq;

public class GatheringBot
{
    public void GatherNearbyNodes()
    {
        var nodes = ObjectManager.GetObjectsOfType<WoWGameObject>()
            .Where(n => n.CanMine || n.CanHarvest)
            .Where(n => !Blacklist.Contains(n))  // Skip blacklisted nodes
            .Where(n => n.Distance < 50)
            .OrderBy(n => n.Distance)
            .FirstOrDefault();
        
        if (nodes == null)
        {
            Console.WriteLine("No available nodes");
            return;
        }
        
        // Try to gather
        if (!Navigator.CanNavigateFully(nodes.Location))
        {
            Console.WriteLine($"Cannot reach {nodes.Name}, blacklisting for 10 minutes");
            Blacklist.Add(nodes, TimeSpan.FromMinutes(10));
            return;
        }
        
        // Move to node
        Navigator.MoveTo(nodes.Location, nodes.Name);
        
        // Interact
        if (nodes.Distance <= nodes.InteractRange)
        {
            nodes.Interact();
            System.Threading.Thread.Sleep(3000);
            
            // Check if gathering failed
            if (nodes.IsValid)
            {
                Console.WriteLine($"Failed to gather {nodes.Name}, blacklisting for 5 minutes");
                Blacklist.Add(nodes, TimeSpan.FromMinutes(5));
            }
        }
    }
}
```

### Example 2: Combat with Evading Mobs

```csharp
public class CombatWithBlacklist
{
    private Dictionary<ulong, int> _evadeCount = new();
    
    public void HandleCombat()
    {
        var target = StyxWoW.Me.CurrentTarget;
        
        if (target == null || !target.IsAlive)
            return;
        
        // Check if target is evading (health resets, not in combat)
        if (IsEvading(target))
        {
            // Track evade count
            if (!_evadeCount.ContainsKey(target.Guid))
                _evadeCount[target.Guid] = 0;
            
            _evadeCount[target.Guid]++;
            
            Console.WriteLine($"{target.Name} evaded {_evadeCount[target.Guid]} times");
            
            // Blacklist after 3 evades
            if (_evadeCount[target.Guid] >= 3)
            {
                Console.WriteLine($"Blacklisting {target.Name} for 15 minutes (repeated evades)");
                Blacklist.Add(target, TimeSpan.FromMinutes(15));
                
                // Clear target
                StyxWoW.Me.ClearTarget();
            }
        }
    }
    
    private bool IsEvading(WoWUnit target)
    {
        return target.HealthPercent > 95 && 
               !target.Combat && 
               target.IsTargetingMeOrPet;
    }
}
```

### Example 3: Quest Object Interaction

```csharp
public class QuestHelper
{
    public void InteractWithQuestObject(WoWGameObject questObj)
    {
        // Skip if blacklisted
        if (Blacklist.Contains(questObj))
        {
            Console.WriteLine($"{questObj.Name} is blacklisted");
            return;
        }
        
        // Move to object
        while (questObj.Distance > questObj.InteractRange && questObj.IsValid)
        {
            Navigator.MoveTo(questObj.Location, questObj.Name);
            System.Threading.Thread.Sleep(100);
        }
        
        // Try to interact
        if (questObj.Distance <= questObj.InteractRange)
        {
            Console.WriteLine($"Interacting with {questObj.Name}");
            
            // Store pre-interaction state
            int questItemCountBefore = GetQuestItemCount();
            
            questObj.Interact();
            System.Threading.Thread.Sleep(2000);
            
            int questItemCountAfter = GetQuestItemCount();
            
            // Check if interaction was successful
            if (questItemCountAfter == questItemCountBefore)
            {
                Console.WriteLine($"{questObj.Name} interaction failed, blacklisting for 10 minutes");
                Blacklist.Add(questObj, TimeSpan.FromMinutes(10));
            }
            else
            {
                Console.WriteLine($"Successfully looted {questObj.Name}");
            }
        }
    }
    
    private int GetQuestItemCount()
    {
        // Implementation to count quest items in bags
        return 0;
    }
}
```

### Example 4: Target Selection with Blacklist

```csharp
using System.Collections.Generic;

public class TargetSelector
{
    public WoWUnit? FindBestTarget()
    {
        var me = StyxWoW.Me;
        
        var targets = ObjectManager.GetObjectsOfType<WoWUnit>()
            .Where(u => u.IsAlive)
            .Where(u => u.IsHostile)
            .Where(u => !Blacklist.Contains(u))  // Exclude blacklisted
            .Where(u => u.Distance < 40)
            .Where(u => !u.IsPet)
            .OrderBy(u => u.Distance)
            .ToList();
        
        if (!targets.Any())
        {
            Console.WriteLine("No valid targets (all blacklisted or out of range)");
            return null;
        }
        
        // Prefer targets already in combat with us
        var aggro = targets.FirstOrDefault(u => u.IsTargetingMeOrPet);
        if (aggro != null)
            return aggro;
        
        // Otherwise, closest target
        return targets.First();
    }
}
```

### Example 5: Temporary Blacklist for Looting

```csharp
public class LootManager
{
    private HashSet<ulong> _attemptedLoots = new();
    
    public void LootNearbyCorpses()
    {
        var corpses = ObjectManager.GetObjectsOfType<WoWUnit>()
            .Where(u => u.Dead)
            .Where(u => u.CanLoot)
            .Where(u => !Blacklist.Contains(u))
            .Where(u => u.Distance < 30)
            .OrderBy(u => u.Distance)
            .ToList();
        
        foreach (var corpse in corpses)
        {
            // Move to corpse
            if (corpse.Distance > corpse.InteractRange)
            {
                Navigator.MoveTo(corpse.Location, "Corpse");
                continue;
            }
            
            // Try to loot
            Console.WriteLine($"Looting {corpse.Name}");
            corpse.Interact();
            System.Threading.Thread.Sleep(1000);
            
            // Check if loot window opened
            bool lootWindowOpen = Lua.GetReturnVal<bool>("return LootFrame:IsVisible()", 0);
            
            if (!lootWindowOpen)
            {
                Console.WriteLine($"Cannot loot {corpse.Name}, blacklisting for 30 seconds");
                Blacklist.Add(corpse, TimeSpan.FromSeconds(30));
            }
            else
            {
                // Loot all items
                Lua.DoString("for i=1,GetNumLootItems() do LootSlot(i) end");
                System.Threading.Thread.Sleep(500);
            }
        }
    }
}
```

### Example 6: Blacklist Cleanup on Bot Start

```csharp
using TreeSharp;

public class MyBot
{
    public void OnStart()
    {
        Console.WriteLine("Bot starting, flushing blacklist");
        
        // Manual flush to remove all expired entries
        Blacklist.Flush();
        
        // Note: Cannot clear all blacklist entries (no Clear() method)
        // Blacklist persists across bot restarts until entries expire
    }
    
    public Composite CreateBehavior()
    {
        return new PrioritySelector(
            // Periodic blacklist cleanup (every 60 seconds)
            new Decorator(
                ctx => DateTime.Now.Second == 0,
                new Action(ctx =>
                {
                    Blacklist.Flush();
                    return RunStatus.Success;
                })
            ),
            
            // ... rest of bot logic
        );
    }
}
```

---

## Common Patterns

### Pattern 1: Try-Then-Blacklist

```csharp
void TryInteract(WoWObject obj, int maxAttempts = 3)
{
    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        obj.Interact();
        System.Threading.Thread.Sleep(1000);
        
        if (InteractionSucceeded())
        {
            Console.WriteLine("Success!");
            return;
        }
        
        Console.WriteLine($"Attempt {attempt}/{maxAttempts} failed");
    }
    
    // All attempts failed, blacklist
    Console.WriteLine("All attempts failed, blacklisting");
    Blacklist.Add(obj, TimeSpan.FromMinutes(5));
}
```

### Pattern 2: Distance-Based Blacklist

```csharp
void BlacklistIfTooFar(WoWObject obj, float maxDistance = 100)
{
    if (obj.Distance > maxDistance)
    {
        Console.WriteLine($"{obj.Name} is {obj.Distance:F1}y away (max: {maxDistance}), blacklisting");
        Blacklist.Add(obj, TimeSpan.FromMinutes(10));
    }
}
```

### Pattern 3: Conditional Blacklist Duration

```csharp
void BlacklistWithDynamicDuration(WoWUnit enemy)
{
    TimeSpan duration;
    
    if (enemy.IsElite)
        duration = TimeSpan.FromMinutes(15); // Elite: longer blacklist
    else if (enemy.Level > StyxWoW.Me.Level + 3)
        duration = TimeSpan.FromMinutes(10); // High level: medium blacklist
    else
        duration = TimeSpan.FromMinutes(5);  // Normal: short blacklist
    
    Console.WriteLine($"Blacklisting {enemy.Name} for {duration.TotalMinutes} minutes");
    Blacklist.Add(enemy, duration);
}
```

---

## Internal Implementation

### Storage Structure

```csharp
// Internal dictionary structure (readonly)
private static readonly Dictionary<ulong, DateTime> dictionary_0;

// GUID -> Expiration Time
// Example:
// {
//   0x0000000012345678: 2024-01-23 15:30:00,  // Expires in future
//   0x0000000087654321: 2024-01-23 14:00:00   // Already expired
// }
```

### Auto-Flush Behavior

```csharp
public static bool Contains(ulong guid)
{
    // Automatically flushes expired entries
    return Contains(guid, flush: true);
}
```

**Flush removes entries where:** `entry.ExpirationTime < DateTime.Now`

---

## Best Practices

### 1. Use Appropriate Durations

```csharp
// SHORT (30s - 2min): Temporary issues
Blacklist.Add(node, TimeSpan.FromSeconds(30));

// MEDIUM (5-10min): Pathing/navigation issues
Blacklist.Add(node, TimeSpan.FromMinutes(5));

// LONG (15min+): Evading mobs, bugged objects
Blacklist.Add(enemy, TimeSpan.FromMinutes(15));

// PERMANENT (until restart): Critical issues
Blacklist.Add(obj, TimeSpan.MaxValue);
```

### 2. Always Check Before Interacting

```csharp
// GOOD - check blacklist first
if (!Blacklist.Contains(target))
{
    Interact(target);
}

// BAD - no blacklist check
Interact(target);
```

### 3. Blacklist After Multiple Failures

```csharp
// GOOD - retry before blacklisting
int attempts = 0;
while (attempts < 3 && !Success())
{
    attempts++;
}
if (attempts >= 3)
    Blacklist.Add(obj, TimeSpan.FromMinutes(5));

// BAD - blacklist immediately
if (!Success())
    Blacklist.Add(obj, TimeSpan.FromMinutes(5));
```

### 4. Log Blacklist Actions

```csharp
// GOOD - informative logging
Console.WriteLine($"Blacklisting {obj.Name} (GUID: {obj.Guid:X}) for {duration.TotalMinutes}m - Reason: {reason}");
Blacklist.Add(obj, duration);

// BAD - silent blacklist
Blacklist.Add(obj, duration);
```

---

## Limitations

### No Clear Method

```csharp
// ❌ Cannot clear all blacklist entries
// No Blacklist.Clear() method exists

// ✅ Workaround: Wait for entries to expire
// Or restart the bot
```

### No List Method

```csharp
// ❌ Cannot enumerate blacklisted GUIDs
// No Blacklist.GetAll() or Blacklist.List() method

// ✅ Workaround: Track blacklisted objects manually
private HashSet<ulong> _myBlacklist = new();
```

### Global State

```csharp
// ⚠️ Blacklist is GLOBAL across all bot components
// One component's blacklist affects all others

// Example: Combat routine blacklists a mob
Blacklist.Add(enemy, TimeSpan.FromMinutes(10));

// Gathering routine also sees this blacklist
if (Blacklist.Contains(enemy.Guid))
{
    // This will be true!
}
```

---

## Troubleshooting

### Objects Stay Blacklisted Forever

**Problem:** Object never becomes available again.

**Solution:** Check expiration time is not `TimeSpan.MaxValue`:

```csharp
// WRONG - permanent blacklist
Blacklist.Add(obj, TimeSpan.MaxValue);

// CORRECT - temporary blacklist
Blacklist.Add(obj, TimeSpan.FromMinutes(10));
```

### Blacklist Not Working

**Problem:** Contains() returns false for blacklisted objects.

**Solution:** Ensure you're using the correct GUID:

```csharp
// Check GUID matches
Console.WriteLine($"Object GUID: {obj.Guid:X}");

// Verify blacklist check
bool isBlacklisted = Blacklist.Contains(obj);
Console.WriteLine($"Is blacklisted: {isBlacklisted}");
```

---

## See Also

- [Targeting](targeting.md) - Target selection (uses Blacklist)
- [Navigator](../pathing/navigator.md) - Movement (blacklist unreachable nodes)
- [ObjectManager](../styx/objectmanager.md) - Finding objects to blacklist
- [WoWObject](../wowobjects/wowobject.md) - Base class for blacklistable objects
