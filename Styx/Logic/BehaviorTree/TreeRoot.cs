using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
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
		private static Thread? _minimizeGuardThread;
		private static string _statusText = "";
		private static string _goalText = "";
		private static readonly object _stateLock = new object();
		private static WindowPlacement? _lastNonMinimizedPlacement;

		// HB 6.2.3: Win32 imports for minimization guard
		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement lpwndpl);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WindowPlacement lpwndpl);

		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		// HB 6.2.3: WINDOWPLACEMENT struct
		[StructLayout(LayoutKind.Sequential)]
		private struct WindowPlacement
		{
			public int length;
			public int flags;
			public int showCmd;
			public System.Drawing.Point ptMinPosition;
			public System.Drawing.Point ptMaxPosition;
			public RECT rcNormalPosition;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int Left, Top, Right, Bottom;
		}

		public static byte TicksPerSecond { get; set; }

		public static BotBase? Current
		{
			get { return BotManager.Current; }
		}

		/// <summary>
		/// State machine for TreeRoot lifecycle — matches HB 6.2.3.
		/// Stop() sets Stopping; worker thread does cleanup then sets Stopped.
		/// </summary>
		public static TreeRootState State { get; private set; } = TreeRootState.Stopped;

		/// <summary>
		/// Returns true when the bot thread is alive and not stopping.
		/// HB 6.2.3: thread != null && thread.IsAlive && State != Stopping
		/// </summary>
		public static bool IsRunning
		{
			get { return _workerThread != null && _workerThread.IsAlive && State != TreeRootState.Stopping && State != TreeRootState.Stopped; }
		}

		static TreeRoot()
		{
			BotEvents.OnBotStart += OnBotStart;
			TicksPerSecond = 13; // ARCH-02: Matches HB 3.3.5a's 13 TPS (~77ms per tick)
		}

		/// <summary>
		/// HB 6.2.3 smethod_3: Called once during initialization to set up
		/// event handlers and start the minimize guard thread.
		/// </summary>
		internal static void Initialize()
		{
			StartMinimizeGuard();
		}

		private static void OnBotStart(EventArgs args)
		{
			// Initialize spell database from Spells.bin before refreshing spells
			SpellDb.Initialize();
			Lua.DoString("ClearTarget();");
			Lua.DoString("MoveForwardStop();MoveBackwardStop();StrafeLeftStop();StrafeRightStop();");

			// ARCH-03: Set CVars for bot safety (HB 3.3.5a sets autoSelfCast + autoLootDefault)
			Lua.DoString("SetCVar('autoSelfCast', 1)");
			Lua.DoString("SetCVar('autoLootDefault', 1)");

			BotPoi.Clear();
			SpellManager.Refresh();
			if (RoutineManager.Current == null)
			{
				throw new Exception("Unable to start. No Combat Routine loaded.");
			}
		}

		// ARCH-03: Track fall state
		private static bool _wasFalling;
		private static int _fallStartTick;

		/// <summary>
		/// HB 6.2.3 smethod_1: Check if WoW is minimized — restore if so.
		/// Called every 50ms by the minimize guard thread.
		/// </summary>
		private static void CheckAndRestoreMinimized()
		{
			var mem = StyxWoW.Memory;
			if (mem == null) return;

			IntPtr hWnd = mem.WindowHandle;
			if (hWnd == IntPtr.Zero) return;

			var placement = new WindowPlacement();
			placement.length = Marshal.SizeOf<WindowPlacement>();
			if (!GetWindowPlacement(hWnd, ref placement))
				return;

			// showCmd: 2=SW_SHOWMINIMIZED, 6=SW_MINIMIZE, 7=SW_SHOWMINNOACTIVE
			bool isMinimized = placement.showCmd == 2 || placement.showCmd == 6 || placement.showCmd == 7;

			if (IsRunning && isMinimized)
			{
				Logging.Write(Colors.Red, "WoW cannot be minimized while the bot is running — restoring window.");

				if (_lastNonMinimizedPlacement != null)
				{
					var restore = _lastNonMinimizedPlacement.Value;
					restore.showCmd = 9; // SW_RESTORE
					SetWindowPlacement(hWnd, ref restore);
				}
				else
				{
					ShowWindow(hWnd, 9); // SW_RESTORE
				}

				Thread.Sleep(500);
			}

			// Cache last known non-minimized placement
			if (!isMinimized)
			{
				_lastNonMinimizedPlacement = placement;
			}
		}

		/// <summary>
		/// HB 6.2.3 smethod_2: Polling loop for minimization guard.
		/// Runs on a background thread for the entire process lifetime.
		/// </summary>
		private static void MinimizeGuardLoop()
		{
			try
			{
				while (StyxWoW.Memory != null && !StyxWoW.Memory.Process.HasExited)
				{
					CheckAndRestoreMinimized();
					Thread.Sleep(50);
				}
			}
			catch
			{
				// Process exited or Memory disposed — silently exit
			}
		}

		/// <summary>
		/// HB 6.2.3 smethod_3: Start the minimization guard thread.
		/// Called once during initialization.
		/// </summary>
		internal static void StartMinimizeGuard()
		{
			if (_minimizeGuardThread != null && _minimizeGuardThread.IsAlive)
				return;

			_minimizeGuardThread = new Thread(MinimizeGuardLoop)
			{
				IsBackground = true,
				Name = "No minimizing WoW"
			};
			_minimizeGuardThread.Start();
		}

		private static void Tick()
		{
			if (State != TreeRootState.Running)
				return;

			// sync ticks-per-second slider value (per-character)
			TicksPerSecond = CharacterSettings.Instance.TicksPerSecond;

			// HB 6.2.3 pattern (smethod_12 + smethod_15):
			// 1. Time the tick
			// 2. AcquireFrame wraps ONLY the work (no sleep inside)
			// 3. Sleep the remainder OUTSIDE the lock
			var sw = Stopwatch.StartNew();

			if (StyxSettings.Instance.UseFrameLock)
			{
				using (StyxWoW.Memory.AcquireFrame(true))
				{
					RunTickBody();
				}

				// HB 5.4.8/6.2.3 safety net: forcibly release the lock if
				// something inside the tick leaked a continuous-execution hold.
				var executor = ObjectManager.Executor;
				if (executor != null && executor.IsExecutingContinuously)
				{
					lock (executor.AssemblyLock)
					{
						if (executor.IsExecutingContinuously)
						{
							Logging.WriteDiagnostic("Frame lock was forcibly released after tick completed");
							executor.EndExecute();
						}
					}
				}
			}
			else
			{
				RunTickBody();
			}

			// HB 6.2.3 pattern: sleep OUTSIDE AcquireFrame so WoW can render
			int remainingMs = (int)Math.Ceiling(1000.0 / TicksPerSecond - sw.Elapsed.TotalMilliseconds);
			if (remainingMs > 0)
			{
				Thread.Sleep(remainingMs);
			}
		}

		// extracted body — NO Thread.Sleep at the end (sleep is in Tick, outside lock)
		private static void RunTickBody()
		{
			if (StyxWoW.Me == null || !StyxWoW.IsInGame)
			{
				Logging.Write("Not in game");
				return;
			}

			// ARCH-03: Check if WoW process has exited
			if (ObjectManager.WoWProcess != null && ObjectManager.WoWProcess.HasExited)
			{
				Logging.Write("WoW process has exited. Stopping bot.");
				Stop("WoW process exited");
				return;
			}

			// ARCH-03: Skip tick while on taxi (flight path)
			if (StyxWoW.Me.OnTaxi)
			{
				return;
			}

			// ARCH-03: Fall tracking — clear navigator during long free-falls
			bool isFalling = StyxWoW.Me.IsFalling;
			if (isFalling && !_wasFalling)
			{
				_fallStartTick = Environment.TickCount;
			}
			else if (isFalling && _wasFalling)
			{
				int fallDuration = Environment.TickCount - _fallStartTick;
				if (fallDuration > 3000) // 3+ seconds of falling
				{
					Navigator.Clear();
				}
			}
			_wasFalling = isFalling;

			WoWPulsator.Pulse(Current?.PulseFlags ?? PulseFlags.All);
			BotEvents.RaisePulse(EventArgs.Empty);

			// BUG-03 fix: Removed mid-air dismount that killed the player.
			// If mounted and flying, let the bot run normally — dismount is
			// handled by individual bot behaviors when appropriate.
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
		}

		public static void Start()
		{
			// HB 6.2.3: entire Start() body under lock to prevent double-click race
			lock (_stateLock)
			{
				if (Current == null)
					return;

				// HB 6.2.3 pattern: if a previous thread is still stopping, join it first
				if (State == TreeRootState.Stopping)
				{
					if (_workerThread != null && _workerThread.IsAlive)
					{
						_workerThread.Join();
					}
					State = TreeRootState.Stopped;
				}

				if (State != TreeRootState.Stopped)
					return;

				try
				{
					State = TreeRootState.Starting;

					// HB 6.2.3: ObjectManager.Update() directly, no GrabFrame
					ObjectManager.Update();
					string lastUsedPath = LevelbotSettings.Instance.LastUsedPath;
					if (!string.IsNullOrEmpty(lastUsedPath) && System.IO.File.Exists(lastUsedPath))
					{
						ProfileManager.LoadNew(lastUsedPath);
					}
					// BUG-20 fix: Use DoInitialize() which guards against double-init
					Current.DoInitialize();
					Current.Start();
					Current.Root?.Start(null);

					// Initialize Navigator BEFORE RaiseBotStart to ensure mesh loading happens
					// Navigator's static constructor subscribes to OnBotStart, so we need to
					// force it to initialize first by touching it
					_ = Navigator.PathPrecision; // Forces static constructor to run

					// RaiseBotStart triggers OnBotStart which calls SpellManager.Refresh()
					// This must be called BEFORE the worker thread starts (like HB 3.3.5a smethod_3)
					BotEvents.RaiseBotStart();

					// Worker thread transitions to Running once it starts ticking
					_workerThread = new Thread(WorkerThread);
					_workerThread.IsBackground = true;
					_workerThread.Name = "TreeRoot Worker";
					_workerThread.Start();

					BotEvents.OnBotStartComplete();
				}
				catch (HonorbuddyUnableToStartException ex)
				{
					Logging.Write(Colors.Red, "Unable to start: {0}", ex.Message);
					State = TreeRootState.Stopped;
				}
				catch (Exception ex)
				{
					Logging.WriteException(ex);
					State = TreeRootState.Stopped;
				}
			}
		}

		/// <summary>
		/// Stop the bot. HB 6.2.3 pattern: ONLY sets State = Stopping.
		/// The worker thread detects the state change, finishes the current tick,
		/// then does the actual cleanup on its own thread — no race condition.
		/// NO Join() — HB 6.2.3 never joins the worker thread from Stop().
		/// </summary>
		public static void Stop(string? reason = null)
		{
			lock (_stateLock)
			{
				if (State != TreeRootState.Running && State != TreeRootState.Starting)
					return;

				Logging.Write(Colors.DeepSkyBlue, "Bot stopping! Reason: {0}", reason ?? "User request");
				State = TreeRootState.Stopping;
			}
			// HB 6.2.3: Stop() returns immediately. The worker thread sees
			// State == Stopping, exits its while loop, and does cleanup in
			// its own finally block. No Join(), no blocking the UI thread.
		}

		private static void WorkerThread()
		{
			// HB 4.3.4/6.2.3: Set invariant culture on bot thread so float.ToString()
			// always produces "1.5" (not "1,5" on European locales). Lua DoString
			// embeds numbers — wrong decimal separator breaks WoW API calls.
			Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
			Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

			// Use lock to prevent race with Stop() called right after Start()
			lock (_stateLock)
			{
				if (State != TreeRootState.Starting)
				{
					// Stop() was called before we could start — bail out
					State = TreeRootState.Stopped;
					return;
				}
				State = TreeRootState.Running;
			}

			try
			{
				// Main tick loop — exits when Stop() sets State to Stopping
				while (State == TreeRootState.Running)
				{
					Tick();
				}
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
			}
			finally
			{
				// HB 6.2.3 pattern: cleanup on worker thread, NO GrabFrame.
				try
				{
					// HB 6.2.3 cleanup order: Current.Stop → Root.Stop → Navigator.Clear → BotPoi.Clear → RaiseBotStopped
					try { Current?.Stop(); } catch { }
					try { Current?.Root?.Stop(null); } catch { }
					try { Navigator.Clear(); } catch { }
					try { BotPoi.Clear(); } catch { }
					try { BotEvents.RaiseBotStopped(); } catch { }
				}
				catch (Exception ex)
				{
					Logging.WriteException(ex);
				}

				State = TreeRootState.Stopped;
				Logging.WriteDebug("Worker thread exited cleanly");
			}
		}

		public static void Restart()
		{
			Stop();
			Start();
		}

		/// <summary>
		/// Current activity text — displayed in the StatusBar at the bottom of the UI.
		/// Fires OnStatusTextChanged event (same as HB 4.3.4).
		/// </summary>
		public static string StatusText
		{
			get { return _statusText; }
			set
			{
				if (!string.IsNullOrEmpty(value) && _statusText != value)
				{
					Logging.WriteDebug("Activity: {0}", value);
				}
				string oldStatus = _statusText;
				_statusText = value;
				OnStatusTextChanged?.Invoke(null, new StatusTextChangedEventArgs(oldStatus, value));
			}
		}

		/// <summary>
		/// Event fired when StatusText changes — UI subscribes to update StatusBar.
		/// Same as HB 4.3.4's TreeRoot.OnStatusTextChanged.
		/// </summary>
		public static event EventHandler<StatusTextChangedEventArgs> OnStatusTextChanged;

		/// <summary>
		/// High-level goal text — displayed in the Info panel.
		/// Same as HB 4.3.4 (no event, polled by UpdateInfoPanel timer).
		/// </summary>
		public static string GoalText
		{
			get { return _goalText; }
			set
			{
				if (!string.IsNullOrEmpty(value) && _goalText != value)
				{
					Logging.WriteDebug("Goal: {0}", value);
				}
				_goalText = value;
			}
		}
	}
}
