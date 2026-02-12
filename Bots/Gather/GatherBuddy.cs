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
using Styx.Logic.Inventory.Frames;
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

        // FEAT-40: Stats tracking
        private int _nodesGathered;
        private int _herbsGathered;
        private int _mineralsGathered;
        private readonly Stopwatch _sessionTimer = new();
        private WoWPoint _vendorLocation = WoWPoint.Empty;

        public override Composite Root => _root ??= CreateRootBehavior();

        /// <summary>
        /// FEAT-40: Session statistics.
        /// </summary>
        public int NodesGathered => _nodesGathered;
        public int HerbsGathered => _herbsGathered;
        public int MineralsGathered => _mineralsGathered;
        public TimeSpan SessionTime => _sessionTimer.Elapsed;

        // ═══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// FEAT-40: Called once when bot is first loaded. Loads persisted blacklist.
        /// </summary>
        public override void Initialize()
        {
            NodeTracker.LoadBlacklist();
            Logging.Write("[GatherBuddy] Initialized (blacklist loaded)");
        }

        public override void Start()
        {
            Logging.Write("[GatherBuddy] Starting...");

            // FEAT-40: Reset stats
            _nodesGathered = 0;
            _herbsGathered = 0;
            _mineralsGathered = 0;
            _sessionTimer.Restart();

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

            // FEAT-40: Save blacklist + print stats
            NodeTracker.SaveBlacklist();
            _sessionTimer.Stop();
            Logging.Write($"[GatherBuddy] Session stats: {_nodesGathered} nodes ({_herbsGathered} herbs, {_mineralsGathered} minerals) in {SessionTime:hh\\:mm\\:ss}");

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

                // 4. FEAT-40: Vendor/Repair when bags full or durability low
                new Decorator(
                    ctx => NeedsVendorOrRepair(),
                    CreateVendorBehavior()
                ),

                // 5. Node gathering
                CreateGatherBehavior(),

                // 6. Movement to next waypoint
                CreateMovementBehavior(),

                // 7. Idle
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
                        // Too far - move closer (FEAT-40: use Flightor if flying)
                        new Decorator(
                            ctx => _currentNode != null && _currentNode.DistanceSqr > 5 * 5,
                            new Sequence(
                                // Dismount if getting close (ground or descend if flying)
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
                                    if (GatherBuddySettings.Instance.UseFlying && Flightor.MountHelper.CanMount)
                                    {
                                        // Use Flightor for flying navigation to node
                                        Flightor.MoveTo(_currentNode!.Location);
                                    }
                                    else
                                    {
                                        Navigator.MoveTo(_currentNode!.Location);
                                    }
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
                                        {
                                            // FEAT-40: Track stats
                                            _nodesGathered++;
                                            if (_currentNode.IsHerb) _herbsGathered++;
                                            if (_currentNode.IsMineral) _mineralsGathered++;
                                            NodeTracker.MarkHarvested(_currentNode);
                                        }
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

                // Move to current waypoint (FEAT-40: use Flightor if flying)
                new Action(ctx =>
                {
                    var targetWaypoint = _waypointQueue!.Peek();

                    if (GatherBuddySettings.Instance.UseFlying && Flightor.MountHelper.CanMount)
                    {
                        // Flying navigation via Flightor
                        float alt = GatherBuddySettings.Instance.FlyingAltitude;
                        var flyDest = new WoWPoint(targetWaypoint.X, targetWaypoint.Y, targetWaypoint.Z + alt);
                        Flightor.MoveTo(flyDest);
                        return RunStatus.Running;
                    }

                    // Ground mount if possible and far away
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

        // ═══════════════════════════════════════════════════════════
        // VENDOR/REPAIR — FEAT-40
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Checks whether the bot should go to a vendor/repair NPC.
        /// </summary>
        private bool NeedsVendorOrRepair()
        {
            if (StyxWoW.Me == null || StyxWoW.Me.Combat)
                return false;

            var settings = GatherBuddySettings.Instance;

            // Check free bag slots
            if (settings.VendorWhenFull)
            {
                int freeSlots = GetFreeBagSlots();
                if (freeSlots <= settings.MinFreeBagSlots)
                    return true;
            }

            // Check durability
            if (settings.RepairAtVendor)
            {
                float durability = GetDurabilityPercent();
                if (durability > 0f && durability < settings.RepairDurabilityPercent)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Creates the vendor/repair composite behavior.
        /// If no vendor is known, logs a warning and skips.
        /// </summary>
        private Composite CreateVendorBehavior()
        {
            return new PrioritySelector(
                new Action(ctx =>
                {
                    // Use nearest vendor NPC if available
                    var vendor = ObjectManager.GetObjectsOfType<WoWUnit>()
                        .Where(u => u.IsVendor && u.IsAlive && !u.IsHostile)
                        .OrderBy(u => u.DistanceSqr)
                        .FirstOrDefault();

                    if (vendor == null)
                    {
                        Logging.WriteDebug("[GatherBuddy] Bags full/durability low but no vendor found nearby");
                        return RunStatus.Failure;
                    }

                    if (vendor.DistanceSqr > 5 * 5)
                    {
                        TreeRoot.StatusText = $"Moving to vendor {vendor.Name}";
                        Navigator.MoveTo(vendor.Location);
                        return RunStatus.Running;
                    }

                    // Interact with vendor
                    WoWMovement.MoveStop();
                    vendor.Interact();
                    TreeRoot.StatusText = $"Vendoring at {vendor.Name}";

                    // Sell greys/whites after a small delay
                    Lua.DoString(
                        "for bag=0,4 do " +
                        "  for slot=1,GetContainerNumSlots(bag) do " +
                        "    local _,_,_,_,_,_,_,_,_,_,price = GetContainerItemInfo(bag,slot); " +
                        "    local link = GetContainerItemLink(bag,slot); " +
                        "    if link then " +
                        "      local _,_,quality = GetItemInfo(link); " +
                        "      if quality and quality <= 1 then UseContainerItem(bag,slot); end " +
                        "    end " +
                        "  end " +
                        "end");

                    // Repair if vendor can repair
                    if (GatherBuddySettings.Instance.RepairAtVendor && vendor.IsRepairMerchant)
                    {
                        Lua.DoString("RepairAllItems()");
                        Logging.Write("[GatherBuddy] Repaired equipment");
                    }

                    Logging.Write("[GatherBuddy] Vendored junk items");
                    return RunStatus.Success;
                })
            );
        }

        /// <summary>
        /// Returns the number of free bag slots via Lua.
        /// </summary>
        private static int GetFreeBagSlots()
        {
            var results = Lua.GetReturnValues("local free=0; for i=0,4 do free=free+GetContainerNumFreeSlots(i) end; return free");
            if (results != null && results.Count > 0 && int.TryParse(results[0], out int free))
                return free;
            return 999; // Assume plenty if we can't read
        }

        /// <summary>
        /// Returns average durability percentage (0-100) across equipped items.
        /// </summary>
        private static float GetDurabilityPercent()
        {
            var results = Lua.GetReturnValues(
                "local total,current=0,0; " +
                "for slot=1,18 do " +
                "  local cur,mx=GetInventoryItemDurability(slot); " +
                "  if cur and mx and mx>0 then total=total+mx; current=current+cur end " +
                "end; " +
                "if total==0 then return 100 end; " +
                "return math.floor(current/total*100)");
            if (results != null && results.Count > 0 && float.TryParse(results[0], out float pct))
                return pct;
            return 100f;
        }
    }
}
