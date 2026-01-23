# WoWAura Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - Aura memory structure and all properties verified for WotLK 3.3.5a (build 12340).

The `WoWAura` class represents an active buff or debuff on a unit. It provides access to spell information, remaining time, stack count, caster, and aura flags.

## Namespace

```csharp
using Styx.Logic.Combat;
```

## Overview

`WoWAura` represents active spell effects (auras) on units. Unlike `WoWSpell`, which contains static spell database information, `WoWAura` represents live aura instances with dynamic properties like remaining duration and stack count.

!!! tip "Finding Auras"
    Access auras through `WoWUnit.Auras`, `WoWUnit.ActiveAuras`, `WoWUnit.Debuffs`, or `WoWUnit.HasAura()`.

---

## Core Properties

### Identifiers

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `SpellId` | `int` | Spell ID of the aura | ✅ |
| `Name` | `string` | Aura display name | ✅ |
| `Rank` | `string` | Spell rank ("Rank 1", "Rank 2", etc.) | ✅ |
| `Spell` | `WoWSpell?` | The spell object for this aura | ✅ |

### Caster

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `CreatorGuid` | `ulong` | GUID of the unit that applied this aura | ✅ |
| `HasCaster` | `bool` | True if aura has a known caster (not item/NPC buff) | ✅ |

### Stack & Level

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `StackCount` | `uint` | Number of aura stacks (1+ for stackable auras) | ✅ |
| `Level` | `int` | Level at which the aura was applied | ✅ |

---

## Duration & Timing

### Time Remaining

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `TimeLeft` | `TimeSpan` | Time remaining on the aura | ✅ |
| `TimeLeftMs` | `uint` | Time remaining in milliseconds | ✅ |
| `Duration` | `uint` | Total duration in milliseconds | ✅ |
| `EndTime` | `uint` | Performance counter value when aura expires | ✅ |
| `HasNoDuration` | `bool` | True if aura is permanent (until cancelled/removed) | ✅ |

```csharp
WoWAura aura = unit.GetAuraByName("Rejuvenation");

if (aura != null)
{
    if (aura.HasNoDuration)
    {
        Console.WriteLine("Permanent aura");
    }
    else
    {
        Console.WriteLine($"Time remaining: {aura.TimeLeft.TotalSeconds:F1}s");
        Console.WriteLine($"Total duration: {aura.Duration / 1000.0:F1}s");
    }
}
```

!!! warning "Performance Counter"
    `EndTime` is based on WoW's internal performance counter (`ObjectManager.PerformanceCounter`), not system time.

---

## Aura Flags

### `AuraFlags Flags`

Raw aura flags bitfield.

```csharp
if ((aura.Flags & WoWAura.AuraFlags.Harmful) != 0)
{
    Console.WriteLine("Harmful aura (debuff)");
}
```

### Flag Properties

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `IsHarmful` | `bool` | Aura is a debuff | ✅ |
| `IsActive` | `bool` | At least one effect is active | ✅ |
| `IsPassive` | `bool` | Aura is passive (commonly Unknown flag) | ✅ |
| `Cancellable` | `bool` | Player can cancel/remove this aura | ✅ |

**AuraFlags Enum:**
```csharp
[Flags]
public enum AuraFlags : byte
{
    None = 0,
    FirstEffect = 1,           // First spell effect active
    SecondEffect = 2,          // Second spell effect active
    ThirdEffect = 4,           // Third spell effect active
    AnyEffectActive = 7,       // Any effect active (1|2|4)
    NoCaster = 8,              // No caster (item/NPC buff)
    Cancellable = 16,          // Can be cancelled by player
    NoDuration = 32,           // Permanent aura
    Unknown = 64,              // Often indicates passive
    Harmful = 128              // Debuff
}
```

---

## Aura Type

### `WoWApplyAuraType ApplyAuraType`

Gets the apply aura type from the first spell effect.

```csharp
var auraType = aura.ApplyAuraType;

switch (auraType)
{
    case WoWApplyAuraType.ModStat:
        Console.WriteLine("Stat modifier aura");
        break;
    case WoWApplyAuraType.PeriodicDamage:
        Console.WriteLine("Damage over time");
        break;
    case WoWApplyAuraType.PeriodicHeal:
        Console.WriteLine("Heal over time");
        break;
    // ...
}
```

---

## Methods

### `bool TryCancel()`

Attempts to cancel the aura (if cancellable and not harmful).

```csharp
WoWAura buff = StyxWoW.Me.GetAuraByName("Blessing of Might");

if (buff != null && buff.Cancellable && !buff.IsHarmful)
{
    if (buff.TryCancel())
    {
        Console.WriteLine("Buff cancelled successfully");
    }
}
```

**Returns:**
- `true` if aura was cancelled
- `false` if aura is not cancellable, is harmful, or cancellation failed

!!! warning "Harmful Auras"
    `TryCancel()` will NOT attempt to remove harmful auras (debuffs). Those require dispel mechanics.

---

## Equality

`WoWAura` implements `IEquatable<WoWAura>` and compares by `SpellId`.

```csharp
WoWAura aura1 = unit.GetAuraByName("Frostbolt");
WoWAura aura2 = unit.GetAuraById(116);

if (aura1 == aura2)
{
    Console.WriteLine("Same aura");
}
```

---

## Memory Structure

### Internal AuraInfo Structure (WotLK 3.3.5a)

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 24)]
internal struct AuraInfo
{
    public ulong CreatorGuid;    // 0x00 (8 bytes)
    public uint SpellId;         // 0x08 (4 bytes)
    public byte Flags;           // 0x0C (1 byte)
    public byte StackCount;      // 0x0D (1 byte)
    public ushort Level;         // 0x0E (2 bytes, padding)
    public uint Duration;        // 0x10 (4 bytes)
    public uint EndTime;         // 0x14 (4 bytes)
}
```

**Total Size:** 24 bytes per aura

!!! info "Advanced Usage"
    `WoWAura.FromAddress(uint address)` creates an aura from a memory address. Used internally by `WoWUnit`.

---

## Complete Examples

### Example 1: Check Aura Status

```csharp
using Styx;
using Styx.Logic.Combat;

void CheckAuraStatus(WoWUnit unit, string auraName)
{
    var aura = unit.GetAuraByName(auraName);
    
    if (aura == null)
    {
        Console.WriteLine($"{auraName}: Not present");
        return;
    }
    
    Console.WriteLine($"=== {aura.Name} {aura.Rank} ===");
    Console.WriteLine($"Spell ID: {aura.SpellId}");
    Console.WriteLine($"Stacks: {aura.StackCount}");
    Console.WriteLine($"Level: {aura.Level}");
    
    if (aura.HasCaster)
    {
        Console.WriteLine($"Caster GUID: {aura.CreatorGuid:X16}");
    }
    else
    {
        Console.WriteLine("No caster (item/NPC buff)");
    }
    
    if (aura.HasNoDuration)
    {
        Console.WriteLine("Duration: Permanent");
    }
    else
    {
        Console.WriteLine($"Time left: {aura.TimeLeft.TotalSeconds:F1}s");
        Console.WriteLine($"Total duration: {aura.Duration / 1000.0:F1}s");
    }
    
    Console.WriteLine($"Harmful: {aura.IsHarmful}");
    Console.WriteLine($"Cancellable: {aura.Cancellable}");
    Console.WriteLine($"Passive: {aura.IsPassive}");
}

// Usage
CheckAuraStatus(StyxWoW.Me, "Rejuvenation");
```

### Example 2: Monitor DoTs on Target

```csharp
void MonitorDebuffs(WoWUnit target)
{
    Console.WriteLine($"=== Debuffs on {target.Name} ===");
    
    foreach (var aura in target.Debuffs.Values)
    {
        // Only show my debuffs
        if (aura.CreatorGuid == StyxWoW.Me.Guid)
        {
            string timeStr = aura.HasNoDuration 
                ? "Permanent" 
                : $"{aura.TimeLeft.TotalSeconds:F1}s";
            
            string stackStr = aura.StackCount > 1 
                ? $"x{aura.StackCount}" 
                : "";
            
            Console.WriteLine($"  {aura.Name} {stackStr} - {timeStr}");
        }
    }
}

// Usage
WoWUnit target = StyxWoW.Me.CurrentTarget;
if (target != null)
{
    MonitorDebuffs(target);
}
```

### Example 3: Refresh DoTs Before Expiry

```csharp
using System.Collections.Generic;

// DoTs to maintain with minimum time before refresh
var dotsToMaintain = new Dictionary<string, double>
{
    { "Corruption", 3.0 },      // Refresh if < 3s remaining
    { "Immolate", 2.0 },        // Refresh if < 2s remaining
    { "Curse of Agony", 4.0 }   // Refresh if < 4s remaining
};

void RefreshDoTs(WoWUnit target)
{
    foreach (var kvp in dotsToMaintain)
    {
        string dotName = kvp.Key;
        double refreshTime = kvp.Value;
        
        var aura = target.GetAuraByName(dotName);
        
        if (aura == null)
        {
            Console.WriteLine($"Applying {dotName}");
            SpellManager.Cast(dotName, target);
            return;
        }
        
        // Check if my DoT
        if (aura.CreatorGuid == StyxWoW.Me.Guid)
        {
            if (!aura.HasNoDuration && aura.TimeLeft.TotalSeconds < refreshTime)
            {
                Console.WriteLine($"Refreshing {dotName} ({aura.TimeLeft.TotalSeconds:F1}s left)");
                SpellManager.Cast(dotName, target);
                return;
            }
        }
        else
        {
            // Not my DoT, reapply anyway
            Console.WriteLine($"Applying {dotName} (overriding other player's DoT)");
            SpellManager.Cast(dotName, target);
            return;
        }
    }
}

// Usage in combat routine
if (Me.CurrentTarget != null && Me.CurrentTarget.IsHostile)
{
    RefreshDoTs(Me.CurrentTarget);
}
```

### Example 4: Remove Bad Buffs

```csharp
// List of buffs to remove
var badBuffs = new[]
{
    "Blessing of Protection",  // Prevents melee attacks
    "Divine Intervention",      // Prevents all actions
    "Polymorph"                // CC effect
};

void RemoveBadBuffs()
{
    foreach (string buffName in badBuffs)
    {
        var aura = StyxWoW.Me.GetAuraByName(buffName);
        
        if (aura != null)
        {
            Console.WriteLine($"Removing {buffName}");
            
            if (aura.TryCancel())
            {
                Console.WriteLine($"  Successfully removed {buffName}");
            }
            else
            {
                Console.WriteLine($"  Failed to remove {buffName} (not cancellable)");
            }
        }
    }
}

// Call periodically in combat
RemoveBadBuffs();
```

### Example 5: Track HoT/DoT Ticks

```csharp
using System;
using Styx.Logic.Combat;

void TrackPeriodicEffects(WoWUnit unit)
{
    Console.WriteLine($"=== Periodic Effects on {unit.Name} ===");
    
    foreach (var aura in unit.ActiveAuras)
    {
        // Get spell to check for periodic effects
        var spell = aura.Spell;
        if (spell == null) continue;
        
        // Check all three effects
        for (int i = 0; i < 3; i++)
        {
            var effect = spell.GetSpellEffect(i);
            
            // Check if periodic
            bool isPeriodic = effect.AuraType == WoWApplyAuraType.PeriodicDamage ||
                             effect.AuraType == WoWApplyAuraType.PeriodicHeal ||
                             effect.AuraType == WoWApplyAuraType.PeriodicEnergize ||
                             effect.AuraType == WoWApplyAuraType.PeriodicLeech;
            
            if (isPeriodic && effect.Amplitude > 0)
            {
                double tickInterval = effect.Amplitude / 1000.0;
                int totalTicks = aura.HasNoDuration 
                    ? -1 
                    : (int)(aura.Duration / effect.Amplitude);
                
                int remainingTicks = aura.HasNoDuration 
                    ? -1 
                    : (int)(aura.TimeLeftMs / effect.Amplitude);
                
                Console.WriteLine($"{aura.Name}:");
                Console.WriteLine($"  Type: {effect.AuraType}");
                Console.WriteLine($"  Tick Interval: {tickInterval:F1}s");
                
                if (totalTicks >= 0)
                    Console.WriteLine($"  Ticks: {remainingTicks}/{totalTicks} remaining");
                else
                    Console.WriteLine($"  Ticks: Infinite");
                
                Console.WriteLine($"  Base Value: {effect.BasePoints}");
                Console.WriteLine($"  Per Tick: {effect.BasePoints / (totalTicks > 0 ? totalTicks : 1)}");
            }
        }
    }
}

// Usage
TrackPeriodicEffects(StyxWoW.Me.CurrentTarget);
```

### Example 6: Dispel Priority System

```csharp
using System.Linq;

// Debuff priority for dispelling (higher = more important)
var debuffPriority = new Dictionary<string, int>
{
    { "Polymorph", 100 },
    { "Fear", 95 },
    { "Curse of Tongues", 90 },
    { "Curse of Weakness", 80 },
    { "Slow", 70 },
    { "Frostbolt", 60 }
};

void DispelPriority(WoWUnit unit)
{
    // Get all dispellable debuffs
    var dispellableDebuffs = unit.Debuffs.Values
        .Where(aura => aura.IsHarmful)
        .Where(aura => aura.Spell != null)
        .Where(aura => IsDispellable(aura.Spell.DispelType))
        .OrderByDescending(aura => GetPriority(aura.Name))
        .ToList();
    
    if (dispellableDebuffs.Any())
    {
        var highestPriority = dispellableDebuffs.First();
        Console.WriteLine($"Dispelling: {highestPriority.Name} (Priority: {GetPriority(highestPriority.Name)})");
        
        // Cast appropriate dispel spell
        var dispelType = highestPriority.Spell.DispelType;
        string dispelSpell = GetDispelSpell(dispelType);
        
        if (!string.IsNullOrEmpty(dispelSpell))
        {
            SpellManager.Cast(dispelSpell, unit);
        }
    }
}

int GetPriority(string auraName)
{
    return debuffPriority.ContainsKey(auraName) ? debuffPriority[auraName] : 0;
}

bool IsDispellable(WoWDispelType type)
{
    // Check if player can dispel this type
    switch (type)
    {
        case WoWDispelType.Magic:
            return SpellManager.HasSpell("Remove Curse") || SpellManager.HasSpell("Dispel Magic");
        case WoWDispelType.Curse:
            return SpellManager.HasSpell("Remove Curse");
        case WoWDispelType.Disease:
            return SpellManager.HasSpell("Cure Disease");
        case WoWDispelType.Poison:
            return SpellManager.HasSpell("Cure Poison");
        default:
            return false;
    }
}

string GetDispelSpell(WoWDispelType type)
{
    switch (type)
    {
        case WoWDispelType.Magic:
            return "Dispel Magic";
        case WoWDispelType.Curse:
            return "Remove Curse";
        case WoWDispelType.Disease:
            return "Cure Disease";
        case WoWDispelType.Poison:
            return "Cure Poison";
        default:
            return null;
    }
}

// Usage
DispelPriority(StyxWoW.Me);
```

---

## Common Aura Patterns

### Check for Crowd Control

```csharp
bool IsCrowdControlled(WoWUnit unit)
{
    return unit.HasAura("Polymorph") ||
           unit.HasAura("Fear") ||
           unit.HasAura("Hibernate") ||
           unit.HasAura("Sap") ||
           unit.HasAura("Hex") ||
           unit.Stunned ||
           unit.Rooted;
}
```

### Check for Immunity

```csharp
bool IsImmune(WoWUnit unit)
{
    return unit.HasAura("Ice Block") ||
           unit.HasAura("Divine Shield") ||
           unit.HasAura("Hand of Protection") ||
           unit.HasAura("Anti-Magic Shell");
}
```

### Track Proc Buffs

```csharp
// Check for proc buffs before using cooldowns
bool HasDamageBuff()
{
    return StyxWoW.Me.HasAura("Bloodlust") ||
           StyxWoW.Me.HasAura("Heroism") ||
           StyxWoW.Me.HasAura("Icy Veins") ||
           StyxWoW.Me.HasAura("Arcane Power");
}

if (HasDamageBuff())
{
    // Use damage cooldowns
    SpellManager.Cast("Mirror Image");
}
```

---

## See Also

- [WoWSpell](wowspell.md) - Spell database information
- [WoWUnit](../wowobjects/wowunit.md) - Unit auras and debuffs
- [SpellManager](spellmanager.md) - Casting and dispelling
- [CustomClass](customclass.md) - Using auras in combat routines
