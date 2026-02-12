using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Loaders;
using Styx.WoWInternals;

namespace Styx.Logic.Combat
{
	/// <summary>
	/// Manages combat routines - loads, compiles, and selects the appropriate routine.
	/// Follows HB pattern: Init() is called after WoW attachment when ObjectManager.Me is available.
	/// FEAT-37: Added CLI parsing, locking, improved selection.
	/// </summary>
	public static class RoutineManager
	{
		private static readonly List<CombatRoutine> _routines = new List<CombatRoutine>();
		private static CombatRoutine _current;
		private static bool _initialized;
		private static readonly object _initLock = new object();

		/// <summary>
		/// Initializes the RoutineManager. Called after WoW is attached and ObjectManager.Me is available.
		/// This is where routines are compiled and loaded, exactly like HB.
		/// </summary>
		public static void Init()
		{
			lock (_initLock)
			{
				if (_initialized)
					return;

				_initialized = true;
			}
			
			Logging.Write("Initializing Combat Routines...");
			
			// Load and compile routines
			LoadCombatRoutines();
			
			// FEAT-37: Check CLI arg /customclass=ClassName
			string? cliRoutine = GetCliRoutineName();
			if (cliRoutine != null)
			{
				Logging.Write("CLI: /customclass={0}", cliRoutine);
				if (SetCurrent(cliRoutine))
					return;
				Logging.Write("CLI routine '{0}' not found, falling back to auto-select.", cliRoutine);
			}

			// Auto-select routine for current class
			SelectRoutineForCurrentClass();
		}

		/// <summary>
		/// FEAT-37: Parses /customclass=ClassName from command line arguments.
		/// </summary>
		private static string? GetCliRoutineName()
		{
			string[] args = Environment.GetCommandLineArgs();
			foreach (string arg in args)
			{
				if (arg.StartsWith("/customclass=", StringComparison.OrdinalIgnoreCase))
					return arg.Substring("/customclass=".Length).Trim();
			}
			return null;
		}

		private static void LoadCombatRoutines()
		{
			string routinesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Routines");
			
			Logging.WriteDebug("Looking for routines in: {0}", routinesPath);
			
			if (!Directory.Exists(routinesPath))
			{
				Logging.WriteDebug("Routines folder not found, creating it...");
				Directory.CreateDirectory(routinesPath);
				return;
			}

			try
			{
				string[] csFiles = Directory.GetFiles(routinesPath, "*.cs", SearchOption.AllDirectories);
				
				if (csFiles.Length == 0)
				{
					Logging.WriteDebug("No .cs files found in Routines folder");
					return;
				}

				Logging.Write("Compiling {0} routine source files...", csFiles.Length);

				try
				{
					IList<CombatRoutine> loadedRoutines = CustomClassLoader.LoadFrom<CombatRoutine>(routinesPath, "v4.0");
					
					foreach (CombatRoutine routine in loadedRoutines)
					{
						_routines.Add(routine);
						Logging.Write("Loaded routine: {0} for class {1}", 
							routine.Name ?? "Unknown", routine.Class);
					}
					
					Logging.Write("Compilation complete. Loaded {0} combat routine(s)", _routines.Count);
				}
				catch (InvalidOperationException ex)
				{
					Logging.Write("COMPILATION ERROR:");
					foreach (var line in ex.Message.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
					{
						Logging.Write("  {0}", line);
					}
				}
				catch (Exception ex)
				{
					Logging.Write("Failed to compile routines: {0}", ex.Message);
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
				Logging.Write("Cannot select routine - player not available");
				_current = new DefaultCombatRoutine();
				return;
			}

			WoWClass playerClass = ObjectManager.Me.Class;
			
			foreach (CombatRoutine routine in _routines)
			{
				if (routine.Class == playerClass)
				{
					_current = routine;
					Logging.Write("Chose {0} as your combat class.", routine.Name);
					Logging.Write("Confirmed {0} as your class.", playerClass);
					
					try
					{
						routine.Initialize();
						Logging.WriteDebug("Routine initialized successfully");
					}
					catch (Exception ex)
					{
						Logging.Write("Routine Initialize() failed: {0}", ex.Message);
						Logging.WriteException(ex);
					}
					return;
				}
			}
			
			// No matching routine found
			Logging.Write("Could not find a routine fitting for your class. Using default.");
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
		/// Sets the current routine by name. Returns true if found.
		/// FEAT-37: Added return value and LegacySpellManager refresh.
		/// </summary>
		public static bool SetCurrent(string routineName)
		{
			foreach (var routine in _routines)
			{
				if (string.Equals(routine.Name, routineName, StringComparison.OrdinalIgnoreCase))
				{
					_current = routine;
					routine.Initialize();
					// Refresh spell manager when routine changes (HB 3.3.5a + 4.3.4 pattern)
					try { LegacySpellManager.Refresh(); } catch { }
					Logging.Write("Combat Routine changed to: {0}", routineName);
					return true;
				}
			}
			return false;
		}

		private sealed class DefaultCombatRoutine : CombatRoutine
		{
			public override string Name => "Default";
			public override WoWClass Class => StyxWoW.Me?.Class ?? WoWClass.None;
			public override void Initialize() { }
		}
	}
}
