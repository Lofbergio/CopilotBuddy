# CopilotBuddy Documentation

Welcome to the **CopilotBuddy** documentation - a WoW Bot Framework for **World of Warcraft 3.3.5a (WotLK)**.

!!! info "WotLK 3.3.5a Build 12340"
    This documentation is specifically for **WotLK 3.3.5a**. While the API is based on HonorBuddy 4.3.4 (Cataclysm), many adaptations have been made for WotLK compatibility.

## Quick Links

- **[API Reference](api/overview.md)** - Complete API documentation
- **[WotLK Compatibility](compatibility/overview.md)** - WotLK-specific information and limitations
- **[Creating Combat Routines](guides/creating-routines.md)** - Guide to building custom combat routines
- **[Changelog](about/changelog.md)** - Version history and updates

## Key Features

- ✅ **WotLK 3.3.5a Compatible** - Tested on build 12340
- ✅ **Combat Routine System** - Create custom combat logic with TreeSharp
- ✅ **Memory Reading** - Direct WoW process memory access
- ✅ **Lua Integration** - Execute Lua code and read game state
- ✅ **Navigation** - Recast-based pathfinding and movement
- ✅ **Bot Framework** - Quest, grind, and combat bot implementations

## Getting Started

### Installation

1. Extract CopilotBuddy to a folder
2. Launch `CopilotBuddy.exe`
3. Attach to WoW 3.3.5a process
4. Select a bot and combat routine

### System Requirements

- Windows 7+ (64-bit)
- .NET 10.0 Runtime
- World of Warcraft 3.3.5a (Build 12340)

## Architecture Overview

```
CopilotBuddy
├── Styx/                    # Core bot framework
│   ├── WoWInternals/       # WoW process interaction
│   │   ├── WoWObjects/     # Game object representations
│   │   ├── Memory/         # Memory reading/writing
│   │   └── Lua/            # Lua execution
│   └── Combat/             # Combat routine system
├── TreeSharp/              # Behavior tree engine
├── Tripper/                # Navigation & pathfinding
├── Bots/                   # Bot implementations
└── Routines/               # Combat routines (runtime compiled)
```

## Recent Changes

### v1.0.0 (Current)
- ✅ **LocalPlayer.Role** - Implemented with `GetRaidRosterInfo()` for WotLK
- ✅ **IsGhost Property** - Added to WoWPlayer for ghost detection
- ✅ **Error #134 Fix** - Resolved crash from non-existent LFG APIs
- ✅ **Monk Removal** - Removed Monk class references (doesn't exist in WotLK)
- ✅ **ConfigurationWindow** - Pure WinForms settings GUI

## Support

Found a bug? Have questions?

- Create an issue on GitHub
- Check the [Known Issues](compatibility/known-issues.md) page
- Review the [API Differences](compatibility/api-differences.md) for WotLK-specific behavior

---

**Note**: This bot is for educational purposes. Use at your own risk.
