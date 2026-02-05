using System;

namespace Styx.Plugins.PluginClass
{
	public abstract class HBPlugin
	{
		public virtual void Initialize()
		{
		}

		public virtual void Dispose()
		{
		}

		public abstract void Pulse();

		public virtual void OnButtonPress()
		{
		}

		public virtual bool WantButton
		{
			get { return false; }
		}

		public virtual string ButtonText
		{
			get { return "Settings"; }
		}

		public abstract string Name { get; }

		public abstract string Author { get; }

		public abstract Version Version { get; }

		public override int GetHashCode()
		{
			return Version.GetHashCode() ^ Name.GetHashCode();
		}

		public override bool Equals(object? obj)
		{
			if (ReferenceEquals(this, obj))
			{
				return true;
			}
			HBPlugin? hbplugin = obj as HBPlugin;
			if (obj == null || hbplugin == null)
			{
				return false;
			}
			if (Name == hbplugin.Name)
			{
				return Version == hbplugin.Version;
			}
			return false;
		}

		protected HBPlugin()
		{
		}
	}
}
