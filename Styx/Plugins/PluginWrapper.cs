using System;
using System.Threading;
using Styx.Helpers;
using Styx.Plugins.PluginClass;

namespace Styx.Plugins
{
	public class PluginWrapper
	{
		public PluginWrapper(HBPlugin plugin)
		{
			if (plugin == null)
			{
				throw new ArgumentNullException("plugin");
			}
			_plugin = plugin;
		}

		public string Author
		{
			get
			{
				try
				{
					return _plugin.Author;
				}
				catch
				{
					return "";
				}
			}
		}

		public string ButtonText
		{
			get
			{
				try
				{
					return _plugin.ButtonText;
				}
				catch
				{
					return "";
				}
			}
		}

		public string PluginName
		{
			get
			{
				try
				{
					return _plugin.Name;
				}
				catch
				{
					return "";
				}
			}
		}

		public Version PluginVersion
		{
			get
			{
				try
				{
					return _plugin.Version;
				}
				catch
				{
					return new Version(0, 0, 0, 0);
				}
			}
		}

		public bool WantButton
		{
			get
			{
				try
				{
					return _plugin.WantButton;
				}
				catch
				{
					return false;
				}
			}
		}

		public void OnButtonPress()
		{
			try
			{
				_plugin.OnButtonPress();
			}
			catch (Exception ex)
			{
				Logging.WriteDebug("Exception in OnButtonPress for plugin: {0}", _plugin.Name);
				Logging.WriteException(ex);
			}
		}

		public void Pulse()
		{
			try
			{
				_plugin.Pulse();
			}
			catch (Exception ex)
			{
				if (ex is ThreadAbortException)
				{
					throw;
				}
				Logging.WriteDebug("Exception in Pulse for plugin: {0}", _plugin.Name);
				Logging.WriteException(ex);
			}
		}

		public static implicit operator PluginWrapper(HBPlugin plugin)
		{
			return new PluginWrapper(plugin);
		}

		private readonly HBPlugin _plugin;
	}
}
