# WoWSpell Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - All spell properties verified for WotLK 3.3.5a (build 12340). Spell database (`Spell.dbc`) is fully parsed.

The `WoWSpell` class represents a spell from the WoW spell database. It provides access to spell information, cooldowns, ranges, costs, effects, and casting properties.

## Namespace

```csharp
using Styx.Logic.Combat;
```

## Overview

`WoWSpell` reads data from the `Spell.dbc` client database file. It provides detailed information about spells without requiring them to be known by the player.

!!! note "Static Information"
    `WoWSpell` contains static spell data from the database. For active auras/buffs on units, use [`WoWAura`](wowaura.md).

---

## Creating WoWSpell Instances

### `WoWSpell.FromId(int spellId)`

Creates a spell object from a spell ID.

```csharp
// Frostbolt (Rank 1)
WoWSpell frostbolt = WoWSpell.FromId(116);

if (frostbolt != null && frostbolt.IsValid)
{
    Console.WriteLine($"Spell: {frostbolt.Name}");
    Console.WriteLine($"Rank: {frostbolt.Rank}");
}
```

**Returns:**
- `WoWSpell` if found in database
- `null` if spell ID doesn't exist

---

## Core Properties

### Identifiers

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Id` | `int` | Spell ID from `Spell.dbc` | ✅ |
| `Name` | `string` | Spell display name | ✅ |
| `Rank` | `string` | Spell rank ("Rank 1", "Rank 2", etc.) | ✅ |
| `IsValid` | `bool` | True if spell data loaded successfully | ✅ |

### Level Requirements

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `BaseLevel` | `uint` | Base level requirement | ✅ |
| `Level` | `uint` | Spell level (for scaling) | ✅ |
| `MaxTargets` | `uint` | Maximum number of targets | ✅ |

---

## Range & Targeting

### Range

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `MinRange` | `float` | Minimum casting range (yards) | ✅ |
| `MaxRange` | `float` | Maximum casting range (yards) | ✅ |
| `HasRange` | `bool` | True if spell has a range | ✅ |
| `RangeDescription` | `string` | Range description ("40 yards", "Melee", etc.) | ✅ |
| `SpellRangeId` | `uint` | Range index from `SpellRange.dbc` | ✅ |

```csharp
WoWSpell fireball = WoWSpell.FromId(133);

Console.WriteLine($"Min Range: {fireball.MinRange} yards");
Console.WriteLine($"Max Range: {fireball.MaxRange} yards");
Console.WriteLine($"Description: {fireball.RangeDescription}");
```

---

## Cost & Resources

### Power Cost

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `PowerCost` | `int` | Flat power cost | ✅ |
| `ManaCostPercent` | `uint` | Mana cost as percentage of base mana | ✅ |
| `PowerType` | `WoWPowerType` | Power type required (Mana, Rage, Energy, etc.) | ✅ |

```csharp
WoWSpell spell = WoWSpell.FromId(686); // Shadow Bolt

Console.WriteLine($"Power Type: {spell.PowerType}");
Console.WriteLine($"Power Cost: {spell.PowerCost}");
Console.WriteLine($"Mana %: {spell.ManaCostPercent}%");
```

**WoWPowerType Enum:**
```csharp
public enum WoWPowerType
{
    Mana = 0,
    Rage = 1,
    Focus = 2,
    Energy = 3,
    Happiness = 4,     // WotLK only (pet happiness)
    Rune = 5,          // Death Knight runes
    RunicPower = 6     // Death Knight runic power
}
```

---

## Cast Time & Cooldown

### Cast Time

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `CastTime` | `uint` | Base cast time in milliseconds | ✅ |
| `IsChanneled` | `bool` | True if spell is channeled | ✅ |
| `IsFunnel` | `bool` | True if spell is funneled (channeled to target) | ✅ |

```csharp
if (spell.IsChanneled)
{
    Console.WriteLine($"Channeled spell, duration: {spell.CastTime}ms");
}
else if (spell.CastTime > 0)
{
    Console.WriteLine($"Cast time: {spell.CastTime / 1000.0:F1}s");
}
else
{
    Console.WriteLine("Instant cast");
}
```

### Cooldown

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Cooldown` | `bool` | True if spell is currently on cooldown | ✅ |
| `CooldownTimeLeft` | `TimeSpan` | Time remaining on cooldown | ✅ |
| `BaseCooldown` | `uint` | Base cooldown in milliseconds | ✅ |
| `Category` | `uint` | Spell category (for shared cooldowns) | ✅ |

```csharp
WoWSpell spell = WoWSpell.FromId(2136); // Fire Blast

if (spell.Cooldown)
{
    Console.WriteLine($"On cooldown for {spell.CooldownTimeLeft.TotalSeconds:F1}s");
}
else
{
    Console.WriteLine("Ready to cast");
}

Console.WriteLine($"Base cooldown: {spell.BaseCooldown / 1000.0:F1}s");
```

---

## Duration

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `BaseDuration` | `int` | Base duration in milliseconds | ✅ |
| `DurationPerLevel` | `int` | Duration increase per caster level | ✅ |
| `MaxDuration` | `int` | Maximum duration in milliseconds | ✅ |

```csharp
WoWSpell polymorph = WoWSpell.FromId(118);

Console.WriteLine($"Base Duration: {polymorph.BaseDuration / 1000}s");
Console.WriteLine($"Max Duration: {polymorph.MaxDuration / 1000}s");
```

---

## Spell Effects

### Properties

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `SpellEffect1` | `SpellEffect` | First spell effect | ✅ |
| `SpellEffect2` | `SpellEffect` | Second spell effect | ✅ |
| `SpellEffect3` | `SpellEffect` | Third spell effect | ✅ |
| `SpellEffects` | `SpellEffect[]` | All spell effects (array of 3) | ✅ |

### `SpellEffect GetSpellEffect(int index)`

Gets a specific spell effect by index (0-2).

```csharp
WoWSpell spell = WoWSpell.FromId(5782); // Fear

SpellEffect effect1 = spell.GetSpellEffect(0);
Console.WriteLine($"Effect 1 Aura: {effect1.AuraType}");
Console.WriteLine($"Base Points: {effect1.BasePoints}");
Console.WriteLine($"Mechanic: {effect1.Mechanic}");
```

### SpellEffect Class

| Property | Type | Description |
|----------|------|-------------|
| `AuraType` | `WoWApplyAuraType` | Aura effect type |
| `BasePoints` | `int` | Base effect value |
| `RealPointsPerLevel` | `float` | Value per caster level |
| `Mechanic` | `int` | Spell mechanic |
| `TargetA` | `uint` | Primary target type |
| `TargetB` | `uint` | Secondary target type |
| `RadiusIndex` | `uint` | Effect radius index |
| `Amplitude` | `uint` | Tick interval for periodic effects |
| `MultipleValue` | `float` | Multiplicative value |
| `ChainTargets` | `uint` | Number of chain targets |
| `ItemType` | `uint` | Created item type |
| `MiscValueA` | `int` | Miscellaneous value A |
| `MiscValueB` | `int` | Miscellaneous value B |
| `TriggerSpell` | `uint` | Triggered spell ID |
| `PointsPerComboPoint` | `float` | Value per combo point |
| `ClassMask` | `ulong[]` | Spell class mask (3 elements) |

---

## School & Mechanics

### Spell School

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `School` | `WoWSpellSchool` | Spell school (Physical, Fire, Frost, etc.) | ✅ |

```csharp
WoWSpell frostbolt = WoWSpell.FromId(116);

switch (frostbolt.School)
{
    case WoWSpellSchool.Frost:
        Console.WriteLine("Frost spell");
        break;
    case WoWSpellSchool.Fire:
        Console.WriteLine("Fire spell");
        break;
    // ...
}
```

**WoWSpellSchool Enum:**
```csharp
[Flags]
public enum WoWSpellSchool
{
    Physical = 0x1,
    Holy = 0x2,
    Fire = 0x4,
    Nature = 0x8,
    Frost = 0x10,
    Shadow = 0x20,
    Arcane = 0x40
}
```

### Dispel Type

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `DispelType` | `WoWDispelType` | Dispel type (Magic, Curse, Disease, etc.) | ✅ |

```csharp
public enum WoWDispelType
{
    None = 0,
    Magic = 1,
    Curse = 2,
    Disease = 3,
    Poison = 4,
    Stealth = 5,
    Invisibility = 6,
    All = 7,
    Enrage = 9
}
```

### Mechanic

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Mechanic` | `WoWSpellMechanic` | Spell mechanic (Stun, Fear, Root, etc.) | ✅ |

```csharp
public enum WoWSpellMechanic
{
    None = 0,
    Charm = 1,
    Disoriented = 2,
    Disarm = 3,
    Distract = 4,
    Fear = 5,
    Grip = 6,
    Root = 7,
    SlowAttack = 8,
    Silence = 9,
    Sleep = 10,
    Snare = 11,
    Stun = 12,
    Freeze = 13,
    Knockout = 14,
    Bleed = 15,
    Bandage = 16,
    Polymorph = 17,
    Banish = 18,
    Shield = 19,
    Shackle = 20,
    Mount = 21,
    Infected = 22,
    Turn = 23,
    Horror = 24,
    Invulnerability = 25,
    Interrupt = 26,
    Daze = 27,
    Discovery = 28,
    ImmuneShield = 29,
    Sapped = 30,
    Enraged = 31
}
```

---

## Targeting

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `TargetType` | `WoWCreatureType` | Target creature type filter | ✅ |
| `CreatesItemId` | `int` | Item ID created by this spell | ✅ |

---

## Stacking & Charges

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `MaxStackCount` | `uint` | Maximum number of stacks | ✅ |

```csharp
WoWSpell spell = WoWSpell.FromId(48441); // Rejuvenation

Console.WriteLine($"Max stacks: {spell.MaxStackCount}");
```

---

## Usability

### `bool CanCast`

Checks if the spell can be cast by the current player (has reagents, enough power, meets conditions).

```csharp
WoWSpell spell = WoWSpell.FromId(133); // Fireball

if (spell.CanCast && !spell.Cooldown)
{
    spell.Cast();
}
```

---

## Casting

### `void Cast()`

Casts the spell.

```csharp
WoWSpell frostbolt = WoWSpell.FromId(116);

if (frostbolt.CanCast && !frostbolt.Cooldown)
{
    frostbolt.Cast();
}
```

!!! note "SpellManager Alternative"
    For more control, use `SpellManager.CastSpellById(id)` or `SpellManager.Cast(name)`.

---

## Additional Properties

### Tooltip

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Tooltip` | `string` | Spell tooltip text | ✅ |

---

## Internal Information

### `SpellEntry InternalInfo`

Gets the raw `SpellEntry` structure from `Spell.dbc`.

```csharp
SpellEntry entry = spell.InternalInfo;
// Access low-level spell data
```

---

## Complete Examples

### Example 1: Spell Information Display

```csharp
using Styx.Logic.Combat;

void DisplaySpellInfo(int spellId)
{
    WoWSpell spell = WoWSpell.FromId(spellId);
    
    if (spell == null || !spell.IsValid)
    {
        Console.WriteLine($"Spell {spellId} not found");
        return;
    }
    
    Console.WriteLine($"=== {spell.Name} {spell.Rank} ===");
    Console.WriteLine($"ID: {spell.Id}");
    Console.WriteLine($"School: {spell.School}");
    Console.WriteLine($"Power Type: {spell.PowerType}");
    Console.WriteLine($"Power Cost: {spell.PowerCost} ({spell.ManaCostPercent}%)");
    Console.WriteLine($"Range: {spell.MinRange}-{spell.MaxRange} yards");
    Console.WriteLine($"Cast Time: {spell.CastTime / 1000.0:F1}s");
    Console.WriteLine($"Cooldown: {spell.BaseCooldown / 1000.0:F1}s");
    Console.WriteLine($"Duration: {spell.BaseDuration / 1000.0:F1}s");
    Console.WriteLine($"Max Stacks: {spell.MaxStackCount}");
    
    if (spell.IsChanneled)
        Console.WriteLine("Channeled spell");
    
    // Display effects
    Console.WriteLine("\nEffects:");
    for (int i = 0; i < 3; i++)
    {
        var effect = spell.GetSpellEffect(i);
        if (effect.AuraType != WoWApplyAuraType.None)
        {
            Console.WriteLine($"  Effect {i + 1}:");
            Console.WriteLine($"    Aura: {effect.AuraType}");
            Console.WriteLine($"    Base Points: {effect.BasePoints}");
            Console.WriteLine($"    Mechanic: {effect.Mechanic}");
        }
    }
}

// Usage
DisplaySpellInfo(133); // Fireball
```

### Example 2: Check Cooldowns

```csharp
// List of important spells to track
var importantSpells = new[]
{
    2136,  // Fire Blast
    122,   // Frost Nova
    1953,  // Blink
    2139   // Counterspell
};

Console.WriteLine("=== Cooldown Status ===");
foreach (int spellId in importantSpells)
{
    var spell = WoWSpell.FromId(spellId);
    if (spell != null && spell.IsValid)
    {
        if (spell.Cooldown)
        {
            Console.WriteLine($"{spell.Name}: {spell.CooldownTimeLeft.TotalSeconds:F1}s remaining");
        }
        else
        {
            Console.WriteLine($"{spell.Name}: READY");
        }
    }
}
```

### Example 3: Find Spells by School

```csharp
using System.Linq;

// Find all Frost spells known by player
var frostSpells = SpellManager.Spells
    .Select(name => WoWSpell.FromId(SpellManager.GetSpellId(name)))
    .Where(spell => spell != null && spell.School == WoWSpellSchool.Frost)
    .ToList();

Console.WriteLine("=== Frost Spells ===");
foreach (var spell in frostSpells)
{
    Console.WriteLine($"{spell.Name} {spell.Rank}");
    Console.WriteLine($"  Cost: {spell.PowerCost} {spell.PowerType}");
    Console.WriteLine($"  Range: {spell.MaxRange} yards");
    Console.WriteLine($"  Cast: {spell.CastTime / 1000.0:F1}s");
}
```

### Example 4: Spell Comparison

```csharp
void CompareSpellRanks(int spellId1, int spellId2)
{
    var spell1 = WoWSpell.FromId(spellId1);
    var spell2 = WoWSpell.FromId(spellId2);
    
    Console.WriteLine($"Comparing {spell1.Name} {spell1.Rank} vs {spell2.Name} {spell2.Rank}");
    Console.WriteLine($"Cost: {spell1.PowerCost} vs {spell2.PowerCost}");
    Console.WriteLine($"Cast Time: {spell1.CastTime}ms vs {spell2.CastTime}ms");
    Console.WriteLine($"Max Range: {spell1.MaxRange}y vs {spell2.MaxRange}y");
    
    var effect1 = spell1.GetSpellEffect(0);
    var effect2 = spell2.GetSpellEffect(0);
    Console.WriteLine($"Base Damage: {effect1.BasePoints} vs {effect2.BasePoints}");
}

// Compare Frostbolt ranks
CompareSpellRanks(116, 205); // Frostbolt Rank 1 vs Rank 2
```

### Example 5: Spell Effect Analysis

```csharp
void AnalyzeSpellEffects(int spellId)
{
    var spell = WoWSpell.FromId(spellId);
    
    Console.WriteLine($"=== {spell.Name} Effects ===");
    
    for (int i = 0; i < 3; i++)
    {
        var effect = spell.GetSpellEffect(i);
        
        if (effect.AuraType == WoWApplyAuraType.None)
            continue;
        
        Console.WriteLine($"\nEffect {i + 1}:");
        Console.WriteLine($"  Aura Type: {effect.AuraType}");
        Console.WriteLine($"  Base Points: {effect.BasePoints}");
        Console.WriteLine($"  Points per Level: {effect.RealPointsPerLevel}");
        Console.WriteLine($"  Mechanic: {effect.Mechanic}");
        Console.WriteLine($"  Target A: {effect.TargetA}");
        Console.WriteLine($"  Target B: {effect.TargetB}");
        Console.WriteLine($"  Radius: {effect.RadiusIndex}");
        
        if (effect.Amplitude > 0)
            Console.WriteLine($"  Tick Interval: {effect.Amplitude}ms");
        
        if (effect.ChainTargets > 0)
            Console.WriteLine($"  Chain Targets: {effect.ChainTargets}");
        
        if (effect.TriggerSpell > 0)
        {
            var triggered = WoWSpell.FromId((int)effect.TriggerSpell);
            Console.WriteLine($"  Triggers: {triggered?.Name ?? $"Spell {effect.TriggerSpell}"}");
        }
    }
}

// Example: Analyze Immolate
AnalyzeSpellEffects(348);
```

---

## Common Spell IDs (WotLK)

### Mage

| Name | ID | Rank |
|------|-----|------|
| Frostbolt | 116 | 1 |
| Frostbolt | 42842 | 16 (Max) |
| Fireball | 133 | 1 |
| Fireball | 42833 | 16 (Max) |
| Arcane Missiles | 5143 | 1 |
| Arcane Missiles | 42846 | 13 (Max) |
| Fire Blast | 2136 | 1 |
| Fire Blast | 42873 | 11 (Max) |
| Frost Nova | 122 | 1 |
| Frost Nova | 42917 | 6 (Max) |
| Blink | 1953 | - |
| Polymorph | 118 | 1 |
| Polymorph | 12826 | 4 (Max) |
| Counterspell | 2139 | - |
| Ice Block | 45438 | - |

### Warlock

| Name | ID | Rank |
|------|-----|------|
| Shadow Bolt | 686 | 1 |
| Shadow Bolt | 47809 | 13 (Max) |
| Immolate | 348 | 1 |
| Immolate | 47811 | 11 (Max) |
| Corruption | 172 | 1 |
| Corruption | 47813 | 10 (Max) |
| Fear | 5782 | 1 |
| Fear | 6215 | 3 (Max) |
| Life Tap | 1454 | 1 |
| Life Tap | 57946 | 8 (Max) |

---

## See Also

- [WoWAura](wowaura.md) - Active spell effects on units
- [SpellManager](spellmanager.md) - Casting and spell management
- [WoWUnit](../wowobjects/wowunit.md) - Unit auras and debuffs
- [CustomClass](customclass.md) - Using spells in combat routines
