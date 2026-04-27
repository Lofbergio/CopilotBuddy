using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Styx;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Patchables;
using Styx.WoWInternals;
using Styx.WoWInternals.DBC;

namespace Bots.DungeonBuddy.Profiles
{
    /// <summary>
    /// Gère les profils XML de DungeonBuddy.
    /// Les profils XML sont placés dans Default Profiles\DungeonBuddy\ à côté de l'exe.
    /// Portage de HB 4.3.4 Bots\DungeonBuddy\Profiles\ProfileManager.cs.
    /// </summary>
    public static class ProfileManager
    {
        private static readonly string _profilesPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Default Profiles", "DungeonBuddy");

        // HB 4.3.4: string_0 = Path.Combine(Logging.ApplicationPath, "Default Profiles\\DungeonBuddy\\")
        public static DungeonProfile CurrentProfile { get; private set; }

        /// <summary>
        /// Scanne Default Profiles\DungeonBuddy\ pour trouver le profil XML correspondant au dungeonId.
        /// </summary>
        public static void LoadProfileForDungeon(uint dungeonId)
        {
            if (!Directory.Exists(_profilesPath))
            {
                Logging.Write("[DungeonBuddy] Profiles folder not found: {0}", _profilesPath);
                return;
            }

            string[] files = Directory.GetFiles(_profilesPath, "*.xml", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                try
                {
                    var root = XElement.Load(file);
                    var dungeonIdEl = root.Element("DungeonId");
                    if (dungeonIdEl == null ||
                        !uint.TryParse(dungeonIdEl.Value, out uint id) ||
                        id != dungeonId)
                    {
                        continue;
                    }

                    UnloadProfile();
                    CurrentProfile = DungeonProfile.Load(root);

                    if (CurrentProfile.Blackspots.Count > 0)
                        BlackspotManager.AddBlackspots(CurrentProfile.Blackspots);

                    Logging.Write("[DungeonBuddy] Loaded profile: {0} ({1} hotspots, {2} blackspots)",
                        Path.GetFileNameWithoutExtension(file),
                        CurrentProfile.HotSpots.Count,
                        CurrentProfile.Blackspots.Count);
                    return;
                }
                catch (Exception ex)
                {
                    Logging.WriteDiagnostic("[DungeonBuddy] Error reading profile {0}: {1}", file, ex.Message);
                }
            }

            Logging.Write("[DungeonBuddy] No profile found for dungeonId: {0}", dungeonId);
        }

        /// <summary>
        /// Charge un profil directement depuis un chemin XML (utilisé par FormConfig).
        /// </summary>
        public static void LoadFromPath(string path)
        {
            try
            {
                var root = XElement.Load(path);
                UnloadProfile();
                CurrentProfile = DungeonProfile.Load(root);

                if (CurrentProfile.Blackspots.Count > 0)
                    BlackspotManager.AddBlackspots(CurrentProfile.Blackspots);

                Logging.Write("[DungeonBuddy] Loaded profile: {0} ({1} hotspots, {2} blackspots)",
                    System.IO.Path.GetFileNameWithoutExtension(path),
                    CurrentProfile.HotSpots.Count,
                    CurrentProfile.Blackspots.Count);
            }
            catch (Exception ex)
            {
                Logging.WriteDiagnostic("[DungeonBuddy] Error loading profile {0}: {1}", path, ex.Message);
            }
        }

        public static void UnloadProfile()
        {
            if (CurrentProfile != null && CurrentProfile.Blackspots.Count > 0)
                BlackspotManager.RemoveBlackspots(CurrentProfile.Blackspots);
            CurrentProfile = null;
        }

        /// <summary>
        /// HB 4.3.4: GetLfgDungeonIdFromMapId
        /// Recherche l'ID du donjon LFG correspondant à un MapId et une difficulté.
        /// Utilise LfgDungeons.dbc pour la correspondance.
        /// </summary>
        /// <param name="mapId">MapId du donjon</param>
        /// <returns>LFG Dungeon ID</returns>
        /// <exception cref="InstanceNotFoundException">Aucun donjon ne correspond au mapId+difficulty</exception>
        public static uint GetLfgDungeonIdFromMapId(uint mapId)
        {
            int difficultyIndex = LfgManager.CurrentDifficulty - 1;

            var table = StyxWoW.Db?[ClientDb.LfgDungeons];
            if (table == null)
                throw new InstanceNotFoundException("Unable to find LfgDungeon for mapId " + mapId);

            uint minIndex = (uint)table.MinIndex;
            uint maxIndex = (uint)table.MaxIndex;

            LfgDungeons[] candidates = null;
            int count = 0;

            for (uint i = minIndex; i <= maxIndex; i++)
            {
                var dungeon = new LfgDungeons(i);
                if (!dungeon.IsValid || dungeon.IsHolidayEvent)
                    continue;
                if (dungeon.MapId == (int)mapId && dungeon.Difficulty == (uint)difficultyIndex)
                {
                    if (candidates == null)
                        candidates = new LfgDungeons[8];
                    if (count < candidates.Length)
                        candidates[count++] = dungeon;
                }
            }

            if (count == 0)
                throw new InstanceNotFoundException("Unable to find LfgDungeon for mapId " + mapId);

            if (count == 1)
                return candidates[0].Id;

            uint dungeonLevel = Lua.GetReturnVal<uint>("return GetCurrentMapDungeonLevel() or 0", 0);
            for (int i = 0; i < count; i++)
            {
                if (candidates[i].RecommendedLevel != dungeonLevel)
                    candidates[i] = null;
            }
            for (int i = 0; i < count; i++)
            {
                if (candidates[i] != null)
                    return candidates[i].Id;
            }
            throw new InstanceNotFoundException("Unable to find LfgDungeon for mapId " + mapId);
        }
    }
}
