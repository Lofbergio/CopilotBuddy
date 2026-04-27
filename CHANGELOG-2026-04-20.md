UPDATE 4
==========================

Bug fixes:
--------------
  * Extractor (mmaps)
    - minRegionArea: rcSqr(12) → rcSqr(20) — removes small isolated navmesh regions on rough hilltops, reducing unreachable areas the bot tries to path into

  * Bot (Navigator.cs)
    - PathPrecision: 2.0f → 1.6f — matches HB 3.3.5a/6.2.3 exact value, bot is now more precise when determining if it has reached a waypoint or destination

  * Singular (Shaman)
    - Added a new Shaman totem parameter to Singular config to fix fire totem selection(or not fixe)

  * Combat (Targeting.cs)
    - Use `IsBeingAttacked` alongside `Combat` and minion combat checks when including targets, preventing transient combat state drops from losing valid enemies.

  * ProfessionBuddy
    - Fixed CombatBot compatibility crash caused by a null reference during forced quest turn-in handling.

  * Plugin
    - Removed `Thread.Sleep(500)` from AutoEquip2 item equip handling to avoid freezing the WoW client when equipping items.

  *  Profile
    - Restored legacy `SetHearthstone` behavior in QuestBot: all `SetHearthstone` profile entries were removed.

  * Misc
    - Minor cleanup in `Bots/Quest/QuestOrder/ForcedQuestTurnIn.cs` and formatting consistency in `Styx/WoWInternals/WoWObjects/WoWItem.cs`.

  * DungeonBuddy
    - Fixed LFG random queue failures by updating `LfgManager.cs` to dynamically select the correct `LFG_Dungeons.dbc` ID based on the player's level (258, 259, 260, 261, 262) instead of hardcoding the level 80 queue.
    - Added missing methods to `ScriptHelpers.cs` (`PartyIncludingMe`, `IsBossAlive`, `CreateInteractWithObject`, `CreateRunAwayFromBad`) to fix script compilation errors for WotLK 3.3.5a dungeons.

