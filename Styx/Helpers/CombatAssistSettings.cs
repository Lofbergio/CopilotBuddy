using System.ComponentModel;
using System.IO;

namespace Styx.Helpers
{
	/// <summary>
	/// Combat assist settings for RAF/party following.
	/// Path: Settings/CombatAssistSettings_{Name}.xml
	/// Pattern from HB 3.3.5a.
	/// </summary>
	public class CombatAssistSettings : Settings
	{
		public static readonly CombatAssistSettings Instance = new CombatAssistSettings();

		public CombatAssistSettings()
			: base(Path.Combine(Logging.ApplicationPath,
				string.Format("Settings\\CombatAssistSettings_{0}.xml",
				(StyxWoW.Me != null) ? StyxWoW.Me.Name : "")))
		{
		}

		/// <summary>
		/// Distance to follow RAF leader.
		/// </summary>
		[Setting]
		[DefaultValue(3)]
		public int RafFollowDistance { get; set; } = 3;
	}
}
