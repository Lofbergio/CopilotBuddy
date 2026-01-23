# ObjectManager Class

Manages all game objects in the WoW client.

**Namespace**: `Styx.WoWInternals`  
**Type**: Static class

## Overview

The `ObjectManager` is the core class for accessing all objects in the WoW game world. It maintains a list of all visible objects (units, players, items, game objects) and provides methods to query and filter them.

!!! warning "WoW 3.3.5a Build 12340"
    All offsets are specific to build 12340. Using different builds will cause crashes.

## Properties

### Me
```csharp
public static LocalPlayer? Me { get; set; }
```
The current player character. Most commonly accessed property.

**Example:**
```csharp
if (ObjectManager.Me.HealthPercent < 50)
{
    Logger.Write("Low health!");
}
```

### IsInGame
```csharp
public static bool IsInGame { get; }
```
Whether the player is currently in-game (not at login screen or loading).

### ObjectList
```csharp
public static List<WoWObject> ObjectList { get; }
```
List of all visible objects in the game world.

!!! note
    This list is updated automatically by the bot framework. Don't modify it directly.

### IsInitialized
```csharp
public static bool IsInitialized { get; }
```
Whether ObjectManager has been initialized and is ready to use.

### PerformanceCounter
```csharp
public static uint PerformanceCounter { get; }
```
WoW's internal performance counter. Used for calculating aura time remaining.

**Offset**: `0x0086AE20`

### LocalGuid
```csharp
public static ulong LocalGuid { get; }
```
The GUID of the local player.

## Methods

### GetObjectsOfType\<T>
```csharp
public static List<T> GetObjectsOfType<T>() where T : WoWObject
public static List<T> GetObjectsOfType<T>(bool allowInheritance) where T : WoWObject
public static List<T> GetObjectsOfType<T>(bool allowInheritance, bool includeMeIfFound) where T : WoWObject
```
Gets all objects of a specific type.

**Parameters:**
- `allowInheritance` - Include derived types (e.g., WoWPlayer when searching for WoWUnit)
- `includeMeIfFound` - Include the local player in results

**Example:**
```csharp
// Get all units
List<WoWUnit> units = ObjectManager.GetObjectsOfType<WoWUnit>();

// Get all players (not NPCs)
List<WoWPlayer> players = ObjectManager.GetObjectsOfType<WoWPlayer>(false);

// Get nearby enemies
var enemies = ObjectManager.GetObjectsOfType<WoWUnit>()
    .Where(u => u.IsHostile && u.IsAlive && u.Distance < 40)
    .ToList();
```

### GetObjectByGuid\<T>
```csharp
public static T? GetObjectByGuid<T>(ulong guid) where T : WoWObject
```
Gets an object by its GUID.

**Example:**
```csharp
ulong targetGuid = ObjectManager.Me.CurrentTargetGuid;
WoWUnit? target = ObjectManager.GetObjectByGuid<WoWUnit>(targetGuid);
if (target != null && target.IsValid)
{
    Logger.Write($"Target: {target.Name}");
}
```

### Initialize
```csharp
public static void Initialize(Memory memory)
```
Initializes the ObjectManager with a Memory instance.

!!! warning
    This is called automatically by the bot framework. Don't call it manually.

### Update
```csharp
public static void Update()
```
Updates the object list from WoW memory.

!!! info
    Called automatically every frame. Don't call manually unless you know what you're doing.

## Events

### OnObjectListUpdateFinished
```csharp
public static event ObjectListUpdateFinishedDelegate OnObjectListUpdateFinished
```
Fired when the object list has finished updating.

## Usage Examples

### Find Nearest Enemy
```csharp
WoWUnit? nearest = ObjectManager.GetObjectsOfType<WoWUnit>()
    .Where(u => u.IsHostile && u.IsAlive && u.Attackable)
    .OrderBy(u => u.Distance)
    .FirstOrDefault();

if (nearest != null)
{
    nearest.Target();
    Logger.Write($"Targeting: {nearest.Name} at {nearest.Distance:F1}y");
}
```

### Find Quest NPCs
```csharp
var questGivers = ObjectManager.GetObjectsOfType<WoWUnit>()
    .Where(u => u.IsQuestGiver && u.Distance < 50)
    .ToList();

Logger.Write($"Found {questGivers.Count} quest givers nearby");
```

### Find Party Members
```csharp
var partyMembers = ObjectManager.GetObjectsOfType<WoWPlayer>()
    .Where(p => p.IsInMyParty && p.Distance < 40)
    .ToList();

foreach (var member in partyMembers)
{
    if (member.HealthPercent < 60)
    {
        Logger.Write($"{member.Name} needs healing!");
    }
}
```

### Count Enemies in Combat
```csharp
int enemiesInCombat = ObjectManager.GetObjectsOfType<WoWUnit>()
    .Count(u => u.IsHostile && 
                u.IsAlive && 
                u.Combat && 
                u.Distance < 30);

Logger.Write($"{enemiesInCombat} enemies in combat nearby");
```

## Memory Offsets (Build 12340)

| Offset | Description |
|--------|-------------|
| 0xC79CE0 | CurMgr base pointer |
| 0x2ED0 | CurMgr offset to actual manager |
| 0xC0 | Local player GUID offset |
| 0xAC | First object in list |
| 0x3C | Next object offset |
| 0xBD0792 | IsInGame flag |
| 0x0086AE20 | Performance counter |

## See Also

- [WoWObject](../wowobjects/wowobject.md) - Base object class
- [WoWUnit](../wowobjects/wowunit.md) - Unit class
- [LocalPlayer](../wowobjects/localplayer.md) - Player class
