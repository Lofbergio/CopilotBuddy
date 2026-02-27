using System;
using System.Diagnostics;
using System.Linq;
using Bots.DungeonBuddy.Avoidance;
using Bots.DungeonBuddy.Enums;
using CommonBehaviors.Actions;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Bots.DungeonBuddy
{
    /// <summary>
    /// DungeonBuddy - BotBase pour Dungeon Finder automatique
    /// WotLK 3.3.5a (patch 3.3 — Dungeon Finder ajouté)
    /// 
    /// State machine:
    ///   NotInLfg → SetRole + Queue → InQueue → Proposal → Accept → InDungeon
    ///   InDungeon → Combat/Follow → DungeonComplete → TeleportOut → Requeue
    ///   
    /// LFG state détecté via GetLFGMode() (API canonique WotLK 3.3).
    /// Events LFG via Lua.Events.AttachEvent (confirmé dans CopilotBuddy).
    /// </summary>
    public class DungeonBuddy : BotBase
    {
        public override string Name => "DungeonBuddy";
        public override bool IsPrimaryType => true;
        public override bool RequiresProfile => false;
        public override bool RequirementsMet => true;
        public override PulseFlags PulseFlags => PulseFlags.All;

        /// <summary>
        /// Return the configuration window used by DungeonBuddy.
        /// This allows the main UI's "Bot Config" button to work when
        /// DungeonBuddy is selected (previously ConfigurationForm was null).
        /// </summary>
        public override object ConfigurationForm => new Forms.FormConfig();

        private PrioritySelector _root;
        private static CombatRoutine Routine => RoutineManager.Current;
        
        // Timers
        private readonly Stopwatch _proposalDelay = new();
        private readonly Stopwatch _requeueDelay = new();
        private readonly Random _rng = new();
        private int _proposalWaitMs;  // Délai aléatoire avant AcceptProposal

        // State tracking
        private uint _lastMapId;
        private bool _hasSetRole;

        public override Composite Root => _root ??= CreateRootBehavior();

        // ═══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════

        public override void Start()
        {
            Logging.Write("[DungeonBuddy] Starting...");
            
            // Charger les scripts de donjon (réflection sur l'assembly)
            DungeonManager.LoadDungeonScripts();
            
            // Attacher les événements LFG
            LfgManager.AttachLfgEvents();
            
            _hasSetRole = false;
            _lastMapId = 0;
            
            Logging.Write("[DungeonBuddy] Started successfully!");
        }

        public override void Stop()
        {
            Logging.Write("[DungeonBuddy] Stopping...");
            
            LfgManager.DetachLfgEvents();
            DungeonManager.Clear();
            BossManager.Reset();
            Bots.DungeonBuddy.Avoidance.AvoidanceManager.Clear();
        }

        public override void Pulse()
        {
            // NOTE: HB 4.3.4 wrappe Root.Tick() dans un FrameLock (ObjectManager.Update + lock).
            // Si on observe des incohérences d'état (objets désync), envisager:
            //   using (StyxWoW.Memory.AcquireFrame()) { Root.Tick(...); }
            // À valider en jeu — pour l'instant on pulse sans FrameLock comme LevelBot.
            
            // Détecter changement de map (entrée/sortie donjon)
            var currentMap = (uint)StyxWoW.Me.MapId;
            if (currentMap != _lastMapId)
            {
                _lastMapId = currentMap;
                OnMapChanged(currentMap);
            }
            
            // Mettre à jour l'avoidance
            Bots.DungeonBuddy.Avoidance.AvoidanceManager.Update();
        }

        private void OnMapChanged(uint newMapId)
        {
            if (StyxWoW.Me.IsInInstance)
            {
                Logging.Write($"[DungeonBuddy] Entered instance (MapId={newMapId})");
                DungeonManager.SetDungeon(newMapId);
                LfgManager.DungeonCompleted = false;
            }
            else
            {
                Logging.Write($"[DungeonBuddy] Left instance");
                DungeonManager.Clear();
                BossManager.Reset();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // BEHAVIOR TREE
        // ═══════════════════════════════════════════════════════════

        private PrioritySelector CreateRootBehavior()
        {
            return new PrioritySelector(
                // 1. Death handling
                CreateDeathBehavior(),
                
                // 2. LFG State Machine (queue, proposal, teleport)
                CreateLfgBehavior(),
                
                // 3. IN DUNGEON: Avoidance (priorité sur combat)
                CreateAvoidanceBehavior(),
                
                // 4. IN DUNGEON: Combat (avec encounter handlers)
                CreateCombatBehavior(),
                
                // 5. IN DUNGEON: Loot
                CreateLootBehavior(),
                
                // 6. IN DUNGEON: Follow tank (si DPS/Healer)
                CreateFollowBehavior(),
                
                // 7. Idle
                new ActionIdle()
            );
        }

        // ═══════════════════════════════════════════════════════════
        // LFG STATE MACHINE
        // ═══════════════════════════════════════════════════════════

        private Composite CreateLfgBehavior()
        {
            return new PrioritySelector(
                // --- PROPOSAL: Accepter avec délai humain ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.Proposal,
                    new Sequence(
                        new Action(ctx =>
                        {
                            if (!_proposalDelay.IsRunning)
                            {
                                // Délai aléatoire 1-3 secondes (pattern HB anti-détection)
                                _proposalWaitMs = _rng.Next(1000, 3000);
                                _proposalDelay.Restart();
                                Logging.Write($"[DungeonBuddy] Proposal! Accepting in {_proposalWaitMs}ms...");
                            }
                            return RunStatus.Success;
                        }),
                        new WaitContinue(
                            TimeSpan.FromSeconds(5),
                            ctx => _proposalDelay.ElapsedMilliseconds >= _proposalWaitMs,
                            new Action(ctx =>
                            {
                                LfgManager.AcceptProposal();
                                LfgManager.ProposalPending = false;
                                _proposalDelay.Reset();
                                return RunStatus.Success;
                            })
                        )
                    )
                ),

                // --- ROLE CHECK: Accepter automatiquement ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.RoleCheck,
                    new Action(ctx =>
                    {
                        Logging.Write("[DungeonBuddy] Role check — accepting...");
                        Lua.DoString("LFDRoleCheckPopupAcceptButton:Click()");
                        return RunStatus.Success;
                    })
                ),

                // --- IN DUNGEON: Dungeon completed → teleport out + requeue ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.InDungeon &&
                           LfgManager.DungeonCompleted &&
                           DungeonBuddySettings.Instance.AutoRequeue,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Logging.Write("[DungeonBuddy] Dungeon complete! Teleporting out...");
                            LfgManager.TeleportOut();
                            LfgManager.DungeonCompleted = false;
                            _requeueDelay.Restart();
                            return RunStatus.Success;
                        }),
                        new WaitContinue(
                            TimeSpan.FromSeconds(10),
                            ctx => !StyxWoW.Me.IsInInstance,
                            new ActionAlwaysSucceed()
                        )
                    )
                ),

                // --- ABANDONED IN DUNGEON: Teleport out ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.AbandonedInDungeon,
                    new Action(ctx =>
                    {
                        Logging.Write("[DungeonBuddy] Abandoned in dungeon, teleporting out...");
                        LfgManager.TeleportOut();
                        return RunStatus.Success;
                    })
                ),

                // --- NOT IN LFG: Set role + Queue ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.NotInLfg &&
                           DungeonBuddySettings.Instance.Mode == DungeonMode.LookingForGroup,
                    new Sequence(
                        // Set role si pas encore fait
                        new DecoratorContinue(
                            ctx => !_hasSetRole,
                            new Action(ctx =>
                            {
                                var role = DungeonBuddySettings.Instance.PreferredRole;
                                LfgManager.SetRole(role);
                                _hasSetRole = true;
                                Logging.Write($"[DungeonBuddy] Role set to {role}");
                                return RunStatus.Success;
                            })
                        ),
                        // Attendre un peu après teleport out avant requeue
                        new Decorator(
                            ctx => !_requeueDelay.IsRunning || _requeueDelay.ElapsedMilliseconds > 3000,
                            new Action(ctx =>
                            {
                                var settings = DungeonBuddySettings.Instance;
                                switch (settings.QueueType)
                                {
                                    case QueueType.RandomDungeon:
                                        LfgManager.QueueForRandomDungeon();
                                        break;
                                    case QueueType.RandomHeroic:
                                        LfgManager.QueueForRandomHeroic();
                                        break;
                                    case QueueType.Specific:
                                        if (settings.SelectedDungeonIds.Length > 0)
                                            LfgManager.QueueForSpecificDungeon(settings.SelectedDungeonIds[0]);
                                        break;
                                }
                                _requeueDelay.Reset();
                                return RunStatus.Success;
                            })
                        )
                    )
                ),

                // --- IN QUEUE: Idle, afficher timer ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.InQueue,
                    new ActionAlwaysFail()
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // DEATH BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateDeathBehavior()
        {
            return new PrioritySelector(
                new Decorator(
                    ctx => StyxWoW.Me.IsDead,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Logging.Write("[DungeonBuddy] Died! Releasing...");
                            Lua.DoString("RepopMe()");
                        }),
                        new WaitContinue(5, ctx => StyxWoW.Me.IsGhost, new ActionAlwaysSucceed())
                    )
                ),
                new Decorator(
                    ctx => StyxWoW.Me.IsGhost && StyxWoW.Me.IsInInstance,
                    new Sequence(
                        new Action(ctx =>
                        {
                            var entrance = DungeonManager.CurrentDungeon?.Entrance ?? WoWPoint.Empty;
                            if (entrance != WoWPoint.Empty)
                                Navigator.MoveTo(entrance);
                            return RunStatus.Running;
                        })
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // AVOIDANCE BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateAvoidanceBehavior()
        {
            return new Decorator(
                ctx => StyxWoW.Me.IsInInstance && 
                       Bots.DungeonBuddy.Avoidance.AvoidanceManager.IsInAvoidance(StyxWoW.Me.Location),
                new Action(ctx =>
                {
                    var safePoint = Bots.DungeonBuddy.Avoidance.AvoidanceManager.GetSafePoint(StyxWoW.Me.Location);
                    Navigator.MoveTo(safePoint);
                    return RunStatus.Running;
                })
            );
        }

        // ═══════════════════════════════════════════════════════════
        // COMBAT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        // Champ pour tracker le behavior d'encounter actif et le boss associé
        // IMPORTANT: Le Composite NE DOIT PAS être re-Start() à chaque pulse.
        // Start() réinitialise l'état interne (Sequences, WaitContinue, etc.)
        // On doit Start() UNE SEULE FOIS quand le boss change, puis Tick() à chaque pulse.
        // Référence: HB 4.3.4 construit les encounter behaviors dans le Root tree
        // via réflection, pas manuellement. Ici on simule ce pattern.
        private Composite _activeEncounterBehavior;
        private uint _activeEncounterBossEntry;

        private Composite CreateCombatBehavior()
        {
            return new PrioritySelector(
                // Rest si hors combat
                new Decorator(
                    ctx => StyxWoW.Me.IsInInstance && !StyxWoW.Me.Combat && 
                           Routine?.RestBehavior != null,
                    Routine.RestBehavior
                ),
                // Encounter handler pour boss
                new Decorator(
                    ctx => StyxWoW.Me.IsInInstance && StyxWoW.Me.Combat &&
                           StyxWoW.Me.CurrentTarget != null,
                    new PrioritySelector(
                        // Vérifier si un encounter handler existe pour la cible
                        new Decorator(
                            ctx => StyxWoW.Me.CurrentTarget.IsBoss,
                            new Action(ctx =>
                            {
                                var boss = StyxWoW.Me.CurrentTarget;
                                
                                // Si le boss a changé, charger le nouveau behavior
                                if (boss.Entry != _activeEncounterBossEntry)
                                {
                                    // Stop l'ancien behavior proprement
                                    if (_activeEncounterBehavior != null)
                                    {
                                        try { _activeEncounterBehavior.Stop(boss); } catch { }
                                        _activeEncounterBehavior = null;
                                    }
                                    
                                    _activeEncounterBossEntry = boss.Entry;
                                    _activeEncounterBehavior = DungeonManager.GetEncounterBehavior(
                                        (int)boss.Entry);
                                    
                                    // Start() UNE SEULE FOIS pour initialiser le Composite
                                    if (_activeEncounterBehavior != null)
                                    {
                                        // Passer le boss comme contexte car les scripts font
                                        // "ctx => boss = ctx as WoWUnit" dans leur PrioritySelector.
                                        _activeEncounterBehavior.Start(boss);
                                    }
                                }
                                
                                if (_activeEncounterBehavior != null)
                                {
                                    // Tick() à chaque pulse avec le boss comme contexte
                                    var result = _activeEncounterBehavior.Tick(boss);
                                    if (result != RunStatus.Running)
                                    {
                                        // Encounter terminé → cleanup
                                        _activeEncounterBehavior.Stop(boss);
                                        _activeEncounterBehavior = null;
                                        _activeEncounterBossEntry = 0;
                                    }
                                    return result;
                                }
                                return RunStatus.Failure;
                            })
                        ),
                        // Combat normal via CombatRoutine
                        Routine?.CombatBehavior ?? new ActionAlwaysFail()
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // LOOT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateLootBehavior()
        {
            return new Decorator(
                ctx => StyxWoW.Me.IsInInstance && !StyxWoW.Me.Combat &&
                       DungeonBuddySettings.Instance.LootMode != LootMode.Never,
                new PrioritySelector(
                    // Loot boss uniquement ou tout
                    new Decorator(
                        ctx =>
                        {
                            var lootable = ObjectManager.GetObjectsOfType<WoWUnit>()
                                .Where(u => u.IsDead && u.CanLoot && u.DistanceSqr < 50 * 50);
                            
                            if (DungeonBuddySettings.Instance.LootMode == LootMode.BossesOnly)
                                lootable = lootable.Where(u => u.IsBoss);
                            
                            return lootable.Any();
                        },
                        new Action(ctx =>
                        {
                            var target = ObjectManager.GetObjectsOfType<WoWUnit>()
                                .Where(u => u.IsDead && u.CanLoot)
                                .OrderBy(u => u.DistanceSqr)
                                .First();
                            
                            if (target.DistanceSqr > 5 * 5)
                            {
                                Navigator.MoveTo(target.Location);
                                return RunStatus.Running;
                            }
                            
                            target.Interact();
                            return RunStatus.Success;
                        })
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // FOLLOW BEHAVIOR (DPS/Healer suit le Tank)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateFollowBehavior()
        {
            return new Decorator(
                ctx => StyxWoW.Me.IsInInstance && !StyxWoW.Me.Combat &&
                       !StyxWoW.Me.IsTank(), // IsTank() = extension method (role-based via UnitGroupRolesAssigned)
                new Action(ctx =>
                {
                    var tank = Helpers.ScriptHelpers.Tank;
                    if (tank == null || !tank.IsAlive)
                        return RunStatus.Failure;
                    
                    float followDist = DungeonBuddySettings.Instance.FollowDistance;
                    if (StyxWoW.Me.Location.DistanceSqr(tank.Location) > followDist * followDist)
                    {
                        Navigator.MoveTo(tank.Location);
                        return RunStatus.Running;
                    }
                    
                    return RunStatus.Failure;
                })
            );
        }
    }
}