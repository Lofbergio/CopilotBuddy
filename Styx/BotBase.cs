using System;
using TreeSharp;

namespace Styx
{
	public abstract class BotBase
	{
		public abstract string Name { get; }

		public abstract Composite Root { get; }

		public abstract PulseFlags PulseFlags { get; }

		public virtual object ConfigurationForm => null;

		public virtual bool IsPrimaryType => true;

		public virtual bool RequirementsMet => false;

		/// <summary>
		/// Gets a value indicating whether this bot requires a profile to be loaded.
		/// If false, the bot can run without a profile (like CombatBot, LazyRaider).
		/// </summary>
		public virtual bool RequiresProfile => false;

		public bool Initialized { get; private set; }

		public void DoInitialize()
		{
			if (Initialized)
				return;

			Initialize();
			Initialized = true;
		}

		public virtual void Pulse() { }

		public virtual void Initialize() { }

		public virtual void Start() { }

		public virtual void Stop() { }

		/// <summary>HB 6.2.3 method_0: Called when bot is paused.</summary>
		public virtual void OnPaused() { }

		/// <summary>HB 6.2.3 method_1: Called when bot is resumed.</summary>
		public virtual void OnResumed() { }

		public override string ToString() => Name;
	}
}
