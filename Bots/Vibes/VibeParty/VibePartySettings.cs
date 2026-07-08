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

		[Setting(Explanation = "Leader only: automatically SHARE each quest you accept with the party (native /share). Followers with 'Auto Accept Shared Quests' on will accept/confirm them. Off by default.")]
		[DefaultValue(false)]
		public bool ShareQuestsToParty { get; set; }

		[Setting(Explanation = "Follower: turn in completed quests at the NPC the leader is interacting with. Follow the leader to the turn-in giver and it hands in whatever's ready. Off by default.")]
		[DefaultValue(false)]
		public bool AutoTurnInQuests { get; set; }

		[DefaultValue(5)]
		[Setting]
		public int FollowDistance { get; set; }

		[DefaultValue(true)]
		[Setting(Explanation = "True if the bot should wait before releasing from corpse in dungeons.")]
		public bool WaitForRessInDungeons { get; set; }

		[DefaultValue(false)]
		[Setting(Explanation = "True if the bot should loot in dungeons.")]
		public bool LootInDungeons { get; set; }

		[DefaultValue(false)]
		[Setting(Explanation = "True if the bot should accept dungeon invites.")]
		public bool AcceptDungeonInvites { get; set; }

		[Setting(Explanation = "True if the bot should accept group invites from the leader.")]
		[DefaultValue(false)]
		public bool AcceptGroupInvitesFromLeader { get; set; }

		[Setting(Explanation = "True if the bot should auto accept shared quests")]
		[DefaultValue(false)]
		public bool AutoAcceptSharedQuests { get; set; }

		public static readonly VibePartySettings Instance = new VibePartySettings();
	}
}
