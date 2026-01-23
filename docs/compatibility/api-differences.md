# API Differences: WotLK 3.3.5a vs Cataclysm 4.3.4

Complete reference of API differences between WotLK 3.3.5a and the base HonorBuddy 4.3.4 API.

!!! warning "Critical"
    CopilotBuddy is based on HonorBuddy 4.3.4 (Cataclysm) API but adapted for **WotLK 3.3.5a build 12340**. Many Cataclysm features don't exist in WotLK.

---

## Classes

### ❌ Not Available in WotLK

| Class | Added In | Notes |
|-------|----------|-------|
| **Monk** | MoP 5.0 | Doesn't exist - all Monk references removed |
| **Demon Hunter** | Legion 7.0 | Doesn't exist |

### ✅ Available in WotLK (10 Classes)

Warrior, Paladin, Hunter, Rogue, Priest, Death Knight (WotLK), Shaman, Mage, Warlock, Druid

---

## Races

### ❌ Not Available in WotLK

| Race | Added In |
|------|----------|
| **Goblin** | Cataclysm 4.0 |
| **Worgen** | Cataclysm 4.0 |
| **Pandaren** | MoP 5.0 |

### ✅ Available in WotLK (10 Races)

**Alliance:** Human, Dwarf, Night Elf, Gnome, Draenei (TBC)  
**Horde:** Orc, Undead, Tauren, Troll, Blood Elf (TBC)

---

## Game Systems

### ✅ Available in WotLK 3.3.5a

| System | Since | Notes |
|--------|-------|-------|
| **LFG Dungeon Finder** | 3.3.0 | Random dungeons, roles (Tank/Healer/DPS) |
| **Dual Spec** | 3.1.0 | Two talent specs |
| **Vehicle System** | 3.0.2 | Vehicle mounts, siege weapons |
| **Achievements** | 3.0.2 | Achievement system |
| **Glyphs** | 3.0.2 | Prime, Major, Minor (v1) |
| **Inscription** | 3.0.2 | Profession added |
| **Heirloom Items** | 3.0.2 | BoA items |
| **Pet Happiness** | 1.0.0 | **Removed in Cata** |

### ❌ Not Available in WotLK

| System | Added In | Impact |
|--------|----------|--------|
| **Transmogrification** | Cataclysm 4.3 | No transmog APIs |
| **Void Storage** | Cataclysm 4.3 | No void storage |
| **Reforging** | Cataclysm 4.0 | No reforge stats |
| **Archaeology** | Cataclysm 4.0 | Profession doesn't exist |
| **Guild Leveling** | Cataclysm 4.0 | Old guild system |
| **Rated Battlegrounds** | Cataclysm 4.0 | No RBG queue |
| **Glyph System v2** | MoP 5.0 | WotLK uses v1 (Prime/Major/Minor) |

---

## Lua API Differences

### ❌ Functions NOT in WotLK

These Cataclysm+ Lua functions don't exist in WotLK 3.3.5a:

| Function | Workaround |
|----------|------------|
| `GetLFGRoles()` | **Use `GetRaidRosterInfo(index)`** - returns role as 10th value |
| `UnitGroupRolesAssigned(unit)` | **Use `GetRaidRosterInfo()`** |
| `GetSpecialization()` | Inspect talents directly |
| `GetSpecializationInfo()` | Not needed in WotLK |
| `IsTransmogrified()` | Transmog doesn't exist |
| `GetVoidItemInfo()` | Void storage doesn't exist |
| `GetReforgeItemInfo()` | Reforging doesn't exist |

### ✅ Functions Available in WotLK

| Function | Notes |
|----------|-------|
| `GetRaidRosterInfo(index)` | **Returns 14 values including role** (10th value) |
| `GetNumRaidMembers()` | Raid size (0-40) |
| `GetNumPartyMembers()` | Party size (0-4, excluding player) |
| `UnitAura(unit, index)` | **Returns 11 values** (Cata returns 13) |
| `GetTalentInfo(tab, index)` | Talent system (old tree-based) |
| `UnitHealth(unit)` | Health |
| `UnitPower(unit, type)` | Power (mana/rage/energy) |
| `GetSpellCooldown(spell)` | Cooldown info |

---

## LocalPlayer.Role Implementation

### ❌ Cataclysm Implementation (Doesn't Work)
```csharp
// HB 4.3.4 - Uses GetLFGRoles() which doesn't exist in WotLK
var role = Lua.GetReturnVal<int>("return GetLFGRoles()", 0);
```

### ✅ WotLK Implementation (CopilotBuddy)
```csharp
// Uses GetRaidRosterInfo() - exists since WotLK 3.3.0
for (int i = 1; i <= 40; i++)
{
    var name = Lua.GetReturnVal<string>($"local name = GetRaidRosterInfo({i}); return name", 0);
    if (name.Equals(Me.Name, StringComparison.OrdinalIgnoreCase))
    {
        var role = Lua.GetReturnVal<string>(
            $"local _,_,_,_,_,_,_,_,_,role = GetRaidRosterInfo({i}); return role or 'NONE'", 0);
        return ParseLFGRole(role); // "TANK", "HEALER", "DAMAGER"
    }
}
return GroupRole.None; // Solo or normal 5-man party
```

**Key Points:**
- Only works in **raids** or **LFG dungeon groups**
- Normal 5-man parties have no roles → returns `GroupRole.None`
- Role strings: `"TANK"`, `"HEALER"`, `"DAMAGER"`, `"NONE"`

---

## WoWPlayer.IsGhost

### ❌ Doesn't Exist in HB 4.3.4

### ✅ Added for WotLK (CopilotBuddy)
```csharp
public virtual bool IsGhost 
{
    get { return (PlayerFlags & 0x10) != 0; }
}
```

Checks `PlayerFlags` bit 0x10 to detect ghost form (dead + released).

---

## Pet Happiness

### ✅ Exists in WotLK (Removed in Cata)
```csharp
public double HappinessPercent { get; } // 1-3 where 3 = 100%
public uint CurrentHappiness { get; }
```

!!! info "WotLK Feature"
    Hunter/Warlock pets have happiness in WotLK. Removed in Cataclysm 4.0.

---

## Aura System

### UnitAura Return Values

**WotLK 3.3.5a (11 values):**
```lua
name, rank, icon, count, debuffType, duration, expirationTime, 
unitCaster, canStealOrPurge, shouldConsolidate, spellId = UnitAura(unit, index, filter)
```

**Cataclysm 4.3.4 (13 values):**
```lua
-- Adds: isBossDebuff, isCastByPlayer
name, rank, icon, count, debuffType, duration, expirationTime, 
unitCaster, canStealOrPurge, shouldConsolidate, spellId, 
isBossDebuff, isCastByPlayer = UnitAura(unit, index, filter)
```

**Impact:** CopilotBuddy parses WotLK format correctly (11 values).

---

## Glyphs

### WotLK Glyph System (v1)

**3 Types:**
- **Prime Glyphs** - N/A in WotLK (added in Cata, removed in MoP)
- **Major Glyphs** - 3 slots
- **Minor Glyphs** - 3 slots

**Cataclysm Glyph System:**
- **Prime Glyphs** - 3 slots (DPS increase)
- **Major Glyphs** - 3 slots (abilities)
- **Minor Glyphs** - 3 slots (cosmetic)

!!! note
    CopilotBuddy uses WotLK glyph system (Major/Minor only).

---

## Talent System

### WotLK: Tree-Based Talents

- **3 talent trees** per class
- **51 points** at level 80
- Tree-based progression (must spend points to unlock deeper tiers)

### Cataclysm: Simplified Talents

- **Choose spec** at level 10
- **41 points** at level 85
- No tree restrictions

**API Differences:**
```lua
-- WotLK
GetTalentInfo(tabIndex, talentIndex) -- Tree-based

-- Cataclysm
GetSpecialization() -- Returns active spec 1-4
```

---

## Power Types

### ✅ Available in WotLK

| Type | Classes | Notes |
|------|---------|-------|
| **Mana** | Mage, Priest, Warlock, Shaman, Paladin, Druid, Hunter | Primary resource |
| **Rage** | Warrior, Druid (Bear) | Generated by damage |
| **Energy** | Rogue, Druid (Cat) | Regenerates over time |
| **Focus** | Hunter pets | Pet resource |
| **Runic Power** | Death Knight | Secondary resource |
| **Runes** | Death Knight | 6 runes (Blood, Frost, Unholy) |
| **Happiness** | Hunter/Warlock pets | **Removed in Cata** |

### ❌ Not in WotLK

- **Holy Power** (Paladin) - Added in Cataclysm
- **Soul Shards** (Warlock resource bar) - Reworked in Cataclysm
- **Chi** (Monk) - MoP
- **Burning Embers** (Warlock) - MoP

---

## Memory Offsets

!!! danger "Build-Specific"
    **All offsets are specific to WotLK 3.3.5a build 12340.** Using offsets from other builds will crash.

### Key Offset Changes (WotLK vs Cata)

| Description | WotLK 3.3.5a | Cataclysm 4.3.4 | Notes |
|-------------|--------------|-----------------|-------|
| **CurMgr Base** | 0xC79CE0 | Different | Object manager |
| **IsInGame** | 0xBD0792 | Different | Game state flag |
| **LocalPlayer GUID** | CurMgr + 0xC0 | Different | Player GUID offset |
| **PerformanceCounter** | 0x0086AE20 | Different | Aura timing |

**Source:** `335offsetsall.txt` (5821 offsets for build 12340)

---

## Removed Features

### ❌ Completely Removed from CopilotBuddy

1. **Monk Class** - All references removed
2. **Transmogrification APIs** - Not implemented
3. **Void Storage** - Not implemented
4. **Reforging** - Not implemented
5. **Archaeology** - Not implemented
6. **Rated Battlegrounds** - Not implemented

---

## Working Features

### ✅ Fully Functional

- **Object Manager** - All object access
- **Memory Reading** - Direct WoW memory access
- **Lua Execution** - DoString, GetReturnVal
- **Movement** - Click-to-move, facing
- **Casting** - Spell detection, interrupts
- **Auras** - Buff/debuff system
- **Combat Routines** - CustomClass system
- **Behavior Trees** - TreeSharp composites
- **Navigation** - Recast pathfinding (basic)
- **Party/Raid** - Group detection, role system (LFG only)
- **Pet System** - Pet control, happiness
- **Inventory** - Items, bags, equipment

---

## Migration Checklist

When porting HonorBuddy 4.3.4 code to CopilotBuddy (WotLK 3.3.5a):

### 🔹 Classes & Races
- [ ] Remove Monk references
- [ ] Remove Goblin/Worgen checks
- [ ] Verify class detection works

### 🔹 Lua APIs
- [ ] Replace `GetLFGRoles()` with `GetRaidRosterInfo()`
- [ ] Replace `UnitGroupRolesAssigned()` with `GetRaidRosterInfo()`
- [ ] Remove transmog/void storage/reforge checks
- [ ] Adjust `UnitAura()` parsing (11 vs 13 values)

### 🔹 Properties
- [ ] Use `LocalPlayer.Role` (implemented with GetRaidRosterInfo)
- [ ] Use `IsGhost` property (added for WotLK)
- [ ] Handle `HappinessPercent` for pets (WotLK only)

### 🔹 Systems
- [ ] Verify glyph detection (Major/Minor only)
- [ ] Update talent inspection (tree-based)
- [ ] Check power type handling (no Holy Power)

### 🔹 Memory
- [ ] Use WotLK 3.3.5a offsets (build 12340)
- [ ] Test all memory reads
- [ ] Verify descriptor access

---

## Testing Checklist

### Essential Tests

1. **Object Access**
   - [ ] ObjectManager.Me works
   - [ ] GetObjectsOfType returns units
   - [ ] GUID reading works

2. **Lua Execution**
   - [ ] DoString executes
   - [ ] GetReturnVal returns values
   - [ ] No crashes on unknown functions

3. **Role Detection**
   - [ ] Solo: Role = None
   - [ ] Party: Role = None
   - [ ] Raid: Role = Tank/Healer/Damage
   - [ ] LFG: Role = assigned role

4. **Combat**
   - [ ] Casting detection works
   - [ ] Aura reading works
   - [ ] Health/power accurate

5. **Movement**
   - [ ] Location accurate
   - [ ] Distance calculations correct
   - [ ] Facing works

---

## Support Matrix

| Feature | WotLK 3.3.5a | Cataclysm 4.3.4 |
|---------|:------------:|:---------------:|
| Classes (10) | ✅ | ✅ |
| Monk Class | ❌ | ❌ (MoP) |
| LFG Dungeon Finder | ✅ | ✅ |
| LFG Roles | ✅ (via GetRaidRosterInfo) | ✅ (via GetLFGRoles) |
| Dual Spec | ✅ | ✅ |
| Transmog | ❌ | ✅ |
| Void Storage | ❌ | ✅ |
| Reforging | ❌ | ✅ |
| Pet Happiness | ✅ | ❌ (removed) |
| Glyphs v1 | ✅ (Major/Minor) | ✅ (Prime/Major/Minor) |
| Archaeology | ❌ | ✅ |

---

## See Also

- [WotLK Compatibility Overview](overview.md)
- [Known Issues](known-issues.md)
- [LocalPlayer.Role](../api/wowobjects/localplayer.md#role)
