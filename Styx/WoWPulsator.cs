using System;
using System.Diagnostics;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Plugins;
using Styx.WoWInternals;

namespace Styx
{
	public static class WoWPulsator
	{
		public static void Pulse(PulseFlags flags)
		{
			try
			{
				Stopwatch[] stopwatches = new Stopwatch[9];
				for (int i = 0; i < stopwatches.Length; i++)
				{
					stopwatches[i] = new Stopwatch();
				}

				stopwatches[0].Start();
				stopwatches[1].Start();

				if ((flags & PulseFlags.Objects) != (PulseFlags)0U)
				{
					ObjectManager.Update();
				}

				if ((flags & PulseFlags.Lua) != (PulseFlags)0U)
				{
					Lua.ProcessEvents();
				}

				stopwatches[2].Start();
				if ((flags & PulseFlags.WoWChat) != (PulseFlags)0U)
				{
					WoWChat.Update();
				}

				stopwatches[3].Start();
				if ((flags & PulseFlags.InfoPanel) != (PulseFlags)0U)
				{
					InfoPanel.Update();
				}

				stopwatches[4].Start();
				if ((flags & PulseFlags.Looting) != (PulseFlags)0U)
				{
					LootTargeting.Instance.Pulse();
				}

				stopwatches[5].Start();
				if ((flags & PulseFlags.Targeting) != (PulseFlags)0U)
				{
					Targeting.Instance.Pulse();
				}

				stopwatches[6].Start();
				if ((flags & PulseFlags.BotEvents) != (PulseFlags)0U)
				{
					BotEvents.PulseEvents();
				}

				stopwatches[7].Start();
				if ((flags & PulseFlags.Plugins) != (PulseFlags)0U)
				{
					PluginManager.Pulse();
				}

				stopwatches[8].Start();
				if (RoutineManager.Current != null)
				{
					RoutineManager.Current.Pulse();
				}
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
			}
		}
	}
}
