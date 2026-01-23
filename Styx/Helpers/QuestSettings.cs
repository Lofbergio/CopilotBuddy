using System.ComponentModel;
using System.IO;

namespace Styx.Helpers
{
	/// <summary>
	/// Quest bot settings.
	/// </summary>
	// TODO: Uncomment when UI is ready
	// [Serializable]
	public class QuestSettings : Settings
	{
		private static QuestSettings? _instance;

		/// <summary>
		/// Gets the singleton instance.
		/// </summary>
		public static QuestSettings Instance => _instance ??= new QuestSettings();

		/// <summary>
		/// Creates a new instance of quest settings.
		/// </summary>
		public QuestSettings()
			: base(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Settings\\QuestSettings_{StyxWoW.Me?.Name ?? ""}.xml"))
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
