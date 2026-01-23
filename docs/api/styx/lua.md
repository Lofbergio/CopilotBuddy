# Lua Class

Executes Lua code in the WoW client.

**Namespace**: `Styx.WoWInternals`  
**Type**: Static class

## Overview

The `Lua` class provides methods to execute Lua code within the WoW client and retrieve values back to C#.

## Methods

### DoString
```csharp
public static void DoString(string lua)
public static void DoString(string lua, string luaFile)
public static void DoString(string format, params object[] args)
```
Executes Lua code without returning a value.

**Example:**
```csharp
// Simple execution
Lua.DoString("print('Hello from C#!')");

// Send chat message
Lua.DoString("SendChatMessage('Hello', 'SAY')");

// With formatting
Lua.DoString("print('Health: {0}%')", ObjectManager.Me.HealthPercent);
```

### GetReturnVal\<T>
```csharp
public static T GetReturnVal<T>(string lua, int retVal)
public static T GetReturnVal<T>(string lua, uint retVal)
```
Executes Lua and returns a specific return value.

**Parameters:**
- `lua` - Lua code to execute (must return values)
- `retVal` - Index of return value to retrieve (0-based)

**Supported Types:**
- `string`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `bool`

**Example:**
```csharp
// Get player name
string name = Lua.GetReturnVal<string>("return UnitName('player')", 0);

// Get player level
int level = Lua.GetReturnVal<int>("return UnitLevel('player')", 0);

// Get health and max health
int health = Lua.GetReturnVal<int>("return UnitHealth('player')", 0);
int maxHealth = Lua.GetReturnVal<int>("return UnitHealthMax('player')", 0);
```

### GetReturnValues
```csharp
public static List<string> GetReturnValues(string lua)
public static List<string> GetReturnValues(string lua, string scriptName)
```
Executes Lua and returns all values as strings.

**Example:**
```csharp
// Get multiple return values
var values = Lua.GetReturnValues("return UnitHealth('player'), UnitHealthMax('player')");
int health = int.Parse(values[0]);
int maxHealth = int.Parse(values[1]);
```

### Escape
```csharp
public static string Escape(string unescaped)
```
Escapes special characters for Lua strings.

**Example:**
```csharp
string message = "He said: \"Hello!\"";
string escaped = Lua.Escape(message);
Lua.DoString($"print('{escaped}')");
```

## Common Lua APIs (WotLK 3.3.5a)

### Unit Information
```csharp
// Name
string name = Lua.GetReturnVal<string>("return UnitName('target')", 0);

// Health
int hp = Lua.GetReturnVal<int>("return UnitHealth('player')", 0);
int maxHp = Lua.GetReturnVal<int>("return UnitHealthMax('player')", 0);

// Power (mana, rage, energy)
int power = Lua.GetReturnVal<int>("return UnitPower('player')", 0);
int maxPower = Lua.GetReturnVal<int>("return UnitPowerMax('player')", 0);

// Level
int level = Lua.GetReturnVal<int>("return UnitLevel('player')", 0);

// Class
string className = Lua.GetReturnVal<string>("return UnitClass('player')", 0);
```

### Raid/Party Information
```csharp
// Number of raid members
int raidSize = Lua.GetReturnVal<int>("return GetNumRaidMembers()", 0);

// Number of party members
int partySize = Lua.GetReturnVal<int>("return GetNumPartyMembers()", 0);

// Raid member info (WotLK 3.3.0+)
string role = Lua.GetReturnVal<string>(
    "local _,_,_,_,_,_,_,_,_,role = GetRaidRosterInfo(1); return role or 'NONE'", 0);
```

### Spell Information
```csharp
// Spell cooldown
var cd = Lua.GetReturnValues("return GetSpellCooldown('Fireball')");
float start = float.Parse(cd[0]);
float duration = float.Parse(cd[1]);

// Check if spell is known
string known = Lua.GetReturnVal<string>("return IsSpellKnown(133) and 'true' or 'false'", 0);
bool isKnown = known == "true";
```

### Inventory
```csharp
// Money (copper)
int copper = Lua.GetReturnVal<int>("return GetMoney()", 0);

// Bag slots
int slots = Lua.GetReturnVal<int>("return GetContainerNumSlots(0)", 0);
```

## WotLK 3.3.5a Limitations

!!! warning "API Differences"
    Some Lua APIs from later expansions don't exist in WotLK:

**Not Available:**
```lua
GetLFGRoles()           -- Use GetRaidRosterInfo() instead
GetSpecialization()     -- Use talent APIs
UnitGroupRolesAssigned() -- Use GetRaidRosterInfo()
```

**Available:**
```lua
GetRaidRosterInfo(index)  -- Returns role info (WotLK 3.3.0+)
GetNumRaidMembers()       -- Raid size
GetNumPartyMembers()      -- Party size
UnitAura(unit, index)     -- Aura info (fewer return values than Cata)
```

## Error Handling

```csharp
try
{
    var result = Lua.GetReturnVal<int>("return SomeFunction()", 0);
}
catch (Exception ex)
{
    Logger.Write($"Lua error: {ex.Message}");
}
```

## Performance Tips

1. **Cache results** - Don't call Lua every frame for static data
2. **Use batching** - Get multiple values in one call
3. **Avoid DoString spam** - Lua execution has overhead

**Bad:**
```csharp
for (int i = 1; i <= 40; i++)
{
    var name = Lua.GetReturnVal<string>($"return GetRaidRosterInfo({i})", 0);
}
```

**Good:**
```csharp
var lua = new StringBuilder();
lua.Append("local names = {}; ");
for (int i = 1; i <= 40; i++)
    lua.Append($"names[{i}] = GetRaidRosterInfo({i}); ");
lua.Append("return unpack(names)");
var names = Lua.GetReturnValues(lua.ToString());
```

## See Also

- [ObjectManager](objectmanager.md) - Game object access
- [WotLK Compatibility](../../compatibility/overview.md) - API limitations
