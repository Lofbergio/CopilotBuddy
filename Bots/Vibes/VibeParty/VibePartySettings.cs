using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Styx;
using Styx.Helpers;

namespace VibeParty
{
	public class VibePartySettings : Settings
	{
		public VibePartySettings()
			: base(Path.Combine(Application.StartupPath, string.Format("Settings\\VibePartySettings_{0}.xml", (StyxWoW.Me != null) ? StyxWoW.Me.Name : "")))
		{
		}

		[Setting(Explanation = "Absolutely idle — the bot does NOTHING: no combat, no follow, no buffing, no leader broadcast. Use it to park a loaded bot. (Unlike Leader mode, this does not keep your buffs up.)")]
		[DefaultValue(false)]
		public bool DoNothing { get; set; }

		[Setting(Explanation = "This instance is the party LEADER: you drive it manually, but the loaded combat routine keeps BUFFS up on you and the party (paladin blessings/seals, mage Intellect, etc. — no more hand-rebuffing), and the LeaderPlugin broadcasts to followers. For a truly do-nothing bot use 'Do Nothing' instead.")]
		[DefaultValue(false)]
		public bool IsLeader { get; set; }

		[DefaultValue(5)]
		[Setting]
		public int FollowDistance { get; set; }

		[DefaultValue(true)]
		[Setting(Explanation = "True if the bot should wait before releasing from corpse in dungeons.")]
		public bool WaitForRessInDungeons { get; set; }

		[DefaultValue(false)]
		[Setting(Explanation = "True if the bot should loot in dungeons.")]
		public bool LootInDungeons { get; set; }

		public static readonly VibePartySettings Instance = new VibePartySettings();
	}
}
