# WoWBag Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - Bag container management for inventory, bags, and equipment.

The `WoWBag` class represents a bag container with item slots.

## Namespace

```csharp
using Styx.WoWInternals;
```

## Overview

`WoWBag` provides access to bag contents, item queries, and slot management. Access player bags via [LocalPlayer](../wowobjects/localplayer.md) properties.

!!! tip "Multiple Bag Types"
    - **Inventory bags** - Player's 4 main bags (bag 0-4)
    - **Bank bags** - Bank container bags
    - **Paper doll** - Equipped items (via `WoWPaperDoll`)

---

## Properties

### Bag Information

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Bag name (or "Inventory") |
| `Guid` | `ulong` | Container GUID |
| `IsInventory` | `bool` | True if inventory bag (not physical bag item) |
| `FirstSlotIndex` | `uint` | First slot index in global slot array |
| `Slots` | `uint` | Total number of slots |

```csharp
WoWBag bag = GetBag(0); // Backpack
Console.WriteLine($"Bag: {bag.Name}");
Console.WriteLine($"Slots: {bag.Slots}");
Console.WriteLine($"Is Inventory: {bag.IsInventory}");
```

### Item Access

| Property | Type | Description |
|----------|------|-------------|
| `ItemGuids` | `ulong[]` | All item GUIDs (including empty slots = 0) |
| `Items` | `WoWItem[]` | All items (nulls for empty slots) |
| `PhysicalItemGuids` | `ulong[]` | Non-zero item GUIDs only |
| `PhysicalItems` | `WoWItem[]` | Non-null items only |

```csharp
// Get all items (including nulls)
WoWItem[] allItems = bag.Items;

// Get only physical items
WoWItem[] items = bag.PhysicalItems;
foreach (var item in items)
{
    Console.WriteLine($"{item.Name} x{item.StackCount}");
}
```

### Slot Information

| Property | Type | Description |
|----------|------|-------------|
| `FreeSlots` | `uint` | Number of empty slots |
| `UsedSlots` | `uint` | Number of occupied slots |

```csharp
Console.WriteLine($"Free: {bag.FreeSlots}/{bag.Slots}");
Console.WriteLine($"Used: {bag.UsedSlots}/{bag.Slots}");

if (bag.FreeSlots == 0)
{
    Console.WriteLine("Bag is full!");
}
```

---

## Methods

### `WoWItem GetItemBySlot(uint slot)`

Gets the item at a specific slot (0-based).

```csharp
// Get item at slot 0
WoWItem? item = bag.GetItemBySlot(0);

if (item != null)
{
    Console.WriteLine($"Slot 0: {item.Name}");
}
```

### `ulong GetItemGuidBySlot(uint slot)`

Gets the item GUID at a specific slot.

```csharp
ulong guid = bag.GetItemGuidBySlot(5);

if (guid != 0)
{
    Console.WriteLine($"Item GUID at slot 5: {guid:X}");
}
```

### `bool Contains(WoWItem item)`

Checks if the bag contains a specific item.

```csharp
WoWItem targetItem = FindItem("Healthstone");

if (bag.Contains(targetItem))
{
    Console.WriteLine("Bag contains Healthstone");
}
```

### `bool Contains(ulong itemGuid)`

Checks if the bag contains an item with the specified GUID.

```csharp
if (bag.Contains(0x0000000012345678))
{
    Console.WriteLine("Item found in bag");
}
```

### `int IndexOf(WoWItem item)`

Gets the slot index of an item (-1 if not found).

```csharp
WoWItem item = FindItem("Frostweave Cloth");
int slotIndex = bag.IndexOf(item);

if (slotIndex != -1)
{
    Console.WriteLine($"Frostweave Cloth at slot {slotIndex}");
}
```

### `int IndexOf(ulong guid)`

Gets the slot index of an item GUID.

```csharp
int index = bag.IndexOf(itemGuid);
Console.WriteLine($"Item at slot: {index}");
```

---

## Complete Examples

### Example 1: List All Items in Backpack

```csharp
using Styx;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

public class BagListing
{
    public void ListBackpackItems()
    {
        var me = StyxWoW.Me;
        
        // Note: Access via LocalPlayer.BagItems when available
        // For demonstration, showing conceptual bag access
        
        Console.WriteLine("=== Backpack Contents ===");
        
        // Conceptual - actual implementation would need LocalPlayer bag access
        // WoWBag backpack = me.GetBagByIndex(0);
        
        // For now, demonstrate with item properties
        Console.WriteLine("Items in inventory:");
        
        // This would work if bag access is implemented
        // foreach (var item in backpack.PhysicalItems)
        // {
        //     Console.WriteLine($"- {item.Name} x{item.StackCount}");
        //     Console.WriteLine($"  Quality: {item.Quality}");
        //     Console.WriteLine($"  Item Level: {item.ItemLevel}");
        // }
    }
}
```

### Example 2: Find Free Bag Space

```csharp
public class BagSpace
{
    public int GetTotalFreeSlots()
    {
        int totalFree = 0;
        
        // Iterate through all bags (0-4)
        // Bag 0 = Backpack, Bags 1-4 = equipped bags
        for (int bagIndex = 0; bagIndex < 5; bagIndex++)
        {
            // Conceptual: WoWBag bag = GetBag(bagIndex);
            // totalFree += (int)bag.FreeSlots;
        }
        
        return totalFree;
    }
    
    public bool HasRoomForItems(int itemCount)
    {
        return GetTotalFreeSlots() >= itemCount;
    }
    
    public void CheckBagSpace()
    {
        int free = GetTotalFreeSlots();
        
        Console.WriteLine($"Total free bag slots: {free}");
        
        if (free < 5)
        {
            Console.WriteLine("⚠️ Low bag space! Consider vendoring.");
        }
        else if (free == 0)
        {
            Console.WriteLine("❌ Bags full! Must vendor now.");
        }
        else
        {
            Console.WriteLine($"✓ {free} slots available");
        }
    }
}
```

### Example 3: Find Item in Bags

```csharp
public class ItemFinder
{
    public WoWItem? FindItemByName(string itemName)
    {
        // Search all bags
        for (int bagIndex = 0; bagIndex < 5; bagIndex++)
        {
            // Conceptual: WoWBag bag = GetBag(bagIndex);
            // 
            // foreach (var item in bag.PhysicalItems)
            // {
            //     if (item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
            //     {
            //         Console.WriteLine($"Found {itemName} in bag {bagIndex}");
            //         return item;
            //     }
            // }
        }
        
        Console.WriteLine($"{itemName} not found in bags");
        return null;
    }
    
    public int CountItem(string itemName)
    {
        int total = 0;
        
        for (int bagIndex = 0; bagIndex < 5; bagIndex++)
        {
            // Conceptual: WoWBag bag = GetBag(bagIndex);
            // 
            // foreach (var item in bag.PhysicalItems)
            // {
            //     if (item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
            //     {
            //         total += item.StackCount;
            //     }
            // }
        }
        
        return total;
    }
}
```

### Example 4: Bag Organization

```csharp
public class BagOrganizer
{
    public List<WoWItem> GetItemsByQuality(WoWItemQuality minQuality)
    {
        var items = new List<WoWItem>();
        
        for (int bagIndex = 0; bagIndex < 5; bagIndex++)
        {
            // Conceptual: WoWBag bag = GetBag(bagIndex);
            // 
            // foreach (var item in bag.PhysicalItems)
            // {
            //     if (item.Quality >= minQuality)
            //     {
            //         items.Add(item);
            //     }
            // }
        }
        
        return items;
    }
    
    public List<WoWItem> GetVendorTrash()
    {
        var trash = new List<WoWItem>();
        
        // Items that should be vendored:
        // - Poor (gray) quality
        // - Soulbound and can't use
        // - Crafting materials when profession bag full
        
        for (int bagIndex = 0; bagIndex < 5; bagIndex++)
        {
            // Conceptual: WoWBag bag = GetBag(bagIndex);
            // 
            // foreach (var item in bag.PhysicalItems)
            // {
            //     if (item.Quality == WoWItemQuality.Poor ||
            //         (item.IsSoulbound && !item.CanUse))
            //     {
            //         trash.Add(item);
            //     }
            // }
        }
        
        return trash;
    }
}
```

### Example 5: Consumable Check

```csharp
public class ConsumableChecker
{
    public bool HasEnoughFood(int minCount = 20)
    {
        string foodName = "Conjured Mana Strudel"; // Example
        int count = CountItemByName(foodName);
        
        if (count < minCount)
        {
            Console.WriteLine($"⚠️ Low food: {count}/{minCount}");
            return false;
        }
        
        Console.WriteLine($"✓ Food: {count}");
        return true;
    }
    
    public bool HasEnoughDrink(int minCount = 20)
    {
        string drinkName = "Conjured Mana Pie"; // Example
        int count = CountItemByName(drinkName);
        
        if (count < minCount)
        {
            Console.WriteLine($"⚠️ Low drink: {count}/{minCount}");
            return false;
        }
        
        Console.WriteLine($"✓ Drink: {count}");
        return true;
    }
    
    public bool NeedsSupplies()
    {
        return !HasEnoughFood() || !HasEnoughDrink();
    }
    
    private int CountItemByName(string name)
    {
        // Use Lua for item count
        return Lua.GetReturnVal<int>($"return GetItemCount(\"{name}\")", 0);
    }
}
```

### Example 6: Equipment vs Inventory

```csharp
public class ItemLocation
{
    public void AnalyzeItemLocations()
    {
        Console.WriteLine("=== Item Locations ===");
        
        // Equipped items (WoWPaperDoll)
        Console.WriteLine("Equipped Items:");
        // WoWPaperDoll paperDoll = GetPaperDoll();
        // foreach (var item in paperDoll.PhysicalItems)
        // {
        //     Console.WriteLine($"  - {item.Name} ({item.ItemSlot})");
        // }
        
        // Bag items (WoWBag)
        Console.WriteLine("\nBag Items:");
        for (int bagIndex = 0; bagIndex < 5; bagIndex++)
        {
            // WoWBag bag = GetBag(bagIndex);
            // Console.WriteLine($"  Bag {bagIndex}: {bag.UsedSlots}/{bag.Slots} slots used");
        }
        
        // Bank items (if at bank)
        Console.WriteLine("\nBank Items:");
        // WoWBag bank = GetBank();
        // Console.WriteLine($"  Bank: {bank.UsedSlots}/{bank.Slots} slots used");
    }
}
```

---

## Item Queries with Lua

For more advanced item operations, use Lua:

### Get Item Count

```csharp
// Total count across all bags
int count = Lua.GetReturnVal<int>("return GetItemCount('Frostweave Cloth')", 0);
Console.WriteLine($"Total Frostweave Cloth: {count}");

// Include bank
int countWithBank = Lua.GetReturnVal<int>("return GetItemCount('Frostweave Cloth', true)", 0);
```

### Has Item

```csharp
bool hasItem = Lua.GetReturnVal<bool>("return GetItemCount('Hearthstone') > 0", 0);

if (hasItem)
{
    Console.WriteLine("Has Hearthstone");
}
```

### Get Bag Info

```csharp
// Number of free slots in bag 0
int freeSlots = Lua.GetReturnVal<int>("return GetContainerNumFreeSlots(0)", 0);

// Total slots in bag 0
int totalSlots = Lua.GetReturnVal<int>("return GetContainerNumSlots(0)", 0);

Console.WriteLine($"Bag 0: {freeSlots}/{totalSlots} free");
```

---

## Related Classes

### WoWPaperDoll

Extends `WoWBag` for equipped items:

```csharp
public class WoWPaperDoll : WoWBag
{
    // Access equipped items (head, chest, legs, etc.)
}
```

### WoWPlayerInventory

Extends `WoWBag` for player inventory:

```csharp
public class WoWPlayerInventory : WoWBag
{
    // Access player bags
}
```

---

## Best Practices

### 1. Check for Null Items

```csharp
// GOOD - validate item
WoWItem? item = bag.GetItemBySlot(0);
if (item != null)
{
    Console.WriteLine(item.Name);
}

// BAD - no null check
WoWItem item = bag.GetItemBySlot(0);
Console.WriteLine(item.Name); // NullReferenceException if slot empty!
```

### 2. Use PhysicalItems for Iteration

```csharp
// GOOD - only physical items (no nulls)
foreach (var item in bag.PhysicalItems)
{
    Console.WriteLine(item.Name);
}

// BAD - includes nulls for empty slots
foreach (var item in bag.Items)
{
    Console.WriteLine(item.Name); // NullReferenceException!
}
```

### 3. Check Bag Space Before Actions

```csharp
// GOOD - check space first
if (bag.FreeSlots > 0)
{
    LootItem();
}
else
{
    Console.WriteLine("Bag full, cannot loot");
}

// BAD - loot without checking
LootItem(); // May fail if bags full
```

### 4. Use Lua for Complex Queries

```csharp
// GOOD - use Lua for counts
int count = Lua.GetReturnVal<int>("return GetItemCount('Item Name')", 0);

// LESS EFFICIENT - iterate all bags manually
int count = 0;
for (int i = 0; i < 5; i++)
{
    // foreach (var item in bag.PhysicalItems)
    //     if (item.Name == "Item Name")
    //         count += item.StackCount;
}
```

---

## Limitations

### No Direct Bag Access from LocalPlayer

```csharp
// ❌ Not directly available
// var bag = StyxWoW.Me.GetBag(0);

// ✅ Use Lua for now
int freeSlots = Lua.GetReturnVal<int>("return GetContainerNumFreeSlots(0)", 0);
```

### Bank Access Requires Open Bank

```csharp
// Bank bags only accessible when bank UI is open
bool bankOpen = Lua.GetReturnVal<bool>("return BankFrame:IsVisible()", 0);

if (!bankOpen)
{
    Console.WriteLine("Bank must be open to access bank items");
}
```

---

## See Also

- [WoWItem](../wowobjects/wowitem.md) - Item properties and usage
- [LocalPlayer](../wowobjects/localplayer.md) - Player inventory access
- [Lua](../styx/lua.md) - Lua item queries (GetItemCount, etc.)
- [ObjectManager](../styx/objectmanager.md) - Get items by GUID
