# WoWPlayer Class

Represents a player character (not the local player).

**Namespace**: `Styx.WoWInternals.WoWObjects`  
**Inherits**: [WoWUnit](wowunit.md)  
**Derived**: [LocalPlayer](localplayer.md)

## Overview

`WoWPlayer` extends `WoWUnit` with player-specific properties like race, class, gender, and faction.

## Properties

### Basic Information

#### Race
```csharp
public WoWRace Race { get; }
```
The player's race.

**WotLK Races:**
- **Alliance**: Human, Dwarf, Night Elf, Gnome, Draenei
- **Horde**: Orc, Undead, Tauren, Troll, Blood Elf

#### Class
```csharp
public WoWClass Class { get; }
```
The player's class.

**WotLK Classes:**
- Warrior, Paladin, Hunter, Rogue, Priest
- Death Knight, Shaman, Mage, Warlock, Druid

!!! note "No Monk"
    Monk doesn't exist in WotLK (added in MoP 5.0).

#### Gender
```csharp
public WoWGender Gender { get; }
```
The player's gender: `Male` or `Female`.

#### IsMale / IsFemale
```csharp
public bool IsMale { get; }
public bool IsFemale { get; }
```
Gender check helpers.

### Faction

#### IsAlliance
```csharp
public bool IsAlliance { get; }
```
Whether the player is Alliance.

#### IsHorde
```csharp
public bool IsHorde { get; }
```
Whether the player is Horde.

**Example:**
```csharp
foreach (var player in ObjectManager.GetObjectsOfType<WoWPlayer>())
{
    if (ObjectManager.Me.IsAlliance && player.IsHorde)
    {
        Logger.Write($"{player.Name} is an enemy player!");
    }
}
```

### Class Helpers

#### IsTank
```csharp
public bool IsTank { get; }
```
Whether the player is a tank class (Warrior, Paladin, Death Knight, Druid).

!!! note
    This is a simplified check. Real tanking depends on spec/stance.

#### IsHealer
```csharp
public bool IsHealer { get; }
```
Whether the player is a healer class (Priest, Paladin, Shaman, Druid).

!!! note
    This is a simplified check. Real healing depends on spec.

### Ghost Status

#### IsGhost
```csharp
public virtual bool IsGhost { get; }
```
Whether the player is in ghost form (dead and released).

!!! info "WotLK Specific"
    This property was added for WotLK. It reads the `PlayerFlags` bit 0x10.

**Example:**
```csharp
if (ObjectManager.Me.IsGhost)
{
    Logger.Write("I'm a ghost - need to find my corpse");
}
```

## Usage Examples

### Find Enemy Players
```csharp
var enemies = ObjectManager.GetObjectsOfType<WoWPlayer>()
    .Where(p => p.IsAlive && 
                p.IsHostile && 
                p.Distance < 40)
    .ToList();

Logger.Write($"Found {enemies.Count} enemy players nearby");
```

### Find Friendly Healers
```csharp
var healers = ObjectManager.GetObjectsOfType<WoWPlayer>()
    .Where(p => p.IsFriendly && 
                p.IsHealer && 
                p.Distance < 100)
    .ToList();

Logger.Write($"{healers.Count} healers in range");
```

### Check Player Class
```csharp
WoWPlayer target = ObjectManager.Me.CurrentTarget as WoWPlayer;
if (target != null)
{
    switch (target.Class)
    {
        case WoWClass.Priest:
            Logger.Write("Target is a priest - interrupt heals!");
            break;
        case WoWClass.Warrior:
            Logger.Write("Target is a warrior - kite!");
            break;
    }
}
```

### Faction Detection
```csharp
var alliancePlayers = ObjectManager.GetObjectsOfType<WoWPlayer>()
    .Count(p => p.IsAlliance && p.IsAlive && p.Distance < 100);

var hordePlayers = ObjectManager.GetObjectsOfType<WoWPlayer>()
    .Count(p => p.IsHorde && p.IsAlive && p.Distance < 100);

Logger.Write($"Alliance: {alliancePlayers}, Horde: {hordePlayers}");
```

## See Also

- [LocalPlayer](localplayer.md) - The current player
- [WoWUnit](wowunit.md) - Base unit class
- [ObjectManager](../styx/objectmanager.md) - Object retrieval
