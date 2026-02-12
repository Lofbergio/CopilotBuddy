using System.ComponentModel;
using System.IO;

namespace Styx.Helpers
{
	/// <summary>
	/// PVP/Battleground settings.
	/// Path: Settings/PVPSettings_{Name}.xml
	/// Pattern from HB 3.3.5a.
	/// </summary>
	public class PVPSettings : Settings
	{
		public static readonly PVPSettings Instance = new PVPSettings();

		public PVPSettings()
			: base(Path.Combine(Logging.ApplicationPath,
				string.Format("Settings\\PVPSettings_{0}.xml",
				(StyxWoW.Me != null) ? StyxWoW.Me.Name : "")))
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
