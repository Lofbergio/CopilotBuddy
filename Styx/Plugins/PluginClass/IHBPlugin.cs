using System;

namespace Styx.Plugins.PluginClass
{
	/// <summary>
	/// Interface for CopilotBuddy plugins.
	/// </summary>
	public interface IHBPlugin : IEquatable<IHBPlugin>, IDisposable
	{
		/// <summary>
		/// Called every frame while the plugin is active.
		/// </summary>
		void Pulse();

		/// <summary>
		/// Called when the user clicks the plugin's UI button.
		/// Only called if WantButton is true.
		/// </summary>
		void OnButtonPress();

		/// <summary>
		/// Called once when the plugin is first loaded.
		/// </summary>
		void Initialize();

		/// <summary>
		/// Gets a value indicating whether the plugin wants a UI button.
		/// </summary>
		bool WantButton { get; }

		/// <summary>
		/// Gets the text displayed on the plugin's UI button.
		/// </summary>
		string ButtonText { get; }

		/// <summary>
		/// Gets the name of the plugin.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Gets the name of the plugin author.
		/// </summary>
		string Author { get; }

		/// <summary>
		/// Gets the version of the plugin.
		/// </summary>
		Version Version { get; }
	}
}
