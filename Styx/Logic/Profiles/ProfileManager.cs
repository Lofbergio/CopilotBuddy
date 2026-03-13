using System;
using System.IO;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.WoWInternals;

namespace Styx.Logic.Profiles
{
	public static class ProfileManager
	{
		private static Profile? _currentOuterProfile;
		private static Profile? _currentProfile;
		private static bool _profileless = false; // Flag for bots that don't use profiles

		static ProfileManager()
		{
			BotEvents.Player.OnLevelUp += OnLevelUp;
			BotEvents.Profile.OnNewProfileLoaded += OnNewProfileLoaded;
			XmlLocation = "";
		}

		private static void OnNewProfileLoaded(BotEvents.Profile.NewProfileLoadedEventArgs args)
		{
			Profile? newProfile = args.NewProfile;
			if (newProfile != null)
			{
				string text = string.Format("Profile loaded: {0}",
					!string.IsNullOrEmpty(newProfile.Name) ? newProfile.Name :
					string.Format("L{0}-{1}", newProfile.MinLevel, newProfile.MaxLevel));
				Logging.Write(text);
			}
			else
			{
				Logging.Write("We either outleveled the currently loaded profile, or something has gone terribly wrong. Can't find a new sub-profile to use.");
			}
		}

		public static string XmlLocation { get; private set; }

		public static Profile? CurrentOuterProfile
		{
			get { return _currentOuterProfile; }
			private set
			{
				Profile? old = _currentOuterProfile;
				_currentOuterProfile = value;
				if (old != _currentOuterProfile && _currentOuterProfile != null)
				{
					BotEvents.Profile.RaiseOuterProfileLoaded(old, _currentOuterProfile);
				}
			}
		}

		public static Profile? CurrentProfile
		{
			get
			{
				// Bots like CombatBot, LazyRaider don't use profiles
				if (_profileless)
					return _currentProfile;
					
				if (_currentProfile == null)
				{
					LoadProfileForLevel();
				}
				if (_currentProfile == null)
				{
					// Check if current bot requires profile (HB 6.2.3 pattern)
					if (BotManager.Current != null && !BotManager.Current.RequiresProfile)
					{
						// Bot doesn't require profile, return null without stopping
						return null;
					}
					
					Logging.Write("No profile loaded. Stopping bot.");
					TreeRoot.Stop();
					return null;
				}
				return _currentProfile;
			}
			private set
			{
				Profile? old = _currentProfile;
				_currentProfile = value;
				if (old != _currentProfile && _currentProfile != null)
				{
					BotEvents.Profile.RaiseNewProfileLoaded(old, _currentProfile);
				}
			}
		}

		private static void OnLevelUp(BotEvents.Player.LevelUpEventArgs args)
		{
			if (Battlegrounds.IsInsideBattleground)
				return;
			Logging.Write("We leveled up! Checking if we need to switch profiles.");
			LoadProfileForLevel();
		}

		private static void LoadProfileForLevel()
		{
			CurrentProfile = GetProfileForLevel(ObjectManager.Me?.Level ?? 1);
		}

		private static Profile? GetProfileForLevel(int level)
		{
			if (CurrentOuterProfile == null)
			{
				return null;
			}

			var sortedProfiles = CurrentOuterProfile.GetScopeSortedProfiles();
			uint mapId = ObjectManager.Me?.MapId ?? 0;
			for (int i = 0; i < sortedProfiles.Count; i++)
			{
				// BUG-10: Check ContinentId matches current map (HB 4.3.4)
				if ((sortedProfiles[i].ContinentId == -1 || (long)sortedProfiles[i].ContinentId == (long)mapId)
					&& level >= sortedProfiles[i].MinLevel && level < sortedProfiles[i].MaxLevel)
				{
					return sortedProfiles[i];
				}
			}
			return null;
		}

		public static void LoadNew(string path, bool rememberMe)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				throw new FileNotFoundException("Profile file not found.", path);
			}

			_profileless = false; // Clear profileless mode when loading real profile
			XmlLocation = path;
			if (rememberMe)
			{
				LevelbotSettings.Instance.LastUsedPath = path;
			}

			StyxWoW.AreaManager.SetArea(null);
			CurrentOuterProfile = new Profile(path, null);
			LoadProfileForLevel();
		}

		public static void LoadNew(string path)
		{
			LoadNew(path, true);
		}

	/// <summary>
	/// Loads an empty profile. Used by bots that don't require a profile (BGBuddy, LazyRaider, CombatBot, etc.)
	/// </summary>
	public static void LoadEmpty()
	{
		_profileless = true;
		StyxWoW.AreaManager.SetArea(null);
		CurrentOuterProfile = new Profile();
		CurrentProfile = null;
		Logging.Write("Running in profileless mode (CombatBot, LazyRaider, etc.)");
	}

	/// <summary>
	/// Clears profileless mode when loading a real profile
	/// </summary>
	public static void ClearProfilelessMode()
	{
		_profileless = false;
	}
}
}
