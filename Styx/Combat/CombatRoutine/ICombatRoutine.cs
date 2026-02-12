using System;

namespace Styx.Combat.CombatRoutine
{
	public interface ICombatRoutine : IDisposable, IBehaviors
	{
		string Name { get; }

		WoWClass Class { get; }

		double? PullDistance { get; }

		bool NeedRest { get; }

		void Rest();

		bool NeedPreCombatBuffs { get; }

		void PreCombatBuff();

		bool NeedPullBuffs { get; }

		void PullBuff();

		void Pull();

		bool NeedCombatBuffs { get; }

		void CombatBuff();

		void Combat();

		bool NeedHeal { get; }

		void Heal();

		void Initialize();

		void OnButtonPress();

		bool WantButton { get; }

		string ButtonText { get; }

		void Pulse();

		/// <summary>
		/// Called when the combat routine is shutting down.
		/// Used for cleanup (timers, event handlers, etc.).
		/// </summary>
		void ShutDown();
	}
}
