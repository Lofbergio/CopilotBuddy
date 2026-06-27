using Styx;
using Styx.WoWInternals.WoWObjects;

namespace Bots.VibeGrinder
{
    /// <summary>What VibeGrinder does with a carried item on a vendor/mail run.</summary>
    public enum DispositionAction
    {
        /// <summary>Never sold or mailed — stays in bags.</summary>
        Keep,
        /// <summary>Sold to the merchant for coin.</summary>
        Vendor,
        /// <summary>Mailed to the bank alt (BoE only — soulbound can't mail; see Classify).</summary>
        Mail,
    }

    /// <summary>
    /// Single source of truth for loot disposition. Decides Keep/Vendor/Mail per item by CATEGORY first
    /// (ItemClass + trade-goods sub-class), then quality, then binding — NOT by quality alone. Quality is
    /// a poor proxy for value: cloth is white but it's the main income; a white sword is white but it's
    /// trash. The per-category action comes from VibeGrinderSettings ("Loot" group); this only resolves
    /// the always-keep floor and the soulbound/epic binding rules. Feeds both VibeGrinder's sell hook
    /// (protect everything that isn't Vendor) and mail hook (queue everything that is Mail).
    ///
    /// READ Loot/CLAUDE.md before changing this — the category-first design is deliberate and was argued
    /// through; don't regress it to quality flags.
    /// </summary>
    public static class ItemDisposition
    {
        private static VibeGrinderSettings S => VibeGrinderSettings.Instance;

        public static DispositionAction Classify(WoWItem item)
        {
            var info = item?.ItemInfo;
            if (info == null) return DispositionAction.Keep;   // unclassifiable → never risk it

            // ---- always-keep floor: roles/bindings we never auto-dispose ----
            if (item.IsAccountBound) return DispositionAction.Keep;              // heirlooms
            if (info.Bond == WoWItemBondType.Quest) return DispositionAction.Keep;
            if (info.BeginQuestId != 0) return DispositionAction.Keep;           // quest-starter items
            switch (info.ItemClass)
            {
                case WoWItemClass.Quest:
                case WoWItemClass.Key:
                case WoWItemClass.Container:    // bags
                case WoWItemClass.Projectile:   // ammo
                case WoWItemClass.Consumable:   // food/drink/potions/bandages
                case WoWItemClass.Reagent:      // spell/crafting reagents
                    return DispositionAction.Keep;
            }

            // Grey (Poor) of any remaining class is vendor trash (incl. a grey "Cracked Sabre").
            if (info.Quality <= WoWItemQuality.Poor) return DispositionAction.Vendor;

            // ---- category → desired action (from settings) ----
            DispositionAction desired;
            switch (info.ItemClass)
            {
                case WoWItemClass.TradeGoods:
                    desired = info.TradeGoodsClass == WoWItemTradeGoodsClass.Meat ? S.MeatAction : S.TradeGoodsAction;
                    break;
                case WoWItemClass.Recipe:
                case WoWItemClass.Gem:
                    desired = S.RecipesGemsAction;
                    break;
                case WoWItemClass.Weapon:
                case WoWItemClass.Armor:
                case WoWItemClass.Quiver:
                    desired = GearByQuality(info.Quality);
                    break;
                default:
                    // Misc/glyph/unknown white+ → keep (don't dump something we can't value).
                    return DispositionAction.Keep;
            }

            return ResolveBinding(desired, item, info.Quality >= WoWItemQuality.Epic);
        }

        private static DispositionAction GearByQuality(WoWItemQuality q)
        {
            switch (q)
            {
                case WoWItemQuality.Common:   return S.WhiteGearAction;
                case WoWItemQuality.Uncommon: return S.GreenGearAction;
                case WoWItemQuality.Rare:     return S.BlueGearAction;
                case WoWItemQuality.Epic:     return S.EpicGearAction;
                default:                      return DispositionAction.Keep;   // legendary+ → never touch
            }
        }

        /// <summary>
        /// Resolve binding: soulbound can't be mailed, so a Mail action degrades to Vendor (non-epic) or
        /// Keep (epic — we never auto-sell epics). A Vendor action on an epic is likewise forced to Keep.
        /// </summary>
        private static DispositionAction ResolveBinding(DispositionAction desired, WoWItem item, bool epic)
        {
            if (desired == DispositionAction.Vendor)
                return epic ? DispositionAction.Keep : DispositionAction.Vendor;

            if (desired == DispositionAction.Mail)
            {
                if (item.IsSoulbound || item.IsConjured)
                    return epic ? DispositionAction.Keep : DispositionAction.Vendor;
                return DispositionAction.Mail;
            }

            return DispositionAction.Keep;
        }
    }
}
