# WoWObject Class

Base class for all objects in the WoW game world.

**Namespace**: `Styx.WoWInternals.WoWObjects`  
**Implements**: `IComparable<WoWObject>`, `IEquatable<WoWObject>`  
**Derived Classes**: [WoWUnit](wowunit.md), WoWItem, WoWGameObject, WoWContainer, WoWCorpse, WoWDynamicObject

## Overview

`WoWObject` is the base class for all in-game objects. It provides core functionality for:
- Object identification (GUID, Entry, Type)
- Position and distance calculations
- Validity checking
- Memory descriptor reading

All visible objects in WoW inherit from this class.

---

## Core Properties

### Guid
```csharp
public virtual ulong Guid { get; }
```
The object's unique identifier (GUID). Persists across game sessions for static objects.

### Entry
```csharp
public uint Entry { get; }
```
The object's database entry ID. Used to identify what type of creature/item/object it is.

**Example:**
```csharp
if (unit.Entry == 6109) // Azuregos
{
    Logger.Write("World boss detected!");
}
```

### Type
```csharp
public virtual WoWObjectType Type { get; }
```
The object's type.

**Types:**
- `Object` - Base object
- `Item` - Items in inventory/bags
- `Container` - Bags
- `Unit` - NPCs, creatures, players
- `Player` - Player characters
- `GameObject` - Chests, nodes, doors
- `DynamicObject` - Area effects (e.g., Blizzard, Consecration)
- `Corpse` - Player/NPC corpses

### TypeFlags
```csharp
public WoWObjectTypeFlag TypeFlags { get; }
```
Bitmask of object type flags.

### BaseAddress
```csharp
public uint BaseAddress { get; private set; }
```
Memory address of the object in the WoW process.

### Name
```csharp
public virtual string Name { get; }
```
The object's name.

---

## Validity

### IsValid
```csharp
public bool IsValid { get; }
```
Whether the object is still valid in memory.

!!! warning "Always Check IsValid"
    Objects can become invalid if they despawn or move out of range. Always check before accessing properties.

**Example:**
```csharp
WoWUnit target = ObjectManager.Me.CurrentTarget;
if (target != null && target.IsValid && target.IsAlive)
{
    // Safe to use target
}
```

### IsDisabled
```csharp
public bool IsDisabled { get; }
```
Whether the object is disabled (phased out or temporarily unavailable).

### IsMe
```csharp
public bool IsMe { get; }
```
Whether this object is the local player.

---

## Position & Distance

### Location
```csharp
public virtual WoWPoint Location { get; }
```
The object's 3D position.

### X, Y, Z
```csharp
public virtual float X { get; }
public virtual float Y { get; }
public virtual float Z { get; }
```
Individual coordinate components.

### Rotation
```csharp
public virtual float Rotation { get; }
```
The object's facing direction in radians.

### RotationDegrees
```csharp
public float RotationDegrees { get; }
```
The object's facing direction in degrees.

### Distance
```csharp
public virtual double Distance { get; }
```
Distance from the player to this object.

### Distance2D
```csharp
public virtual double Distance2D { get; }
```
2D distance (ignoring Z-axis).

### DistanceSqr
```csharp
public virtual double DistanceSqr { get; }
```
Squared distance (faster than Distance - no sqrt).

### GetDistanceTo
```csharp
public double GetDistanceTo(WoWPoint point)
public double GetDistanceTo(WoWObject obj)
```
Get distance to a specific point or object.

**Example:**
```csharp
double distToVendor = ObjectManager.Me.GetDistanceTo(vendor);
if (distToVendor < 5.0)
{
    vendor.Interact();
}
```

---

## Quest & Interaction

### QuestGiverStatus
```csharp
public QuestGiverStatus QuestGiverStatus { get; }
```
The object's quest status indicator.

**Values:**
- `None` - No quest
- `Available` - Yellow ! (new quest)
- `Incomplete` - Gray ? (quest in progress)
- `Complete` - Yellow ? (quest complete)
- `Reward` - Quest reward available
- `Future` - Quest not yet available

### InteractType
```csharp
public WoWInteractType InteractType { get; }
```
How to interact with the object.

---

## Methods

### Interact / RightClick
```csharp
public virtual void Interact()
public virtual void RightClick()
```
Right-clicks the object to interact with it.

### Face
```csharp
public void Face()
```
Turns the player to face this object.

### IsFacing
```csharp
public bool IsFacing(float degrees = 70f)
```
Whether the player is facing this object within the specified degree cone.

---

## Comparison & Equality

### CompareTo
```csharp
public int CompareTo(WoWObject? other)
```
Compares by distance (for sorting).

### Equals
```csharp
public bool Equals(WoWObject? other)
public override bool Equals(object? obj)
```
Compares by GUID.

### GetHashCode
```csharp
public override int GetHashCode()
```
Returns hash of GUID.

---

## Events

### OnInvalidate
```csharp
public event ObjectInvalidateDelegate OnInvalidate
```
Fired when the object becomes invalid.

---

## Usage Examples

### Safe Object Access
```csharp
WoWUnit? target = ObjectManager.Me.CurrentTarget;
if (target != null && target.IsValid && target.IsAlive)
{
    Logger.Write($"Target: {target.Name} at {target.Distance:F1}y");
}
else
{
    Logger.Write("No valid target");
}
```

### Sort Objects by Distance
```csharp
var nearbyUnits = ObjectManager.GetObjectsOfType<WoWUnit>()
    .Where(u => u.IsValid && u.IsHostile)
    .OrderBy(u => u.Distance)
    .ToList();
```

### Check Quest Status
```csharp
foreach (var unit in ObjectManager.GetObjectsOfType<WoWUnit>())
{
    if (unit.QuestGiverStatus == QuestGiverStatus.Available)
    {
        Logger.Write($"{unit.Name} has a quest!");
    }
}
```

---

## WotLK 3.3.5a Offsets

| Offset | Description |
|--------|-------------|
| 0x14 | Object Type |
| 0x30 | Object GUID |
| 0xBC | Object Flags |
| 0x08 | Descriptor Pointer |

!!! warning "Build-Specific"
    These offsets are for build 12340 only.

---

## See Also

- [WoWUnit](wowunit.md) - Living entities
- [WoWPlayer](wowplayer.md) - Player characters
- [LocalPlayer](localplayer.md) - The current player
- [ObjectManager](../styx/objectmanager.md) - Object management
