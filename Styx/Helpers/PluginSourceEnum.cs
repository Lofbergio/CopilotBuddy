namespace Styx.Helpers
{
	/// <summary>
	/// Source of plugins to load.
	/// </summary>
	public enum PluginSourceEnum
	{
		/// <summary>
		/// Load from pre-compiled assemblies.
		/// </summary>
		DynamicAssemblies,

		/// <summary>
		/// Compile from source files.
		/// </summary>
		DynamicCompilation,

		/// <summary>
		/// Load from both sources.
		/// </summary>
		Both
	}
}
