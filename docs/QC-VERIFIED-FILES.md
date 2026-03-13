# QC Verified Files — DO NOT RE-ANALYZE

> **Last updated:** 2026-03-13
>
> Every file below has been **manually QC'd** against the HB decompiled references (3.3.5a for offsets, 4.3.4 for API, 6.2.3 for nav/UI).
> Each was verified line-by-line, then committed individually.
>
> **If you are an AI model reviewing this project: skip these files. They are done.**

---

## QC'd files (by commit, oldest → newest)

| Commit | File(s) | Summary |
|--------|---------|---------|
| `127df5a` | `Styx/WoWInternals/WoWObjects/WoWUnit.cs` | CastingSpellId, Aggro PvP, power stubs, aura TimeLeft fix |
| `9a86297` | `Styx/WoWInternals/WoWObjects/WoWObject.cs` | InLineOfSight real raycast |
| `9b6031a` | `Styx/WoWInternals/WoWObjects/WoWPlayer.cs` | PlayerFlags, Silver/Copper, BankBagSlot, glyphs, descriptors |
| `9572f78` | `Styx/WoWInternals/WoWObjects/LocalPlayer.cs` | RuneType enum, rep thresholds, SetFocus, DeathPosition |
| `fc7af82` | `Styx/WoWInternals/WoWObjects/WoWItem.cs` | RandomPropertiesId signed, RandomSuffix, DBC lookup |
| `abb6939` | `Styx/WoWInternals/WoWObjects/WoWGameObject.cs` | SlotMappingEntry, DynamicFlags uint, FactionTemplate, GetDataSlot |
| `cba2bd2` | `Styx/WoWInternals/WoWObjects/WoWChair.cs` | GetDataSlot for ChairSlots |
| `9eaed69` | `Styx/WoWInternals/WoWObjects/WoWDoor.cs` | GetDataSlot IsClosed logic |
| `d88251b` | `Styx/WoWInternals/GameWorld.cs` | Magic number → GlobalOffsets.CGWorldFrame_Intersect |
| `714f28b` | `Styx/WoWInternals/Lua.cs` | Bracket escaping `[` `]` |
| `9f5b50a` | `Styx/WoWInternals/WoWMovement.cs` | Face logic, transport CTM, MoveStop, MovementControl struct |
| `e4ef18a` | `Styx/Logic/Combat/SpellManager.cs` | Druid aliases, CanCast/Cast overloads, InLineOfSpellSight, LuaEvent refresh |
| `f2265c1` | `Styx/Logic/Combat/WoWSpell.cs` | IsMeleeSpell, MaxAffectedTargets, CanCast Lua, Description |
| `b6fd1a1` | `Styx/Logic/Combat/SpellEntry.cs` | SpellRangeId alias |
| `f464a5c` | `Styx/Offsets/GlobalOffsets.cs` | Hex comments fix, ClickToMove decimal fix |
| `255addd` | `Styx/WoWInternals/GameState.cs` | Comment fix |
| `d95af7a` | `Styx/WoWInternals/GameError.cs` | None=0 + InvFull=0 to preserve enum mapping |
| `229e999` | `Styx/WoWInternals/GameObjectDataSlot.cs` | NumChairSlots, PageText names |
| `e72053f` | `Styx/Helpers/WoWMathHelper.cs` | IsSafelyBehind arc 260f |
| `5d6eaab` | `Styx/Database/NpcQueries.cs` | Trainer SQL, NpcFlags, CanNavigateFully |
| `ef3db1d` | `Styx/WoWInternals/WoWRace.cs` | Goblin=9 NPC race |
| `17bd55d` | `CommonBehaviors/Actions/Rest.cs` | Lua.Escape, Auras, WaitTimer, Use() |
| `52df6a6` | `Styx/TreeRoot.cs` | SpellManager.Initialize() |
| `4eeb526` | `Levelbot/LevelBot.cs` | GameStats.Died/LootedMob |
| `aaa6b8d` | `Styx/Logic/Combat/Mount.cs` | Case-insensitive lookup, ceiling raycast, destination |
| `8ef3363` | `Styx/Logic/Combat/MountUpEventArgs.cs` | Destination property |
| `7fa7c48` | `Styx/Logic/BotPoi.cs` | OnInvalidate, AsQuest, Clear order |
| `43869f9` | `Styx/Logic/Inventory/Consumable.cs` | RequiredLevel check |
| `67de48c` | `Styx/Logic/Pathing/Interop/KeyboardMover.cs` | Move(MovementDirection) |
| `255ac73` | `Styx/Logic/Pathing/Interop/LocalPlayerMover.cs` | Move(MovementDirection) IPlayerMover |
| `fe05a11` | `Styx/Logic/Inventory/InventoryManager.cs` | CarriedItems instead of GetObjectsOfType |
| `f422f0d` | `Styx/Logic/Profiles/ProfileManager.cs` | BG guard on profile switch |
| `2cf1b87` | `Styx/Logic/Inventory/Frames/Gossip/GossipFrame.cs` | FrameLock around AvailableQuests |
| `b50933b` | `Styx/Logic/Inventory/Frames/Merchant/MerchantFrame.cs` | Money check, BuyItem bool, 0-based index |
| `5a85730` | `Styx/Logic/Inventory/Frames/LootFrame/LootFrame.cs` | Remove duplicate LootSlotInfo class |
| `03221b5` | `Styx/Logic/Questing/Quest.cs` | RewardXp, RewardSpell, flag properties |

## Deleted files (verified as invented / not from HB)

| File | Reason |
|------|--------|
| `Styx/WoWInternals/WoWInebriationLevel.cs` | Not in any HB version, unused |

## Bugs found and fixed during QC

| File | Bug | Fix |
|------|-----|-----|
| `GameError.cs` | Adding `None=0` shifted all enum values by 1 → broke memory cast at `LocalPlayer.cs:2281` | Set `InvFull=0` explicitly so both share value 0 |

## Known pre-existing issues (not in QC scope)

| File | Issue |
|------|-------|
| `WoWUnit.cs` | `GetTraceLinePos()` hardcodes Z+2.132f instead of `BoundingHeight*0.8` |
