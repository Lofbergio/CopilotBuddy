# SpellManager Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - All casting methods, cooldown detection, and GCD tracking verified for WotLK 3.3.5a (build 12340).

The `SpellManager` class provides high-level spell casting, cooldown management, and spell availability checking. This is the primary interface for casting spells in combat routines.

## Namespace

```csharp
using Styx.Logic.Combat;
```

## Overview

`SpellManager` is a static class that manages the player's known spells, tracks cooldowns, handles casting, and provides convenience methods for spell management.

!!! tip "Primary Casting Interface"
    Always use `SpellManager` for casting spells in combat routines instead of directly calling Lua functions.

---

## Spell Dictionary

### `Dictionary<string, WoWSpell> Spells`

Dictionary of all known spells, indexed by spell name (case-insensitive).

```csharp
// Check if spell exists in dictionary
if (SpellManager.Spells.ContainsKey("Frostbolt"))
{
    WoWSpell frostbolt = SpellManager.Spells["Frostbolt"];
    Console.WriteLine($"Frostbolt ID: {frostbolt.Id}");
}
```

**Aliases:**
- `KnownSpells` - Same as `Spells`
- `RawSpells` - Same as `Spells` (HB 4.3.4 compatibility)

### `int NumKnownSpells`

Gets the total number of spells known by the player.

```csharp
Console.WriteLine($"Player knows {SpellManager.NumKnownSpells} spells");
```

---

## Initialization

### `void Refresh()`

Refreshes the spell dictionary from the player's known spells.

```csharp
SpellManager.Refresh();
Console.WriteLine($"Loaded {SpellManager.Spells.Count} spells");
```

!!! note "Auto-Refresh"
    `SpellManager` automatically refreshes when the spell count changes (new spell learned, etc.).

---

## Spell Availability

### `bool HasSpell(string name)`

Checks if the player knows a spell by name.

```csharp
if (SpellManager.HasSpell("Frostbolt"))
{
    Console.WriteLine("Player knows Frostbolt");
}
```

### `bool HasSpell(int spellId)`

Checks if the player knows a spell by ID.

```csharp
if (SpellManager.HasSpell(116)) // Frostbolt ID
{
    Console.WriteLine("Player knows Frostbolt");
}
```

### `WoWSpell? GetSpellByName(string name)`

Gets a spell object by name.

```csharp
WoWSpell? spell = SpellManager.GetSpellByName("Fire Blast");
if (spell != null)
{
    Console.WriteLine($"Fire Blast range: {spell.MaxRange} yards");
}
```

---

## Casting State

### `bool CastBarVisible`

True if the player is currently casting (cast bar visible).

```csharp
if (SpellManager.CastBarVisible)
{
    Console.WriteLine("Currently casting...");
}
```

### `bool GlobalCooldown`

True if the global cooldown (GCD) is active.

```csharp
if (SpellManager.GlobalCooldown)
{
    Console.WriteLine("GCD active, cannot cast");
}
```

### `TimeSpan GlobalCooldownLeft`

Time remaining on the global cooldown.

```csharp
TimeSpan gcdLeft = SpellManager.GlobalCooldownLeft;
if (gcdLeft > TimeSpan.Zero)
{
    Console.WriteLine($"GCD: {gcdLeft.TotalMilliseconds:F0}ms remaining");
}
```

---

## Spell Casting

### Basic Casting

#### `bool Cast(string spellName)`

Casts a spell by name.

```csharp
if (SpellManager.Cast("Frostbolt"))
{
    Console.WriteLine("Cast Frostbolt");
}
```

**Returns:** `true` if cast was attempted, `false` if spell cannot be cast

#### `bool Cast(string spellName, WoWUnit target)`

Casts a spell on a specific target.

```csharp
WoWUnit enemy = StyxWoW.Me.CurrentTarget;
if (enemy != null)
{
    SpellManager.Cast("Polymorph", enemy);
}
```

#### `bool Cast(WoWSpell spell)`

Casts a spell using a `WoWSpell` object.

```csharp
WoWSpell spell = SpellManager.GetSpellByName("Fire Blast");
if (spell != null)
{
    SpellManager.Cast(spell);
}
```

### Casting by ID

#### `void CastSpellById(int spellId)`

Casts a spell by ID on yourself.

```csharp
SpellManager.CastSpellById(116); // Frostbolt
```

#### `void CastSpellById(int spellId, ulong targetGuid)`

Casts a spell by ID on a specific target.

```csharp
WoWUnit target = StyxWoW.Me.CurrentTarget;
SpellManager.CastSpellById(118, target.Guid); // Polymorph
```

#### `void CastSpellById(int spellId, WoWUnit target)`

Casts a spell by ID on a target unit.

```csharp
SpellManager.CastSpellById(2136, StyxWoW.Me.CurrentTarget); // Fire Blast
```

---

## Can Cast Checks

### `bool CanCast(string spellName)`

Checks if a spell can be cast right now.

```csharp
if (SpellManager.CanCast("Frostbolt"))
{
    SpellManager.Cast("Frostbolt");
}
```

**Checks performed:**
- Spell is known
- Not on global cooldown
- Spell not on cooldown
- Player not casting

### `bool CanCast(string spellName, WoWUnit target, bool checkRange = true, bool checkMovement = false)`

Checks if a spell can be cast on a specific target.

```csharp
WoWUnit enemy = StyxWoW.Me.CurrentTarget;
if (enemy != null && SpellManager.CanCast("Frostbolt", enemy, checkRange: true))
{
    SpellManager.Cast("Frostbolt", enemy);
}
```

**Parameters:**
- `spellName` - Name of the spell
- `target` - Target unit
- `checkRange` - Check if target is in range (future implementation)
- `checkMovement` - Check if player movement prevents casting (future implementation)

### `bool CanCast(int spellId, WoWUnit target, bool checkRange = true, bool checkMovement = false)`

Checks if a spell can be cast by ID.

```csharp
if (SpellManager.CanCast(133, StyxWoW.Me.CurrentTarget)) // Fireball
{
    SpellManager.CastSpellById(133);
}
```

### `bool CanCastSpell(string name)`

Low-level check if spell can be cast (used internally by `CanCast`).

```csharp
if (SpellManager.CanCastSpell("Counterspell"))
{
    // Spell is available
}
```

---

## Advanced Casting

### `bool CastableSpell(WoWSpell spell)`

Checks if a spell is castable considering resources, movement, and effects.

```csharp
WoWSpell spell = SpellManager.Spells["Frostbolt"];
if (SpellManager.CastableSpell(spell))
{
    Console.WriteLine("Frostbolt is castable");
}
```

**Advanced checks:**
- Power cost vs available power
- Cast time vs player movement
- Spell effects (stealth, feign death, etc.)
- Rune availability (Death Knights)

### `void StopCasting()`

Stops the current cast.

```csharp
// Interrupt cast if enemy starts casting
if (enemy.IsCasting && !SpellManager.GlobalCooldown)
{
    SpellManager.StopCasting();
    SpellManager.Cast("Counterspell", enemy);
}
```

---

## Spell Casting Helpers

### `bool CastSpell(string name, ulong targetGuid, bool returnImmediately)`

Advanced spell casting with wait control.

```csharp
// Cast and wait for cast to complete
SpellManager.CastSpell("Pyroblast", 0, returnImmediately: false);

// Cast and return immediately
SpellManager.CastSpell("Instant Spell", 0, returnImmediately: true);
```

**Parameters:**
- `name` - Spell name
- `targetGuid` - Target GUID (0 for self)
- `returnImmediately` - If false, waits for cast time + GCD

---

## Terrain Clicking

### `bool ClickRemoteLocation(WoWPoint location)`

Clicks a remote location (used for ground-target spells).

```csharp
// Cast Blizzard at a specific location
WoWPoint targetLocation = enemy.Location;
if (SpellManager.Cast("Blizzard"))
{
    // Wait for targeting cursor
    System.Threading.Thread.Sleep(100);
    SpellManager.ClickRemoteLocation(targetLocation);
}
```

!!! warning "Ground-Target Spells"
    This method is for spells like Blizzard, Consecration, etc. that require clicking a location.

---

## Complete Examples

### Example 1: Basic Combat Rotation

```csharp
using Styx;
using Styx.Logic.Combat;

void FrostMageRotation()
{
    var me = StyxWoW.Me;
    var target = me.CurrentTarget;
    
    if (target == null || !target.IsHostile || !target.IsAlive)
        return;
    
    // Wait for GCD
    if (SpellManager.GlobalCooldown)
        return;
    
    // Don't interrupt current cast
    if (me.IsCasting)
        return;
    
    // Cooldowns
    if (SpellManager.CanCast("Icy Veins"))
    {
        SpellManager.Cast("Icy Veins");
        return;
    }
    
    // Instant casts while moving
    if (me.IsMoving)
    {
        if (SpellManager.CanCast("Fire Blast"))
        {
            SpellManager.Cast("Fire Blast");
            return;
        }
        
        if (SpellManager.CanCast("Ice Lance") && target.HasAura("Fingers of Frost"))
        {
            SpellManager.Cast("Ice Lance");
            return;
        }
    }
    
    // Main filler
    if (SpellManager.CanCast("Frostbolt"))
    {
        SpellManager.Cast("Frostbolt");
    }
}
```

### Example 2: Interrupt System

```csharp
void InterruptEnemyCasts()
{
    var target = StyxWoW.Me.CurrentTarget;
    
    if (target == null || !target.IsCasting)
        return;
    
    // Check if casting important spell
    if (target.CastingSpell != null)
    {
        string spellName = target.CastingSpell.Name;
        
        // Interrupt list
        var importantSpells = new[] { "Heal", "Polymorph", "Fear", "Pyroblast" };
        
        if (importantSpells.Any(s => spellName.Contains(s)))
        {
            // Try Counterspell
            if (SpellManager.CanCast("Counterspell", target))
            {
                Console.WriteLine($"Interrupting {spellName}");
                SpellManager.Cast("Counterspell", target);
                return;
            }
        }
    }
}
```

### Example 3: Cooldown Management

```csharp
using System.Collections.Generic;

class CooldownManager
{
    private Dictionary<string, DateTime> _lastCastTimes = new();
    
    bool CanUseAbility(string spellName, int customCooldown = 0)
    {
        // Check spell availability
        if (!SpellManager.CanCast(spellName))
            return false;
        
        // Check custom cooldown
        if (customCooldown > 0 && _lastCastTimes.ContainsKey(spellName))
        {
            var timeSinceLastCast = DateTime.Now - _lastCastTimes[spellName];
            if (timeSinceLastCast.TotalSeconds < customCooldown)
            {
                return false;
            }
        }
        
        return true;
    }
    
    void UseAbility(string spellName)
    {
        if (SpellManager.Cast(spellName))
        {
            _lastCastTimes[spellName] = DateTime.Now;
        }
    }
}

// Usage
var cooldowns = new CooldownManager();

// Only use Mirror Image every 3 minutes
if (cooldowns.CanUseAbility("Mirror Image", customCooldown: 180))
{
    cooldowns.UseAbility("Mirror Image");
}
```

### Example 4: DoT Management

```csharp
using System.Linq;

void MaintainDoTs()
{
    var target = StyxWoW.Me.CurrentTarget;
    if (target == null) return;
    
    // List of DoTs with refresh thresholds
    var dots = new Dictionary<string, double>
    {
        { "Corruption", 3.0 },
        { "Immolate", 2.0 },
        { "Curse of Agony", 4.0 }
    };
    
    foreach (var kvp in dots)
    {
        string dotName = kvp.Key;
        double refreshTime = kvp.Value;
        
        var aura = target.GetAuraByName(dotName);
        
        // Not present or about to expire
        if (aura == null || 
            (aura.CreatorGuid == StyxWoW.Me.Guid && 
             aura.TimeLeft.TotalSeconds < refreshTime))
        {
            if (SpellManager.CanCast(dotName, target))
            {
                Console.WriteLine($"Applying {dotName}");
                SpellManager.Cast(dotName, target);
                return;
            }
        }
    }
}
```

### Example 5: Proc-Based Casting

```csharp
void UseProcAbilities()
{
    var me = StyxWoW.Me;
    
    // Brain Freeze proc (free Fireball)
    if (me.HasAura("Brain Freeze") && SpellManager.CanCast("Fireball"))
    {
        Console.WriteLine("Using Brain Freeze proc");
        SpellManager.Cast("Fireball");
        return;
    }
    
    // Hot Streak proc (free Pyroblast)
    if (me.HasAura("Hot Streak") && SpellManager.CanCast("Pyroblast"))
    {
        Console.WriteLine("Using Hot Streak proc");
        SpellManager.Cast("Pyroblast");
        return;
    }
    
    // Fingers of Frost proc (boosted Ice Lance)
    if (me.HasAura("Fingers of Frost") && SpellManager.CanCast("Ice Lance"))
    {
        Console.WriteLine("Using Fingers of Frost proc");
        SpellManager.Cast("Ice Lance");
        return;
    }
}
```

### Example 6: AoE Rotation

```csharp
void AoERotation()
{
    var me = StyxWoW.Me;
    
    // Count nearby enemies
    var nearbyEnemies = ObjectManager.GetObjectsOfType<WoWUnit>()
        .Where(u => u.IsHostile && u.IsAlive)
        .Where(u => u.Distance < 40)
        .ToList();
    
    int enemyCount = nearbyEnemies.Count;
    
    if (enemyCount >= 3)
    {
        Console.WriteLine($"AoE mode: {enemyCount} enemies");
        
        // Blizzard (ground target AoE)
        if (SpellManager.CanCast("Blizzard") && enemyCount >= 4)
        {
            // Calculate center of enemy group
            float avgX = nearbyEnemies.Average(e => e.X);
            float avgY = nearbyEnemies.Average(e => e.Y);
            float avgZ = nearbyEnemies.Average(e => e.Z);
            
            var targetLocation = new WoWPoint(avgX, avgY, avgZ);
            
            if (SpellManager.Cast("Blizzard"))
            {
                System.Threading.Thread.Sleep(100);
                SpellManager.ClickRemoteLocation(targetLocation);
                return;
            }
        }
        
        // Flamestrike
        if (SpellManager.CanCast("Flamestrike") && enemyCount >= 3)
        {
            var centerEnemy = nearbyEnemies
                .OrderBy(e => e.Distance)
                .First();
            
            if (SpellManager.Cast("Flamestrike"))
            {
                System.Threading.Thread.Sleep(100);
                SpellManager.ClickRemoteLocation(centerEnemy.Location);
                return;
            }
        }
        
        // Arcane Explosion (if close)
        if (SpellManager.CanCast("Arcane Explosion") && enemyCount >= 5)
        {
            int closeEnemies = nearbyEnemies.Count(e => e.Distance < 10);
            if (closeEnemies >= 3)
            {
                SpellManager.Cast("Arcane Explosion");
                return;
            }
        }
    }
}
```

---

## Internal Implementation

### Cooldown Detection (WotLK 3.3.5a)

```csharp
// Cooldown list base address
private const uint CooldownListBase = 0xD3EDB4;

// Cooldown structure
// +0: Next pointer
// +4: Prev pointer
// +16: Start time (performance counter)
// +44: Duration (milliseconds)
```

The cooldown system walks a linked list in memory to check active spell cooldowns.

### Spell_C__HandleTerrainClick

```csharp
// Function address (WoW 3.3.5a)
private const uint Spell_C__HandleTerrainClick = 0x80B740;

// Used for ground-target spell clicking
```

---

## Best Practices

### 1. Always Check Before Casting

```csharp
// GOOD
if (SpellManager.CanCast("Frostbolt"))
{
    SpellManager.Cast("Frostbolt");
}

// BAD - may fail silently
SpellManager.Cast("Frostbolt");
```

### 2. Respect Global Cooldown

```csharp
// Check GCD before every action
if (SpellManager.GlobalCooldown)
    return;

// Your rotation logic here
```

### 3. Don't Interrupt Important Casts

```csharp
// Check if casting before starting new spell
if (StyxWoW.Me.IsCasting)
{
    var currentSpell = StyxWoW.Me.CastingSpell;
    
    // Don't interrupt long casts for instant spells
    if (currentSpell != null && currentSpell.CastTime > 1500)
        return;
}
```

### 4. Use Spell Objects for Repeated Access

```csharp
// Cache spell objects
private WoWSpell? _frostbolt;

WoWSpell GetFrostbolt()
{
    return _frostbolt ?? SpellManager.GetSpellByName("Frostbolt");
}
```

---

## See Also

- [WoWSpell](wowspell.md) - Spell information
- [WoWAura](wowaura.md) - Aura/buff management
- [CustomClass](customclass.md) - Creating combat routines
- [LocalPlayer](../wowobjects/localplayer.md) - Player spell lists
