using System.ComponentModel;
using System.IO;

namespace Styx.Helpers
{
	/// <summary>
	/// Quest bot settings.
	/// Path: Settings/QuestSettings_{Name}.xml
	/// Pattern from HB 3.3.5a.
	/// </summary>
	public class QuestSettings : Settings
	{
		public static readonly QuestSettings Instance = new QuestSettings();

		public QuestSettings()
			: base(Path.Combine(Logging.ApplicationPath,
				string.Format("Settings\\QuestSettings_{0}.xml",
				(StyxWoW.Me != null) ? StyxWoW.Me.Name : "")))
		{
		}

		/// <summary>
		/// Minimum quest level relative to character level.
		/// </summary>
		[Setting("MinQuestLevel", "The minimum level of quests. This is relative to your character.")]
		[DefaultValue(3)]
		public int MinQuestLevel { get; set; } = 3;

		/// <summary>
		/// Maximum quest level relative to character level.
		/// </summary>
		[Setting("MaxQuestLevel", "The maximum level of quests. This is relative to your character.")]
		[DefaultValue(2)]
		public int MaxQuestLevel { get; set; } = 2;
	}
}
