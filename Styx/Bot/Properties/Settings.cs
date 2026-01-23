using System;
using System.CodeDom.Compiler;
using System.Configuration;
using System.Runtime.CompilerServices;

namespace Styx.Bot.Properties
{
	/// <summary>
	/// Application settings for the bot.
	/// </summary>
	[GeneratedCode("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "10.0.0.0")]
	[CompilerGenerated]
	internal sealed partial class Settings : ApplicationSettingsBase
	{
		private static readonly Settings _default = (Settings)Synchronized(new Settings());

		/// <summary>
		/// Default settings instance.
		/// </summary>
		public static Settings Default => _default;

		/// <summary>
		/// UI locale setting.
		/// </summary>
		[UserScopedSetting]
		[DefaultSettingValue("en-US")]
		public string Locale
		{
			get => (string)this["Locale"];
			set => this["Locale"] = value;
		}
	}
}
