using System;
using System.ComponentModel;
using System.IO;
using Bots.DungeonBuddy.Enums;
using Styx;
using Styx.Helpers;
using DefaultValueAttribute = System.ComponentModel.DefaultValueAttribute;

namespace Bots.DungeonBuddy
{
    public class DungeonBuddySettings : Settings
    {
        private static DungeonBuddySettings _instance;

        public static DungeonBuddySettings Instance =>
            _instance ?? (_instance = new DungeonBuddySettings());

        public DungeonBuddySettings()
            : base(Path.Combine(
                Logging.ApplicationPath,
                $"Settings\\DungeonBuddySettings_{StyxWoW.Me?.Name ?? ""}.xml"))
        {
            if (SelectedDungeonIds == null)
                SelectedDungeonIds = new uint[0];
            if (PartyMembers == null)
                PartyMembers = new string[0];
            Load();
        }

        // ═══════════════════════════════════════════════════════════
        // ADVANCED
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(true)]
        [Category("Advanced")]
        [DisplayName("Use FrameLock")]
        [Description("Setting this to true can provide a big performance improvement but badly written code that uses any form of Thread.Sleep() can cause wow to freeze momentarily")]
        public bool UseFrameLock { get; set; }

        // ═══════════════════════════════════════════════════════════
        // DUNGEON
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(QueueType.RandomDungeon)]
        [Category("Dungeon")]
        [DisplayName("Queue Type")]
        [Description("Random or specific dungeon")]
        public QueueType QueueType { get; set; }

        [Setting]
        [Category("Dungeon")]
        [DisplayName("Selected Random")]
        [Description("The Random dungeons to queue for. Example: Random Wrath of the Lich King Dungeon")]
        public string SelectedRandom { get; set; }

        [Setting]
        [Category("Dungeon")]
        [DisplayName("Selected Random Heroic")]
        [Description("The Random heroics to queue for. Example: Random Wrath of the Lich King Heroic")]
        public string SelectedHeroicRandom { get; set; }

        [Setting]
        [Browsable(false)]
        public uint[] SelectedDungeonIds { get; set; }

        // ═══════════════════════════════════════════════════════════
        // LOOT
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(LootMode.BossesOnly)]
        [Category("Loot")]
        [DisplayName("Loot Mode")]
        [Description("Set to Always to loot every mob (warning: may lose sight of tank)")]
        public LootMode LootMode { get; set; }

        [Setting, DefaultValue(false)]
        [Category("Loot")]
        [DisplayName("Mail BoE items")]
        [Description("Mails all BOE items that are not vendored to the 'Recipient' in HB settings")]
        public bool MailBoeItems { get; set; }

        [Setting, DefaultValue(WoWItemQuality.Rare)]
        [Category("Loot")]
        [DisplayName("Maximum Vendor Item Quality")]
        [Description("The maximum item quality to vendor")]
        public WoWItemQuality SellItemQuality { get; set; }

        [Setting, DefaultValue(3)]
        [Category("Loot")]
        [DisplayName("Minimum Free Bag Slots")]
        [Description("Bot will port out of dungeon and sell items if number of free bag slots is less than this value")]
        public int MinFreeBagSlots { get; set; }

        [Setting, DefaultValue(WoWItemQuality.Uncommon)]
        [Category("Loot")]
        [DisplayName("Minimum Mail Item Quality")]
        [Description("The minimum BOE item quality to mail")]
        public WoWItemQuality MailItemQuality { get; set; }

        [Setting, DefaultValue(LootRollType.Greed)]
        [Category("Loot")]
        [DisplayName("Loot Roll Type")]
        [Description("How to roll on group loot: Need (1), Greed (2, default), or Pass (3)")]
        public LootRollType LootRollType { get; set; }

        // ═══════════════════════════════════════════════════════════
        // MISC
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(20)]
        [Category("Misc")]
        [DisplayName("Following Distance")]
        [Description("Max distance before following tank (Default: 20)")]
        public int FollowingDistance { get; set; }

        [Setting, DefaultValue(false)]
        [Category("Misc")]
        [DisplayName("Kill Optional Bosses")]
        [Description("Kill bosses that are not necessary to complete the dungeon")]
        public bool KillOptionalBosses { get; set; }

        [Setting]
        [Browsable(false)]
        public bool ShowAllDungeons { get; set; }

        // ═══════════════════════════════════════════════════════════
        // PARTY
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(PartyMode.Off)]
        [Category("Party")]
        [DisplayName("Party Mode")]
        [Description("True if party mode is enabled, false otherwise. Should be set to true for each bot")]
        public PartyMode PartyMode { get; set; }

        [Setting]
        [Category("Party")]
        [DisplayName("Party Member Names")]
        [Description("Only set this for the leader bot. Should remain empty for followers")]
        public string[] PartyMembers { get; set; }
    }
}
