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
using Styx.Logic.BehaviorTree;
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

        private PrioritySelector? _root;
        private static CombatRoutine Routine => RoutineManager.Current;
        
        // Timers
        private readonly Stopwatch _proposalDelay = new();
        private readonly Stopwatch _requeueDelay = new();
        private readonly Random _rng = new();
        private int _proposalWaitMs;  // Délai aléatoire avant AcceptProposal

        // State tracking
        private uint _lastMapId;
        private bool _hasSetRole;

        // Active dungeon behavior — updated when dungeon changes (fixes stale reference at tree-build time)
        private Composite? _activeDungeonBehavior;

        // Debug throttle — log SoloFarm condition once per 3s to avoid spam
        private readonly WaitTimer _dbgSoloFarmThrottle = new WaitTimer(TimeSpan.FromSeconds(3));

        public override Composite Root => _root ??= CreateRootBehavior();

        // ═══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════

        public override void Start()
        {
            Logging.Write("[DungeonBuddy] Starting...");
            
            var settings = DungeonBuddySettings.Instance;
            Logging.Write($"[DungeonBuddy] Config: QueueType={settings.QueueType}, SelectedDungeons=[{string.Join(",", settings.SelectedDungeonIds)}]");
            
            // Charger les scripts de donjon (réflection sur l'assembly)
            DungeonManager.LoadDungeonScripts();
            
            // Attacher les événements LFG
            LfgManager.AttachLfgEvents();
            
            _hasSetRole = false;
            _lastMapId = StyxWoW.Me.MapId;
            _activeDungeonBehavior = null;
            
            // SoloFarm: if already inside the selected dungeon, activate the script immediately
            if (settings.QueueType == QueueType.SoloFarm &&
                settings.SelectedDungeonIds.Length > 0 &&
                StyxWoW.Me.IsInInstance)
            {
                DungeonManager.SetDungeonById(settings.SelectedDungeonIds[0]);
            }
            
            Logging.Write("[DungeonBuddy] Started successfully!");
        }

        public override void Stop()
        {
            Logging.Write("[DungeonBuddy] Stopping...");
            
            LfgManager.DetachLfgEvents();
            DungeonManager.Clear();
            BossManager.Reset();
            Bots.DungeonBuddy.Avoidance.AvoidanceManager.Clear();
            _activeDungeonBehavior = null;
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
                
                var settings = DungeonBuddySettings.Instance;
                if (settings.QueueType == QueueType.SoloFarm && settings.SelectedDungeonIds.Length > 0)
                {
                    // SoloFarm: player walked in manually — use the selected dungeon ID directly.
                    // LfgManager.CurrentLfgDungeonId == 0 in SoloFarm (no LFG queue), so
                    // GetLfgDungeonIdFromMapId fallback can't be relied on.
                    DungeonManager.SetDungeonById(settings.SelectedDungeonIds[0]);
                }
                else
                {
                    DungeonManager.SetDungeon(newMapId);
                }
                
                _activeDungeonBehavior = null;  // force re-Start on the new dungeon behavior
                LfgManager.DungeonCompleted = false;
            }
            else
            {
                Logging.Write($"[DungeonBuddy] Left instance");
                DungeonManager.Clear();
                BossManager.Reset();
                _activeDungeonBehavior = null;
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

                // 3. SOLO FARM: Move to dungeon entrance
                CreateSoloFarmBehavior(),

                // 4. IN DUNGEON: Avoidance
                CreateAvoidanceBehavior(),

                // 5. IN DUNGEON: Dungeon script behavior
                CreateDungeonBehavior(),

                // 6. IN DUNGEON: Combat (encounter handlers via DungeonManager)
                CreateCombatBehavior(),

                // 6. IN DUNGEON: Loot
                CreateLootBehavior(),

                // 8. Follow tank
                CreateFollowBehavior(),

                // 9. Idle
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
                           LfgManager.DungeonCompleted,
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
                           DungeonBuddySettings.Instance.QueueType != QueueType.SoloFarm,
                    new Sequence(
                        // Set role si pas encore fait
                        new DecoratorContinue(
                            ctx => !_hasSetRole,
                            new Action(ctx =>
                            {
                                LfgManager.SetRole(PartyRole.Dps);
                                _hasSetRole = true;
                                Logging.Write("[DungeonBuddy] Role set to Dps");
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
                                _requeueDelay.Restart();
                                return RunStatus.Success;
                            })
                        )
                    )
                ),

                // --- IN QUEUE: Idle, afficher timer ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.InQueue,
                    new ActionAlwaysFail()
                ),

                // Debug: log when LFG behavior falls through completely
                new Action(ctx =>
                {
                    Logging.WriteDebug($"[DB:LFG] fallthrough — LfgState={LfgManager.CurrentState} QueueType={DungeonBuddySettings.Instance.QueueType}");
                    return RunStatus.Failure;
                })
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
                            var entrance = DungeonManager.CurrentDungeon?.Entrance ?? WoWPoint.Zero;
                            if (entrance != WoWPoint.Zero)
                                Navigator.MoveTo(entrance);
                            return RunStatus.Running;
                        })
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // SOLO FARM BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateSoloFarmBehavior()
        {
            return new Decorator(
                ctx =>
                {
                    var me = StyxWoW.Me;
                    if (me == null) return false;
                    bool notDungeon = !me.CurrentMap.IsDungeon;
                    bool isSoloFarm = DungeonBuddySettings.Instance.QueueType == QueueType.SoloFarm;
                    if (_dbgSoloFarmThrottle.IsFinished)
                    {
                        _dbgSoloFarmThrottle.Reset();
                        Logging.WriteDebug($"[DB:SoloFarm] cond: notDungeon={notDungeon} isSoloFarm={isSoloFarm} MapId={me.MapId} MapType={me.CurrentMap.MapType} IsInInstance={me.IsInInstance}");
                    }
                    return notDungeon && isSoloFarm;
                },
                new Sequence(
                    new Action(ctx =>
                    {
                        var settings = DungeonBuddySettings.Instance;
                        if (settings.SelectedDungeonIds.Length == 0)
                        {
                            Logging.WriteDebug("[DB:SoloFarm] No dungeon selected in settings");
                            TreeRoot.StatusText = "SoloFarm: No dungeon selected";
                            return RunStatus.Failure;
                        }

                        uint selectedDungeonId = settings.SelectedDungeonIds[0];
                        Logging.WriteDebug($"[DB:SoloFarm] selectedDungeonId={selectedDungeonId}");

                        // Set CurrentDungeon if not set or wrong dungeon
                        if (DungeonManager.CurrentDungeon == null ||
                            DungeonManager.CurrentDungeon.DungeonId != selectedDungeonId)
                        {
                            Logging.WriteDebug($"[DB:SoloFarm] Calling SetDungeonById({selectedDungeonId})");
                            DungeonManager.SetDungeonById(selectedDungeonId);
                        }

                        if (DungeonManager.CurrentDungeon == null)
                        {
                            Logging.WriteDebug($"[DB:SoloFarm] CurrentDungeon still null after SetDungeonById");
                            TreeRoot.StatusText = "SoloFarm: Dungeon not found in scripts";
                            return RunStatus.Failure;
                        }

                        var entrance = DungeonManager.CurrentDungeon.Entrance;
                        Logging.WriteDebug($"[DB:SoloFarm] entrance={entrance} dungeon={DungeonManager.CurrentDungeon.Name}");
                        if (entrance == WoWPoint.Zero)
                        {
                            Logging.WriteDebug($"[DB:SoloFarm] Entrance is Zero for {DungeonManager.CurrentDungeon.Name}");
                            TreeRoot.StatusText = $"SoloFarm: No entrance for {DungeonManager.CurrentDungeon.Name}";
                            return RunStatus.Failure;
                        }

                        float distSq = StyxWoW.Me.Location.DistanceSqr(entrance);
                        Logging.WriteDebug($"[DB:SoloFarm] distSq={distSq:F1} MyPos={StyxWoW.Me.Location}");
                        if (distSq > 5 * 5)
                        {
                            TreeRoot.StatusText = $"SoloFarm: Moving to {DungeonManager.CurrentDungeon.Name} entrance";
                            Navigator.MoveTo(entrance);
                            return RunStatus.Running;
                        }

                        TreeRoot.StatusText = $"SoloFarm: At {DungeonManager.CurrentDungeon.Name} entrance - enter manually";
                        return RunStatus.Success;
                    })
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // AVOIDANCE BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateAvoidanceBehavior()
        {
            return new Decorator(
                ctx =>
                {
                    var me = StyxWoW.Me;
                    return me != null && me.IsInInstance &&
                           Bots.DungeonBuddy.Avoidance.AvoidanceManager.IsInAvoidance(me.Location);
                },
                new Action(ctx =>
                {
                    var me = StyxWoW.Me;
                    if (me == null)
                        return RunStatus.Failure;

                    var safePoint = Bots.DungeonBuddy.Avoidance.AvoidanceManager.GetSafePoint(me.Location);
                    Navigator.MoveTo(safePoint);
                    return RunStatus.Running;
                })
            );
        }

        private Composite CreateDungeonBehavior()
        {
            // IMPORTANT: do NOT pass DungeonManager.CurrentDungeonBehavior as a constructor argument —
            // that evaluates the property ONCE at tree-build time (when CurrentDungeon is still null),
            // storing a permanent empty PrioritySelector.  Instead, re-evaluate each tick via Action
            // and manage Start/Stop manually using the _activeDungeonBehavior instance field.
            return new Decorator(
                ctx =>
                {
                    var me = StyxWoW.Me;
                    return me != null && me.IsInInstance && DungeonManager.CurrentDungeon != null;
                },
                new Action(ctx =>
                {
                    var current = DungeonManager.CurrentDungeonBehavior;
                    if (current == null)
                        return RunStatus.Failure;

                    // Call Start() once when the behavior instance changes (new dungeon loaded).
                    // HB equivalent: composite_0, nulled only on dungeon change (method_41/method_43),
                    // not on tick result. Do NOT Stop/null here on Failure — that would cause a
                    // tight Start/Stop loop every pulse when the script has nothing to do.
                    if (_activeDungeonBehavior != current)
                    {
                        _activeDungeonBehavior?.Stop(ctx);
                        _activeDungeonBehavior = current;
                        _activeDungeonBehavior.Start(ctx);
                    }

                    return _activeDungeonBehavior.Tick(ctx);
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
        private Composite? _activeEncounterBehavior;
        private uint _activeEncounterBossEntry;

        private Composite CreateCombatBehavior()
        {
            // HB 4.3.4 DungeonBot.method_0() pattern:
            //   Non-combat: Rest → PreCombatBuff → [find target, move, pull]
            //   In combat:  [boss encounter handler] → CombatBehavior
            return new PrioritySelector(
                // ── Hors combat ────────────────────────────────────────────
                new Decorator(
                    ctx => StyxWoW.Me.IsInInstance && !StyxWoW.Me.Combat,
                    new PrioritySelector(
                        // Se reposer si HP/mana faible
                        Routine?.RestBehavior ?? new ActionAlwaysFail(),
                        // Buffs pré-combat (Lightning Shield, etc.)
                        Routine?.PreCombatBuffBehavior ?? new ActionAlwaysFail(),
                        // Trouver une cible hostile, se rapprocher et pull
                        new Sequence(
                            new Action(ctx =>
                            {
                                // Mettre à jour la liste de cibles via le système Targeting
                                Targeting.Instance.Pulse();
                                var target = Targeting.Instance.FirstUnit;
                                if (target == null)
                                    return RunStatus.Failure;
                                // Cibler uniquement si ce n'est pas déjà la cible actuelle
                                if (StyxWoW.Me.CurrentTargetGuid != target.Guid)
                                    target.Target();
                                return RunStatus.Success;
                            }),
                            // Se déplacer vers la cible si hors portée de mêlée
                            new Decorator(
                                ctx => StyxWoW.Me.CurrentTarget != null &&
                                       StyxWoW.Me.CurrentTarget.DistanceSqr > 5f * 5f,
                                new Action(ctx =>
                                {
                                    Navigator.MoveTo(StyxWoW.Me.CurrentTarget.Location);
                                    return RunStatus.Running;
                                })
                            ),
                            // Pull via la routine de combat
                            Routine?.PullBehavior ?? new ActionAlwaysFail()
                        )
                    )
                ),
                // ── En combat ──────────────────────────────────────────────
                new Decorator(
                    ctx => StyxWoW.Me.IsInInstance && StyxWoW.Me.Combat,
                    new PrioritySelector(
                        // Encounter handler spécifique si c'est un boss
                        new Decorator(
                            ctx => StyxWoW.Me.CurrentTarget != null && StyxWoW.Me.CurrentTarget.IsBoss,
                            new Action(ctx =>
                            {
                                var boss = StyxWoW.Me.CurrentTarget;

                                if (boss.Entry != _activeEncounterBossEntry)
                                {
                                    try { _activeEncounterBehavior?.Stop(boss); } catch { }
                                    _activeEncounterBehavior = null;
                                    _activeEncounterBossEntry = boss.Entry;
                                    _activeEncounterBehavior = DungeonManager.GetEncounterBehavior((int)boss.Entry);
                                    _activeEncounterBehavior?.Start(boss);
                                }

                                if (_activeEncounterBehavior != null)
                                {
                                    var result = _activeEncounterBehavior.Tick(boss);
                                    if (result != RunStatus.Running)
                                    {
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
                    
                    float followDist = DungeonBuddySettings.Instance.FollowingDistance;
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