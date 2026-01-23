using System;
using System.IO;
using System.Reflection;

namespace Styx.Helpers
{
	public class LevelbotSettings : Settings
	{
		private static string GetSettingsPath()
		{
			string startupPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
			return Path.Combine(startupPath, string.Format("Settings\\LevelbotSettings_{0}.xml", (StyxWoW.Me != null) ? StyxWoW.Me.Name : ""));
		}

		public LevelbotSettings()
			: base(GetSettingsPath())
		{
		}

		[Setting]
		[DefaultValue("")]
		public string MailRecipient { get; set; }

		[Setting]
		[DefaultValue("Food Name Here")]
		public string FoodName { get; set; }

		[Setting]
		[DefaultValue("Drink Name Here")]
		public string DrinkName { get; set; }

		[Setting]
		[DefaultValue("Mount Name Here")]
		public string MountName { get; set; }

		[DefaultValue(true)]
		[Setting]
		public bool LootMobs { get; set; }

		[DefaultValue(false)]
		[Setting]
		public bool SkinMobs { get; set; }

		[Setting]
		[DefaultValue(false)]
		public bool NinjaSkin { get; set; }

		[DefaultValue(true)]
		[Setting]
		public bool LootChests { get; set; }

		[Setting]
		[DefaultValue(false)]
		public bool HarvestMinerals { get; set; }

		[Setting]
		[DefaultValue(false)]
		public bool HarvestHerbs { get; set; }

		[Setting]
		[DefaultValue(false)]
		public bool UseMount { get; set; }

		[Setting]
		[DefaultValue(30)]
		public int PullDistance { get; set; }

		[Setting]
		[DefaultValue(75)]
		public int MountDistance { get; set; }

		[Setting]
		[DefaultValue(45)]
		public int LootRadius { get; set; }

		[Setting]
		[DefaultValue(false)]
		public bool FindVendorsAutomatically { get; set; }

		[DefaultValue(false)]
		[Setting]
		public bool TrainNewSkills { get; set; }

		[Setting]
		[DefaultValue(true)]
		public bool LearnFlightPaths { get; set; }

		[DefaultValue(false)]
		[Setting]
		public bool RessAtSpiritHealers { get; set; }

		[DefaultValue(false)]
		[Setting]
		public bool GroundMountFarmingMode { get; set; }

		[Setting]
		[DefaultValue("")]
		public string LastUsedPath { get; set; }

		public static readonly LevelbotSettings Instance = new LevelbotSettings();
	}
}
