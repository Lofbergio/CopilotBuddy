using System;
using System.Collections.Generic;
using System.IO;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Loaders;
using Styx.WoWInternals;

namespace Styx.Logic.Combat
{
	/// <summary>
	/// Manages combat routines - loads, compiles, and selects the appropriate routine.
	/// Follows HB pattern: Init() is called after WoW attachment when ObjectManager.Me is available.
	/// </summary>
	public static class RoutineManager
	{
		private static readonly List<CombatRoutine> _routines = new List<CombatRoutine>();
		private static CombatRoutine _current;
		private static bool _initialized;

		/// <summary>
		/// Initializes the RoutineManager. Called after WoW is attached and ObjectManager.Me is available.
		/// This is where routines are compiled and loaded, exactly like HB.
		/// </summary>
		public static void Init()
		{
			if (_initialized)
				return;

			_initialized = true;
			
			Logging.Write("Initializing Combat Routines...");
			
			// Load and compile routines
			LoadCombatRoutines();
			
			// Auto-select routine for current class
			SelectRoutineForCurrentClass();
		}

		private static void LoadCombatRoutines()
		{
			string routinesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Routines");
			
			Logging.Write("[RoutineManager] Looking for routines in: {0}", routinesPath);
			
			if (!Directory.Exists(routinesPath))
			{
				Logging.Write("[RoutineManager] Routines folder not found, creating it...");
				Directory.CreateDirectory(routinesPath);
				return;
			}

			try
			{
				string[] csFiles = Directory.GetFiles(routinesPath, "*.cs", SearchOption.AllDirectories);
				
				if (csFiles.Length == 0)
				{
					Logging.Write("[RoutineManager] No .cs files found in Routines folder");
					return;
				}

				Logging.Write("[RoutineManager] Found {0} routine source files, compiling...", csFiles.Length);

				try
				{
					IList<CombatRoutine> loadedRoutines = CustomClassLoader.LoadFrom<CombatRoutine>(routinesPath, "v4.0");
					
					foreach (CombatRoutine routine in loadedRoutines)
					{
						_routines.Add(routine);
						Logging.Write("[RoutineManager] Loaded routine: {0} for class {1}", 
							routine.Name ?? "Unknown", routine.Class);
					}
					
					Logging.Write("[RoutineManager] Compilation complete. Loaded {0} combat routine(s)", _routines.Count);
				}
				catch (InvalidOperationException ex)
				{
					Logging.Write("[RoutineManager] COMPILATION ERROR:");
					foreach (var line in ex.Message.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
					{
						Logging.Write("  {0}", line);
					}
				}
				catch (Exception ex)
				{
					Logging.Write("[RoutineManager] Failed to compile routines: {0}", ex.Message);
					Logging.WriteException(ex);
				}
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
			}
		}

		private static void SelectRoutineForCurrentClass()
		{
			if (ObjectManager.Me == null)
			{
				Logging.Write("[RoutineManager] Cannot select routine - player not available");
				_current = new DefaultCombatRoutine();
				return;
			}

			WoWClass playerClass = ObjectManager.Me.Class;
			
			foreach (CombatRoutine routine in _routines)
			{
				if (routine.Class == playerClass)
				{
					_current = routine;
					Logging.Write("Selected Combat Routine: {0} for {1}", routine.Name, playerClass);
					
					try
					{
						routine.Initialize();
						Logging.Write("[RoutineManager] Routine initialized successfully");
					}
					catch (Exception ex)
					{
						Logging.Write("[RoutineManager] Routine Initialize() failed: {0}", ex.Message);
						Logging.WriteException(ex);
					}
					return;
				}
			}
			
			// No matching routine found
			Logging.Write("No Combat Routine found for {0}. Using default.", playerClass);
			_current = new DefaultCombatRoutine();
		}

		/// <summary>
		/// Gets the currently selected combat routine.
		/// </summary>
		public static CombatRoutine Current
		{
			get
			{
				if (_current == null)
				{
					_current = new DefaultCombatRoutine();
					Logging.Write("No Combat Routine loaded. Using default.");
				}
				return _current;
			}
		}

		/// <summary>
		/// Gets all loaded routines.
		/// </summary>
		public static IReadOnlyList<CombatRoutine> Routines => _routines;

		/// <summary>
		/// Sets the current routine by name.
		/// </summary>
		public static void SetCurrent(string routineName)
		{
			foreach (var routine in _routines)
			{
				if (routine.Name == routineName)
				{
					_current = routine;
					routine.Initialize();
					Logging.Write("Combat Routine changed to: {0}", routineName);
					return;
				}
			}
		}

		private sealed class DefaultCombatRoutine : CombatRoutine
		{
			public override string Name => "Default";
			public override WoWClass Class => StyxWoW.Me?.Class ?? WoWClass.None;
			public override void Initialize() { }
		}
	}
}
