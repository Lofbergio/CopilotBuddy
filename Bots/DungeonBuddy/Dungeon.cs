using System;
using System.Collections.Generic;
using System.Linq;
using Bots.DungeonBuddy.Avoidance;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.WoWInternals.WoWObjects;

namespace Bots.DungeonBuddy.Profiles
{
    /// <summary>
    /// Classe de base pour tous les scripts de donjon.
    /// Chaque donjon hérite de cette classe.
    /// NOTE: Namespace = Bots.DungeonBuddy.Profiles pour compatibilité avec les 32 scripts
    /// existants qui font "using Bots.DungeonBuddy.Profiles;"
    /// </summary>
    public abstract class Dungeon : IDisposable
    {
        private readonly List<AvoidInfo> _avoidInfos = new();
        private bool _disposed;

        /// <summary>
        /// Nom du donjon (affiché depuis LFG_Dungeons.dbc)
        /// </summary>
        public virtual string Name => DungeonId > 0 ? $"Dungeon {DungeonId}" : "Unknown";

        /// <summary>
        /// ID du donjon dans LFG_Dungeons.dbc
        /// </summary>
        public abstract uint DungeonId { get; }

        /// <summary>
        /// Position de l'entrée du donjon (pour corpse run)
        /// </summary>
        public virtual WoWPoint Entrance => WoWPoint.Empty;

        /// <summary>
        /// Position de la sortie du donjon
        /// </summary>
        public virtual WoWPoint ExitLocation => WoWPoint.Empty;

        /// <summary>
        /// True si le corpse run peut se faire en volant
        /// </summary>
        public virtual bool IsFlyingCorpseRun => false;

        /// <summary>
        /// True si le donjon est terminé (tous les boss tués)
        /// </summary>
        public virtual bool IsComplete => BossManager.CurrentBoss == null;

        // ═══════════════════════════════════════════════════════════
        // TARGETING FILTERS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Filtre de ciblage: Ajouter des cibles
        /// </summary>
        public virtual void IncludeTargetsFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
        {
        }

        /// <summary>
        /// Filtre de ciblage: Retirer des cibles
        /// </summary>
        public virtual void RemoveTargetsFilter(List<WoWObject> units)
        {
        }

        /// <summary>
        /// Filtre de ciblage: Modifier les priorités
        /// </summary>
        public virtual void WeighTargetsFilter(List<Targeting.TargetPriority> units)
        {
        }

        // ═══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Appelé quand le joueur entre dans le donjon
        /// </summary>
        internal void Attach()
        {
            Targeting.Instance.IncludeTargetsFilter += IncludeTargetsFilter;
            Targeting.Instance.WeighTargetsFilter += WeighTargetsFilter;
            Targeting.Instance.RemoveTargetsFilter += RemoveTargetsFilter;
            
            // Ajouter les avoids
            Bots.DungeonBuddy.Avoidance.AvoidanceManager.AddRange(_avoidInfos.Where(a => !Bots.DungeonBuddy.Avoidance.AvoidanceManager.AvoidInfos.Contains(a)));
            
            OnEnter();
        }

        /// <summary>
        /// Appelé quand le joueur quitte le donjon
        /// </summary>
        internal void Detach()
        {
            Targeting.Instance.IncludeTargetsFilter -= IncludeTargetsFilter;
            Targeting.Instance.WeighTargetsFilter -= WeighTargetsFilter;
            Targeting.Instance.RemoveTargetsFilter -= RemoveTargetsFilter;
            
            // Retirer les avoids
            Bots.DungeonBuddy.Avoidance.AvoidanceManager.RemoveAll(a => _avoidInfos.Contains(a));
            
            OnExit();
        }

        /// <summary>
        /// Override pour logique d'entrée custom
        /// </summary>
        public virtual void OnEnter()
        {
        }

        /// <summary>
        /// Override pour logique de sortie custom
        /// </summary>
        public virtual void OnExit()
        {
        }

        // ═══════════════════════════════════════════════════════════
        // AVOIDANCE HELPERS
        // ═══════════════════════════════════════════════════════════

        protected void AddAvoid(AvoidInfo avoidInfo)
        {
            Bots.DungeonBuddy.Avoidance.AvoidanceManager.Add(avoidInfo);
            _avoidInfos.Add(avoidInfo);
        }

        protected void AddAvoidRange(IEnumerable<AvoidInfo> avoidInfos)
        {
            foreach (var a in avoidInfos)
                AddAvoid(a);
        }

        // ═══════════════════════════════════════════════════════════
        // IDISPOSABLE
        // ═══════════════════════════════════════════════════════════

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        ~Dungeon()
        {
            Dispose();
        }
    }
}