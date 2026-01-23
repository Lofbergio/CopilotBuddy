using System;
using TreeSharp;

namespace Styx.Combat.CombatRoutine
{
	public abstract class CombatRoutine : MarshalByRefObject, IBehaviors, IDisposable, ICombatRoutine
	{
		public void Dispose()
		{
			ShutDown();
		}

		public abstract string Name { get; }

		public abstract WoWClass Class { get; }

		public virtual double? PullDistance
		{
			get { return null; }
		}

		public virtual bool NeedRest
		{
			get { return false; }
		}

		public virtual void Rest()
		{
		}

		public virtual bool NeedPreCombatBuffs
		{
			get { return false; }
		}

		public virtual void PreCombatBuff()
		{
		}

		public virtual bool NeedPullBuffs
		{
			get { return false; }
		}

		public virtual void PullBuff()
		{
		}

		public virtual void Pull()
		{
		}

		public virtual bool NeedCombatBuffs
		{
			get { return false; }
		}

		public virtual void CombatBuff()
		{
		}

		public virtual void Combat()
		{
		}

		public virtual bool NeedHeal
		{
			get { return false; }
		}

		public virtual void Heal()
		{
		}

		public virtual void Initialize()
		{
		}

		public virtual void OnButtonPress()
		{
		}

		public virtual bool WantButton
		{
			get { return false; }
		}

		public string ButtonText
		{
			get { return "Settings"; }
		}

		public virtual void Pulse()
		{
		}

		public virtual Composite RestBehavior
		{
			get { return null; }
		}

		public virtual Composite PreCombatBuffBehavior
		{
			get { return null; }
		}

		public virtual Composite PullBuffBehavior
		{
			get { return null; }
		}

		public virtual Composite PullBehavior
		{
			get { return null; }
		}

		public virtual Composite CombatBuffBehavior
		{
			get { return null; }
		}

		public virtual Composite CombatBehavior
		{
			get { return null; }
		}

		public virtual Composite HealBehavior
		{
			get { return null; }
		}

		public virtual Composite MoveToTargetBehavior
		{
			get { return null; }
		}

		public virtual void ShutDown()
		{
		}

		protected CombatRoutine()
		{
		}
	}
}
