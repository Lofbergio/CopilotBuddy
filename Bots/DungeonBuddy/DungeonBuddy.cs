using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bots.DungeonBuddy.Avoidance;
using Bots.DungeonBuddy.Enums;
using Bots.DungeonBuddy.Helpers;
using Bots.Grind;
using CommonBehaviors.Actions;
using CommonBehaviors.Decorators;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Inventory;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
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
        private uint _lastObservedLfgMapId;
        private uint _lastObservedLfgDungeonId;
        private bool _hasSetRole;

        // Active dungeon behavior — updated when dungeon changes (fixes stale reference at tree-build time)
        private Composite? _activeDungeonBehavior;
        private readonly WaitTimer _soloFarmExitTimer = new WaitTimer(TimeSpan.FromSeconds(30.0));

        // Death behavior fields (HB stopwatch_0, woWPoint_1, waitTimer_6, woWUnit_0 parity)
        private readonly Stopwatch _deathTimer = new Stopwatch();
        private WoWPoint _corpseRunBreadcrumb = WoWPoint.Zero;
        private readonly WaitTimer _corpseRunWaitTimer = new WaitTimer(TimeSpan.FromMinutes(2.0));
        private WoWUnit? _corpseSpiritHealer;
        private readonly Stopwatch _debugMoveLogThrottle = new();
        private readonly Stopwatch _debugDungeonLogThrottle = new();
        private readonly Stopwatch _soloFarmStatusLogThrottle = new();
        private readonly WaitTimer _pathValidationRefreshTimer = new WaitTimer(TimeSpan.FromSeconds(2.0));
        private dynamic? _cachedBossPath;
        private bool _soloFarmResetInstancesPending;
        private WoWPoint _outsideFlyPoint = WoWPoint.Zero;
        private readonly List<uint> _healthstoneEntries = new List<uint> { 51999U, 52000U, 52001U, 52002U, 52003U, 52004U, 52005U, 67248U, 67250U };
        private readonly uint[] _mageTableEntries = { 186812U, 207386U, 207387U };

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
            Targeting.Instance = new DungeonTargeting();

            Logging.Write("[DungeonBuddy] Script folder: {0}", System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dungeon Scripts"));
            Logging.Write("[DungeonBuddy] Profile folder: {0}", System.IO.Path.Combine(Logging.ApplicationPath, "Default Profiles\\DungeonBuddy\\"));

            // Attacher les événements LFG
            LfgManager.AttachLfgEvents();

            _hasSetRole = false;
            _lastMapId = StyxWoW.Me.MapId;
            _lastObservedLfgMapId = _lastMapId;
            _lastObservedLfgDungeonId = LfgManager.CurrentLfgDungeonId;
            _activeDungeonBehavior = null;

            // SoloFarm HB parity: précharger le donjon sélectionné même hors instance
            // pour avoir CurrentDungeon + profile disponibles avant d'entrer.
            if (settings.QueueType == QueueType.SoloFarm &&
                settings.SelectedDungeonIds.Length > 0)
            {
                DungeonManager.SetDungeonById(settings.SelectedDungeonIds[0]);
            }

            Logging.Write("[DungeonBuddy] Started successfully!");
        }

        public override void Stop()
        {
            Logging.Write("[DungeonBuddy] Stopping...");

            LfgManager.DetachLfgEvents();
            Targeting.Instance = new Targeting();
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

            // Keep targeting list fresh every pulse (HB behavior depends on a live FirstUnit/TargetList).
            if (StyxWoW.Me.IsInInstance)
                Targeting.Instance.Pulse();

            // Recovery: si on est dans un donjon mais CurrentDungeon == null (race condition
            // pendant le loading screen — OnMapChanged a pu firer avant IsInInstance = true),
            // réinitialiser immédiatement.
            if (StyxWoW.Me.CurrentMap.IsDungeon && DungeonManager.CurrentDungeon == null)
            {
                var settings2 = DungeonBuddySettings.Instance;
                if (settings2.QueueType == QueueType.SoloFarm && settings2.SelectedDungeonIds.Length > 0)
                    DungeonManager.SetDungeonById(settings2.SelectedDungeonIds[0]);
                else
                    DungeonManager.SetDungeon(StyxWoW.Me.MapId);
            }

        }

        private void OnMapChanged(uint newMapId)
        {
            // HB 4.3.4 method_41: détermine la direction (entrée/sortie) via CurrentMap.IsDungeon,
            // PAS IsInInstance. IsInInstance peut être false pendant le loading screen même
            // si MapId est déjà 389 (dungeon) — ce qui causait un faux « Left instance »
            // suivi de DungeonManager.Clear() + perte de CurrentDungeon.
            bool isInDungeonMap = StyxWoW.Me.CurrentMap.IsDungeon;

            if (isInDungeonMap)
            {
                Logging.Write($"[DungeonBuddy] Entered instance (MapId={newMapId})");

                var settings = DungeonBuddySettings.Instance;
                if (settings.QueueType == QueueType.SoloFarm && settings.SelectedDungeonIds.Length > 0)
                    DungeonManager.SetDungeonById(settings.SelectedDungeonIds[0]);
                else
                    DungeonManager.SetDungeon(newMapId);

                _activeDungeonBehavior = null;
                LfgManager.DungeonCompletedReason = CompleteReason.None;
                _soloFarmResetInstancesPending = false;
                _outsideFlyPoint = WoWPoint.Zero;
            }
            else
            {
                // Quitte l'instance — seulement clear si on était effectivement dans un donjon
                if (DungeonManager.CurrentDungeon != null)
                {
                    Logging.Write($"[DungeonBuddy] Left instance");
                    DungeonManager.Clear();
                    BossManager.Reset();
                    _activeDungeonBehavior = null;

                    if (LfgManager.DungeonCompletedReason != CompleteReason.None)
                        _soloFarmResetInstancesPending = true;

                    // SoloFarm: re-sélectionner immédiatement le donjon cible hors instance
                    // pour garder script/profile actifs entre deux runs.
                    var settings = DungeonBuddySettings.Instance;
                    if (settings.QueueType == QueueType.SoloFarm && settings.SelectedDungeonIds.Length > 0)
                        DungeonManager.SetDungeonById(settings.SelectedDungeonIds[0]);
                }
            }
        }

        private static bool ShouldLog(Stopwatch throttle, int intervalMs)
        {
            if (!throttle.IsRunning || throttle.ElapsedMilliseconds >= intervalMs)
            {
                throttle.Restart();
                return true;
            }

            return false;
        }

        // ═══════════════════════════════════════════════════════════
        // BEHAVIOR TREE
        // ═══════════════════════════════════════════════════════════

        private PrioritySelector CreateRootBehavior()
        {
            return new PrioritySelector(
                // HB method_12 parity: only run this block while we're on a dungeon map.
                new Decorator(
                    ctx => StyxWoW.Me.CurrentMap.IsDungeon,
                    new PrioritySelector(
                        // 1) method_27
                        CreateDeathBehavior(),

                        // 2) method_45
                        CreateLfgBehavior(),

                        // 3) BossManager.CreateCheckForDeadBossBehavior
                        BossManager.CreateCheckForDeadBossBehavior(),

                        // 4) method_11
                        CreateDungeonBehavior(),

                        // 5) method_0
                        CreateCombatBehavior(),

                        // 6) method_13 guarded by smethod_127
                        new Decorator(
                            ctx => DungeonBuddySettings.Instance.QueueType == QueueType.SoloFarm &&
                                   LfgManager.DungeonCompletedReason != CompleteReason.None &&
                                   _soloFarmExitTimer.IsFinished,
                            CreateSoloFarmExitBehavior()),

                        // 7) method_19
                        CreateLootBehavior(),

                        // 8) method_4
                        CreateInDungeonSupportBehavior()
                    )
                ),

                // HB method_14-equivalent outside dungeon for SoloFarm portal travel.
                CreateOutsideDungeonBehavior(),

                new ActionIdle()
            );
        }

        private Composite CreateOutsideDungeonBehavior()
        {
            IEnumerable<PoiType> vendorPois = new[]
            {
                PoiType.Sell,
                PoiType.Buy,
                PoiType.Repair,
                PoiType.Train
            };

            return new Decorator(
                ctx => !StyxWoW.Me.CurrentMap.IsDungeon,
                new PrioritySelector(
                    new Decorator(
                        ctx => DungeonBuddySettings.Instance.QueueType == QueueType.SoloFarm &&
                               DungeonManager.CurrentDungeon != null,
                        new DecoratorIsNotPoiType(
                            vendorPois,
                            new PrioritySelector(
                                new Decorator(
                                    ctx => _soloFarmResetInstancesPending,
                                    new Sequence(
                                        new ActionSetActivity("Reseting Instances"),
                                        new Action(ctx =>
                                        {
                                            Logging.Write("Reseting Instances");
                                            Lua.DoString("ResetInstances();");
                                            return RunStatus.Success;
                                        }),
                                        new Action(ctx =>
                                        {
                                            _soloFarmResetInstancesPending = false;
                                            _outsideFlyPoint = WoWPoint.Zero;
                                            return RunStatus.Success;
                                        })
                                    )
                                ),

                                new Decorator(
                                    ctx => !DungeonManager.CurrentDungeon.IsFlyingCorpseRun,
                                    new PrioritySelector(
                                        new ActionSetActivity("Moving to Instance Portal on foot"),
                                        new Decorator(
                                            ctx => !ObjectManager.Me.Mounted &&
                                                   Mount.ShouldMount(DungeonManager.CurrentDungeon.Entrance) &&
                                                   Mount.CanMount(),
                                            new PrioritySelector(
                                                new Decorator(
                                                    ctx => StyxWoW.Me.IsMoving,
                                                    new Action(ctx =>
                                                    {
                                                        WoWMovement.MoveStop();
                                                        return RunStatus.Success;
                                                    })
                                                ),
                                                new Action(ctx =>
                                                {
                                                    Mount.MountUp(() => DungeonManager.CurrentDungeon.Entrance);
                                                    return RunStatus.Success;
                                                })
                                            )
                                        ),
                                        CreateSoloFarmBehavior()
                                    )
                                ),

                                new Decorator(
                                    ctx => DungeonManager.CurrentDungeon.IsFlyingCorpseRun,
                                    new PrioritySelector(
                                        new ActionSetActivity("Flying to Instance Portal"),
                                        new Decorator(
                                            ctx => DungeonManager.CurrentDungeon.CorpseRunBreadCrumb == null ||
                                                   DungeonManager.CurrentDungeon.CorpseRunBreadCrumb.Count == 0,
                                            new Action(ctx =>
                                            {
                                                Flightor.MoveTo(DungeonManager.CurrentDungeon.Entrance);
                                                return RunStatus.Running;
                                            })
                                        ),
                                        new Sequence(
                                            new Action(ctx =>
                                            {
                                                var crumbs = DungeonManager.CurrentDungeon.CorpseRunBreadCrumb;
                                                if (_outsideFlyPoint == WoWPoint.Zero && crumbs != null && crumbs.Count > 0)
                                                {
                                                    crumbs.CycleTo(crumbs.First);
                                                    _outsideFlyPoint = crumbs.Dequeue();
                                                }
                                                return RunStatus.Success;
                                            }),
                                            new Action(ctx =>
                                            {
                                                var crumbs = DungeonManager.CurrentDungeon.CorpseRunBreadCrumb;
                                                if (crumbs == null || crumbs.Count == 0)
                                                {
                                                    Flightor.MoveTo(DungeonManager.CurrentDungeon.Entrance);
                                                    return RunStatus.Running;
                                                }

                                                if (StyxWoW.Me.Location.Distance2DSqr(_outsideFlyPoint) < 225f)
                                                {
                                                    _outsideFlyPoint = crumbs.Dequeue();
                                                    if (_outsideFlyPoint == crumbs.First)
                                                        return RunStatus.Success;
                                                }

                                                Flightor.MoveTo(_outsideFlyPoint);
                                                return RunStatus.Running;
                                            })
                                        )
                                    )
                                )
                            )
                        )
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // HB METHOD_19 (UTILITY INTERACTIONS)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateLootBehavior()
        {
            // HB method_19 = method_20 + method_21
            return new PrioritySelector(
                CreateRitualAssistBehavior(),
                CreateConsumableObjectBehavior());
        }

        private WoWGameObject? Ritual
        {
            get
            {
                return ObjectManager.GetObjectsOfType<WoWGameObject>()
                    .FirstOrDefault(go => go.SubType == WoWGameObjectType.Ritual &&
                                          go.CreatedByGuid != StyxWoW.Me.Guid &&
                                          StyxWoW.Me.PartyMembers.Any(p => p.Guid == go.CreatedByGuid));
            }
        }

        private WoWGameObject? MageTable
        {
            get
            {
                return ObjectManager.GetObjectsOfType<WoWGameObject>()
                    .FirstOrDefault(go => _mageTableEntries.Contains(go.Entry) &&
                                          (StyxWoW.Me.PartyMembers.Any(p => p.Guid == go.CreatedByGuid) ||
                                           StyxWoW.Me.Guid == go.CreatedByGuid));
            }
        }

        private WoWGameObject? SoulWell
        {
            get
            {
                return ObjectManager.GetObjectsOfType<WoWGameObject>()
                    .FirstOrDefault(go => go.Entry == 181621U &&
                                          (StyxWoW.Me.PartyMembers.Any(p => p.Guid == go.CreatedByGuid) ||
                                           StyxWoW.Me.Guid == go.CreatedByGuid));
            }
        }

        private int CarriedMageFoodCount
        {
            get
            {
                return StyxWoW.Me.CarriedItems.Sum(item =>
                {
                    if (item == null || item.ItemInfo == null || item.ItemInfo.ItemClass != WoWItemClass.Consumable)
                        return 0;

                    if (item.ItemSpells == null || item.ItemSpells.Count == 0 || item.ItemSpells[0].ActualSpell == null)
                        return 0;

                    return item.ItemSpells[0].ActualSpell.Name.Contains("Refreshment") ? (int)item.StackCount : 0;
                });
            }
        }

        private bool HasHearthStone
        {
            get
            {
                return StyxWoW.Me.CarriedItems.Any(item =>
                    item != null &&
                    item.ItemSpells != null &&
                    item.ItemSpells.Any(spell => spell.ActualSpell != null && spell.ActualSpell.Name == "Healthstone"));
            }
        }

        private Composite CreateRitualAssistBehavior()
        {
            return new Decorator(
                ctx => !StyxWoW.Me.IsCasting,
                new PrioritySelector(
                    new ContextChangeHandler(ctx => Ritual),
                    new Decorator(
                        ctx => ctx is WoWGameObject,
                        new Sequence(
                            new Action(ctx =>
                            {
                                var ritual = (WoWGameObject)ctx;
                                Logging.Write("Assisting with ritual casted by {0}", ritual.CreatedBy.Name);
                                return RunStatus.Success;
                            }),
                            new DecoratorContinue(
                                ctx => ((WoWGameObject)ctx).DistanceSqr > 36.0,
                                new Action(ctx =>
                                {
                                    var ritual = (WoWGameObject)ctx;
                                    Navigator.MoveTo(WoWMathHelper.CalculatePointFrom(StyxWoW.Me.Location, ritual.Location, 6f));
                                    return RunStatus.Success;
                                })
                            ),
                            new DecoratorContinue(
                                ctx => StyxWoW.Me.IsMoving,
                                new Sequence(
                                    new Action(ctx =>
                                    {
                                        WoWMovement.MoveStop();
                                        return RunStatus.Success;
                                    }),
                                    new WaitContinue(4, ctx => !StyxWoW.Me.IsMoving, new ActionAlwaysSucceed())
                                )
                            ),
                            new Action(ctx =>
                            {
                                ((WoWGameObject)ctx).Interact();
                                return RunStatus.Success;
                            }),
                            new WaitContinue(2, ctx => StyxWoW.Me.IsCasting, new ActionAlwaysSucceed()),
                            new WaitContinue(25, ctx => !StyxWoW.Me.IsCasting, new ActionAlwaysSucceed())
                        )
                    )
                )
            );
        }

        private Composite CreateConsumableObjectBehavior()
        {
            return new PrioritySelector(
                new PrioritySelector(
                    new ContextChangeHandler(ctx => MageTable),
                    new Decorator(
                        ctx => ctx is WoWGameObject && CarriedMageFoodCount < 80 && StyxWoW.Me.FreeNormalBagSlots > 1,
                        new Sequence(
                            new Action(ctx =>
                            {
                                Logging.Write("Getting Mage food");
                                return RunStatus.Success;
                            }),
                            new DecoratorContinue(
                                ctx => ((WoWGameObject)ctx).DistanceSqr > 25.0,
                                new Action(ctx => Navigator.GetRunStatusFromMoveResult(
                                    Navigator.MoveTo(WoWMathHelper.CalculatePointFrom(StyxWoW.Me.Location, ((WoWGameObject)ctx).Location, 5f))))),
                            new Action(ctx =>
                            {
                                ((WoWGameObject)ctx).Interact();
                                return RunStatus.Success;
                            }),
                            new WaitContinue(2, ctx => false, new ActionAlwaysSucceed())
                        )
                    )
                ),
                new PrioritySelector(
                    new ContextChangeHandler(ctx => SoulWell),
                    new Decorator(
                        ctx => ctx is WoWGameObject && !HasHearthStone && StyxWoW.Me.FreeNormalBagSlots > 1,
                        new Sequence(
                            new Action(ctx =>
                            {
                                Logging.Write("Getting Warlock Healthstone");
                                return RunStatus.Success;
                            }),
                            new DecoratorContinue(
                                ctx => ((WoWGameObject)ctx).DistanceSqr > 25.0,
                                new Action(ctx =>
                                {
                                    Navigator.MoveTo(WoWMathHelper.CalculatePointFrom(StyxWoW.Me.Location, ((WoWGameObject)ctx).Location, 5f));
                                    return RunStatus.Success;
                                })),
                            new Action(ctx =>
                            {
                                ((WoWGameObject)ctx).Interact();
                                return RunStatus.Success;
                            }),
                            new WaitContinue(1, ctx => false, new ActionAlwaysSucceed())
                        )
                    )
                )
            );
        }

        private Composite CreateLfgBehavior()
        {
            return new PrioritySelector(
                // --- RESURRECT REQUEST (HB method_97 branch) ---
                new Decorator(
                    ctx => LfgManager.ResurrectRequestPending,
                    new PrioritySelector(
                        new Decorator(
                            ctx => ScriptHelpers.GetReturnVal<int>("return GetCorpseRecoveryDelay()", 0) != 0,
                            new Action(ctx =>
                            {
                                Logging.Write("[DungeonBuddy] Waiting for corpse recovery delay to expire...");
                                return RunStatus.Success;
                            })
                        ),
                        new Sequence(
                            new Action(ctx =>
                            {
                                Logging.Write("[DungeonBuddy] Accepting resurrect");
                                Lua.DoString("AcceptResurrect()");
                                return RunStatus.Success;
                            }),
                            new WaitContinue(
                                TimeSpan.FromSeconds(5),
                                ctx => StyxWoW.Me.IsAlive || ScriptHelpers.GetReturnVal<bool>("if ResurrectGetOfferer() == nil then return 1 end return nil", 0),
                                new ActionAlwaysSucceed()),
                            new Action(ctx =>
                            {
                                LfgManager.ResurrectRequestPending = false;
                                return RunStatus.Success;
                            })
                        )
                    )
                ),

                // --- PROPOSAL: random delayed accept (HB method_99 branch) ---
                new Decorator(
                    ctx => CanAcceptLfgProposal(),
                    new Sequence(
                        new Action(ctx =>
                        {
                            if (_proposalWaitMs == 0)
                            {
                                _proposalWaitMs = _rng.Next(1000, 3000);
                                _proposalDelay.Restart();
                                Logging.Write("[DungeonBuddy] Proposal received, accepting in {0}ms", _proposalWaitMs);
                            }
                            return RunStatus.Success;
                        }),
                        new WaitContinue(
                            TimeSpan.FromSeconds(60),
                            ctx => _proposalDelay.ElapsedMilliseconds >= _proposalWaitMs || LfgManager.ProposalFailed || LfgManager.ProposalSucceeded || StyxWoW.Me.Combat,
                            new ActionAlwaysSucceed()),
                        new PrioritySelector(
                            new DecoratorContinue(
                                ctx => LfgManager.ProposalSucceeded && DungeonManager.CurrentDungeon != null,
                                new Sequence(
                                    new Action(ctx =>
                                    {
                                        DungeonManager.Clear();
                                        BossManager.Reset();
                                        _activeDungeonBehavior = null;
                                        return RunStatus.Success;
                                    }),
                                    new Action(ctx =>
                                    {
                                        _lastObservedLfgDungeonId = 0U;
                                        return RunStatus.Success;
                                    }),
                                    new WaitContinue(
                                        TimeSpan.FromSeconds(4),
                                        ctx => !StyxWoW.IsInWorld,
                                        new ActionAlwaysSucceed())
                                )
                            ),
                            new Decorator(
                                ctx => LfgManager.ProposalPending && !LfgManager.ProposalFailed && !LfgManager.ProposalSucceeded,
                                new Sequence(
                                    new Action(ctx =>
                                    {
                                        Logging.Write("[DungeonBuddy] Accepting dungeon invite");
                                        LfgManager.AcceptProposal();
                                        return RunStatus.Success;
                                    }),
                                    new Action(ctx =>
                                    {
                                        _proposalWaitMs = 0;
                                        return RunStatus.Success;
                                    })
                                )
                            ),
                            new Action(ctx =>
                            {
                                _proposalWaitMs = 0;
                                LfgManager.ResetProposalFlags();
                                return RunStatus.Success;
                            })
                        )
                    )
                ),

                // --- PARTY MODE OFF: leave party after completion (HB smethod_308-311) ---
                new Decorator(
                    ctx => ShouldLeavePartyAfterCompletion,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Logging.Write("[DungeonBuddy] Dungeon completed. Leaving. [Reason: {0}]", LfgManager.DungeonCompletedReason);
                            return RunStatus.Success;
                        }),
                        new Action(ctx =>
                        {
                            Lua.DoString(StyxWoW.Me.IsInParty ? "LeaveParty() LFGTeleport(true)" : "LFGTeleport(true)");
                            return RunStatus.Success;
                        }),
                        new Action(ctx =>
                        {
                            DungeonManager.Clear();
                            BossManager.Reset();
                            _activeDungeonBehavior = null;
                            _lastObservedLfgDungeonId = 0U;
                            return RunStatus.Success;
                        }),
                        new WaitContinue(
                            TimeSpan.FromSeconds(4),
                            ctx => !StyxWoW.IsInWorld,
                            new ActionAlwaysSucceed())
                    )
                ),

                // --- BOOT PROPOSAL CONTINUE (HB bool_7) ---
                new Decorator(
                    ctx => LfgManager.BootProposalActive,
                    new Action(ctx =>
                    {
                        Logging.Write("[DungeonBuddy] LFG continue offer received, consuming boot proposal flag.");
                        LfgManager.BootProposalActive = false;
                        return RunStatus.Success;
                    })
                ),

                // --- ROLE CHECK (HB method_111 branch) ---
                new Decorator(
                    ctx => LfgManager.RoleCheckPending,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Logging.Write("[DungeonBuddy] Role check in progress");
                            Lua.DoString("LFDRoleCheckPopupAcceptButton:Click() StaticPopup1Button1:Click()");
                            return RunStatus.Success;
                        }),
                        new Action(ctx =>
                        {
                            LfgManager.RoleCheckPending = false;
                            return RunStatus.Success;
                        })
                    )
                ),

                // --- PARTY INVITE (HB method_113 branch) ---
                new Decorator(
                    ctx => LfgManager.PartyInvitePending,
                    new Sequence(
                        new DecoratorContinue(
                            ctx => DungeonBuddySettings.Instance.PartyMode == PartyMode.Follower,
                            new Action(ctx =>
                            {
                                Logging.Write("[DungeonBuddy] Accepting party invite");
                                Lua.DoString("AcceptGroup()");
                                return RunStatus.Success;
                            })
                        ),
                        new Action(ctx =>
                        {
                            LfgManager.PartyInvitePending = false;
                            return RunStatus.Success;
                        })
                    )
                ),

                // --- FOLLOWER: if we are leader, promote tank (HB smethod_321-322) ---
                new Decorator(
                    ctx => ShouldPromoteLeaderToTankInFollowerMode,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Logging.Write("[DungeonBuddy] I am not supposed to be party leader. Fixing");
                            return RunStatus.Success;
                        }),
                        new Action(ctx =>
                        {
                            Lua.DoString("for n=0,4 do if UnitGroupRolesAssigned('party'..n) == 'TANK' then PromoteToLeader('party'..n) end end");
                            return RunStatus.Success;
                        })
                    )
                ),

                // --- Dungeon script completion -> set completion delay (HB smethod_323-324) ---
                new Decorator(
                    ctx => ShouldMarkDungeonCompletedFromCurrentDungeon,
                    new Action(ctx =>
                    {
                        int delay = DungeonBuddySettings.Instance.PartyMode != PartyMode.Off ? 10 : 30;
                        _soloFarmExitTimer.Reset();
                        LfgManager.SetDungeonCompleted(CompleteReason.Completed, delay);
                        return RunStatus.Success;
                    })
                ),

                // --- IN DUNGEON (LFG): Dungeon completed → teleport out + requeue ---
                // SoloFarm se gère séparément via CreateSoloFarmExitBehavior (walk to portal).
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.InDungeon &&
                           LfgManager.DungeonCompletedReason != CompleteReason.None &&
                           LfgManager.ExitDelayTimer.IsFinished &&
                           DungeonBuddySettings.Instance.QueueType != QueueType.SoloFarm,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Logging.Write("[DungeonBuddy] Dungeon complete! Teleporting out...");
                            LfgManager.TeleportOut();
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

                // --- REQUEUE (HB method_110 branch) ---
                new Decorator(
                    ctx => ShouldRequeue && DungeonBuddySettings.Instance.QueueType != QueueType.SoloFarm,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Logging.Write("[DungeonBuddy] Dungeon run is over. Requeuing");
                            QueueForSettings();
                            LfgManager.DungeonCompletedReason = CompleteReason.None;
                            return RunStatus.Success;
                        })
                    )
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

                // --- MAP/LFG DUNGEON CHANGE SYNC (HB method_115-118) ---
                new Decorator(
                    ctx => ShouldSyncCurrentDungeonFromLfgState(),
                    new Sequence(
                        new Action(ctx =>
                        {
                            RefreshCurrentDungeonFromLfgState();
                            return RunStatus.Success;
                        }),
                        new Action(ctx =>
                        {
                            _lastObservedLfgMapId = StyxWoW.Me.MapId;
                            _lastObservedLfgDungeonId = LfgManager.CurrentLfgDungeonId;
                            return RunStatus.Success;
                        })
                    )
                ),

                new ActionAlwaysFail()
            );
        }

        // HB ShouldRequeue parity (DungeonBot.ShouldRequeue)
        private bool ShouldRequeue
        {
            get
            {
                if (LfgManager.DungeonCompletedReason == CompleteReason.None ||
                    !LfgManager.ExitDelayTimer.IsFinished ||
                    !LfgManager.PostProposalTimer.IsFinished)
                {
                    return false;
                }

                if (DungeonBuddySettings.Instance.PartyMode != PartyMode.Off || StyxWoW.Me.IsInParty)
                {
                    if (DungeonBuddySettings.Instance.PartyMode == PartyMode.Leader && StyxWoW.Me.IsInParty)
                    {
                        if (!StyxWoW.Me.PartyMembers.All(p => p.IsAlive) || ISeeAGhost)
                            return false;
                    }
                }

                bool needsMaintenance = Vendors.NeedClassTraining ||
                                        ShouldRepairInSoloFarm(this) ||
                                        ShouldBuyDrinksInSoloFarm(this) ||
                                        ShouldSellItemsInSoloFarm(this);

                if (!StyxWoW.Me.IsAlive)
                    return false;

                if (needsMaintenance &&
                    !(StyxWoW.Me.CurrentMap.IsInstance && LfgManager.CurrentLfgDungeonId == 0U))
                {
                    return false;
                }

                return LfgManager.CurrentState != LfgState.InQueue;
            }
        }

        // HB method_99 parity
        private bool CanAcceptLfgProposal()
        {
            if (!LfgManager.ProposalPending || !StyxWoW.Me.IsAlive)
                return false;

            bool needsMaintenance = Vendors.NeedClassTraining ||
                                    ShouldRepairInSoloFarm(this) ||
                                    ShouldBuyDrinksInSoloFarm(this) ||
                                    ShouldSellItemsInSoloFarm(this);

            if (!needsMaintenance)
                return true;

            return StyxWoW.Me.CurrentMap.IsInstance && LfgManager.CurrentLfgDungeonId == 0U;
        }

        // HB smethod_308 parity
        private bool ShouldLeavePartyAfterCompletion =>
            LfgManager.DungeonCompletedReason != CompleteReason.None &&
            LfgManager.ExitDelayTimer.IsFinished &&
            DungeonBuddySettings.Instance.QueueType != QueueType.SoloFarm &&
            DungeonBuddySettings.Instance.PartyMode == PartyMode.Off &&
            StyxWoW.Me.IsInParty;

        // HB smethod_321 parity
        private static bool ShouldPromoteLeaderToTankInFollowerMode =>
            DungeonBuddySettings.Instance.PartyMode == PartyMode.Follower &&
            ScriptHelpers.GetReturnVal<bool>("return IsPartyLeader()", 0);

        // HB smethod_323 parity
        private static bool ShouldMarkDungeonCompletedFromCurrentDungeon =>
            DungeonManager.CurrentDungeon != null &&
            LfgManager.DungeonCompletedReason != CompleteReason.Completed &&
            DungeonManager.CurrentDungeon.IsComplete;

        // HB ISeeAGhost parity
        private bool ISeeAGhost
        {
            get
            {
                return StyxWoW.Me.GroupInfo.RaidMembers.Any(member =>
                {
                    var player = member.ToPlayer();
                    if (player != null)
                        return player.IsGhost;

                    return member.Ghost && member.Health <= member.HealthMax * 0.01;
                });
            }
        }

        private void QueueForSettings()
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
        }

        // HB method_115 parity
        private bool ShouldSyncCurrentDungeonFromLfgState()
        {
            if ((_lastObservedLfgMapId == StyxWoW.Me.MapId && _lastObservedLfgDungeonId == LfgManager.CurrentLfgDungeonId) || StyxWoW.Me.IsGhost)
                return false;

            if (!StyxWoW.Me.CurrentMap.IsDungeon && !StyxWoW.Me.CurrentMap.IsRaid)
                return LfgManager.CurrentLfgDungeonId > 0U;

            return true;
        }

        // HB method_116 parity (calls method_42)
        private void RefreshCurrentDungeonFromLfgState()
        {
            uint dungeonId = LfgManager.CurrentLfgDungeonId;
            if (dungeonId == 0U)
                dungeonId = Bots.DungeonBuddy.Profiles.ProfileManager.GetLfgDungeonIdFromMapId(StyxWoW.Me.MapId);

            if (dungeonId > 0U)
                DungeonManager.SetDungeonById(dungeonId);
            else if (StyxWoW.Me.CurrentMap.IsDungeon || StyxWoW.Me.CurrentMap.IsRaid)
                DungeonManager.SetDungeon(StyxWoW.Me.MapId);
        }

        // ═══════════════════════════════════════════════════════════
        // DEATH BEHAVIOR (HB method_27 parity)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateDeathBehavior()
        {
            return new PrioritySelector(
                // Branch 0: Priest SoR (smethod_263/264/265)
                new Decorator(
                    ctx => IsPriestWithSpiritOfRedemptionAuraNoAlliesNearby(),
                    new Sequence(
                        new Action(ctx => { Logging.Write("Nobody around to heal so canceling Spirit of Redemption"); return RunStatus.Success; }),
                        new Action(ctx => { StyxWoW.Me.GetAuraById(27827).TryCancel(); return RunStatus.Success; })
                    )
                ),

                // Branch 1: Main death sequence (smethod_266-277)
                new Decorator(
                    ctx => StyxWoW.Me.IsDead && StyxWoW.Me.Level > 0,
                    new Sequence(
                        // smethod_267: Failure + reset
                        new Action(ctx =>
                        {
                            BossManager.BossTimer.Reset();
                            Navigator.NavigationProvider.StuckHandler.Reset();
                            Avoidance.AvoidanceManager.ClearAvoidPath();
                            return RunStatus.Failure;
                        }),

                        // smethod_268: !stopwatch_0.IsRunning (predicate)
                        new Decorator(
                            ctx => !_deathTimer.IsRunning,
                            new Action(ctx => { _deathTimer.Start(); return RunStatus.Success; })
                        ),

                        // smethod_270/271/272: Soulstone/Ankh
                        new Decorator(
                            ctx => HasSoulstoneOrAnkh(),
                            new Sequence(
                                new Action(ctx => { Logging.Write("Ankh or soulstone is available"); return RunStatus.Success; }),
                                new Action(ctx => { Lua.DoString("UseSoulstone()"); return RunStatus.Success; })
                            )
                        ),

                        // smethod_273: ShouldReleaseToCorpseRun (smethod_22 full logic)
                        new Decorator(
                            ctx => ShouldReleaseToCorpseRun(),
                            new Sequence(
                                // smethod_274: Log
                                new Action(ctx => { Logging.Write("Releasing corpse"); return RunStatus.Success; }),
                                // smethod_275: RepopMe
                                new Action(ctx => { Lua.DoString("RepopMe()"); return RunStatus.Success; }),
                                // method_87: waitTimer_6.Reset()
                                new Action(ctx => { _corpseRunWaitTimer.Reset(); return RunStatus.Success; }),
                                // smethod_276: stopwatch_0.Reset()
                                new Action(ctx => { _deathTimer.Reset(); return RunStatus.Success; }),

                                // smethod_277: CorpseRunBreadCrumb.Count > 0
                                new Decorator(
                                    ctx => DungeonManager.CurrentDungeon != null &&
                                           DungeonManager.CurrentDungeon.CorpseRunBreadCrumb != null &&
                                           DungeonManager.CurrentDungeon.CorpseRunBreadCrumb.Count > 0,
                                    new Sequence(
                                        new Action(ctx =>
                                        {
                                            var crumbs = DungeonManager.CurrentDungeon.CorpseRunBreadCrumb;
                                            crumbs.CycleTo(crumbs.First);
                                            _corpseRunBreadcrumb = crumbs.Dequeue();
                                            return RunStatus.Success;
                                        }),
                                        // WaitContinue(10, smethod_278 = Me.IsGhost && !IsDungeon)
                                        new WaitContinue(TimeSpan.FromSeconds(10),
                                            ctx => StyxWoW.Me.IsGhost && !StyxWoW.Me.CurrentMap.IsDungeon,
                                            new ActionAlwaysSucceed()),
                                        // method_88/89/90/91/92: flying breadcrumb movement
                                        new Action(ctx =>
                                        {
                                            // smethod_89: woWPoint_1 == Zero
                                            if (_corpseRunBreadcrumb == WoWPoint.Zero)
                                            {
                                                // smethod_90: woWPoint_1 = Dequeue
                                                var crumbs = DungeonManager.CurrentDungeon.CorpseRunBreadCrumb;
                                                if (crumbs != null && crumbs.Count > 0)
                                                    _corpseRunBreadcrumb = crumbs.Dequeue();
                                            }
                                            // smethod_91/92
                                            else if (StyxWoW.Me.Location.Distance2DSqr(_corpseRunBreadcrumb) < 225f)
                                            {
                                                var crumbs = DungeonManager.CurrentDungeon.CorpseRunBreadCrumb;
                                                if (crumbs != null && crumbs.Count > 0 && crumbs.Peek() != crumbs.First)
                                                    _corpseRunBreadcrumb = crumbs.Dequeue();
                                            }
                                            if (_corpseRunBreadcrumb != WoWPoint.Zero)
                                                Flightor.MoveTo(_corpseRunBreadcrumb);
                                            return RunStatus.Running;
                                        })
                                    )
                                ),

                                // smethod_278: Me.IsGhost && !IsDungeon -> wait + entrance
                                new Decorator(
                                    ctx => StyxWoW.Me.IsGhost && !StyxWoW.Me.CurrentMap.IsDungeon,
                                    new Sequence(
                                        new Action(ctx =>
                                        {
                                            // method_94: Flightor.MoveTo(entrance)
                                            var entrance = GetDungeonEntrance();
                                            if (entrance != WoWPoint.Zero)
                                                Flightor.MoveTo(entrance);
                                            return RunStatus.Running;
                                        })
                                    )
                                ),

                                // Non-flying fallback
                                new Action(ctx =>
                                {
                                    // method_95: Navigator.MoveTo(entrance)
                                    var entrance = GetDungeonEntrance();
                                    if (entrance != WoWPoint.Zero)
                                        Navigator.MoveTo(entrance);
                                    return RunStatus.Running;
                                })
                            )
                        ),

                        // smethod_279: not should release -> Running (wait)
                        new Action(ctx => RunStatus.Running)
                    )
                ),

                // Branch 2: Ghost handling (smethod_279-282)
                new Decorator(
                    ctx => StyxWoW.Me.IsGhost && DungeonManager.CurrentDungeon != null,
                    new PrioritySelector(
                        // smethod_281: ZoneId == 3521 -> spirit healer behavior
                        new Decorator(
                            ctx => StyxWoW.Me.ZoneId == 3521U,
                            CreateCorpseRecoveryBehavior()
                        ),

                        // smethod_282: IsFlyingCorpseRun
                        new Decorator(
                            ctx => DungeonManager.CurrentDungeon.IsFlyingCorpseRun,
                            new PrioritySelector(
                                new Decorator(
                                    ctx => _corpseRunBreadcrumb == WoWPoint.Zero,
                                    new Action(ctx =>
                                    {
                                        var crumbs = DungeonManager.CurrentDungeon.CorpseRunBreadCrumb;
                                        if (crumbs != null && crumbs.Count > 0)
                                        {
                                            crumbs.CycleTo(crumbs.First);
                                            _corpseRunBreadcrumb = crumbs.Dequeue();
                                        }
                                        return RunStatus.Success;
                                    })
                                ),
                                new Decorator(
                                    ctx => _corpseRunBreadcrumb != WoWPoint.Zero &&
                                           StyxWoW.Me.Location.Distance2DSqr(_corpseRunBreadcrumb) < 225f,
                                    new Action(ctx =>
                                    {
                                        var crumbs = DungeonManager.CurrentDungeon.CorpseRunBreadCrumb;
                                        if (crumbs != null && crumbs.Count > 0 && crumbs.Peek() != crumbs.First)
                                            _corpseRunBreadcrumb = crumbs.Dequeue();
                                        return RunStatus.Success;
                                    })
                                ),
                                new Action(ctx =>
                                {
                                    if (_corpseRunBreadcrumb != WoWPoint.Zero)
                                        Flightor.MoveTo(_corpseRunBreadcrumb);
                                    return RunStatus.Running;
                                })
                            )
                        ),

                        // method_94/95: Navigator to entrance
                        new Action(ctx =>
                        {
                            var entrance = GetDungeonEntrance();
                            if (entrance != WoWPoint.Zero)
                                Navigator.MoveTo(entrance);
                            return RunStatus.Running;
                        }),

                        // LevelBot.CreateDeathBehavior() fallback (HB array22[1])
                        LevelBot.CreateDeathBehavior()
                    )
                )
            );
        }

        // smethod_263: Priest Spirit of Redemption
        private bool IsPriestWithSpiritOfRedemptionAuraNoAlliesNearby()
        {
            if (StyxWoW.Me.Class != WoWClass.Priest || !StyxWoW.Me.HasAura(27827))
                return false;
            return !StyxWoW.Me.PartyMembers.Any(p => p.IsAlive && p.Location.DistanceSqr(StyxWoW.Me.Location) <= 1600);
        }

        // smethod_270: HasSoulstone
        private bool HasSoulstoneOrAnkh()
        {
            return ScriptHelpers.GetReturnVal<string>("return HasSoulstone()", 0) != null;
        }

        // smethod_21: CanRezClass (Priest, Paladin, Shaman, Druid = true)
        private static bool CanRezClass(WoWPlayer player)
        {
            if (player == null) return false;
            switch (player.Class)
            {
                case WoWClass.Priest:
                case WoWClass.Paladin:
                case WoWClass.Shaman:
                case WoWClass.Druid:
                    return true;
                default:
                    return false;
            }
        }

        // smethod_22 (full): ShouldReleaseToCorpseRun — HB DungeonBot.smethod_22 lines 3154-3230
        private bool ShouldReleaseToCorpseRun()
        {
            using (StyxWoW.Memory.AcquireFrame())
            {
                // smethod_288→Select: Boss → Class272(Boss, WoWUnit)
                // smethod_289→Where: Class272 → bool (party.Any(IsAlive) && unit != null && unit.Combat)
                // smethod_290→Select: Class272 → Boss
                // .Any() → if true, return false (don't release)
                bool bossInCombatWithAlivePartyMember = BossManager.BossEncounters
                    .Select(b => new { boss = b, unit = b.ToWoWUnit() })
                    .Where(x => x.unit != null && x.unit.Combat && StyxWoW.Me.PartyMembers.Any(p => p.ToPlayer()?.IsAlive == true))
                    .Select(x => x.boss)
                    .Any();
                if (bossInCombatWithAlivePartyMember)
                    return false;

                // 5min timeout
                if (_deathTimer.Elapsed >= TimeSpan.FromMinutes(5.0))
                    return true;

                // Class142.bool_0 = PartyMode && TankAlive && HealerAlive
                bool tankAliveAndInDungeon = StyxWoW.Me.GroupInfo.RaidMembers.Any(m => m.HasRole(WoWPartyMember.GroupRole.Tank) && (m.ToPlayer()?.IsAlive == true) && m.AreaTableId == (int)StyxWoW.Me.CurrentMap.AreaTableId);
                bool healerAliveAndInDungeon = StyxWoW.Me.GroupInfo.RaidMembers.Any(m => m.HasRole(WoWPartyMember.GroupRole.Healer) && (m.ToPlayer()?.IsAlive == true) && m.AreaTableId == (int)StyxWoW.Me.CurrentMap.AreaTableId);
                bool partyModeAndTankHealerAlive = DungeonBuddySettings.Instance.PartyMode != PartyMode.Off && tankAliveAndInDungeon && healerAliveAndInDungeon;

                // smethod_291→Select: WoWPartyMember → Class257(member, player)
                // smethod_292→Select: Class257 → Class273(loc = player?.Location ?? member.Location3D)
                // smethod_293→Select: Class273 → Class274(isOnline = player != null || member.IsOnline)
                // smethod_294→Select: Class274 → Class275(isAlive = player ? player.IsAlive && not SoR : member.!Ghost && !Dead || health > 0)
                // smethod_295→Select: Class275 → Class276(mapId = member.CurrentMap.MapId)
                // smethod_296→Select: Class276 → Class277(inDungeon = mapId == null || mapId == Me.MapId)
                // smethod_297→Select: Class277 → Class278(canRez = CanRezClass(player) || member.Role.IsHealer)
                // Where(@class.method_0): isOnline && isAlive && inDungeon && canRez && CanNavigateFully && distance checks
                // smethod_298→Select: Class278 → WoWPartyMember
                // .Any() → if false (no eligible members), return true (release)
                var chain = StyxWoW.Me.GroupInfo.RaidMembers
                    .Select(m => new { member = m, player = m.ToPlayer() })
                    .Select(x => new
                    {
                        x.member,
                        x.player,
                        loc = x.player?.Location ?? x.member.Location3D,
                        isOnline = x.player != null || x.member.IsOnline,
                        isAlive = x.player != null
                            ? x.player.IsAlive
                            : (!x.member.Ghost && !x.member.Dead || x.member.Health > x.member.HealthMax * 0.01),
                        mapId = (uint?)null,
                        inDungeon = true,
                        canRez = (x.player != null && CanRezClass(x.player)) || x.member.HasRole(WoWPartyMember.GroupRole.Healer)
                    })
                    .Where(x => x.isOnline && x.isAlive && x.inDungeon && x.canRez);

                bool anyEligibleMember = chain
                    .Where(x => Navigator.CanNavigateFully(StyxWoW.Me.Location, x.loc))
                    .Any(x =>
                    {
                        float distSq = x.loc.DistanceSqr(StyxWoW.Me.Location);
                        if (distSq < 3600f)
                            return true;
                        if (partyModeAndTankHealerAlive && distSq < 40000f)
                            return true;
                        return false;
                    });

                if (anyEligibleMember)
                    return false;

                return true;
            }
        }

        // method_28: Corpse recovery behavior (HB Class141 — PrioritySelector with ContextChangeHandler + 3 decorators)
        private Composite CreateCorpseRecoveryBehavior()
        {
            return new PrioritySelector(
                // ContextChangeHandler (HB Class141.method_0): finds spirit healer, stores in field, returns it
                new ContextChangeHandler(SpiritHealerContextChangeHandler),

                // Guard: Me.IsGhost && spirit healer (HB Class141.method_1)
                new Decorator(
                    ctx => StyxWoW.Me.IsGhost && _corpseSpiritHealer != null,
                    new PrioritySelector(
                        // Decorator 1: method_2 (WithinInteractRange) -> Sequence(Interact, WaitContinue(2,false), AcceptXPLoss)
                        new Decorator(
                            ctx => _corpseSpiritHealer.WithinInteractRange,
                            new Sequence(
                                new Action(ctx => { _corpseSpiritHealer.Interact(); return RunStatus.Success; }),
                                new WaitContinue(TimeSpan.FromSeconds(2), ctx => false, new ActionAlwaysSucceed()),
                                new Action(ctx => { Lua.DoString("AcceptXPLoss()"); return RunStatus.Success; })
                            )
                        ),
                        // Decorator 2: method_4 (WithinInteractRange) -> Action(Interact)
                        new Decorator(
                            ctx => _corpseSpiritHealer.WithinInteractRange,
                            new Action(ctx => { _corpseSpiritHealer.Interact(); return RunStatus.Success; })
                        ),
                        // Decorator 3: method_6 (!WithinInteractRange) -> Action(MoveTo)
                        new Decorator(
                            ctx => !_corpseSpiritHealer.WithinInteractRange,
                            new Action(ctx => { Navigator.MoveTo(_corpseSpiritHealer.Location); return RunStatus.Running; })
                        )
                    )
                )
            );
        }

        private object SpiritHealerContextChangeHandler(object context)
        {
            _corpseSpiritHealer = ObjectManager.GetObjectsOfType<WoWUnit>().FirstOrDefault(u => u.IsSpiritHealer);
            return _corpseSpiritHealer;
        }

        // DungeonEntrance (HB method_96 / smethod_287)
        private WoWPoint GetDungeonEntrance()
        {
            if (DungeonManager.CurrentDungeon == null)
                return WoWPoint.Zero;
            var portalEntries = new[] { 192507U, 207896U };
            var portal = ObjectManager.GetObjectsOfType<WoWGameObject>()
                .FirstOrDefault(o => portalEntries.Contains(o.Entry));
            if (portal != null)
                return portal.Location;
            return DungeonManager.CurrentDungeon.Entrance;
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
                    return notDungeon && isSoloFarm;
                },
                new Sequence(
                    new Action(ctx =>
                    {
                        var settings = DungeonBuddySettings.Instance;
                        if (settings.SelectedDungeonIds.Length == 0)
                        {
                            if (ShouldLog(_soloFarmStatusLogThrottle, 5000))
                                Logging.Write("[DungeonBuddy] SoloFarm: no dungeon selected in settings");
                            TreeRoot.StatusText = "SoloFarm: No dungeon selected";
                            return RunStatus.Failure;
                        }

                        uint selectedDungeonId = settings.SelectedDungeonIds[0];

                        // Utiliser le script déjà sélectionné (HB parity), fallback sur lookup direct.
                        if (DungeonManager.CurrentDungeon == null || DungeonManager.CurrentDungeon.DungeonId != selectedDungeonId)
                            DungeonManager.SetDungeonById(selectedDungeonId);

                        var entranceFromCurrent = DungeonManager.CurrentDungeon?.Entrance ?? WoWPoint.Zero;
                        var entrance = DungeonManager.GetEntranceForDungeon(selectedDungeonId);
                        if (entranceFromCurrent != WoWPoint.Zero)
                            entrance = entranceFromCurrent;

                        if (entrance == WoWPoint.Zero)
                        {
                            if (ShouldLog(_soloFarmStatusLogThrottle, 5000))
                                Logging.Write("[DungeonBuddy] SoloFarm: entrance unavailable for dungeonId={0}", selectedDungeonId);
                            TreeRoot.StatusText = "SoloFarm: Entrance unavailable";
                            return RunStatus.Failure;
                        }

                        float distSq = StyxWoW.Me.Location.DistanceSqr(entrance);
                        if (ShouldLog(_soloFarmStatusLogThrottle, 5000))
                        {
                            double dist = Math.Sqrt(distSq);
                            Logging.Write("[DungeonBuddy] SoloFarm: moving to entrance (dungeonId={0}, distance={1:F1})", selectedDungeonId, dist);
                        }

                        // HB outside-instance portal travel keeps driving toward entrance until map changes.
                        TreeRoot.StatusText = "SoloFarm: Moving to dungeon entrance";
                        Navigator.MoveTo(entrance);
                        return RunStatus.Running;
                    })
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // SOLO FARM EXIT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Equivalent de HB method_13() — SoloFarm exit sequence.
        /// Quand tous les boss sont morts : marche vers ExitLocation (ou tente LeaveBattlefield).
        /// Une fois sorti de l'instance, OnMapChanged reset DungeonCompleted + BossManager,
        /// puis CreateSoloFarmBehavior retourne vers l'entrée pour un nouveau run.
        /// </summary>
        private Composite CreateSoloFarmExitBehavior()
        {
            return new Decorator(
                ctx => DungeonBuddySettings.Instance.QueueType == QueueType.SoloFarm &&
                       LfgManager.DungeonCompletedReason != CompleteReason.None &&
                       _soloFarmExitTimer.IsFinished &&
                       StyxWoW.Me.IsInInstance,
                new Action(ctx =>
                {
                    TreeRoot.StatusText = "SoloFarm: Dungeon complete — moving to exit";

                    var exit = DungeonManager.CurrentDungeon?.ExitLocation ?? WoWPoint.Zero;
                    if (exit != WoWPoint.Zero)
                    {
                        if (StyxWoW.Me.Location.DistanceSqr(exit) > 4 * 4)
                        {
                            Navigator.MoveTo(exit);
                            return RunStatus.Running;
                        }
                        // Reached exit portal — OnMapChanged will fire and reset the bot state.
                    }
                    else
                    {
                        Logging.WriteDebug("[DungeonBuddy] SoloFarm: ExitLocation not defined — bot will idle at end of run.");
                    }

                    return RunStatus.Running;
                })
            );
        }

        // ═══════════════════════════════════════════════════════════
        // HOTSPOT MOVEMENT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private int _hotspotIndex;

        /// <summary>
        /// Equivalent de HB method_11() — mouvement vers le prochain hotspot du profil.
        /// Quand il n'y a rien à tuer à portée, avance vers le prochain point du circuit.
        /// Si pas de profil, marche vers le prochain boss enregistré non-mort.
        /// </summary>
        private Composite CreateHotspotMovementBehavior()
        {
            return new Decorator(
                ctx => StyxWoW.Me.IsInInstance &&
                       !StyxWoW.Me.Combat &&
                       LfgManager.DungeonCompletedReason == CompleteReason.None &&
                       Targeting.Instance.TargetList.Count == 0,
                new Action(ctx =>
                {
                    // Try profile hotspots first
                    var profile = Bots.DungeonBuddy.Profiles.ProfileManager.CurrentProfile;
                    if (profile != null && profile.HotSpots.Count > 0)
                    {
                        if (_hotspotIndex >= profile.HotSpots.Count)
                            _hotspotIndex = 0;

                        var target = profile.HotSpots[_hotspotIndex];
                        if (StyxWoW.Me.Location.DistanceSqr(target) < 5 * 5)
                        {
                            _hotspotIndex = (_hotspotIndex + 1) % profile.HotSpots.Count;
                            target = profile.HotSpots[_hotspotIndex];
                        }

                        TreeRoot.StatusText = $"Moving to hotspot [{_hotspotIndex + 1}/{profile.HotSpots.Count}]";
                        Navigator.MoveTo(target);
                        return RunStatus.Running;
                    }

                    // Fallback: walk toward next living registered boss
                    var nextBoss = BossManager.CurrentBoss;
                    if (nextBoss != null)
                    {
                        TreeRoot.StatusText = $"Moving to boss: {nextBoss.Name}";
                        Navigator.MoveTo(nextBoss.Location);
                        return RunStatus.Running;
                    }

                    return RunStatus.Failure;
                })
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
                    {
                        if (ShouldLog(_debugDungeonLogThrottle, 1000))
                            Logging.WriteDebug($"[DB:DungeonTick] CurrentDungeonBehavior=null MapId={StyxWoW.Me.MapId} IsInInstance={StyxWoW.Me.IsInInstance}");
                        return RunStatus.Failure;
                    }

                    // Call Start() once when the behavior instance changes (new dungeon loaded).
                    // HB equivalent: composite_0, nulled only on dungeon change (method_41/method_43),
                    // not on tick result. Do NOT Stop/null here on Failure — that would cause a
                    // tight Start/Stop loop every pulse when the script has nothing to do.
                    if (_activeDungeonBehavior != current)
                    {
                        Logging.WriteDebug($"[DB:DungeonTick] Switching dungeon behavior instance: {DungeonManager.CurrentDungeon?.Name ?? "<null>"}");
                        _activeDungeonBehavior?.Stop(ctx);
                        _activeDungeonBehavior = current;
                        _activeDungeonBehavior.Start(ctx);
                    }

                    RunStatus result = _activeDungeonBehavior.Tick(ctx);
                    if (ShouldLog(_debugDungeonLogThrottle, 1000))
                    {
                        var first = Targeting.Instance.FirstUnit;
                        string targetInfo = first == null ? "none" : $"{first.Name}({first.Entry}) dist={Math.Sqrt(first.DistanceSqr):F1}";
                        Logging.WriteDebug($"[DB:DungeonTick] result={result} Poi={BotPoi.Current.Type} Target={targetInfo} InCombat={StyxWoW.Me.Combat}");
                    }

                    return result;
                })
            );
        }


        // ═══════════════════════════════════════════════════════════
        // COMBAT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        // HB 4.3.4 DungeonBot.waitTimer_0 = 2.0s (retarget cooldown)
        private readonly WaitTimer _retargetTimer = new WaitTimer(TimeSpan.FromSeconds(2.0));
        // HB 4.3.4 DungeonBot.waitTimer_1 = 3.0s (aggro grace period)
        private readonly WaitTimer _aggroTimer = new WaitTimer(TimeSpan.FromSeconds(3.0));

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
            // HB 4.3.4 DungeonBot.method_0() — exact port
            return new PrioritySelector(

                // ── BRANCHE 1 : Hors combat (smethod_24 = !Me.Combat) ──────────
                new Decorator(
                    ctx => !StyxWoW.Me.Combat,
                    new PrioritySelector(
                        Routine?.RestBehavior ?? new ActionAlwaysFail(),
                        Routine?.PreCombatBuffBehavior ?? new ActionAlwaysFail(),

                        // Set Kill POI from targeting list when idle (no active POI)
                        new DecoratorContinue(
                            ctx => BotPoi.Current.Type == PoiType.None &&
                                   Targeting.Instance.FirstUnit != null &&
                                   !Targeting.Instance.FirstUnit.Dead,
                            new Action(ctx =>
                            {
                                BotPoi.Current = new BotPoi(Targeting.Instance.FirstUnit, PoiType.Kill);
                                return RunStatus.Success;
                            })
                        ),

                        new DecoratorIsPoiType(PoiType.Kill, new PrioritySelector(

                            // If kill POI object is temporarily unresolved (streaming/object cache),
                            // move toward the POI location so we do not idle waiting for aggro.
                            new Decorator(
                                ctx => BotPoi.Current.AsObject == null &&
                                       BotPoi.Current.Location != WoWPoint.Zero &&
                                       StyxWoW.Me.Location.DistanceSqr(BotPoi.Current.Location) > 4 * 4,
                                new Sequence(
                                    new Action(ctx =>
                                    {
                                        if (ShouldLog(_debugMoveLogThrottle, 1000))
                                            Logging.WriteDebug($"[DB:CombatMove] POI object unresolved -> moving to POI location {BotPoi.Current.Location}");
                                        return RunStatus.Success;
                                    }),
                                    new NavigationAction(ctx => BotPoi.Current.Location)
                                )
                            ),

                            // [0] smethod_25 : TargetList vide/mort OU (aggroTimer pas expiré ET mob ni agressif ni taggué)
                            new Decorator(
                                ctx =>
                                {
                                    if (Targeting.Instance.TargetList.Count == 0 || Targeting.Instance.FirstUnit.Dead)
                                        return true;
                                    if (!_aggroTimer.IsFinished && !Targeting.Instance.FirstUnit.IsTargetingMyPartyMember)
                                        return !Targeting.Instance.FirstUnit.TaggedByMe;
                                    return false;
                                },
                                new Sequence(
                                    new ActionClearPoi("No targets in list1"),
                                    // smethod_26/27/28 : clear current target if set, wait until cleared
                                    new DecoratorContinue(
                                        ctx => StyxWoW.Me.CurrentTarget != null,
                                        new Sequence(
                                            new Action(ctx => { StyxWoW.Me.ClearTarget(); return RunStatus.Success; }),
                                            new WaitContinue(5, ctx => StyxWoW.Me.CurrentTarget == null, new ActionAlwaysSucceed())
                                        )
                                    )
                                )
                            ),

                            // [1] smethod_29/30/31 : BotPoi ne pointe pas FirstUnit → mettre à jour le POI
                            new Decorator(
                                ctx => BotPoi.Current.AsObject != null &&
                                       BotPoi.Current.AsObject.ToUnit() != Targeting.Instance.FirstUnit,
                                new Sequence(
                                    new Action(ctx => { Logging.WriteDebug("Current POI is not the best target. Changing."); return RunStatus.Success; }),
                                    new ActionSetPoi(true, ctx => new BotPoi(Targeting.Instance.FirstUnit, PoiType.Kill))
                                )
                            ),

                            // [2] smethod_32/33/34/35 : pas en LOS → naviguer
                            new Decorator(
                                ctx => !Targeting.Instance.FirstUnit.InLineOfSpellSight,
                                new PrioritySelector(
                                    new Decorator(
                                        ctx => !Navigator.CanNavigateFully(StyxWoW.Me.Location, Targeting.Instance.FirstUnit.Location),
                                        new Action(ctx =>
                                        {
                                            Blacklist.Add(Targeting.Instance.FirstUnit, TimeSpan.FromMinutes(25.0));
                                            return RunStatus.Success;
                                        })
                                    ),
                                    new NavigationAction(ctx => Targeting.Instance.FirstUnit.Location)
                                )
                            ),

                            // [3] smethod_36/37/38 : pas encore ciblé → Target() + wait 5s
                            new Decorator(
                                ctx => (_retargetTimer.IsFinished && StyxWoW.Me.CurrentTarget != BotPoi.Current.AsObject.ToUnit())
                                       || StyxWoW.Me.CurrentTarget == null,
                                new Sequence(
                                    new Action(ctx =>
                                    {
                                        BotPoi.Current.AsObject.ToUnit().Target();
                                        _retargetTimer.Reset();
                                        return RunStatus.Success;
                                    }),
                                    new WaitContinue(5,
                                        ctx => StyxWoW.Me.CurrentTarget != null &&
                                               StyxWoW.Me.CurrentTarget == BotPoi.Current.AsObject.ToUnit(),
                                        new ActionAlwaysSucceed())
                                )
                            ),

                            // [4] PullBuff (smethod_39)
                            Routine?.PullBuffBehavior ?? new ActionAlwaysFail(),

                            // [5] MoveToTarget si disponible (smethod_39)
                            new Decorator(
                                ctx => Routine?.MoveToTargetBehavior != null,
                                Routine?.MoveToTargetBehavior ?? new ActionAlwaysFail()
                            ),

                                // [5b] Navigate to melee range when Routine has no MoveToTargetBehavior.
                                // HB method_0(): NavigationAction navigates toward FirstUnit after targeting.
                                // Without this, melee routines fail to pull when target is in LOS but out
                                // of melee range — the pull behavior returns Failure and the tree idles.
                                new Decorator(
                                    ctx => Routine?.MoveToTargetBehavior == null &&
                                           StyxWoW.Me.CurrentTarget != null &&
                                           StyxWoW.Me.CurrentTarget.DistanceSqr > 4 * 4,
                                    new Sequence(
                                        new Action(ctx =>
                                        {
                                            if (ShouldLog(_debugMoveLogThrottle, 1000))
                                                Logging.WriteDebug($"[DB:CombatMove] Moving to melee range of {StyxWoW.Me.CurrentTarget?.Name} dist={Math.Sqrt(StyxWoW.Me.CurrentTarget?.DistanceSqr ?? 0):F1}");
                                            return RunStatus.Success;
                                        }),
                                        new NavigationAction(ctx => StyxWoW.Me.CurrentTarget.Location)
                                    )
                                ),

                                // [6] Pull
                                Routine?.PullBehavior ?? new ActionAlwaysFail()
                            ))
                        )
                    ),

                // ── BRANCHE 2 : En combat (smethod_40 = Me.Combat) ────────────
                new Decorator(
                    ctx => StyxWoW.Me.Combat,
                    new PrioritySelector(

                        // smethod_41 : reset aggro timer, toujours Failure pour continuer
                        new Action(ctx => { _aggroTimer.Reset(); return RunStatus.Failure; }),

                        new DecoratorIsPoiType(PoiType.Kill, new PrioritySelector(

                            // [0] smethod_42/43/44/45 : FirstUnit mort/null ou POI invalide → ClearPoi
                            new Decorator(
                                ctx => Targeting.Instance.FirstUnit == null ||
                                       Targeting.Instance.FirstUnit.Dead ||
                                       BotPoi.Current.AsObject == null ||
                                       !BotPoi.Current.AsObject.IsValid,
                                new Sequence(
                                    new ActionClearPoi("No targets in list2"),
                                    // smethod_43/44/45 : clear current target if set, wait until cleared
                                    new DecoratorContinue(
                                        ctx => StyxWoW.Me.CurrentTarget != null,
                                        new Sequence(
                                            new Action(ctx => { StyxWoW.Me.ClearTarget(); return RunStatus.Success; }),
                                            new WaitContinue(2, ctx => StyxWoW.Me.CurrentTarget == null, new ActionAlwaysSucceed())
                                        )
                                    )
                                )
                            ),

                            // [1] smethod_46/47 : BotPoi != FirstUnit → change POI, retourne Failure
                            new Decorator(
                                ctx => BotPoi.Current.AsObject.ToUnit() != Targeting.Instance.FirstUnit,
                                new Sequence(
                                    new Action(ctx =>
                                    {
                                        Logging.WriteDebug("Current POI is not the best pull target. Changing.");
                                        BotPoi.Current = new BotPoi(Targeting.Instance.FirstUnit, PoiType.Kill);
                                        return RunStatus.Failure;
                                    })
                                )
                            ),

                            // [2] smethod_48/49/50 : retarget en combat si nécessaire
                            new Decorator(
                                ctx => (_retargetTimer.IsFinished && StyxWoW.Me.CurrentTarget != BotPoi.Current.AsObject.ToUnit())
                                       || StyxWoW.Me.CurrentTarget == null,
                                new Sequence(
                                    new Action(ctx =>
                                    {
                                        BotPoi.Current.AsObject.ToUnit().Target();
                                        _retargetTimer.Reset();
                                        return RunStatus.Success;
                                    }),
                                    new WaitContinue(2,
                                        ctx => StyxWoW.Me.CurrentTarget != null &&
                                               BotPoi.Current.AsObject != null &&
                                               StyxWoW.Me.CurrentTarget == BotPoi.Current.AsObject.ToUnit(),
                                        new ActionAlwaysSucceed())
                                )
                            ),

                            // Encounter handler (CopilotBuddy — boss encounter scripts via réflexion)
                            new Decorator(
                        // Utilise la liste enregistrée au lieu du flag IsBoss (cassé pour les boss de bas niveau)
                        ctx => StyxWoW.Me.CurrentTarget != null &&
                               BossManager.Bosses.Any(b => b.EntryId == StyxWoW.Me.CurrentTarget.Entry),
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

                            // [3] HealBehavior
                            Routine?.HealBehavior ?? new ActionAlwaysFail(),

                            // [4] CombatBuffBehavior
                            Routine?.CombatBuffBehavior ?? new ActionAlwaysFail(),

                            // [5] CombatBehavior
                            Routine?.CombatBehavior ?? new ActionAlwaysFail()
                        ))
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // IN-DUNGEON SUPPORT (HB method_4 / method_5)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateInDungeonSupportBehavior()
        {
            return new Decorator(
                ctx => StyxWoW.Me.IsInInstance,
                new PrioritySelector(
                    new Decorator(
                        ctx => DungeonBuddySettings.Instance.QueueType == QueueType.SoloFarm,
                        new Sequence(
                            CreateSoloFarmSupportBehavior(),
                            new ActionAlwaysSucceed()
                        )
                    ),

                    CreateFollowBehavior()
                )
            );
        }

        private Composite CreateSoloFarmSupportBehavior()
        {
            return new PrioritySelector(
                // HB smethod_91..95 guards (maintenance/vendor checks).
                // Keep behavior non-blocking here; movement/targeting remains the primary in-instance action.
                new Decorator(ShouldRepairInSoloFarm, new ActionAlwaysFail()),
                new Decorator(ShouldBuyDrinksInSoloFarm, new ActionAlwaysFail()),
                new Decorator(ShouldSellItemsInSoloFarm, new ActionAlwaysFail()),
                new Decorator(ShouldTrainInSoloFarm, new ActionAlwaysFail()),
                new Decorator(IsInWrongInstanceInSoloFarm, new ActionAlwaysFail()),

                new PrioritySelector(
                    // HB smethod_96/smethod_97: set kill POI from targeting when POI is not Kill.
                    new Decorator(
                        ShouldSetSoloFarmKillPoi,
                        new ActionSetPoi(true, CreateSoloFarmKillPoi)
                    ),

                    // HB smethod_98 + method_22: move when not actually in combat.
                    new Decorator(
                        ShouldMoveInSoloFarm,
                        CreateSoloFarmMovementBehavior()
                    )
                )
            );
        }

        private Composite CreateSoloFarmMovementBehavior()
        {
            WoWPoint moveTo = WoWPoint.Zero;

            return new PrioritySelector(
                new ContextChangeHandler(ctx => moveTo = GetSoloFarmMoveToPoint()),
                new Decorator(
                    ctx => moveTo != WoWPoint.Zero,
                    new PrioritySelector(
                        Helpers.ScriptHelpers.CreateMountBehavior(() => moveTo),
                        new Action(ctx =>
                        {
                            Navigator.MoveTo(moveTo);
                            return RunStatus.Success;
                        })
                    )
                )
            );
        }

        /// <summary>
        /// Port de HB 4.3.4 DungeonBot.method_23() — calcul du point de mouvement solo farm.
        /// 6 priorités: in-combat membre → mort → distant (>=2) → Hotspot POI → boss ObjectManager → breadcrumbs.
        /// </summary>
        private WoWPoint GetSoloFarmMoveToPoint()
        {
            // Case 1: In-combat party member (HB method_23 priority 1)
            var inCombatMember = StyxWoW.Me.PartyMemberInfos
                .Select(pm => pm.ToPlayer())
                .FirstOrDefault(p => p != null && p.Combat);
            if (inCombatMember != null)
            {
                TreeRoot.StatusText = $"Moving towards in combat party member {inCombatMember.Name}";
                return inCombatMember.Location;
            }

            // Case 2: Dead party member (HB method_23 priority 2)
            var deadMember = StyxWoW.Me.PartyMemberInfos
                .Select(pm => pm.ToPlayer())
                .FirstOrDefault(p => p != null && p.IsDead);
            if (deadMember != null)
            {
                TreeRoot.StatusText = $"Moving towards dead party member {deadMember.Name}";
                return deadMember.Location;
            }

            // Case 3: 2+ distant party members (HB method_23 priority 3)
            var followDist = DungeonBuddySettings.Instance.FollowingDistance;
            var distantMembers = StyxWoW.Me.PartyMemberInfos
                .Select(pm => pm.ToPlayer())
                .Where(p => p != null && StyxWoW.Me.Location.DistanceSqr(p.Location) > followDist * followDist * 4.0)
                .ToArray();
            if (distantMembers.Length >= 2)
            {
                TreeRoot.StatusText = $"Moving towards distant party member {distantMembers[0].Name}";
                return distantMembers[0].Location;
            }

            // Case 4: Hotspot POI (HB method_23 priority 4)
            if (BotPoi.Current.Type == PoiType.Hotspot && BotPoi.Current.Location != WoWPoint.Zero)
            {
                var hotspot = BotPoi.Current.Location;
                if (StyxWoW.Me.Location.DistanceSqr(hotspot) <= 16f)
                    BotPoi.Clear("Reached Hotspot location");
                return hotspot;
            }

            // Case 5: Find boss unit directly in ObjectManager (HB method_23 priority 5 / method_24)
            var bossUnit = FindCurrentBossUnit();
            if (bossUnit != null)
            {
                TreeRoot.StatusText = $"Moving towards boss {bossUnit.Name}";
                return bossUnit.Location;
            }

            // Case 6: Boss PathBreadCrumbs (HB method_23 priority 6)
            TreeRoot.StatusText = string.Empty;
            var currentBoss = BossManager.Bosses.FirstOrDefault(b => !b.IsDead);
            if (currentBoss != null && currentBoss.PathBreadCrumbs.Count > 0)
            {
                var crumb = currentBoss.PathBreadCrumbs.Peek();
                if (StyxWoW.Me.Location.DistanceSqr(crumb) < 25f)
                    currentBoss.PathBreadCrumbs.Dequeue();
                return crumb;
            }

            return WoWPoint.Zero;
        }

        /// <summary>
        /// Port de HB 4.3.4 DungeonBot.method_24() — cherche le prochain boss à tuer dans
        /// l'ObjectManager directement (bypass targeting), ordonné par KillOrder puis distance.
        /// </summary>
        private static WoWUnit? FindCurrentBossUnit()
        {
            var bosses = BossManager.Bosses;
            return ObjectManager.GetObjectsOfType<WoWUnit>()
                .Where(u => u.IsValid && u.IsAlive && IsTargetableBossUnit(u, bosses))
                .OrderBy(u =>
                {
                    for (int i = 0; i < bosses.Count; i++)
                        if (bosses[i].Entry == u.Entry) return i;
                    return int.MaxValue;
                })
                .ThenBy(u => u.DistanceSqr)
                .FirstOrDefault();
        }

        /// <summary>
        /// Port de HB 4.3.4 DungeonBot.method_25() — filtre de validité boss pour navigation.
        /// </summary>
        private static bool IsTargetableBossUnit(WoWUnit unit, IReadOnlyList<BossManager.Boss> bosses)
        {
            var currentBoss = bosses.FirstOrDefault(b => !b.IsDead);
            if (currentBoss != null && unit.Entry == currentBoss.Entry)
                return true;
            var unitBoss = bosses.FirstOrDefault(b => b.Entry == unit.Entry);
            if (unitBoss != null && !unitBoss.IsOptional && !unitBoss.IsDead)
                return true;
            return false;
        }

        private WoWPoint GetSoloFarmFollowPoint(WoWPlayer player)
        {
            bool inLineOfSpellSight = player.InLineOfSpellSight;
            if (player.DistanceSqr <= DungeonBuddySettings.Instance.FollowingDistance * DungeonBuddySettings.Instance.FollowingDistance && inLineOfSpellSight)
                return WoWPoint.Zero;

            WoWPoint moveTo = player.Location;
            bool foundAdjustedPoint = false;
            if (inLineOfSpellSight)
            {
                for (float distance = 12f; distance <= 18f; distance += 1f)
                {
                    WoWPoint point = WoWMathHelper.CalculatePointFrom(StyxWoW.Me.Location, player.Location, distance);
                    if (Navigator.CanNavigateFully(StyxWoW.Me.Location, point))
                    {
                        foundAdjustedPoint = true;
                        moveTo = point;
                        break;
                    }
                }
            }

            if (!foundAdjustedPoint &&
                !Navigator.CanNavigateFully(StyxWoW.Me.Location, moveTo) &&
                player.DistanceSqr < 10000.0 &&
                Math.Abs(player.Z - StyxWoW.Me.Z) < 30f)
            {
                Navigator.PlayerMover.MoveTowards(new Tripper.XNAMath.Vector3(moveTo.X, moveTo.Y, moveTo.Z));
            }

            return moveTo;
        }

        // HB smethod_91
        private static bool ShouldRepairInSoloFarm(object context)
        {
            return StyxWoW.Me.LowestDurabilityPercent <= 0.1;
        }

        // HB smethod_92
        private static bool ShouldBuyDrinksInSoloFarm(object context)
        {
            return StyxWoW.Me.PowerType == WoWPowerType.Mana &&
                   CharacterSettings.Instance.DrinkAmount > 0 &&
                   !Consumable.GetDrinks().Any();
        }

        // HB smethod_93
        private static bool ShouldSellItemsInSoloFarm(object context)
        {
            return StyxWoW.Me.FreeBagSlots < DungeonBuddySettings.Instance.MinFreeBagSlots &&
                   GetItemsToSellCount() > 0;
        }

        // HB smethod_94
        private static bool ShouldTrainInSoloFarm(object context)
        {
            return Vendors.NeedClassTraining && LfgManager.DungeonCompletedReason != CompleteReason.None;
        }

        // HB smethod_95
        private static bool IsInWrongInstanceInSoloFarm(object context)
        {
            if (LfgManager.CurrentLfgDungeonId == 0)
                return false;

            uint currentDungeonId = Bots.DungeonBuddy.Profiles.ProfileManager.GetLfgDungeonIdFromMapId(StyxWoW.Me.MapId);
            return currentDungeonId != LfgManager.CurrentLfgDungeonId;
        }

        // HB smethod_96
        private static bool ShouldSetSoloFarmKillPoi(object context)
        {
            return BotPoi.Current.Type != PoiType.Kill &&
                   Targeting.Instance.FirstUnit != null &&
                   Targeting.Instance.FirstUnit.IsAlive;
        }

        // HB smethod_97
        private static BotPoi CreateSoloFarmKillPoi(object context)
        {
            return new BotPoi(Targeting.Instance.FirstUnit, PoiType.Kill);
        }

        // HB smethod_98
        private static bool ShouldMoveInSoloFarm(object context)
        {
            return !StyxWoW.Me.IsActuallyInCombat;
        }

        private static int GetItemsToSellCount()
        {
            return StyxWoW.Me.CarriedItems.Count(item =>
                item.IsValid &&
                item.SellPrice > 0 &&
                item.Quality == WoWItemQuality.Poor &&
                !Styx.Logic.Profiles.ProtectedItemsManager.Contains(item.Entry));
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
                    bool tankTooFar = StyxWoW.Me.Location.DistanceSqr(tank.Location) > followDist * followDist;
                    bool tankOffBossPath = !IsUnitOnPathToCurrentBoss(tank);
                    if (tankTooFar || tankOffBossPath)
                    {
                        Navigator.MoveTo(tank.Location);
                        return RunStatus.Running;
                    }

                    return RunStatus.Failure;
                })
            );
        }

        // HB 4.3.4 DungeonBot.method_9 parity: validate whether a party member is near
        // the cached path from me to current boss, refreshed every 2 seconds.
        private bool IsUnitOnPathToCurrentBoss(WoWPlayer player)
        {
            if (player == null || BossManager.CurrentBoss == null)
                return false;

            if (_cachedBossPath == null || _pathValidationRefreshTimer.IsFinished)
            {
                dynamic navProvider = Navigator.NavigationProvider;
                _cachedBossPath = navProvider.Nav.FindPath(StyxWoW.Me.Location, BossManager.CurrentBoss.Location);
                _pathValidationRefreshTimer.Reset();
            }

            if (_cachedBossPath == null || !_cachedBossPath.Succeeded)
                return false;

            WoWPoint memberLocation = player.Location;
            for (int i = 1; i < _cachedBossPath.Points.Length; i++)
            {
                WoWPoint segmentStart = _cachedBossPath.Points[i - 1];
                WoWPoint segmentEnd = _cachedBossPath.Points[i];
                if (memberLocation.GetNearestPointOnSegment(segmentStart, segmentEnd).DistanceSqr(memberLocation) <= 900f)
                    return true;
            }

            return false;
        }
    }
}