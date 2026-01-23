using System;
using System.ComponentModel;
using System.IO;

namespace Styx.Helpers
{
	/// <summary>
	/// PVP/Battleground settings.
	/// </summary>
	// TODO: Uncomment when UI is ready
	// [Serializable]
	public class PVPSettings : Settings
	{
		private static PVPSettings? _instance;

		/// <summary>
		/// Gets the singleton instance.
		/// </summary>
		public static PVPSettings Instance => _instance ??= new PVPSettings();

		/// <summary>
		/// Creates a new instance of PVP settings.
		/// </summary>
		public PVPSettings()
			: base(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Settings\\PVPSettings_{StyxWoW.Me?.Name ?? ""}.xml"))
		{
		}

		/// <summary>
		/// First battleground to queue for.
		/// </summary>
		[Setting("BG1", "The first BG you would like to queue for.")]
		[DefaultValue(Logic.BattlegroundType.WSG)]
		public Logic.BattlegroundType BG1 { get; set; } = Logic.BattlegroundType.WSG;

		/// <summary>
		/// Second battleground to queue for.
		/// </summary>
		[Setting("BG2", "The second BG you would like to queue for.")]
		[DefaultValue(Logic.BattlegroundType.None)]
		public Logic.BattlegroundType BG2 { get; set; } = Logic.BattlegroundType.None;

		/// <summary>
		/// Third battleground to queue for.
		/// </summary>
		[Setting("BG3", "The third BG you would like to queue for.")]
		[DefaultValue(Logic.BattlegroundType.None)]
		public Logic.BattlegroundType BG3 { get; set; } = Logic.BattlegroundType.None;
	}
}
