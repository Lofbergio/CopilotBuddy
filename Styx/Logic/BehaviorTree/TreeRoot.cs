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
			// Initialize spell database from Spells.bin before refreshing spells
			SpellDb.Initialize();
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
					// Grab frame first to sync with WoW's main thread (like HB 3.3.5a)
					if (ObjectManager.Executor != null)
					{
						ObjectManager.Executor.GrabFrame();
					}
					ObjectManager.Update();
					string lastUsedPath = LevelbotSettings.Instance.LastUsedPath;
					if (!string.IsNullOrEmpty(lastUsedPath) && System.IO.File.Exists(lastUsedPath))
					{
						ProfileManager.LoadNew(lastUsedPath);
					}
					Current.Initialize();
					Current.Start();
					Current.Root?.Start(null);
					IsRunning = true;
					
					// Initialize Navigator BEFORE RaiseBotStart to ensure mesh loading happens
					// Navigator's static constructor subscribes to OnBotStart, so we need to
					// force it to initialize first by touching it
					_ = Navigator.PathPrecision; // Forces static constructor to run
					
					// RaiseBotStart triggers OnBotStart which calls SpellManager.Refresh()
					// This must be called BEFORE the worker thread starts (like HB 3.3.5a smethod_3)
					BotEvents.RaiseBotStart();
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
			Logging.Write("Stopping the bot...");

			if (!IsRunning || Current == null)
				return;

			try
			{
				// HB 3.3.5a: Grab a frame to ensure memory consistency
				try { ObjectManager.Executor?.GrabFrame(); } catch { }

				// Clear navigation first (HB behavior)
				try { Navigator.Clear(); } catch { }

				// Notify handlers the bot is stopping
				try { BotEvents.OnBotStopping(); } catch { }

				// Stop the bot's current actions and stop the root behavior
				try { Current.Stop(); } catch { }
				try { Current.Root?.Stop(new object()); } catch { }

				// Notify handlers that stop is complete
				try { BotEvents.RaiseBotStopped(); } catch { }
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
			}
			finally
			{
				// Signal the worker thread to exit
				IsRunning = false;

				// HB 3.3.5a: Wait for thread to exit gracefully
				if (_workerThread != null)
				{
					// Give the thread time to exit on its own
					if (!_workerThread.Join(TimeSpan.FromSeconds(2)))
					{
						Logging.WriteDebug("Worker thread did not exit gracefully");
					}
				}
				_workerThread = null;
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
