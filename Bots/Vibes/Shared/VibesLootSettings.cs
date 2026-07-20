using System.ComponentModel;
using System.IO;
using Styx;
using Styx.Helpers;

namespace Bots.Vibes.Shared
{
    /// <summary>What a Vibe bot does with a carried item on a vendor/mail run.</summary>
    public enum DispositionAction
    {
        /// <summary>Never sold or mailed — stays in bags.</summary>
        Keep,
        /// <summary>Sold to the merchant for coin.</summary>
        Vendor,
        /// <summary>Mailed to the bank alt (BoE only — soulbound can't mail; see ItemDisposition).</summary>
        Mail,
    }

    /// <summary>
    /// Loot policy for the WHOLE Vibe suite. It lives here, not in a botbase, because every Vibe bot
    /// classifies items through the same <see cref="ItemDisposition"/> — so while these lived on
    /// VibeGrinderSettings, a VibeParty run silently obeyed VibeGrinder's panel and had no way to differ.
    /// "What is this item worth to me" is a property of the character, not of which bot is driving it.
    ///
    /// Surfaced in each bot's PropertyGrid as an expandable "Loot" node bound to this one instance, so
    /// both UIs edit the same policy rather than two copies that drift.
    ///
    /// Mailing is enabled by CharacterSettings.MailRecipient alone (see MailboxService.MailingConfigured)
    /// — there is deliberately no second on/off flag here.
    /// </summary>
    public class VibesLootSettings : Settings
    {
        private static VibesLootSettings _instance;

        /// <summary>Lazily created on first access (after game attach, so Me.Name is valid).</summary>
        public static VibesLootSettings Instance => _instance ??= new VibesLootSettings();

        public VibesLootSettings()
            : base(Path.Combine(Logging.ApplicationPath,
                string.Format("Settings\\VibesLootSettings_{0}.xml",
                    StyxWoW.Me != null ? StyxWoW.Me.Name : "")))
        {
        }

        // Decided by category, not quality: cloth (white) is income, a white sword is trash. Soulbound
        // items can't be mailed — a Mail action on a soulbound item vendors it (epics are always kept).
        // Grey is always vendored; quest/keys/bags/ammo/consumables/reagents/heirlooms are always kept.
        // See VibeGrinder/Loot/CLAUDE.md.
        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Mail)]
        [Category("Loot"), Description("Cloth, leather, ore, herbs, enchanting mats — the main grind income. Mail to bank by default.")]
        public DispositionAction TradeGoodsAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Vendor)]
        [Category("Loot"), Description("Cooking meat (a low-value trade good). Vendor by default.")]
        public DispositionAction MeatAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Vendor)]
        [Category("Loot"), Description("White (common) weapons/armor. Vendor by default (low value).")]
        public DispositionAction WhiteGearAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Vendor)]
        [Category("Loot"), Description("Green (uncommon) BoE gear. Vendor by default; set to Mail if you disenchant/AH them (soulbound greens are always vendored).")]
        public DispositionAction GreenGearAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Mail)]
        [Category("Loot"), Description("Blue (rare) gear. BoE mailed to bank by default; soulbound blues are vendored.")]
        public DispositionAction BlueGearAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Mail)]
        [Category("Loot"), Description("Epic gear. BoE mailed to bank; epics are NEVER auto-sold regardless of this (soulbound epics are kept).")]
        public DispositionAction EpicGearAction { get; set; }

        [Setting, Styx.Helpers.DefaultValue(DispositionAction.Mail)]
        [Category("Loot"), Description("Recipes/patterns and gems. Mail to bank by default.")]
        public DispositionAction RecipesGemsAction { get; set; }

        public override string ToString() => "Loot policy (shared by all Vibe bots)";
    }
}
