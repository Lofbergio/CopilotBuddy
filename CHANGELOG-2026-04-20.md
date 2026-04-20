CopilotBuddy
==========================

Bug fixes:
--------------
  * WoWObjects
    - WoWContainer: null-safe Bag delegates — Slots, UsedSlots, FreeSlots, ItemGuids, Items all guard against null Bag (HB 4.3.4 has same bug)

  * Questing
    - QuestLog.GetQuestInfo: fixed offset formula — was (158 + index * 5 * 4), now ((158 + index * 5) * 4) to match ReadDescriptor pattern (HB 4.3.4 has same bug)
    - ActionSelectReward: ported HB 4.3.4 WeightSetEx.EvaluateItem(itemInfo, itemStats) scoring with ItemStats parsed from item link; vendor sell-price fallback for non-equippable items (replaces custom AutoEquipper.EvaluateRewards)

  * Character Management
    - AutoEquipper.EvaluateItem: dual-slot comparison for Finger1/Finger2 and Trinket1/Trinket2 via GetSecondaryItemSlot(); null guard on StyxWoW.Me.Inventory (HB 4.3.4 and 6.2.3 both check both slots)

  * GatherBuddy
    - GatherbuddyBot: added SleepForLag after Interact before WaitContinue(!IsCasting) — server needs time to start cast channel

