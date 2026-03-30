using System.Collections.Generic;
using System.Linq;
using Styx;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Bots.DungeonBuddy.Profiles;

namespace Bots.DungeonBuddy
{
    /// <summary>
    /// Gère les boss du donjon actuel.
    /// Track les boss tués et le boss actuel à target.
    /// </summary>
    public static class BossManager
    {
        private static readonly HashSet<uint> _killedBossIds = new();
        private static readonly List<BossInfo> _bosses = new();

        public class BossInfo
        {
            public uint EntryId { get; set; }
            public string Name { get; set; }
            public bool IsOptional { get; set; }
            public bool IsDead { get; set; }
        }

        /// <summary>
        /// Boss actuel à tuer (le plus proche, non-mort)
        /// </summary>
        public static WoWUnit CurrentBoss
        {
            get
            {
                return ObjectManager.GetObjectsOfType<WoWUnit>()
                    .Where(u => u.IsBoss && u.IsAlive && !_killedBossIds.Contains(u.Entry))
                    .OrderBy(u => u.DistanceSqr)
                    .FirstOrDefault();
            }
        }

        /// <summary>
        /// Liste de tous les boss du donjon
        /// </summary>
        public static IReadOnlyList<BossInfo> Bosses => _bosses;

        /// <summary>
        /// Initialise les boss pour le donjon actuel
        /// </summary>
        public static void Initialize(Dungeon dungeon)
        {
            _killedBossIds.Clear();
            _bosses.Clear();
            
            // Les boss sont découverts dynamiquement via les handlers
        }

        /// <summary>
        /// Marque un boss comme tué
        /// </summary>
        public static void MarkBossDead(uint entryId)
        {
            _killedBossIds.Add(entryId);
            
            var boss = _bosses.FirstOrDefault(b => b.EntryId == entryId);
            if (boss != null)
                boss.IsDead = true;
        }

        /// <summary>
        /// Register un boss (appelé par DungeonManager lors du chargement des scripts)
        /// </summary>
        public static void RegisterBoss(uint entryId, string name, bool isOptional = false)
        {
            if (!_bosses.Any(b => b.EntryId == entryId))
            {
                _bosses.Add(new BossInfo
                {
                    EntryId = entryId,
                    Name = name,
                    IsOptional = isOptional,
                    IsDead = false
                });
            }
        }

        /// <summary>
        /// Marque un boss comme tué (par nom, utilisé par l'UI)
        /// </summary>
        public static void MarkBossDead(string name)
        {
            var boss = _bosses.FirstOrDefault(b => b.Name == name);
            if (boss != null)
            {
                boss.IsDead = true;
                _killedBossIds.Add(boss.EntryId);
            }
        }

        /// <summary>
        /// Remet un boss à vivant (par nom, utilisé par l'UI)
        /// </summary>
        public static void ResetBoss(string name)
        {
            var boss = _bosses.FirstOrDefault(b => b.Name == name);
            if (boss != null)
            {
                boss.IsDead = false;
                _killedBossIds.Remove(boss.EntryId);
            }
        }

        /// <summary>
        /// Vérifie si tous les boss obligatoires sont morts
        /// </summary>
        public static bool AreAllRequiredBossesDead()
        {
            return _bosses.Where(b => !b.IsOptional).All(b => b.IsDead);
        }

        /// <summary>
        /// Reset pour nouveau donjon
        /// </summary>
        public static void Reset()
        {
            _killedBossIds.Clear();
            _bosses.Clear();
        }
    }
}