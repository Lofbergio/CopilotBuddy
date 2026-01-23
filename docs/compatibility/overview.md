# WotLK 3.3.5a Compatibility

CopilotBuddy is built for **World of Warcraft 3.3.5a (WotLK)** using an API based on HonorBuddy 4.3.4 (Cataclysm).

## Version Information

- **Target Version**: WoW 3.3.5a
- **Build Number**: 12340
- **API Base**: HonorBuddy 4.3.4 (Cataclysm)
- **Framework**: .NET 10.0

## Key Differences from Cataclysm

### Classes

**Available in WotLK (10 classes):**
- Warrior, Paladin, Hunter, Rogue, Priest
- Death Knight, Shaman, Mage, Warlock, Druid

**NOT Available:**
- ❌ **Monk** - Added in Mists of Pandaria 5.0
- ❌ **Demon Hunter** - Added in Legion 7.0

### Game Systems

| System | WotLK 3.3.5a | Notes |
|--------|--------------|-------|
| LFG Dungeon Finder | ✅ Available | Added in patch 3.3.0 |
| LFG Roles (Tank/Healer/DPS) | ✅ Available | Via `GetRaidRosterInfo()` |
| Dual Spec | ✅ Available | Added in patch 3.1.0 |
| Transmogrification | ❌ Not Available | Added in Cataclysm 4.3.0 |
| Void Storage | ❌ Not Available | Added in Cataclysm 4.3.0 |
| Reforging | ❌ Not Available | Added in Cataclysm 4.0.1 |

### Lua API Differences

Some Lua APIs don't exist or work differently in WotLK:

**Not Available:**
```lua
-- ❌ These don't exist in WotLK 3.3.5a
GetLFGRoles()                    -- Use GetRaidRosterInfo() instead
GetSpecialization()              -- Use talent inspection instead
IsTransmogrified()               -- Transmog doesn't exist
```

**Available but Different:**
```lua
-- ✅ These work but may have different parameters
GetRaidRosterInfo(index)         -- Returns role information
GetTalentInfo(tab, index)        -- Different return values than Cata
UnitAura(unit, index)            -- Different number of return values
```

See [API Differences](api-differences.md) for complete list.

### Memory Offsets

All memory offsets are specific to **build 12340**. Using offsets from other builds will crash.

**Offset File**: `335offsetsall.txt` (5821 offsets)

**Critical Offsets:**
- Party Member GUIDs: `0xBD1DD8`
- Raid Member Pointers: `0xBECFC8`
- Object Manager: See `Styx/WoWInternals/Offsets.cs`

## Adaptations Made

### 1. LocalPlayer.Role

**Original (Cataclysm):**
```csharp
// Uses GetLFGRoles() - doesn't exist in WotLK
var result = Lua.GetReturnVal<int>("return GetLFGRoles()", 0);
```

**Adapted (WotLK):**
```csharp
// Uses GetRaidRosterInfo() - exists in WotLK 3.3.0+
var role = Lua.GetReturnVal<string>(
    "local _,_,_,_,_,_,_,_,_,role = GetRaidRosterInfo(index); return role", 0);
```

### 2. WoWPlayer.IsGhost

**Added Property:**
```csharp
public virtual bool IsGhost 
{
    get { return (PlayerFlags & 0x10) != 0; }
}
```

This property didn't exist in the original HB 4.3.4 API but was needed for WotLK.

### 3. Monk Class Removed

All references to the Monk class have been removed from:
- Settings classes
- Combat routines
- Class detection logic

## Known Issues

See [Known Issues](known-issues.md) for current bugs and limitations.

## Testing Status

| Feature | Status | Notes |
|---------|--------|-------|
| Memory Reading | ✅ Working | All offsets validated |
| Object Manager | ✅ Working | Unit/player enumeration works |
| Lua Execution | ✅ Working | Both DoString and GetReturnVal |
| Combat Routines | ✅ Working | Singular WotLK port functional |
| LFG Role Detection | ✅ Working | Uses GetRaidRosterInfo() |
| Navigation | ⚠️ Partial | Basic movement works, advanced pathfinding untested |
| Bot Framework | ⚠️ Partial | Combat bot works, quest/grind bots untested |

## Version History

### v1.0.0 - Initial WotLK Release
- Core API adapted for WotLK 3.3.5a
- LocalPlayer.Role implementation
- ERROR #134 crash fix
- Monk class removal
- ConfigurationWindow GUI

---

**Next**: Read [API Differences](api-differences.md) for detailed API change documentation.
