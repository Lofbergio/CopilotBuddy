# BotPoi Class (Point of Interest)

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - Point of Interest system for bot coordination and task management.

The `BotPoi` class represents a Point of Interest (POI) that directs bot behavior toward a specific target, location, or objective.

## Namespace

```csharp
using Styx.Logic.POI;
```

## Overview

**POI** is a global state system that indicates what the bot should focus on. All bot components check the current POI to coordinate their actions.

!!! tip "Singleton Pattern"
    Access via `BotPoi.Current` - single global POI shared across all bot components.

---

## POI Types (PoiType Enum)

```csharp
public enum PoiType
{
    None,           // No POI set
    Kill,           // Target to kill
    Loot,           // Corpse/object to loot
    Skin,           // Corpse to skin
    Harvest,        // Node to harvest (herb/ore)
    Collect,        // Object to collect
    Buy,            // Vendor to buy from
    Sell,           // Vendor to sell to
    Repair,         // NPC to repair at
    Train,          // Trainer to learn from
    Mail,           // Mailbox to use
    Quest,          // Quest objective
    QuestPickUp,    // Quest to accept
    QuestTurnIn,    // Quest to complete
    InnKeeper,      // Inn keeper to set hearthstone
    Taxi,           // Flight master
    Resurrect,      // Spirit healer
    Hotspot         // Movement hotspot
}
```

---

## Static Access

### `BotPoi Current`

Gets or sets the current global POI.

```csharp
// Get current POI
BotPoi currentPoi = BotPoi.Current;
Console.WriteLine($"Current POI: {currentPoi.Type}");

// Set new POI
WoWUnit enemy = FindEnemy();
BotPoi.Current = new BotPoi(enemy, PoiType.Kill);
```

### `void Clear(string reason = "")`

Clears the current POI.

```csharp
// Clear POI
BotPoi.Clear("Enemy dead");

// Clear with logging
BotPoi.Clear("Quest completed");
```

---

## Properties

### Core Properties

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `PoiType` | POI type |
| `Name` | `string?` | Name of POI target |
| `Guid` | `ulong` | GUID of object (if applicable) |
| `Entry` | `uint` | Entry ID of object/NPC |
| `Location` | `WoWPoint` | Position of POI |

```csharp
var poi = BotPoi.Current;
Console.WriteLine($"POI: {poi.Name} at {poi.Location}");
Console.WriteLine($"Type: {poi.Type}, Entry: {poi.Entry}");
```

### Object Access

| Property | Type | Description |
|----------|------|-------------|
| `AsObject` | `WoWObject?` | POI as WoWObject (if available) |
| `AsUnit` | `WoWUnit?` | POI as WoWUnit |
| `AsPlayer` | `WoWPlayer?` | POI as WoWPlayer |
| `AsGameObject` | `WoWGameObject?` | POI as WoWGameObject |
| `AsItem` | `WoWItem?` | POI as WoWItem |

```csharp
// Access POI as WoWUnit
WoWUnit? target = BotPoi.Current.AsUnit;
if (target != null && target.IsAlive)
{
    target.Target();
}
```

### Profile Object Access

| Property | Type | Description |
|----------|------|-------------|
| `AsVendor` | `Vendor?` | POI as profile vendor |
| `AsMailbox` | `Mailbox?` | POI as profile mailbox |
| `AsPickUp` | `PickUpNode?` | POI as quest pickup node |
| `AsTurnIn` | `TurnInNode?` | POI as quest turn-in node |

### Distance

| Property | Type | Description |
|----------|------|-------------|
| `DistanceToPoi` | `double` | Distance from player to POI location |

```csharp
if (BotPoi.Current.DistanceToPoi < 5.0)
{
    Console.WriteLine("At POI location");
}
```

---

## Constructors

### Object POI

```csharp
// Create POI for a WoWObject
WoWUnit enemy = FindEnemy();
BotPoi killPoi = new BotPoi(enemy, PoiType.Kill);
BotPoi.Current = killPoi;
```

### Location POI

```csharp
// Create POI for a location
WoWPoint hotspot = new WoWPoint(1234, 5678, 100);
BotPoi movePoi = new BotPoi(hotspot, PoiType.Hotspot);
BotPoi.Current = movePoi;
```

### Profile Objects

```csharp
// Vendor POI
Vendor vendor = GetVendorFromProfile();
BotPoi vendorPoi = new BotPoi(vendor, PoiType.Sell);

// Mailbox POI
Mailbox mailbox = GetMailboxFromProfile();
BotPoi mailPoi = new BotPoi(mailbox);

// Quest POI
Quest quest = GetCurrentQuest();
BotPoi questPoi = new BotPoi(quest);
```

---

## Complete Examples

### Example 1: Combat POI Management

```csharp
using Styx.Logic.POI;
using TreeSharp;

public class CombatPOI
{
    public static Composite CreateCombatBehavior()
    {
        return new PrioritySelector(
            // Set kill POI when targeting
            new Decorator(
                ctx => StyxWoW.Me.GotTarget && BotPoi.Current.Type != PoiType.Kill,
                new Action(ctx =>
                {
                    var target = StyxWoW.Me.CurrentTarget;
                    Console.WriteLine($"Setting kill POI: {target.Name}");
                    BotPoi.Current = new BotPoi(target, PoiType.Kill);
                    return RunStatus.Success;
                })
            ),
            
            // Clear POI when target dies
            new Decorator(
                ctx => BotPoi.Current.Type == PoiType.Kill && 
                       (BotPoi.Current.AsUnit == null || !BotPoi.Current.AsUnit.IsAlive),
                new Action(ctx =>
                {
                    Console.WriteLine("Target dead, clearing kill POI");
                    BotPoi.Clear("Target died");
                    return RunStatus.Success;
                })
            ),
            
            // Combat logic
            new Action(ctx => HandleCombat())
        );
    }
    
    private static RunStatus HandleCombat()
    {
        // Combat implementation
        return RunStatus.Success;
    }
}
```

### Example 2: Looting POI

```csharp
public class LootingPOI
{
    public static Composite CreateLootBehavior()
    {
        return new PrioritySelector(
            // Find lootable corpse
            new Decorator(
                ctx => BotPoi.Current.Type != PoiType.Loot,
                new Action(ctx =>
                {
                    var corpse = ObjectManager.GetObjectsOfType<WoWUnit>()
                        .Where(u => u.Dead && u.CanLoot)
                        .Where(u => u.Distance < 40)
                        .OrderBy(u => u.Distance)
                        .FirstOrDefault();
                    
                    if (corpse != null)
                    {
                        Console.WriteLine($"Setting loot POI: {corpse.Name}");
                        BotPoi.Current = new BotPoi(corpse, PoiType.Loot);
                        return RunStatus.Success;
                    }
                    
                    return RunStatus.Failure;
                })
            ),
            
            // Move to corpse
            new Decorator(
                ctx => BotPoi.Current.DistanceToPoi > 5.0,
                new Action(ctx =>
                {
                    Navigator.MoveTo(BotPoi.Current.Location, "Corpse");
                    return RunStatus.Running;
                })
            ),
            
            // Loot corpse
            new Action(ctx =>
            {
                var corpse = BotPoi.Current.AsUnit;
                if (corpse != null && corpse.CanLoot)
                {
                    corpse.Interact();
                    System.Threading.Thread.Sleep(1000);
                }
                
                BotPoi.Clear("Looted");
                return RunStatus.Success;
            })
        );
    }
}
```

### Example 3: Gathering POI

```csharp
public class GatheringPOI
{
    public static Composite CreateGatherBehavior()
    {
        return new PrioritySelector(
            // Find herb/ore node
            new Decorator(
                ctx => BotPoi.Current.Type != PoiType.Harvest,
                new Action(ctx =>
                {
                    var node = ObjectManager.GetObjectsOfType<WoWGameObject>()
                        .Where(go => go.CanMine || go.CanHarvest)
                        .Where(go => go.Distance < 50)
                        .Where(go => !Blacklist.Contains(go))
                        .OrderBy(go => go.Distance)
                        .FirstOrDefault();
                    
                    if (node != null)
                    {
                        Console.WriteLine($"Setting harvest POI: {node.Name}");
                        BotPoi.Current = new BotPoi(node, PoiType.Harvest);
                        return RunStatus.Success;
                    }
                    
                    return RunStatus.Failure;
                })
            ),
            
            // Check if reachable
            new Decorator(
                ctx => !Navigator.CanNavigateFully(BotPoi.Current.Location),
                new Action(ctx =>
                {
                    Console.WriteLine("Node unreachable, blacklisting");
                    var node = BotPoi.Current.AsGameObject;
                    if (node != null)
                    {
                        Blacklist.Add(node, TimeSpan.FromMinutes(10));
                    }
                    BotPoi.Clear("Unreachable");
                    return RunStatus.Failure;
                })
            ),
            
            // Move to node
            new Decorator(
                ctx => BotPoi.Current.DistanceToPoi > 5.0,
                new Action(ctx =>
                {
                    Navigator.MoveTo(BotPoi.Current.Location, BotPoi.Current.Name);
                    return RunStatus.Running;
                })
            ),
            
            // Gather node
            new Action(ctx =>
            {
                var node = BotPoi.Current.AsGameObject;
                if (node != null)
                {
                    node.Interact();
                    System.Threading.Thread.Sleep(3000);
                }
                
                BotPoi.Clear("Gathered");
                return RunStatus.Success;
            })
        );
    }
}
```

### Example 4: Quest POI System

```csharp
public class QuestPOI
{
    public static Composite CreateQuestBehavior()
    {
        return new PrioritySelector(
            // Turn in completed quests
            new Decorator(
                ctx => HasCompletedQuests(),
                new Sequence(
                    new Action(ctx =>
                    {
                        var turnIn = GetQuestTurnIn();
                        if (turnIn != null)
                        {
                            BotPoi.Current = new BotPoi(turnIn);
                            return RunStatus.Success;
                        }
                        return RunStatus.Failure;
                    }),
                    
                    new Action(ctx => MoveToAndTurnInQuest())
                )
            ),
            
            // Pick up new quests
            new Decorator(
                ctx => CanAcceptQuests(),
                new Sequence(
                    new Action(ctx =>
                    {
                        var pickUp = GetQuestPickUp();
                        if (pickUp != null)
                        {
                            BotPoi.Current = new BotPoi(pickUp);
                            return RunStatus.Success;
                        }
                        return RunStatus.Failure;
                    }),
                    
                    new Action(ctx => MoveToAndAcceptQuest())
                )
            ),
            
            // Quest objectives
            new Decorator(
                ctx => HasActiveQuests(),
                new Action(ctx => HandleQuestObjectives())
            )
        );
    }
    
    private static bool HasCompletedQuests() => false;
    private static TurnInNode? GetQuestTurnIn() => null;
    private static RunStatus MoveToAndTurnInQuest() => RunStatus.Success;
    private static bool CanAcceptQuests() => false;
    private static PickUpNode? GetQuestPickUp() => null;
    private static RunStatus MoveToAndAcceptQuest() => RunStatus.Success;
    private static bool HasActiveQuests() => false;
    private static RunStatus HandleQuestObjectives() => RunStatus.Success;
}
```

### Example 5: Vendor POI

```csharp
public class VendorPOI
{
    public static void GoToVendor(Vendor vendor)
    {
        // Set vendor POI
        Console.WriteLine($"Going to vendor: {vendor.Name}");
        BotPoi.Current = new BotPoi(vendor, PoiType.Sell);
        
        // Move to vendor
        while (BotPoi.Current.DistanceToPoi > 5.0)
        {
            Navigator.MoveTo(BotPoi.Current.Location, vendor.Name);
            System.Threading.Thread.Sleep(100);
        }
        
        // Find vendor NPC
        var vendorNpc = ObjectManager.GetObjectsOfType<WoWUnit>()
            .Where(u => u.Entry == vendor.Entry)
            .OrderBy(u => u.Distance)
            .FirstOrDefault();
        
        if (vendorNpc != null)
        {
            vendorNpc.Interact();
            System.Threading.Thread.Sleep(1000);
            
            // Sell items
            SellJunk();
            
            // Repair
            if (vendorNpc.IsRepairMerchant)
            {
                RepairAll();
            }
        }
        
        BotPoi.Clear("Vendor complete");
    }
    
    private static void SellJunk() { }
    private static void RepairAll() { }
}
```

### Example 6: POI Priority System

```csharp
public class POIPriority
{
    public static Composite CreatePriorityBehavior()
    {
        return new PrioritySelector(
            // HIGHEST: Resurrection (dead)
            new Decorator(
                ctx => StyxWoW.Me.Dead && BotPoi.Current.Type != PoiType.Resurrect,
                new Action(ctx =>
                {
                    BotPoi.Current = new BotPoi(StyxWoW.Me.CorpsePoint, PoiType.Resurrect);
                    return RunStatus.Success;
                })
            ),
            
            // HIGH: Combat (in combat)
            new Decorator(
                ctx => StyxWoW.Me.Combat && BotPoi.Current.Type != PoiType.Kill,
                new Action(ctx =>
                {
                    if (StyxWoW.Me.GotTarget)
                    {
                        BotPoi.Current = new BotPoi(StyxWoW.Me.CurrentTarget, PoiType.Kill);
                    }
                    return RunStatus.Success;
                })
            ),
            
            // MEDIUM: Looting (corpse nearby)
            new Decorator(
                ctx => !StyxWoW.Me.Combat && BotPoi.Current.Type == PoiType.None,
                new Action(ctx =>
                {
                    var corpse = ObjectManager.GetObjectsOfType<WoWUnit>()
                        .Where(u => u.Dead && u.CanLoot && u.Distance < 20)
                        .FirstOrDefault();
                    
                    if (corpse != null)
                    {
                        BotPoi.Current = new BotPoi(corpse, PoiType.Loot);
                    }
                    
                    return RunStatus.Success;
                })
            ),
            
            // LOW: Quest objectives
            new Decorator(
                ctx => BotPoi.Current.Type == PoiType.None,
                new Action(ctx =>
                {
                    // Set quest POI
                    return RunStatus.Success;
                })
            )
        );
    }
}
```

---

## Best Practices

### 1. Always Check POI Type

```csharp
// GOOD - check POI type before accessing
if (BotPoi.Current.Type == PoiType.Kill)
{
    WoWUnit? target = BotPoi.Current.AsUnit;
    if (target != null)
    {
        // Handle combat
    }
}

// BAD - assuming POI type
WoWUnit target = BotPoi.Current.AsUnit; // May be null!
```

### 2. Clear POI When Complete

```csharp
// GOOD - clear when done
BotPoi.Clear("Task completed");

// BAD - leaving stale POI
// (next component will see old POI)
```

### 3. Validate POI Object

```csharp
// GOOD - validate object still exists
var unit = BotPoi.Current.AsUnit;
if (unit != null && unit.IsValid && unit.IsAlive)
{
    // Use unit
}

// BAD - no validation
BotPoi.Current.AsUnit.Target(); // May be null or invalid!
```

### 4. Use Descriptive Clear Reasons

```csharp
// GOOD - informative logging
BotPoi.Clear("Enemy died");
BotPoi.Clear("Node gathered");
BotPoi.Clear("Quest completed");

// BAD - no reason
BotPoi.Clear();
```

---

## Integration with Bot Components

### Targeting Integration

```csharp
// Targeting system checks POI for priority targets
if (BotPoi.Current.Type == PoiType.Kill)
{
    var poiTarget = BotPoi.Current.AsUnit;
    if (poiTarget != null)
    {
        // Prioritize POI target
        targetScore += 150.0;
    }
}
```

### Navigator Integration

```csharp
// Navigator uses POI location for movement
if (BotPoi.Current.Type != PoiType.None)
{
    Navigator.MoveTo(BotPoi.Current.Location);
}
```

---

## Events

POI changes trigger the player death event handler:

```csharp
BotEvents.Player.OnPlayerDied += OnPlayerDied;

private static void OnPlayerDied()
{
    BotPoi.Clear("Player died");
}
```

---

## Troubleshooting

### POI Not Clearing

**Problem:** Old POI remains after task completion.

**Solution:** Always call `BotPoi.Clear()` when done:

```csharp
// After looting
BotPoi.Clear("Looted corpse");

// After killing
BotPoi.Clear("Enemy dead");
```

### POI Object Null

**Problem:** `AsUnit`/`AsGameObject` returns null.

**Solution:** Check POI type and validate object:

```csharp
if (BotPoi.Current.Type == PoiType.Kill)
{
    var unit = BotPoi.Current.AsUnit;
    if (unit != null && unit.IsValid)
    {
        // Safe to use
    }
}
```

---

## See Also

- [Targeting](targeting.md) - Uses POI for target priority
- [Navigator](../pathing/navigator.md) - Uses POI for movement
- [Blacklist](blacklist.md) - Blacklist unreachable POIs
- [TreeSharp](../treesharp/overview.md) - POI management in behavior trees
