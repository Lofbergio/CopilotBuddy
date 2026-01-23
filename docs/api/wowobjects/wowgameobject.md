# WoWGameObject Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - All GameObject types, states, and descriptor fields verified for WotLK 3.3.5a (build 12340).

The `WoWGameObject` class represents world objects like mining nodes, herb nodes, chests, doors, mailboxes, and other interactive objects.

## Namespace

```csharp
using Styx.WoWInternals.WoWObjects;
```

## Inheritance

```
WoWObject
  └─ WoWGameObject
```

## Overview

GameObjects are static or interactive world objects. They have positions, states, and can be interacted with (mined, herbalized, looted, opened, etc.).

---

## Core Properties

### Identifiers

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Guid` | `ulong` | Unique GameObject GUID | ✅ |
| `Entry` | `uint` | GameObject ID from `GameObjectTemplate.dbc` | ✅ |
| `Name` | `string` | GameObject display name | ✅ |
| `DisplayId` | `uint` | Display model ID | ✅ |

### Position & Rotation

!!! note "Position Override"
    GameObjects use different memory offsets for position than Units.

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Location` | `WoWPoint` | 3D position (X, Y, Z) | ✅ |
| `X` | `float` | X coordinate | ✅ |
| `Y` | `float` | Y coordinate | ✅ |
| `Z` | `float` | Z coordinate | ✅ |
| `Rotation` | `float` | Rotation angle (radians) | ✅ |
| `ParentRotation` | `Vector4` | Quaternion rotation (X, Y, Z, W) | ✅ |
| `Distance` | `double` | Distance from player | ✅ |
| `Distance2D` | `double` | 2D distance from player | ✅ |

**Position Offsets (3.3.5a):**
- `GO_POSITION_X_OFFSET = 0xE8` (232)
- `GO_POSITION_Y_OFFSET = 0xEC` (236)
- `GO_POSITION_Z_OFFSET = 0xF0` (240)
- `GO_ROTATION_OFFSET = 0xF8` (248)

### Creator & Ownership

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `CreatedByGuid` | `ulong` | GUID of the player/unit who created this object | ✅ |
| `CreatedBy` | `WoWUnit?` | The unit who created this object | ✅ |
| `Faction` | `uint` | Faction template ID | ✅ |
| `FactionTemplateId` | `uint` | Faction template ID (alias) | ✅ |
| `Level` | `int` | GameObject level | ✅ |

---

## State & Type

### `WoWGameObjectState State`

The current state of the GameObject.

```csharp
if (chest.State == WoWGameObjectState.Ready)
{
    chest.Interact();
}
```

**States:**
- `Ready = 0` - Ready to interact (default state)
- `Active = 1` - Currently active/in-use
- `ActiveAlternative = 2` - Alternative active state
- `Destroyed = 3` - Destroyed/depleted

### `WoWGameObjectType SubType`

The type of GameObject.

```csharp
if (gameObject.SubType == WoWGameObjectType.Herb)
{
    Console.WriteLine("Found herb node!");
}
```

**Common Types:**
- `Door = 0`
- `Button = 1`
- `Questgiver = 2`
- `Chest = 3`
- `Generic = 5`
- `Trap = 6`
- `Chair = 7`
- `SpellFocus = 8`
- `Goober = 10`
- `Transport = 11`
- `FishingBobber = 17`
- `Ritual = 18`
- `Mailbox = 19`
- `AuctionHouse = 20`
- `Fishinghole = 25`
- `Herb = 50` (custom classification)
- `MiningNode = 51` (custom classification)

---

## Flags

### `GameObjectFlags Flags`

Raw flags bitfield.

```csharp
if ((go.Flags & GameObjectFlags.Locked) != 0)
{
    Console.WriteLine("Object is locked");
}
```

### Flag Properties

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Locked` | `bool` | Object is locked (requires key/lockpicking) | ✅ |
| `InUse` | `bool` | Object is being used by someone | ✅ |
| `Triggered` | `bool` | Object has been triggered | ✅ |
| `Transport` | `bool` | Object is a transport (boat, zeppelin) | ✅ |
| `IsTransport` | `bool` | Alias for `Transport` | ✅ |

**Flag Values:**
```csharp
[Flags]
public enum GameObjectFlags : uint
{
    None = 0x0,
    InUse = 0x1,
    Locked = 0x2,
    ConditionInteract = 0x4,
    Transport = 0x8,
    NotSelectable = 0x10,
    NoDespawn = 0x20,
    Triggered = 0x40,
    Damaged = 0x200,
    Destroyed = 0x400
}
```

---

## Interaction Checks

### Gathering Nodes

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `CanMine` | `bool` | Can be mined (mining node + ready state) | ✅ |
| `IsMineral` | `bool` | Is a mining node | ✅ |
| `CanHarvest` | `bool` | Can be herbalized (herb + ready state) | ✅ |
| `IsHerb` | `bool` | Is an herb node | ✅ |
| `CanFish` | `bool` | Is a fishing bobber | ✅ |

### Looting & Usage

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `CanLoot` | `bool` | Can be looted (chest/fishinghole not locked + ready) | ✅ |
| `CanUse()` | `bool` | Can be used (not locked, not in use) | ✅ |
| `CanUseNow()` | `bool` | Alias for `CanUse()` | ✅ |
| `CanUseNow(out GameError)` | `bool` | Can be used (with error reason) | ✅ |

### Type Helpers

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `IsChest` | `bool` | Is a chest | ✅ |
| `IsMiningNode` | `bool` | Is a mining node | ✅ |
| `IsDoor` | `bool` | Is a door | ✅ |
| `IsButton` | `bool` | Is a button | ✅ |
| `IsQuestGiver` | `bool` | Is a quest giver object | ✅ |
| `IsMailbox` | `bool` | Is a mailbox | ✅ |

---

## Interaction

### `InteractRange`

Gets the interaction range for this GameObject.

```csharp
float range = go.InteractRange; // Usually 4.25f (4.5f - 0.25f margin)
```

### `Interact()`

Interacts with the GameObject (right-click).

```csharp
if (go.CanUse() && go.Distance < go.InteractRange)
{
    go.Interact();
}
```

---

## Advanced Properties

### Spell Focus

```csharp
WoWSpellFocus focus = go.SpellFocus;
if (focus != WoWSpellFocus.None)
{
    Console.WriteLine($"Spell focus: {focus}");
}
```

### Display & Appearance

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Model` | `string` | Model name (same as `Name`) | ✅ |
| `ArtKit` | `byte` | Art kit ID | ✅ |
| `AnimationProgress` | `byte` | Animation progress (0-255) | ✅ |

### Dynamic Flags

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `DynamicFlags` | `ushort` | Dynamic flags (16-bit) | ✅ |
| `FlagsDynamic` | `ushort` | Alias for `DynamicFlags` | ✅ |

### Raw Bytes

```csharp
uint bytes1 = go.Bytes1;
// Packed: [State][SubType][ArtKit][AnimProgress]
```

---

## Complete Examples

### Example 1: Mine Copper Nodes

```csharp
using Styx;
using Styx.WoWInternals.WoWObjects;
using System.Linq;

// Find nearby copper veins
var copperVeins = ObjectManager.GetObjectsOfType<WoWGameObject>()
    .Where(go => go.Entry == 1731) // Copper Vein Entry
    .Where(go => go.CanMine)
    .Where(go => go.Distance < 40)
    .OrderBy(go => go.Distance)
    .ToList();

if (copperVeins.Any())
{
    var vein = copperVeins.First();
    
    Console.WriteLine($"Found {vein.Name} at {vein.Distance:F1} yards");
    
    // Move to it
    if (vein.Distance > vein.InteractRange)
    {
        Navigator.MoveTo(vein.Location);
    }
    else
    {
        // Mine it
        vein.Interact();
    }
}
```

### Example 2: Herb Farming

```csharp
// Find all herbs in range
var herbs = ObjectManager.GetObjectsOfType<WoWGameObject>()
    .Where(go => go.CanHarvest)
    .Where(go => go.Distance < 50)
    .OrderBy(go => go.Distance)
    .ToList();

foreach (var herb in herbs)
{
    Console.WriteLine($"Herb: {herb.Name} (Entry: {herb.Entry})");
    Console.WriteLine($"  Distance: {herb.Distance:F1}y");
    Console.WriteLine($"  State: {herb.State}");
    Console.WriteLine($"  Position: {herb.Location}");
    
    // Check if someone else is using it
    if (herb.InUse)
    {
        Console.WriteLine($"  WARNING: {herb.Name} is in use");
        continue;
    }
    
    // Harvest it
    if (herb.Distance <= herb.InteractRange)
    {
        herb.Interact();
        break;
    }
}
```

### Example 3: Chest Looting

```csharp
// Find nearby chests
var chests = ObjectManager.GetObjectsOfType<WoWGameObject>()
    .Where(go => go.IsChest)
    .Where(go => go.Distance < 30)
    .ToList();

foreach (var chest in chests)
{
    Console.WriteLine($"Chest: {chest.Name}");
    Console.WriteLine($"  State: {chest.State}");
    Console.WriteLine($"  Locked: {chest.Locked}");
    Console.WriteLine($"  In Use: {chest.InUse}");
    Console.WriteLine($"  Can Loot: {chest.CanLoot}");
    
    if (chest.CanLoot && chest.Distance <= chest.InteractRange)
    {
        chest.Interact();
        break;
    }
}
```

### Example 4: Mailbox Finder

```csharp
// Find nearest mailbox
var mailbox = ObjectManager.GetObjectsOfType<WoWGameObject>()
    .Where(go => go.IsMailbox)
    .OrderBy(go => go.Distance)
    .FirstOrDefault();

if (mailbox != null)
{
    Console.WriteLine($"Found {mailbox.Name} at {mailbox.Distance:F1} yards");
    
    if (mailbox.Distance > mailbox.InteractRange)
    {
        Navigator.MoveTo(mailbox.Location);
    }
    else
    {
        mailbox.Interact();
    }
}
else
{
    Console.WriteLine("No mailbox found nearby");
}
```

### Example 5: GameObject Information Display

```csharp
void DisplayGameObjectInfo(WoWGameObject go)
{
    Console.WriteLine($"=== {go.Name} ===");
    Console.WriteLine($"Entry: {go.Entry}");
    Console.WriteLine($"GUID: {go.Guid:X16}");
    Console.WriteLine($"Type: {go.SubType}");
    Console.WriteLine($"State: {go.State}");
    Console.WriteLine($"Distance: {go.Distance:F1} yards");
    Console.WriteLine($"Position: ({go.X:F1}, {go.Y:F1}, {go.Z:F1})");
    Console.WriteLine($"Rotation: {go.Rotation:F2} radians");
    
    // Flags
    Console.WriteLine("Flags:");
    if (go.Locked) Console.WriteLine("  - Locked");
    if (go.InUse) Console.WriteLine("  - In Use");
    if (go.Triggered) Console.WriteLine("  - Triggered");
    if (go.Transport) Console.WriteLine("  - Transport");
    
    // Interaction
    Console.WriteLine($"Interact Range: {go.InteractRange:F1} yards");
    Console.WriteLine($"Can Use: {go.CanUse()}");
    
    // Type-specific
    if (go.IsMiningNode)
        Console.WriteLine($"Can Mine: {go.CanMine}");
    if (go.IsHerb)
        Console.WriteLine($"Can Harvest: {go.CanHarvest}");
    if (go.IsChest)
        Console.WriteLine($"Can Loot: {go.CanLoot}");
    
    // Creator
    if (go.CreatedByGuid != 0)
    {
        var creator = go.CreatedBy;
        if (creator != null)
            Console.WriteLine($"Created by: {creator.Name}");
    }
}
```

### Example 6: Gathering Route

```csharp
// Gather all mining nodes and herbs in an area
void GatherResources()
{
    // Find all gathering nodes
    var nodes = ObjectManager.GetObjectsOfType<WoWGameObject>()
        .Where(go => go.CanMine || go.CanHarvest)
        .Where(go => !go.InUse)
        .Where(go => go.Distance < 100)
        .OrderBy(go => go.Distance)
        .ToList();
    
    Console.WriteLine($"Found {nodes.Count} gathering nodes");
    
    foreach (var node in nodes)
    {
        // Move to node
        while (node.Distance > node.InteractRange)
        {
            if (!node.IsValid)
                break;
            
            Navigator.MoveTo(node.Location);
            System.Threading.Thread.Sleep(100);
        }
        
        // Gather
        if (node.IsValid && node.Distance <= node.InteractRange)
        {
            Console.WriteLine($"Gathering {node.Name}...");
            node.Interact();
            
            // Wait for gathering to complete
            System.Threading.Thread.Sleep(2000);
        }
    }
}
```

---

## Descriptor Field Offsets (WotLK 3.3.5a)

For advanced users working with raw descriptors:

| Field | Offset | Size | Description |
|-------|--------|------|-------------|
| `GAMEOBJECT_CREATED_BY` | 0x0 | 8 | Creator GUID |
| `GAMEOBJECT_DISPLAYID` | 0x8 | 4 | Display ID |
| `GAMEOBJECT_FLAGS` | 0xC | 4 | GameObject flags |
| `GAMEOBJECT_PARENTROTATION` | 0x10 | 16 | Parent rotation (4 floats) |
| `GAMEOBJECT_DYNAMIC` | 0x20 | 2 | Dynamic flags |
| `GAMEOBJECT_FACTION` | 0x24 | 4 | Faction template |
| `GAMEOBJECT_LEVEL` | 0x28 | 4 | Level |
| `GAMEOBJECT_BYTES_1` | 0x2C | 4 | Packed bytes (state, type, artkit, anim) |

---

## Known GameObject Entries

### Mining Nodes (WotLK)

| Name | Entry | Skill Required |
|------|-------|----------------|
| Copper Vein | 1731 | 1 |
| Tin Vein | 1732 | 65 |
| Iron Deposit | 1735 | 125 |
| Gold Vein | 1734 | 155 |
| Mithril Deposit | 2040 | 175 |
| Thorium Vein | 324 | 230 |
| Fel Iron Deposit | 181555 | 275 |
| Adamantite Deposit | 181556 | 325 |
| Cobalt Deposit | 189978 | 350 |
| Saronite Deposit | 189979 | 400 |
| Titanium Vein | 191133 | 450 |

### Herb Nodes (WotLK)

| Name | Entry | Skill Required |
|------|-------|----------------|
| Peacebloom | 1618 | 1 |
| Silverleaf | 1617 | 1 |
| Earthroot | 1619 | 15 |
| Mageroyal | 1620 | 50 |
| Briarthorn | 1621 | 70 |
| Stranglekelp | 2045 | 85 |
| Bruiseweed | 1622 | 100 |
| Wild Steelbloom | 1623 | 115 |
| Kingsblood | 1624 | 125 |
| Liferoot | 2041 | 150 |
| Fadeleaf | 2042 | 160 |
| Goldthorn | 2046 | 170 |
| Khadgar's Whisker | 2043 | 185 |
| Wintersbite | 2044 | 195 |
| Firebloom | 2866 | 205 |
| Purple Lotus | 142140 | 210 |
| Arthas' Tears | 142141 | 220 |
| Sungrass | 142142 | 230 |
| Blindweed | 142143 | 235 |
| Ghost Mushroom | 142144 | 245 |
| Gromsblood | 142145 | 250 |
| Golden Sansam | 176583 | 260 |
| Dreamfoil | 176584 | 270 |
| Mountain Silversage | 176586 | 280 |
| Plaguebloom | 176587 | 285 |
| Icecap | 176588 | 290 |
| Black Lotus | 176589 | 300 |
| Fel Lotus | 181271 | 350 |
| Goldclover | 189973 | 350 |
| Tiger Lily | 190169 | 375 |
| Talandra's Rose | 190170 | 385 |
| Lichbloom | 190171 | 425 |
| Icethorn | 190172 | 435 |
| Frost Lotus | 190175 | 450 |

---

## Reactions

### `WoWUnitReaction GetReactionTowards(WoWUnit unit)`

Gets the reaction of this GameObject towards a unit (based on creator).

```csharp
var reaction = gameObject.GetReactionTowards(StyxWoW.Me);
if (reaction == WoWUnitReaction.Hostile)
{
    Console.WriteLine("Object is hostile!");
}
```

---

## WorldMatrix

### `Matrix4x4 WorldMatrix`

Gets the world transformation matrix for this GameObject.

```csharp
Matrix4x4 matrix = go.WorldMatrix;
// Includes position and rotation
```

---

## See Also

- [WoWObject](wowobject.md) - Base object class
- [WoWUnit](wowunit.md) - Unit objects
- [ObjectManager](../styx/objectmanager.md) - Getting GameObjects
- [Navigator](../navigation/navigator.md) - Moving to GameObjects
