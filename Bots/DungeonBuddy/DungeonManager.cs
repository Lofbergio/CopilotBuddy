using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bots.DungeonBuddy.Attributes;
using Bots.DungeonBuddy.Profiles;
using Styx;
using Styx.Helpers;
using TreeSharp;

namespace Bots.DungeonBuddy
{
    /// <summary>
    /// Charge et gère les scripts de donjons.
    /// Les scripts .cs sont inclus dans le .csproj et compilés avec le projet.
    /// </summary>
    public static class DungeonManager
    {
        private static readonly Dictionary<uint, Type> _dungeonTypes = new();
        private static readonly Dictionary<uint, Dictionary<int, MethodInfo>> _encounterHandlers = new();
        private static readonly Dictionary<uint, Dictionary<int, MethodInfo>> _objectHandlers = new();
        
        private static Dungeon _currentDungeon;

        /// <summary>
        /// Donjon actif
        /// </summary>
        public static Dungeon CurrentDungeon => _currentDungeon;

        /// <summary>
        /// Charge tous les scripts de donjon via réflection sur l'assembly compilée.
        /// Les scripts .cs doivent être inclus dans le .csproj.
        /// </summary>
        public static void LoadDungeonScripts()
        {
            _dungeonTypes.Clear();
            _encounterHandlers.Clear();
            _objectHandlers.Clear();

            LoadDungeonTypes();

            Logging.Write($"[DungeonBuddy] Loaded {_dungeonTypes.Count} dungeon scripts");
        }

        /// <summary>
        /// Scanne l'assembly pour trouver tous les types héritant de Dungeon.
        /// NOTE: Les fichiers .cs des scripts doivent être inclus dans le .csproj
        /// pour être compilés. La compilation dynamique (comme HB) peut être ajoutée plus tard.
        /// Cette méthode est appelée une seule fois, pas par fichier.
        /// </summary>
        private static void LoadDungeonTypes()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var dungeonTypes = assembly.GetTypes()
                    .Where(t => t.IsSubclassOf(typeof(Dungeon)) && !t.IsAbstract);

                foreach (var type in dungeonTypes)
                {
                    try
                    {
                        var instance = (Dungeon)Activator.CreateInstance(type);
                        var dungeonId = instance.DungeonId;
                        instance.Dispose();

                        if (!_dungeonTypes.ContainsKey(dungeonId))
                        {
                            _dungeonTypes[dungeonId] = type;
                            IndexHandlers(type, dungeonId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.WriteDiagnostic($"[DungeonBuddy] Error loading {type.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDiagnostic($"[DungeonBuddy] Error scanning dungeon types: {ex.Message}");
            }
        }

        private static void IndexHandlers(Type dungeonType, uint dungeonId)
        {
            _encounterHandlers[dungeonId] = new Dictionary<int, MethodInfo>();
            _objectHandlers[dungeonId] = new Dictionary<int, MethodInfo>();

            foreach (var method in dungeonType.GetMethods())
            {
                // Index encounter handlers
                var encounterAttrs = method.GetCustomAttributes<EncounterHandlerAttribute>();
                foreach (var attr in encounterAttrs)
                {
                    _encounterHandlers[dungeonId][attr.BossEntry] = method;
                    BossManager.RegisterBoss((uint)attr.BossEntry, attr.BossName);
                }

                // Index object handlers
                var objectAttrs = method.GetCustomAttributes<ObjectHandlerAttribute>();
                foreach (var attr in objectAttrs)
                {
                    _objectHandlers[dungeonId][attr.ObjectEntry] = method;
                }
            }
        }

        /// <summary>
        /// Active le script de donjon approprié
        /// </summary>
        public static void SetDungeon(uint mapId)
        {
            // Détacher l'ancien donjon
            _currentDungeon?.Detach();
            _currentDungeon = null;

            // Trouver le type de donjon correspondant au MapId
            Type matchedType = null;
            foreach (var kvp in _dungeonTypes)
            {
                var dungeonId = kvp.Key;
                if (dungeonId == mapId || GetMapIdForDungeon(dungeonId) == mapId)
                {
                    matchedType = kvp.Value;
                    break;
                }
            }

            if (matchedType != null)
            {
                _currentDungeon = (Dungeon)Activator.CreateInstance(matchedType);
                _currentDungeon.Attach();
                BossManager.Initialize(_currentDungeon);
                Logging.Write($"[DungeonBuddy] Activated script: {_currentDungeon.Name}");
            }
            else
            {
                Logging.Write($"[DungeonBuddy] No script found for map {mapId}");
            }
        }

        /// <summary>
        /// Obtient le behavior pour un boss spécifique
        /// </summary>
        public static Composite GetEncounterBehavior(int bossEntryId)
        {
            if (_currentDungeon == null)
                return null;

            var dungeonId = _currentDungeon.DungeonId;
            
            if (_encounterHandlers.TryGetValue(dungeonId, out var handlers) &&
                handlers.TryGetValue(bossEntryId, out var method))
            {
                return method.Invoke(_currentDungeon, null) as Composite;
            }

            return null;
        }

        /// <summary>
        /// Obtient le behavior pour un objet spécifique
        /// </summary>
        public static Composite GetObjectBehavior(int objectEntryId)
        {
            if (_currentDungeon == null)
                return null;

            var dungeonId = _currentDungeon.DungeonId;
            
            if (_objectHandlers.TryGetValue(dungeonId, out var handlers) &&
                handlers.TryGetValue(objectEntryId, out var method))
            {
                return method.Invoke(_currentDungeon, null) as Composite;
            }

            return null;
        }

        private static uint GetMapIdForDungeon(uint lfgDungeonId)
        {
            // Mapping LFG DungeonId → MapId pour WotLK
            // Ces IDs viennent de LFG_Dungeons.dbc
            // Note: Les IDs normaux et héroïques mappent vers le même MapId
            // DungeonIds vérifiés depuis les 32 scripts dans Dungeon Scripts\Wrath of the Lich King\
            return lfgDungeonId switch
            {
                202 => 574,  // Utgarde Keep (Normal)
                242 => 574,  // Utgarde Keep (Heroic)
                203 => 575,  // Utgarde Pinnacle (Normal)
                205 => 575,  // Utgarde Pinnacle (Heroic)
                204 => 601,  // Azjol-Nerub (Normal)
                241 => 601,  // Azjol-Nerub (Heroic)
                206 => 578,  // The Oculus (Normal)
                211 => 578,  // The Oculus (Heroic)
                207 => 602,  // Halls of Lightning (Normal)
                212 => 602,  // Halls of Lightning (Heroic)
                208 => 599,  // Halls of Stone (Normal)
                213 => 599,  // Halls of Stone (Heroic)
                209 => 595,  // Culling of Stratholme (Normal)
                210 => 595,  // Culling of Stratholme (Heroic)
                214 => 600,  // Drak'Tharon Keep (Normal)
                215 => 600,  // Drak'Tharon Keep (Heroic)
                216 => 604,  // Gundrak (Normal)
                217 => 604,  // Gundrak (Heroic)
                218 => 619,  // Ahn'kahet (Normal)
                219 => 619,  // Ahn'kahet (Heroic)
                220 => 608,  // Violet Hold (Normal)
                221 => 608,  // Violet Hold (Heroic)
                225 => 576,  // The Nexus (Normal)
                226 => 576,  // The Nexus (Heroic)
                245 => 650,  // Trial of the Champion (Normal)
                249 => 650,  // Trial of the Champion (Heroic)
                251 => 632,  // Forge of Souls (Normal)
                252 => 632,  // Forge of Souls (Heroic)
                253 => 658,  // Pit of Saron (Normal)
                254 => 658,  // Pit of Saron (Heroic)
                255 => 668,  // Halls of Reflection (Normal)
                256 => 668,  // Halls of Reflection (Heroic)
                _ => 0
            };
        }

        public static void Clear()
        {
            _currentDungeon?.Detach();
            _currentDungeon = null;
        }
    }
}