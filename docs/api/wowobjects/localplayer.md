# LocalPlayer Class

Represents the current player character.

**Namespace**: `Styx.WoWInternals.WoWObjects`  
**Inherits**: [WoWPlayer](wowplayer.md)  
**Access**: `StyxWoW.Me`

## Overview

The `LocalPlayer` class provides access to the current player character's properties and methods. It extends `WoWPlayer` with additional functionality specific to the controlled character.

## Properties

### Basic Information

#### Name
```csharp
public string Name { get; }
```
The player's character name.

**Example:**
```csharp
Logger.Write($"Character: {StyxWoW.Me.Name}");
// Output: Character: Arthas
```

#### Level
```csharp
public int Level { get; }
```
The player's current level (1-80 in WotLK).

#### Class
```csharp
public WoWClass Class { get; }
```
The player's class.

**WotLK Classes:**
- `Warrior`, `Paladin`, `Hunter`, `Rogue`, `Priest`
- `DeathKnight`, `Shaman`, `Mage`, `Warlock`, `Druid`

#### Race
```csharp
public WoWRace Race { get; }
```
The player's race.

### Combat & Status

#### Combat
```csharp
public bool Combat { get; }
```
Whether the player is in combat.

**Example:**
```csharp
if (StyxWoW.Me.Combat)
{
    Logger.Write("We are in combat!");
}
```

#### Dead
```csharp
public bool Dead { get; }
```
Whether the player is dead.

#### IsGhost
```csharp
public override bool IsGhost { get; }
```
Whether the player is in ghost form (dead and released).

!!! note "WotLK Specific"
    This property was added specifically for WotLK. It reads the `PlayerFlags` bit 0x10.

#### Mounted
```csharp
public bool Mounted { get; }
```
Whether the player is on a mount.

#### IsMounted
```csharp
public bool IsMounted { get; }
```
Alias for `Mounted`.

### Health & Resources

#### Health
```csharp
public uint Health { get; }
```
Current health points.

#### MaxHealth / HealthMax
```csharp
public uint MaxHealth { get; }
public uint HealthMax { get; }
```
Maximum health points.

#### HealthPercent
```csharp
public double HealthPercent { get; }
```
Health percentage (0-100).

**Example:**
```csharp
if (StyxWoW.Me.HealthPercent < 30)
{
    Logger.Write("Low health! Heal up!");
}
```

#### Power
```csharp
public uint Power { get; }
```
Current power (mana, rage, energy, runic power).

#### MaxPower
```csharp
public uint MaxPower { get; }
```
Maximum power.

#### PowerPercent
```csharp
public double PowerPercent { get; }
```
Power percentage (0-100).

#### PowerType
```csharp
public WoWPowerType PowerType { get; }
```
Type of power resource:
- `Mana`, `Rage`, `Focus`, `Energy`, `Happiness`, `Runes`, `RunicPower`

### Group & Raid

#### IsInParty
```csharp
public bool IsInParty { get; }
```
Whether the player is in a party (5-man group).

#### IsInRaid
```csharp
public bool IsInRaid { get; }
```
Whether the player is in a raid (10/25-man).

#### Role
```csharp
public WoWPartyMember.GroupRole Role { get; }
```
The player's LFG role (Tank, Healer, Damage, None).

**Roles:**
- `GroupRole.None` - No role assigned (solo or normal party)
- `GroupRole.Tank` - Tank role
- `GroupRole.Healer` - Healer role  
- `GroupRole.Damage` - DPS role

!!! info "WotLK Implementation"
    This uses `GetRaidRosterInfo()` which exists in WotLK 3.3.0+. Only works in raids or LFG dungeon groups. Returns `None` for solo or normal parties.

**Example:**
```csharp
if (StyxWoW.Me.Role == WoWPartyMember.GroupRole.Tank)
{
    Logger.Write("I am the tank!");
}
```

#### PartyMemberInfos
```csharp
public List<WoWPartyMember> PartyMemberInfos { get; }
```
List of party members (excluding self).

#### RaidMemberInfos
```csharp
public List<WoWPartyMember> RaidMemberInfos { get; }
```
List of raid members (excluding self).

### Target & Focus

#### CurrentTarget
```csharp
public WoWUnit CurrentTarget { get; }
```
The currently targeted unit.

**Example:**
```csharp
WoWUnit target = StyxWoW.Me.CurrentTarget;
if (target != null && target.IsValid && target.IsAlive)
{
    Logger.Write($"Target: {target.Name} ({target.HealthPercent}%)");
}
```

#### CurrentTargetGuid
```csharp
public ulong CurrentTargetGuid { get; }
```
GUID of the current target.

#### FocusedUnit
```csharp
public WoWUnit FocusedUnit { get; }
```
The focus target.

#### FocusedUnitGuid
```csharp
public ulong FocusedUnitGuid { get; }
```
GUID of the focus target.

### Location & Movement

#### Location
```csharp
public WoWPoint Location { get; }
```
The player's current 3D position.

#### IsMoving
```csharp
public bool IsMoving { get; }
```
Whether the player is moving.

#### IsFalling
```csharp
public bool IsFalling { get; }
```
Whether the player is falling.

#### IsFlying
```csharp
public bool IsFlying { get; }
```
Whether the player is flying (flying mount or flight form).

### Pet

#### HasPet
```csharp
public bool HasPet { get; }
```
Whether the player has an active pet.

#### Pet
```csharp
public override WoWUnit? Pet { get; }
```
The player's active pet or summoned creature.

**Example:**
```csharp
if (StyxWoW.Me.HasPet)
{
    WoWUnit pet = StyxWoW.Me.Pet;
    Logger.Write($"Pet: {pet.Name} ({pet.HealthPercent}%)");
}
```

## Methods

### SetFocus
```csharp
public void SetFocus(WoWUnit unit)
public void SetFocus(ulong guid)
```
Sets the focus target.

**Example:**
```csharp
WoWUnit boss = ObjectManager.GetObjectsOfType<WoWUnit>()
    .FirstOrDefault(u => u.Entry == 12345);
if (boss != null)
    StyxWoW.Me.SetFocus(boss);
```

### GetSkill
```csharp
public WoWSkill? GetSkill(SkillLine skillLine)
public WoWSkill? GetSkill(int skillLineId)
```
Gets a skill by skill line.

**Example:**
```csharp
WoWSkill? mining = StyxWoW.Me.GetSkill(SkillLine.Mining);
if (mining != null)
{
    Logger.Write($"Mining: {mining.CurrentValue}/{mining.MaxValue}");
}
```

### GetAllSkills
```csharp
public List<WoWSkill> GetAllSkills()
```
Gets all known skills.

### GetPartyMemberGuid
```csharp
public ulong GetPartyMemberGuid(int index)
```
Gets the GUID of a party member by index (0-3).

### GetRaidMemberGuid
```csharp
public ulong GetRaidMemberGuid(int index)
```
Gets the GUID of a raid member by index (0-39).

## Usage Examples

### Check Health and Heal

```csharp
if (StyxWoW.Me.HealthPercent < 50)
{
    if (SpellManager.CanCast("Flash Heal"))
    {
        SpellManager.Cast("Flash Heal");
        Logger.Write("Healing myself!");
    }
}
```

### Target Nearest Enemy

```csharp
WoWUnit nearest = ObjectManager.GetObjectsOfType<WoWUnit>()
    .Where(u => u.IsHostile && u.IsAlive && u.Distance < 40)
    .OrderBy(u => u.Distance)
    .FirstOrDefault();

if (nearest != null)
{
    nearest.Target();
    Logger.Write($"Targeting: {nearest.Name}");
}
```

### Check Combat State

```csharp
if (!StyxWoW.Me.Combat && StyxWoW.Me.HealthPercent < 80)
{
    Logger.Write("Out of combat and injured - eating food");
    // Use food item logic here
}
```

### Party Healing Example

```csharp
foreach (var member in StyxWoW.Me.PartyMemberInfos)
{
    WoWPlayer player = member.ToPlayer();
    if (player != null && player.IsValid && player.HealthPercent < 60)
    {
        player.Target();
        SpellManager.Cast("Heal");
        Logger.Write($"Healing {player.Name}");
        break;
    }
}
```

## See Also

- [WoWPlayer](wowplayer.md) - Parent class
- [WoWUnit](wowunit.md) - Base unit class
- [ObjectManager](../styx/objectmanager.md) - Object retrieval
