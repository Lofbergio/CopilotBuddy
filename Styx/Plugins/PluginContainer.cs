using System;
using Styx.Plugins.PluginClass;

namespace Styx.Plugins
{
	public class PluginContainer
	{
		public PluginWrapper Plugin { get; private set; }

		public bool Enabled { get; set; }

		public PluginContainer(HBPlugin plugin, bool enabled)
		{
			Plugin = plugin;
			Enabled = enabled;
		}
	}
}
