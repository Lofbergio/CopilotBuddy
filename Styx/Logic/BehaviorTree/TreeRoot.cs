using System;
using System.Threading;
using System.Windows.Media;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using TreeSharp;

namespace Styx.Logic.BehaviorTree
{
	public static class TreeRoot
	{
		private static Thread? _workerThread;
		private static string _statusText = "";
		private static string _goalText = "";

		public static byte TicksPerSecond { get; set; }

		public static BotBase? Current
		{
			get { return BotManager.Current; }
		}

		public static bool IsRunning { get; private set; }

		static TreeRoot()
		{
			BotEvents.OnBotStart += OnBotStart;
			TicksPerSecond = 10;
		}

		private static void OnBotStart(EventArgs args)
		{
			Lua.DoString("ClearTarget();");
			Lua.DoString("MoveForwardStop();MoveBackwardStop();StrafeLeftStop();StrafeRightStop();");
			BotPoi.Clear();
			SpellManager.Refresh();
			if (RoutineManager.Current == null)
			{
				throw new Exception("Unable to start. No Combat Routine loaded.");
			}
		}

		private static void Tick()
		{
			if (StyxWoW.Me == null || !StyxWoW.IsInGame)
			{
				Logging.Write("Not in game. Waiting...");
			}
			else
			{
				WoWPulsator.Pulse(Current?.PulseFlags ?? PulseFlags.All);
				BotEvents.RaisePulse(EventArgs.Empty);

				if (StyxWoW.Me.Mounted && StyxWoW.Me.IsFlying)
				{
					Logging.Write("Flying. Dismounting before starting.");
					Mount.Dismount();
					Thread.Sleep(100);
				}
				else
				{
					Current?.Pulse();
					try
					{
						Current?.Root?.Tick(null);
						RunStatus? lastStatus = Current?.Root?.LastStatus;
						if (lastStatus != RunStatus.Running)
						{
							Current?.Root?.Stop(null);
							Current?.Root?.Start(null);
						}
					}
					catch (Exception ex)
					{
						Logging.WriteException(ex);
						Current?.Root?.Stop(null);
						Current?.Root?.Start(null);
						BotPoi.Clear();
					}
					Thread.Sleep(1000 / (int)TicksPerSecond);
				}
			}
		}

		public static void Start()
		{
			if (!IsRunning && Current != null)
			{
				try
				{
					ObjectManager.Update();
					string lastUsedPath = LevelbotSettings.Instance.LastUsedPath;
					if (!string.IsNullOrEmpty(lastUsedPath) && System.IO.File.Exists(lastUsedPath))
					{
						ProfileManager.LoadNew(lastUsedPath);
					}
					Current.Initialize();
					Current.Start();
					BotEvents.RaiseBotStarted();
					Current.Root?.Start(null);
					IsRunning = true;

					_workerThread = new Thread(WorkerThread);
					_workerThread.IsBackground = true;
					_workerThread.Name = "TreeRoot Worker";
					_workerThread.Start();

					BotEvents.OnBotStartComplete();
				}
				catch (HonorbuddyUnableToStartException ex)
				{
					Logging.Write(Colors.Red, "Unable to start: {0}", ex.Message);
					IsRunning = false;
				}
				catch (Exception ex)
				{
					Logging.WriteException(ex);
					IsRunning = false;
				}
			}
		}

		public static void Stop()
		{
			try
			{
				if (IsRunning && Current != null)
				{
					Navigator.Clear();
					BotEvents.OnBotStopping();
					Current.Stop();
					Current.Root?.Stop(null);
					BotEvents.RaiseBotStopped();
				}
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
			}
			finally
			{
				IsRunning = false;
				if (_workerThread != null && _workerThread.IsAlive)
				{
					_workerThread.Abort();
				}
			}
		}

		private static void WorkerThread()
		{
			while (IsRunning)
			{
				Tick();
			}
		}

		public static void Restart()
		{
			Stop();
			Start();
		}

		public static string StatusText
		{
			get { return _statusText; }
			set
			{
				_statusText = value;
				Logging.WriteDebug("StatusText: " + _statusText);
			}
		}

		public static string GoalText
		{
			get { return _goalText; }
			set
			{
				_goalText = value;
				Logging.WriteDebug("GoalText: {0}", _goalText);
			}
		}
	}
}
