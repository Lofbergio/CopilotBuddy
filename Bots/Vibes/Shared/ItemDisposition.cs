using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Combat;
using Styx.WoWInternals.WoWObjects;

namespace Bots.Vibes.Shared
{
    /// <summary>
    /// Single source of truth for loot disposition across the WHOLE Vibe suite. Decides Keep/Vendor/Mail
    /// per item by CATEGORY first (ItemClass + trade-goods sub-class), then quality, then binding — NOT by
    /// quality alone. Quality is a poor proxy for value: cloth is white but it's the main income; a white
    /// sword is white but it's trash. The per-category action comes from <see cref="VibesLootSettings"/>;
    /// this only resolves the always-keep floor and the soulbound/epic binding rules.
    ///
    /// Lives in Shared/ because VibeGrinder AND VibeParty both classify through it. While it sat under
    /// VibeGrinder/ and read VibeGrinderSettings, VibeParty's loot policy was governed by a panel belonging
    /// to a bot it isn't — including a mailing flag that made VibeParty silently unable to mail at all.
    ///
    /// READ VibeGrinder/Loot/CLAUDE.md before changing this — the category-first design is deliberate and
    /// was argued through; don't regress it to quality flags.
    /// </summary>
    public static class ItemDisposition
    {
        private static VibesLootSettings S => VibesLootSettings.Instance;

        /// <summary>Mailing is usable when a recipient is set — one switch, in General settings, for
        /// every bot. See <see cref="MailboxService.MailingConfigured"/>.</summary>
        private static bool MailingConfigured => MailboxService.MailingConfigured;

        /// <summary>This character eats trade-goods meat via its pet — a hunter who has trained Feed Pet.
        /// Asked of the spellbook rather than the level, so an untamed low hunter doesn't hoard yet.</summary>
        private static bool CanFeedAPet
            => StyxWoW.Me != null && StyxWoW.Me.Class == WoWClass.Hunter && SpellManager.HasSpell("Feed Pet");

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
                    if (info.TradeGoodsClass == WoWItemTradeGoodsClass.Meat && CanFeedAPet)
                    {
                        // A hunter's pet food is class kit, exactly like ammo above — an unhappy pet loses
                        // damage and only FEEDING fixes it (Mend Pet does not). GoodVibes can only feed
                        // trade-goods Meat (its diet match is GetItemInfo's subtype against GetPetFoodTypes,
                        // and consumable "Food & Drink" never matches a pet diet), so MeatAction=Vendor was
                        // selling the one item class the hunter can eat: she fed 9 times, hit a vendor run,
                        // and starved for the rest of the night. Unconditional Keep, not a reserve — the
                        // vendor sweep protects by item ENTRY, so a partial "keep N, sell the surplus" can't
                        // be expressed here; ammo sets the precedent for hoarding essential kit.
                        return DispositionAction.Keep;
                    }
                    desired = S.TradeGoodsAction;
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
                // Only BoE items can be mailed, and only when mailing is actually set up. Otherwise
                // DEGRADE rather than hoard unmailable valuables until bags jam and the bot stops:
                // epics are kept (never auto-sold), everything else is vendored so the grind continues.
                if (!item.IsSoulbound && !item.IsConjured && MailingConfigured)
                    return DispositionAction.Mail;
                return epic ? DispositionAction.Keep : DispositionAction.Vendor;
            }

            return DispositionAction.Keep;
        }
    }
}
