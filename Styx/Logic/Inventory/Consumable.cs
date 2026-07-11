#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Inventory
{
    /// <summary>
    /// Helper class for finding consumable items (food and drink).
    /// </summary>
    public static class Consumable
    {
        // Our memory item-cache reader (ItemInfo) returns null for many consumables on this server, so
        // subclass/spell detection is blind to bought food/water (Singular kept logging "no food/drink").
        // Detect via the game's TOOLTIP instead — the same path the UI uses. The marker is the seated-use
        // line ("Use: Must remain seated while drinking. Restores X mana over Y sec."): mana ⇒ drink,
        // health ⇒ food. It must be THAT line — a loose any-line "mana…sec" match classified mp5 GEAR as
        // water ("Equip: Restores 5 mana per 5 sec.", Deathchill Armor 2026-07-04) and the bot chain-
        // "drank" an equippable rare. One cached Lua scan (≤1/sec) answers all callers; map matches back
        // to WoWItems by Entry (a reliable memory field).
        private static readonly System.Diagnostics.Stopwatch _scanAge = new System.Diagnostics.Stopwatch();
        private static List<KeyValuePair<uint, int>> _cacheFood = new List<KeyValuePair<uint, int>>();
        private static List<KeyValuePair<uint, int>> _cacheDrink = new List<KeyValuePair<uint, int>>();

        private const string BagScanLua = @"
local pl = UnitLevel('player')
local tip = CopilotBuddyScanTip
if not tip then tip = CreateFrame('GameTooltip','CopilotBuddyScanTip',nil,'GameTooltipTemplate') end
local f,d = '',''
for bag=0,4 do
  for slot=1,GetContainerNumSlots(bag) do
    local link = GetContainerItemLink(bag,slot)
    if link then
      local id = tonumber(string.match(link,'item:(%d+)'))
      if id then
        tip:SetOwner(UIParent,'ANCHOR_NONE') tip:ClearLines() tip:SetBagItem(bag,slot)
        local hasMana,hasHealth,req = false,false,0
        for l=1,tip:NumLines() do
          local fs = _G['CopilotBuddyScanTipTextLeft'..l]
          local t = fs and fs:GetText()
          if t then
            local lt = string.lower(t)
            if string.find(lt,'must remain seated',1,true) then
              if string.find(lt,'mana',1,true) then hasMana=true end
              if string.find(lt,'health',1,true) then hasHealth=true end
            end
            local r = string.match(t,'Requires Level (%d+)')
            if r then req = tonumber(r) end
          end
        end
        if req <= pl then
          if hasMana then d = d..id..':'..req..',' end
          if hasHealth then f = f..id..':'..req..',' end
        end
      end
    end
  end
end
return f..'|'..d";

        private static void EnsureScanFresh()
        {
            if (_scanAge.IsRunning && _scanAge.Elapsed.TotalMilliseconds < 1000)
                return;
            string res;
            try { res = Lua.GetReturnVal<string>(BagScanLua, 0) ?? "|"; }
            catch { res = "|"; }
            string[] halves = res.Split('|');
            _cacheFood = ParseEntries(halves.Length > 0 ? halves[0] : string.Empty);
            _cacheDrink = ParseEntries(halves.Length > 1 ? halves[1] : string.Empty);
            _scanAge.Restart();
        }

        private static List<KeyValuePair<uint, int>> ParseEntries(string csv)
        {
            var list = new List<KeyValuePair<uint, int>>();
            foreach (string tok in csv.Split(','))
            {
                if (tok.Length == 0) continue;
                string[] kv = tok.Split(':');
                if (kv.Length == 2 && uint.TryParse(kv[0], out uint entry) && int.TryParse(kv[1], out int req))
                    list.Add(new KeyValuePair<uint, int>(entry, req));
            }
            return list;
        }

        private static List<WoWItem> MapToItems(List<KeyValuePair<uint, int>> entries)
        {
            var bag = ObjectManager.Me.BagItems;
            var result = new List<WoWItem>();
            foreach (var kv in entries)
            {
                WoWItem wi = bag.FirstOrDefault(it => it != null && it.Entry == kv.Key);
                if (wi != null && !result.Contains(wi))
                    result.Add(wi);
            }
            return result;
        }

        private static WoWItem BestOf(List<KeyValuePair<uint, int>> entries)
        {
            if (entries.Count == 0)
                return null;
            uint bestEntry = 0;
            int bestReq = -1;
            foreach (var kv in entries)
                if (kv.Value > bestReq) { bestReq = kv.Value; bestEntry = kv.Key; }
            return ObjectManager.Me.BagItems.FirstOrDefault(it => it != null && it.Entry == bestEntry);
        }

        /// <summary>All food items currently in bags (tooltip-detected).</summary>
        public static List<WoWItem> GetFood()
        {
            EnsureScanFresh();
            return MapToItems(_cacheFood);
        }

        /// <summary>All drink items currently in bags (tooltip-detected).</summary>
        public static List<WoWItem> GetDrinks()
        {
            EnsureScanFresh();
            return MapToItems(_cacheDrink);
        }

        /// <summary>Best (highest usable tier) food in bags, or null. includeSpecialtyItems kept for API
        /// compatibility — tooltip detection already covers all edible food.</summary>
        public static WoWItem GetBestFood(bool includeSpecialtyItems)
        {
            EnsureScanFresh();
            return BestOf(_cacheFood);
        }

        /// <summary>Best (highest usable tier) drink in bags, or null.</summary>
        public static WoWItem GetBestDrink(bool includeSpecialtyItems)
        {
            EnsureScanFresh();
            return BestOf(_cacheDrink);
        }

        /// <summary>Total count (summed stacks) of usable food currently in bags.</summary>
        public static int GetFoodCount() { EnsureScanFresh(); return CountStacks(_cacheFood); }

        /// <summary>Total count (summed stacks) of usable drink currently in bags.</summary>
        public static int GetDrinkCount() { EnsureScanFresh(); return CountStacks(_cacheDrink); }

        // ---- Hunter ammo (3.3.5a: only hunters consume ammo; thrown is non-consumable since 3.1,
        // and warrior/rogue ranged slots are stat sticks the bot never Shoots with) ----

        /// <summary>The projectile class the equipped ranged weapon consumes (Bow/Crossbow → Arrow,
        /// Gun → Bullet); None for non-hunters or non-ammo weapons.</summary>
        public static WoWItemProjectileClass NeededAmmoClass()
        {
            if (StyxWoW.Me == null || StyxWoW.Me.Class != Styx.Combat.CombatRoutine.WoWClass.Hunter)
                return WoWItemProjectileClass.None;
            WoWItem ranged = ObjectManager.Me.Inventory.Equipped.Items[(int)InventorySlot.RangedSlot - 1];
            var info = ranged != null ? ranged.ItemInfo : null;
            if (info == null) return WoWItemProjectileClass.None;
            switch (info.WeaponClass)
            {
                case WoWItemWeaponClass.Bow:
                case WoWItemWeaponClass.Crossbow: return WoWItemProjectileClass.Arrow;
                case WoWItemWeaponClass.Gun:      return WoWItemProjectileClass.Bullet;
                default: return WoWItemProjectileClass.None;
            }
        }

        /// <summary>Rounds of the given projectile class on hand: the loaded ammo slot (Lua — the
        /// slot-0 item isn't in the equipped-items array) plus matching bag stacks.</summary>
        public static int GetAmmoCount(WoWItemProjectileClass projectile)
        {
            if (projectile == WoWItemProjectileClass.None) return 0;
            int total = 0;
            try { total = Lua.GetReturnVal<int>("return GetInventoryItemCount('player', 0) or 0", 0); }
            catch { /* frame not ready — bag count still answers */ }
            foreach (var it in ObjectManager.Me.BagItems)
            {
                var info = it != null ? it.ItemInfo : null;
                if (info != null && info.ItemClass == WoWItemClass.Projectile && info.ProjectileClass == projectile)
                    total += (int)it.StackCount;
            }
            return total;
        }

        // Sum StackCount across every bag item whose Entry is a detected food/drink. The scan can list an entry
        // once per occupied slot, so dedupe to an id set first, then count all stacks (handles a split stack).
        private static int CountStacks(List<KeyValuePair<uint, int>> entries)
        {
            var ids = new HashSet<uint>();
            foreach (var kv in entries) ids.Add(kv.Key);
            int total = 0;
            foreach (var it in ObjectManager.Me.BagItems)
                if (it != null && ids.Contains(it.Entry)) total += (int)it.StackCount;
            return total;
        }

        // Food/drink is NOT identified by a spell literally named "Food"/"Drink" — those spell names don't
        // exist (3.3.5a). Identify by item class Consumable + subclass Food & Drink (5), then split food vs
        // drink by the regen aura the use-spell applies while seated: mana-regen ⇒ drink, health-regen ⇒
        // food. Same logic the vendor-buy detection uses (MerchantFrame.GetBestConsumableFromVendor).
        private const int SubClassFoodDrink = 5;

        private static readonly WoWApplyAuraType[] DrinkAuras =
            { WoWApplyAuraType.ObsModMana, WoWApplyAuraType.ModPowerRegen, WoWApplyAuraType.PeriodicEnergize };
        private static readonly WoWApplyAuraType[] FoodAuras =
            { WoWApplyAuraType.ObsModHealth, WoWApplyAuraType.ModRegen, WoWApplyAuraType.ModHealthRegenPercent };

        /// <summary>True if the item is a Food &amp; Drink consumable whose use-spell restores health (food).</summary>
        public static bool IsFoodItem(ItemInfo info) => HasConsumableAura(info, FoodAuras);

        /// <summary>True if the item is a Food &amp; Drink consumable whose use-spell restores mana (drink).</summary>
        public static bool IsDrinkItem(ItemInfo info) => HasConsumableAura(info, DrinkAuras);

        private static bool HasConsumableAura(ItemInfo info, WoWApplyAuraType[] auras)
        {
            if (info == null)
                return false;
            if ((int)info.ItemClass != (int)WoWItemClass.Consumable || info.SubClassId != SubClassFoodDrink)
                return false;

            int[] spellIds = info.SpellId;
            if (spellIds == null)
                return false;
            foreach (int sid in spellIds)
            {
                if (sid == 0)
                    continue;
                WoWSpell spell = WoWSpell.FromId(sid);
                if (spell?.SpellEffects == null)
                    continue;
                foreach (SpellEffect eff in spell.SpellEffects)
                {
                    if (eff != null && Array.IndexOf(auras, eff.AuraType) >= 0)
                        return true;
                }
            }
            return false;
        }

    }
}
