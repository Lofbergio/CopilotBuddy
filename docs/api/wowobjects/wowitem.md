# WoWItem Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - All item properties and descriptor fields verified for WotLK 3.3.5a (build 12340).

The `WoWItem` class represents an item in your inventory, bank, or equipped on your character. It provides access to all item properties, enchantments, durability, and usage methods.

## Namespace

```csharp
using Styx.WoWInternals.WoWObjects;
```

## Inheritance

```
WoWObject
  └─ WoWItem
```

## Overview

Items don't have a world position (Location is always `WoWPoint.Zero`). They are referenced by GUID and exist in bags, bank, or equipment slots.

---

## Core Properties

### Identifiers

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Guid` | `ulong` | Unique item instance GUID | ✅ |
| `Entry` | `uint` | Item ID from `Item.dbc` | ✅ |
| `Name` | `string` | Item display name (includes suffix) | ✅ |
| `Link` | `string` | Item link (for chat/Lua) | ✅ |

### Owner & Container

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `OwnerGuid` | `ulong` | GUID of the player who owns this item | ✅ |
| `ContainerGuid` | `ulong` | GUID of the bag containing this item (0 if equipped) | ✅ |
| `CreatorGuid` | `ulong` | GUID of the player who crafted this item | ✅ |
| `GiftCreatorGuid` | `ulong` | GUID of the player who gifted/wrapped this item | ✅ |

### Stack & Charges

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `StackCount` | `uint` | Number of items in the stack | ✅ |
| `SpellCharges` | `uint` | Remaining charges for items with spell triggers | ✅ |
| `Duration` | `uint` | Remaining duration in milliseconds (for temporary items) | ✅ |

### Durability

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Durability` | `uint` | Current durability points | ✅ |
| `MaxDurability` | `uint` | Maximum durability points | ✅ |
| `DurabilityPercent` | `double` | Durability percentage (0-100) | ✅ |
| `IsBroken` | `bool` | True if durability is 0 | ✅ |

### Flags

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `Flags` | `uint` | Raw item flags bitfield | ✅ |
| `IsSoulbound` | `bool` | Item is soulbound | ✅ |
| `IsConjured` | `bool` | Item is conjured (disappears on logout) | ✅ |
| `IsOpenable` | `bool` | Item can be opened (containers, presents) | ✅ |
| `IsGiftWrapped` | `bool` | Item is gift-wrapped | ✅ |
| `IsTotem` | `bool` | Item is a totem | ✅ |
| `TriggersSpell` | `bool` | Item triggers a spell on use | ✅ |
| `HasEquipCooldown` | `bool` | Item has cooldown when equipped | ✅ |
| `IsWand` | `bool` | Item is a wand | ✅ |
| `IsWrappingPaper` | `bool` | Item is wrapping paper | ✅ |
| `IsCharter` | `bool` | Item is a guild/arena charter | ✅ |
| `IsReadable` | `bool` | Item can be read (books, scrolls) | ✅ |
| `IsPvPItem` | `bool` | Item is PvP-related | ✅ |
| `CanExpire` | `bool` | Item has expiration time | ✅ |
| `CanProspect` | `bool` | Item can be prospected (jewelcrafting) | ✅ |
| `IsUniqueEquipped` | `bool` | Only one can be equipped at a time | ✅ |
| `IsThrownWeapon` | `bool` | Item is a thrown weapon | ✅ |
| `IsAccountBound` | `bool` | Item is bound to account | ✅ |
| `IsEnchantScroll` | `bool` | Item is an enchantment scroll | ✅ |
| `IsMillable` | `bool` | Item can be milled (inscription) | ✅ |
| `IsBoundToEquipOnAcquire` | `bool` | Item binds when equipped | ✅ |

---

## Item Information (from Cache)

These properties come from `ItemInfo` (item cache/database):

| Property | Type | Description | WotLK Support |
|----------|------|-------------|---------------|
| `ItemInfo` | `ItemInfo` | Full item cache data | ✅ |
| `Quality` | `WoWItemQuality` | Item quality/rarity | ✅ |
| `RequiredLevel` | `int` | Required level to use/equip | ✅ |
| `ItemLevel` | `int` | Item level (iLvl) | ✅ |
| `ItemClass` | `WoWItemClass` | Item class (Weapon, Armor, Consumable, etc.) | ✅ |
| `EquipSlot` | `WoWInventorySlot` | Equipment slot this item goes in | ✅ |
| `SellPrice` | `int` | Vendor sell price in copper | ✅ |
| `BuyPrice` | `int` | Vendor buy price in copper | ✅ |

---

## Enchantments

### Temporary Enchantment

```csharp
WoWItemEnchantment tempEnchant = item.TemporaryEnchantment;
if (tempEnchant.IsValid)
{
    Console.WriteLine($"Temporary enchant: {tempEnchant.Name}");
    Console.WriteLine($"Duration: {tempEnchant.ExpirationTimestamp}ms");
    Console.WriteLine($"Charges: {tempEnchant.ChargesLeft}");
}
```

### All Enchantments

```csharp
// Get by index (0-11)
for (uint i = 0; i < 12; i++)
{
    var enchant = item.GetEnchantment(i);
    if (enchant.IsValid)
        Console.WriteLine($"Slot {i}: {enchant.Name}");
}

// Get by name
var enchant = item.GetEnchantment("Fiery Weapon");

// Get by ID
var enchant = item.GetEnchantmentById(803);
```

### WoWItemEnchantment Class

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `uint` | Enchantment ID |
| `Name` | `string` | Enchantment display name |
| `IsValid` | `bool` | True if enchantment exists |
| `ExpirationTimestamp` | `int` | When enchantment expires (ms) |
| `ChargesLeft` | `int` | Remaining charges |

**Methods:**
- `WoWItemStat? GetStat(int index)` - Gets stat bonus at index 0-2

---

## Random Properties & Suffixes

```csharp
// Random property ID
uint randomPropId = item.RandomPropertiesId;
uint propertySeed = item.PropertySeed;

// Random properties object
WoWItemRandomProperties randomProps = item.RandomProperties;
if (randomProps.IsValid)
{
    Console.WriteLine($"Suffix: {randomProps.Name}");
    // Example: "of the Bear", "of the Eagle"
}
```

!!! example "Random Item Names"
    Items with random properties show their suffix in the name:
    ```csharp
    // Item with Entry 12345 and random suffix ID 67
    string name = item.Name; // "Leather Gloves of the Bear"
    ```

---

## Stats & Damage

### Item Stats

```csharp
// Get stat by index
WoWItemStat stat = item.GetStat(0);
Console.WriteLine($"{stat.Type}: {stat.Value}");

// Get stat type/value directly
WoWItemStatType type = item.GetStatType(0);
int value = item.GetStatValue(0);

// Get all stats from ItemStats
ItemStats stats = item.GetItemStats();
foreach (var kvp in stats.Stats)
{
    Console.WriteLine($"{kvp.Key}: {kvp.Value}");
}
```

### Weapon Damage

```csharp
// Damage range (for weapons)
float minDmg = item.GetMinDamage(0);
float maxDmg = item.GetMaxDamage(0);
int dmgType = item.GetDamageType(0);

// DPS calculation
ItemStats stats = item.ItemStats;
if (stats.DPS > 0)
{
    Console.WriteLine($"Weapon DPS: {stats.DPS}");
}
```

---

## Item Spells

Many items have "Use:" or "Equip:" spell effects. Access them with:

```csharp
List<WoWItemSpell> spells = item.ItemSpells;
foreach (var itemSpell in spells)
{
    Console.WriteLine($"Spell ID: {itemSpell.Id}");
    Console.WriteLine($"Trigger: {itemSpell.TriggerId}");
    Console.WriteLine($"Cooldown: {itemSpell.Cooldown}ms");
    Console.WriteLine($"Charges: {itemSpell.Charges}");
    
    // Get the actual spell object
    WoWSpell? spell = itemSpell.ActualSpell;
    if (spell != null)
    {
        Console.WriteLine($"Spell name: {spell.Name}");
    }
}

// Get spell by index (0-4)
WoWItemSpell? spell1 = item.GetSpell(0);
```

### WoWItemSpell Class

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `int` | Spell ID |
| `TriggerId` | `int` | Trigger type (on use, on equip, etc.) |
| `Charges` | `int` | Number of charges |
| `Cooldown` | `int` | Cooldown in milliseconds |
| `Category` | `int` | Spell category |
| `CategoryCooldown` | `int` | Category cooldown |
| `ActualSpell` | `WoWSpell?` | The actual spell object |
| `IsValid` | `bool` | True if spell exists |

---

## Sockets

```csharp
// Get socket color by index (0-2)
WoWSocketColor color = item.GetSocketColor(0);

switch (color)
{
    case WoWSocketColor.Meta:
        Console.WriteLine("Meta socket");
        break;
    case WoWSocketColor.Red:
        Console.WriteLine("Red socket");
        break;
    case WoWSocketColor.Yellow:
        Console.WriteLine("Yellow socket");
        break;
    case WoWSocketColor.Blue:
        Console.WriteLine("Blue socket");
        break;
}
```

---

## Bag Location

### `BagIndex`

Gets the bag index containing this item (0-4 for bags, -1 for equipped/bank).

```csharp
int bagIndex = item.BagIndex;
// 0 = Backpack
// 1-4 = Bag slots
// -1 = Not in bags (equipped, bank, etc.)
```

### `BagSlot`

Gets the slot index within the bag.

```csharp
int slot = item.BagSlot;
Console.WriteLine($"Item is in bag {item.BagIndex}, slot {slot}");
```

---

## Using Items

### `bool Use()`

Uses the item (right-click).

```csharp
if (item.Use())
{
    Console.WriteLine("Item used successfully");
}
```

### `bool Use(ulong targetGuid, bool forceUse = false)`

Uses the item on a specific target.

```csharp
// Use bandage on yourself
item.Use(StyxWoW.Me.Guid);

// Use item on another player
WoWPlayer target = ObjectManager.GetObjectByGuid<WoWPlayer>(targetGuid);
item.Use(target.Guid, forceUse: true);
```

**Parameters:**
- `targetGuid`: Target GUID (0 for self/no target)
- `forceUse`: Force usage even if not normally allowed

### `void UseContainerItem()`

Uses the item via Lua (safer for UI-restricted items).

```csharp
item.UseContainerItem();
```

### `void PickUp()`

Picks up the item (puts it on cursor).

```csharp
item.PickUp();
```

---

## Cooldown & Usability

### `int Cooldown`

Gets remaining cooldown in milliseconds.

```csharp
int cooldown = item.Cooldown;
if (cooldown > 0)
{
    Console.WriteLine($"On cooldown for {cooldown / 1000}s");
}
else
{
    item.Use();
}
```

### `bool Usable`

Checks if the item is usable by the current player.

```csharp
if (item.Usable && item.Cooldown == 0)
{
    item.Use();
}
```

---

## Item Quality Enum

```csharp
public enum WoWItemQuality
{
    Poor = 0,       // Gray
    Common = 1,     // White
    Uncommon = 2,   // Green
    Rare = 3,       // Blue
    Epic = 4,       // Purple
    Legendary = 5,  // Orange
    Artifact = 6,   // Gold
    Heirloom = 7    // Light blue
}
```

---

## Item Class Enum

```csharp
public enum WoWItemClass
{
    Consumable = 0,
    Container = 1,
    Weapon = 2,
    Gem = 3,
    Armor = 4,
    Reagent = 5,
    Projectile = 6,
    TradeGoods = 7,
    Generic = 8,
    Recipe = 9,
    Money = 10,
    Quiver = 11,
    Quest = 12,
    Key = 13,
    Permanent = 14,
    Miscellaneous = 15,
    Glyph = 16
}
```

---

## Complete Examples

### Example 1: Find and Use Healing Potions

```csharp
using Styx;
using Styx.WoWInternals.WoWObjects;

// Find all healing potions in bags
var potions = ObjectManager.GetObjectsOfType<WoWItem>()
    .Where(i => i.Entry == 929) // Healing Potion (Entry ID)
    .ToList();

if (potions.Any())
{
    var potion = potions.First();
    
    // Check if usable
    if (potion.Usable && potion.Cooldown == 0)
    {
        Console.WriteLine($"Using {potion.Name} (Stack: {potion.StackCount})");
        potion.Use();
    }
}
```

### Example 2: Check Equipment Durability

```csharp
// Get all equipped items
var equippedItems = StyxWoW.Me.Inventory.Equipped.Items;

foreach (var item in equippedItems)
{
    if (item != null && item.MaxDurability > 0)
    {
        Console.WriteLine($"{item.Name}: {item.DurabilityPercent:F1}%");
        
        if (item.IsBroken)
        {
            Console.WriteLine($"  WARNING: {item.Name} is broken!");
        }
        else if (item.DurabilityPercent < 20)
        {
            Console.WriteLine($"  ALERT: {item.Name} is low durability!");
        }
    }
}
```

### Example 3: List All Enchantments

```csharp
WoWItem weapon = StyxWoW.Me.Inventory.Equipped.MainHand;

Console.WriteLine($"Enchantments on {weapon.Name}:");
for (uint i = 0; i < 12; i++)
{
    var enchant = weapon.GetEnchantment(i);
    if (enchant.IsValid)
    {
        Console.WriteLine($"  Slot {i}: {enchant.Name}");
        
        // Get stats from enchantment
        for (int statIdx = 0; statIdx < 3; statIdx++)
        {
            var stat = enchant.GetStat(statIdx);
            if (stat != null)
            {
                Console.WriteLine($"    +{stat.Value} {stat.Type}");
            }
        }
        
        if (enchant.ChargesLeft > 0)
        {
            Console.WriteLine($"    Charges: {enchant.ChargesLeft}");
        }
    }
}
```

### Example 4: Item Information Display

```csharp
void DisplayItemInfo(WoWItem item)
{
    Console.WriteLine($"=== {item.Name} ===");
    Console.WriteLine($"Entry: {item.Entry}");
    Console.WriteLine($"Quality: {item.Quality}");
    Console.WriteLine($"Item Level: {item.ItemLevel}");
    Console.WriteLine($"Required Level: {item.RequiredLevel}");
    Console.WriteLine($"Class: {item.ItemClass}");
    Console.WriteLine($"Equipment Slot: {item.EquipSlot}");
    Console.WriteLine($"Stack: {item.StackCount}");
    
    if (item.MaxDurability > 0)
    {
        Console.WriteLine($"Durability: {item.Durability}/{item.MaxDurability} ({item.DurabilityPercent:F1}%)");
    }
    
    if (item.RandomProperties.IsValid)
    {
        Console.WriteLine($"Random Suffix: {item.RandomProperties.Name}");
    }
    
    Console.WriteLine($"Sell Price: {item.SellPrice} copper");
    Console.WriteLine($"Buy Price: {item.BuyPrice} copper");
    
    // Display flags
    Console.WriteLine("Flags:");
    if (item.IsSoulbound) Console.WriteLine("  - Soulbound");
    if (item.IsBoundToEquipOnAcquire) Console.WriteLine("  - Binds on Equip");
    if (item.IsAccountBound) Console.WriteLine("  - Account Bound");
    if (item.IsUniqueEquipped) Console.WriteLine("  - Unique-Equipped");
    
    // Item spells
    if (item.ItemSpells.Count > 0)
    {
        Console.WriteLine("Item Spells:");
        foreach (var spell in item.ItemSpells)
        {
            Console.WriteLine($"  - {spell.ActualSpell?.Name ?? $"Spell {spell.Id}"}");
        }
    }
}
```

---

## Descriptor Field Offsets (WotLK 3.3.5a)

For advanced users working with raw descriptors:

| Field | Index | Byte Offset | Size | Description |
|-------|-------|-------------|------|-------------|
| `ITEM_FIELD_OWNER` | 6 | 24 | 8 | Owner GUID |
| `ITEM_FIELD_CONTAINED` | 8 | 32 | 8 | Container GUID |
| `ITEM_FIELD_CREATOR` | 10 | 40 | 8 | Creator GUID |
| `ITEM_FIELD_GIFTCREATOR` | 12 | 48 | 8 | Gift creator GUID |
| `ITEM_FIELD_STACK_COUNT` | 14 | 56 | 4 | Stack count |
| `ITEM_FIELD_DURATION` | 15 | 60 | 4 | Duration |
| `ITEM_FIELD_SPELL_CHARGES` | 16 | 64 | 20 | Spell charges (5 fields) |
| `ITEM_FIELD_FLAGS` | 21 | 84 | 4 | Item flags |
| `ITEM_FIELD_ENCHANTMENT` | 22 | 88 | 144 | Enchantments (12×3 fields) |
| `ITEM_FIELD_PROPERTY_SEED` | 58 | 232 | 4 | Property seed |
| `ITEM_FIELD_RANDOM_PROPERTIES_ID` | 59 | 236 | 4 | Random properties ID |
| `ITEM_FIELD_DURABILITY` | 60 | 240 | 4 | Current durability |
| `ITEM_FIELD_MAXDURABILITY` | 61 | 244 | 4 | Max durability |

---

## See Also

- [WoWObject](wowobject.md) - Base object class
- [WoWSpell](../combat/wowspell.md) - Spell information
- [LocalPlayer](localplayer.md) - Player inventory access
- [ObjectManager](../styx/objectmanager.md) - Getting items
