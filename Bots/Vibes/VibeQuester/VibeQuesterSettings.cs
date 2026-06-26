using System.Collections.Generic;
using System.IO;
using System.Linq;
using Styx.Helpers;

namespace VibeQuester
{
    // Uses CB's Settings base: [Setting] properties auto-persist to Settings/VibeQuesterSettings.xml,
    // loaded on construction, written by Save(). [DefaultValue] supplies first-run defaults.
    public class VibeQuesterSettings : Settings
    {
        public VibeQuesterSettings()
            : base(Path.Combine(SettingsDirectory, "VibeQuesterSettings.xml"))
        {
        }

        [Setting, DefaultValue(500)]
        public int ScanStartDistance { get; set; }

        [Setting, DefaultValue(250)]
        public int ScanStep { get; set; }

        [Setting, DefaultValue(4000)]
        public int ScanMaxDistance { get; set; }

        [Setting, DefaultValue(20)]
        public int MaxQuestsPerProfile { get; set; }

        [Setting(Explanation = "How many levels above the player a quest's level (and its objective mobs) may be before it's skipped. Higher = reaches for tougher/higher quests.")]
        [DefaultValue(3)]
        public int MaxMobOverLevel { get; set; }

        [Setting(Explanation = "How long an auto-blacklisted quest (death/no-hotspot/stuck) stays skipped before it's retried, in minutes.")]
        [DefaultValue(20)]
        public int AutoBlacklistMinutes { get; set; }

        [Setting, DefaultValue(true)]
        public bool PreferNearbyQuests { get; set; }

        [Setting, DefaultValue(true)]
        public bool EnableAutoVendor { get; set; }

        [Setting, DefaultValue(true)]
        public bool EnableAutoTrain { get; set; }

        // Mail valuables to a bank alt at faction-safe mailboxes during vendor runs (shared
        // MailboxService: enemy-territory boxes filtered offline + a live runtime backstop; best
        // food/drink auto-protected from MailWhite). Also needs MailRecipient + MailWhite/MailGreen
        // set in the general settings, and Mailboxes.db in the runtime root. Off by default.
        [Setting(Explanation = "Mail valuables to a bank alt at faction-safe mailboxes. Requires MailRecipient + MailWhite/MailGreen set in general settings and Mailboxes.db present. Best food/drink are auto-protected.")]
        [DefaultValue(false)]
        public bool EnableMailing { get; set; }

        // Grey/Poor is the actual vendor-trash tier (white/Common is food/ammo/reagents/class items),
        // so it's the only one safe to sell by default. White/Green/Blue stay opt-in.
        [Setting, DefaultValue(true)]
        public bool SellGrey { get; set; }

        [Setting, DefaultValue(false)]
        public bool SellWhite { get; set; }

        [Setting, DefaultValue(false)]
        public bool SellGreen { get; set; }

        [Setting, DefaultValue(false)]
        public bool SellBlue { get; set; }

        // Never-sell list: comma-separated item IDs or name substrings. The engine's SellItemQualities
        // has no safety net, so this is how class-ambiguous essentials (shaman Totems, warlock Soul
        // Shards — not reliably class Reagent) survive a white/grey sweep.
        [Setting(Explanation = "Never sell items whose name contains any of these, or matching item IDs (comma-separated). e.g. Totem, Soul Shard, 6265")]
        [DefaultValue("Totem,Soul Shard,Ankh")]
        public string SellKeepList { get; set; }

        public IEnumerable<string> SellKeepTokens =>
            (SellKeepList ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);

        // Not [Setting] — handled outside the framework: the vendor blacklist has its own runtime-saved
        // file (vendor_blacklist.txt); the quest blacklist stays session-scoped until the TTL nav-blacklist.
        public HashSet<int> BlacklistedQuests { get; set; } = new HashSet<int>();
        public HashSet<int> BlacklistedVendors { get; set; } = new HashSet<int>();

        public string BlacklistText
        {
            get => string.Join(",", BlacklistedQuests);
            set
            {
                BlacklistedQuests = new HashSet<int>();
                if (string.IsNullOrWhiteSpace(value)) return;
                foreach (string part in value.Split(','))
                    if (int.TryParse(part.Trim(), out int id))
                        BlacklistedQuests.Add(id);
            }
        }

        public string VendorBlacklistText
        {
            get => string.Join(",", BlacklistedVendors);
            set
            {
                BlacklistedVendors = new HashSet<int>();
                if (string.IsNullOrWhiteSpace(value)) return;
                foreach (string part in value.Split(','))
                    if (int.TryParse(part.Trim(), out int id))
                        BlacklistedVendors.Add(id);
            }
        }
    }
}
