using System.ComponentModel;
using System.IO;

namespace Styx.Helpers
{
	/// <summary>
	/// Combat assist settings for RAF/party following.
	/// </summary>
	// TODO: Uncomment when UI is ready
	// [Serializable]
	public class CombatAssistSettings : Settings
	{
		private static CombatAssistSettings? _instance;

		/// <summary>
		/// Gets the singleton instance.
		/// </summary>
		public static CombatAssistSettings Instance => _instance ??= new CombatAssistSettings();

		/// <summary>
		/// Creates a new instance of combat assist settings.
		/// </summary>
		public CombatAssistSettings()
			: base(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Settings\\CombatAssistSettings_{StyxWoW.Me?.Name ?? ""}.xml"))
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
