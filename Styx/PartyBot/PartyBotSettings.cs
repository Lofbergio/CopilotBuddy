using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Styx;
using Styx.Helpers;

namespace PartyBot
{
	public class PartyBotSettings : Settings
	{
		public PartyBotSettings()
			: base(Path.Combine(Application.StartupPath, string.Format("Settings\\PartyBotSettings_{0}.xml", (StyxWoW.Me != null) ? StyxWoW.Me.Name : "")))
		{
		}

		[Setting(Explanation = "True if this bot should be run with the LeaderPlugin (setting this to true will make it do nothing)")]
		[DefaultValue(false)]
		public bool DoNothing { get; set; }

		[Setting(Explanation = "True if this instance is the party LEADER. Auto-enables the LeaderPlugin (broadcasts to followers) and idles this bot so you drive it manually. One checkbox instead of 'Do Nothing' + enabling the plugin by hand.")]
		[DefaultValue(false)]
		public bool IsLeader { get; set; }

		[Setting(Explanation = "Leader only: auto-Greed every loot roll. Leave OFF if you use AutoEquip2 — it rolls Need on upgrades / Greed otherwise and hooks the same event, so both would fight. Only enable if the leader has no loot-rolling plugin.")]
		[DefaultValue(false)]
		public bool LeaderAutoRollGreed { get; set; }

		[DefaultValue(5)]
		[Setting]
		public int FollowDistance { get; set; }

		[DefaultValue(true)]
		[Setting(Explanation = "True if the bot should wait before releasing from corpse in dungeons.")]
		public bool WaitForRessInDungeons { get; set; }

		[DefaultValue(false)]
		[Setting(Explanation = "True if the bot should loot in dungeons.")]
		public bool LootInDungeons { get; set; }

		[Setting(Explanation = "True if the bot should accept battlefield ports.")]
		[DefaultValue(false)]
		public bool AcceptBattlefieldPorts { get; set; }

		[DefaultValue(false)]
		[Setting(Explanation = "True if the bot should accept dungeon invites.")]
		public bool AcceptDungeonInvites { get; set; }

		[Setting(Explanation = "True if the bot should accept group invites from the leader.")]
		[DefaultValue(false)]
		public bool AcceptGroupInvitesFromLeader { get; set; }

		[Setting(Explanation = "True if the bot should auto accept shared quests")]
		[DefaultValue(false)]
		public bool AutoAcceptSharedQuests { get; set; }

		[DefaultValue("19,20,21,22")]
		[Setting]
		private string Setting_BlacklistedSlots { get; set; } = "19,20,21,22";

		[Setting]
		[DefaultValue("4")]
		private string Setting_BlacklistedEquipQualities { get; set; } = "4";

		[Setting]
		[DefaultValue(true)]
		public bool UseCachedWeights { get; set; }

		[Setting]
		[DefaultValue(true)]
		public bool IgnoreHeirlooms { get; set; }

		public List<WoWInventorySlot> BlacklistedInventorySlots
		{
			get
			{
				List<WoWInventorySlot> list = new List<WoWInventorySlot>();
				if (Setting_BlacklistedSlots.Length <= 0)
					return list;
				foreach (string text in Setting_BlacklistedSlots.Split(new char[] { ',' }))
				{
					try { list.Add((WoWInventorySlot)int.Parse(text)); }
					catch (Exception) { }
				}
				return list;
			}
			set
			{
				if (value.Count <= 0)
				{
					Setting_BlacklistedSlots = "";
					return;
				}
				string text = "";
				foreach (WoWInventorySlot slot in value)
					text = text + (int)slot + ",";
				Setting_BlacklistedSlots = text.Substring(0, text.Length - 1);
			}
		}

		public List<WoWItemQuality> BlacklistedEquipQualities
		{
			get
			{
				List<WoWItemQuality> list = new List<WoWItemQuality>();
				if (Setting_BlacklistedEquipQualities.Length <= 0)
					return list;
				foreach (string text in Setting_BlacklistedEquipQualities.Split(new char[] { ',' }))
				{
					try { list.Add((WoWItemQuality)int.Parse(text)); }
					catch (Exception) { }
				}
				return list;
			}
			set
			{
				if (value.Count <= 0)
				{
					Setting_BlacklistedEquipQualities = "";
					return;
				}
				string text = "";
				foreach (WoWItemQuality q in value)
					text = text + (int)q + ",";
				Setting_BlacklistedEquipQualities = text.Substring(0, text.Length - 1);
			}
		}

		public static readonly PartyBotSettings Instance = new PartyBotSettings();
	}
}
