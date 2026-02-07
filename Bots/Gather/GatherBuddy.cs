using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CommonBehaviors.Actions;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Bots.Gather
{
    /// <summary>
    /// GatherBuddy - Gathering bot for WoW 3.3.5a (WotLK).
    /// Harvests herbs and minerals along a waypoint route loaded from a profile.
    /// </summary>
    public class GatherBuddy : BotBase
    {
        // ═══════════════════════════════════════════════════════════
        // BOTBASE IMPLEMENTATION
        // ═══════════════════════════════════════════════════════════

        public override string Name => "GatherBuddy";
        public override bool IsPrimaryType => true;
        public override bool RequiresProfile => true;
        public override bool RequirementsMet => true;
        public override PulseFlags PulseFlags => PulseFlags.All;

        private PrioritySelector? _root;
        private CircularQueue<WoWPoint>? _waypointQueue;
        private List<WoWPoint> _waypoints = new();
        private WoWGameObject? _currentNode;
        private readonly Stopwatch _cleanupTimer = new();
        private static CombatRoutine? Routine => RoutineManager.Current;

        public override Composite Root => _root ??= CreateRootBehavior();

        // ═══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════

        public override void Start()
        {
            Logging.Write("[GatherBuddy] Starting...");

            // Load waypoints from ProfileManager (loaded via UI "Load Profile" button)
            if (ProfileManager.CurrentProfile == null)
            {
                throw new Exception("[GatherBuddy] No profile loaded. Use 'Load Profile' button first.");
            }

            // Extract waypoints from GrindArea or HotspotManager
            _waypoints.Clear();
            float heightMod = GatherBuddySettings.Instance.HeightModifier;

            if (ProfileManager.CurrentProfile.GrindArea?.Hotspots != null &&
                ProfileManager.CurrentProfile.GrindArea.Hotspots.Count > 0)
            {
                Logging.Write("[GatherBuddy] Loading waypoints from GrindArea");
                foreach (var hotspot in ProfileManager.CurrentProfile.GrindArea.Hotspots)
                {
                    _waypoints.Add(hotspot.ToWoWPoint().Add(0f, 0f, heightMod));
                }
            }
            else if (ProfileManager.CurrentProfile.HotspotManager?.Hotspots != null &&
                     ProfileManager.CurrentProfile.HotspotManager.Hotspots.Count > 0)
            {
                Logging.Write("[GatherBuddy] Loading waypoints from HotspotManager");
                foreach (var point in ProfileManager.CurrentProfile.HotspotManager.Hotspots)
                {
                    _waypoints.Add(point.Add(0f, 0f, heightMod));
                }
            }
            else
            {
                throw new Exception("[GatherBuddy] Profile has no hotspots. Load a profile with <Hotspots> or <GrindArea>.");
            }

            // Build circular queue from waypoints
            _waypointQueue = new CircularQueue<WoWPoint>();
            foreach (var wp in _waypoints)
                _waypointQueue.Enqueue(wp);

            // Start at nearest waypoint
            if (StyxWoW.Me != null)
            {
                var nearest = _waypoints.OrderBy(w => w.DistanceSqr(StyxWoW.Me.Location)).First();
                _waypointQueue.CycleTo(nearest);
            }

            // Setup Bounce mode event handlers
            if (GatherBuddySettings.Instance.PathingType == PathType.Bounce)
            {
                _waypointQueue.OnEndOfQueue += OnQueueCycle;
            }

            Logging.Write($"[GatherBuddy] Loaded {_waypoints.Count} waypoints");

            // Targeting filters
            Targeting.Instance.IncludeTargetsFilter += IncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter += IncludeLootFilter;

            _cleanupTimer.Start();
            Logging.Write("[GatherBuddy] Started successfully!");
        }

        public override void Stop()
        {
            Logging.Write("[GatherBuddy] Stopping...");

            Targeting.Instance.IncludeTargetsFilter -= IncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter -= IncludeLootFilter;

            if (_waypointQueue != null)
            {
                _waypointQueue.OnEndOfQueue -= OnQueueCycle;
            }

            NodeTracker.Reset();
            _cleanupTimer.Stop();
        }

        public override void Pulse()
        {
            // Periodic cleanup of expired node tracking
            if (_cleanupTimer.ElapsedMilliseconds > 30000)
            {
                NodeTracker.CleanupExpired();
                _cleanupTimer.Restart();
            }
        }

        private void OnQueueCycle(object? sender, EventArgs e)
        {
            // Bounce mode: reverse waypoints when reaching end
            _waypoints.Reverse();
            _waypointQueue = new CircularQueue<WoWPoint>();
            foreach (var wp in _waypoints)
                _waypointQueue.Enqueue(wp);
            _waypointQueue.OnEndOfQueue += OnQueueCycle;
        }

        // ═══════════════════════════════════════════════════════════
        // BEHAVIOR TREE
        // ═══════════════════════════════════════════════════════════

        private PrioritySelector CreateRootBehavior()
        {
            return new PrioritySelector(
                // 1. Death management
                CreateDeathBehavior(),

                // 2. Combat (if aggro)
                CreateCombatBehavior(),

                // 3. Loot (if LootMobs enabled)
                new Decorator(
                    ctx => GatherBuddySettings.Instance.LootMobs,
                    CreateLootBehavior()
                ),

                // 4. Node gathering
                CreateGatherBehavior(),

                // 5. Movement to next waypoint
                CreateMovementBehavior(),

                // 6. Idle
                new ActionIdle()
            );
        }

        // ═══════════════════════════════════════════════════════════
        // DEATH BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateDeathBehavior()
        {
            return new PrioritySelector(
                // Dead - release spirit
                new Decorator(
                    ctx => StyxWoW.Me != null && StyxWoW.Me.IsDead,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Logging.Write("[GatherBuddy] Died! Releasing...");
                            Lua.DoString("RepopMe()");
                            return RunStatus.Success;
                        }),
                        new WaitContinue(5, ctx => StyxWoW.Me != null && StyxWoW.Me.IsGhost, new ActionAlwaysSucceed())
                    )
                ),
                // Ghost - return to corpse
                new Decorator(
                    ctx => StyxWoW.Me != null && StyxWoW.Me.IsGhost,
                    new PrioritySelector(
                        new Decorator(
                            ctx => StyxWoW.Me!.Location.Distance(StyxWoW.Me.CorpsePoint) < 40,
                            new Action(ctx =>
                            {
                                Lua.DoString("RetrieveCorpse()");
                                return RunStatus.Success;
                            })
                        ),
                        new Action(ctx =>
                        {
                            Navigator.MoveTo(StyxWoW.Me!.CorpsePoint);
                            return RunStatus.Running;
                        })
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // COMBAT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateCombatBehavior()
        {
            return new PrioritySelector(
                // Rest if not in combat and routine has rest behavior
                new Decorator(
                    ctx => StyxWoW.Me != null && !StyxWoW.Me.Combat && Routine?.RestBehavior != null,
                    Routine!.RestBehavior
                ),
                // Combat if in combat and have a target
                new Decorator(
                    ctx => StyxWoW.Me != null && StyxWoW.Me.Combat && Targeting.Instance.FirstUnit != null,
                    new PrioritySelector(
                        // Dismount first
                        new Decorator(
                            ctx => StyxWoW.Me!.Mounted,
                            new Action(ctx =>
                            {
                                Mount.Dismount("Combat");
                                return RunStatus.Success;
                            })
                        ),
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
                ctx => StyxWoW.Me != null && !StyxWoW.Me.Combat &&
                       LootTargeting.Instance.FirstObject != null &&
                       LootTargeting.Instance.FirstObject.DistanceSqr < 30 * 30,
                new PrioritySelector(
                    // Close enough to interact
                    new Decorator(
                        ctx => LootTargeting.Instance.FirstObject.DistanceSqr < 5 * 5,
                        new Action(ctx =>
                        {
                            LootTargeting.Instance.FirstObject.Interact();
                            return RunStatus.Success;
                        })
                    ),
                    // Move to lootable
                    new Action(ctx =>
                    {
                        Navigator.MoveTo(LootTargeting.Instance.FirstObject.Location);
                        return RunStatus.Running;
                    })
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // GATHER BEHAVIOR (BOT CORE)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateGatherBehavior()
        {
            return new PrioritySelector(
                // ContextChanger: find best node each tick
                ctx =>
                {
                    _currentNode = FindBestNode();
                    return ctx;
                },

                // No node found - fall through to movement
                new Decorator(
                    ctx => _currentNode == null,
                    new ActionAlwaysFail()
                ),

                // Node found - move to it and harvest
                new Sequence(
                    // Log the find
                    new Action(ctx =>
                    {
                        Logging.WriteDiagnostic($"[GatherBuddy] Found {_currentNode!.Name} at {_currentNode.Distance:F0}y");
                        return RunStatus.Success;
                    }),

                    new PrioritySelector(
                        // Too far - move closer
                        new Decorator(
                            ctx => _currentNode != null && _currentNode.DistanceSqr > 5 * 5,
                            new Sequence(
                                // Dismount if getting close
                                new DecoratorContinue(
                                    ctx => StyxWoW.Me != null && StyxWoW.Me.Mounted && _currentNode!.DistanceSqr < 15 * 15,
                                    new Action(ctx =>
                                    {
                                        Mount.Dismount("Gathering");
                                        return RunStatus.Success;
                                    })
                                ),
                                new Action(ctx =>
                                {
                                    Navigator.MoveTo(_currentNode!.Location);
                                    return RunStatus.Running;
                                })
                            )
                        ),

                        // Close enough - interact
                        new Decorator(
                            ctx => _currentNode != null && _currentNode.DistanceSqr <= 5 * 5 &&
                                   StyxWoW.Me != null && !StyxWoW.Me.IsCasting,
                            new Sequence(
                                // Stop, dismount, face, interact
                                new Action(ctx =>
                                {
                                    WoWMovement.MoveStop();

                                    if (StyxWoW.Me!.Mounted)
                                        Mount.Dismount("Gathering");

                                    if (GatherBuddySettings.Instance.FaceNodes)
                                        WoWMovement.Face(_currentNode!.Location);

                                    _currentNode!.Interact();
                                    Logging.Write($"[GatherBuddy] Gathering {_currentNode.Name}");
                                    return RunStatus.Success;
                                }),

                                // Wait for cast to finish
                                new WaitContinue(
                                    5,
                                    ctx => StyxWoW.Me != null && !StyxWoW.Me.IsCasting,
                                    new Action(ctx =>
                                    {
                                        if (_currentNode != null)
                                            NodeTracker.MarkHarvested(_currentNode);
                                        _currentNode = null;
                                        return RunStatus.Success;
                                    })
                                )
                            )
                        )
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // MOVEMENT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateMovementBehavior()
        {
            return new PrioritySelector(
                // No waypoints loaded
                new Decorator(
                    ctx => _waypointQueue == null || _waypointQueue.Count == 0,
                    new Action(ctx =>
                    {
                        Logging.Write("[GatherBuddy] No waypoints! Load a profile with hotspots.");
                        return RunStatus.Failure;
                    })
                ),

                // Arrived at waypoint - advance to next
                new Decorator(
                    ctx => StyxWoW.Me != null &&
                           StyxWoW.Me.Location.DistanceSqr(_waypointQueue!.Peek()) < 15 * 15,
                    new Action(ctx =>
                    {
                        _waypointQueue!.Dequeue();
                        return RunStatus.Success;
                    })
                ),

                // Move to current waypoint
                new Action(ctx =>
                {
                    var targetWaypoint = _waypointQueue!.Peek();

                    // Mount if possible and far away
                    if (StyxWoW.Me != null && !StyxWoW.Me.Mounted &&
                        Mount.CanMount() &&
                        StyxWoW.Me.Location.DistanceSqr(targetWaypoint) > 50 * 50)
                    {
                        Mount.MountUp();
                        return RunStatus.Running;
                    }

                    Navigator.MoveTo(targetWaypoint);
                    return RunStatus.Running;
                })
            );
        }

        // ═══════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Finds the best node to harvest based on distance, type, and validity.
        /// </summary>
        private WoWGameObject? FindBestNode()
        {
            var settings = GatherBuddySettings.Instance;
            float maxRangeSqr = settings.NodeDetectionRange * settings.NodeDetectionRange;

            return ObjectManager.GetObjectsOfType<WoWGameObject>()
                .Where(obj =>
                {
                    // Distance check
                    if (obj.DistanceSqr > maxRangeSqr)
                        return false;

                    // Node type check
                    bool isValidType =
                        (settings.GatherHerbs && obj.IsHerb && obj.CanHarvest) ||
                        (settings.GatherMinerals && obj.IsMineral && obj.CanMine);

                    if (!isValidType)
                        return false;

                    // Blacklist check
                    if (!NodeTracker.IsNodeValid(obj))
                        return false;

                    // Anti-ninja: check if another player is near the node
                    if (settings.NoNinja)
                    {
                        bool playerNearby = ObjectManager.GetObjectsOfType<WoWPlayer>()
                            .Any(p => !p.IsMe && p.IsAlive &&
                                      p.Location.DistanceSqr(obj.Location) < 15 * 15);
                        if (playerNearby)
                            return false;
                    }

                    return true;
                })
                .OrderBy(obj => obj.DistanceSqr)
                .FirstOrDefault();
        }

        // ═══════════════════════════════════════════════════════════
        // TARGETING FILTERS
        // ═══════════════════════════════════════════════════════════

        private void IncludeTargetsFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
        {
            // Ignore elites if configured
            if (GatherBuddySettings.Instance.IgnoreElites)
            {
                incoming.RemoveAll(obj => obj is WoWUnit unit && unit.Elite);
            }
        }

        private void IncludeLootFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
        {
            // Add lootable mobs if LootMobs enabled
            if (GatherBuddySettings.Instance.LootMobs)
            {
                foreach (var obj in incoming)
                {
                    if (obj is WoWUnit unit && unit.IsDead && unit.CanLoot)
                        outgoing.Add(obj);
                }
            }
        }
    }
}
