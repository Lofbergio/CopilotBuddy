using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CommonBehaviors.Actions;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.CommonBot;
using Styx.Database;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Inventory;
using Styx.Logic.Inventory.Frames;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.MailBox;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Patchables;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Bots.Gatherbuddy
{
    /// <summary>
    /// GatherBuddy - Full-featured gathering bot for WoW 3.3.5a (WotLK).
    /// Harvests herbs, minerals, chests, skins mobs along a waypoint route.
    /// Supports profile vendors, mailboxes, blackspots, sell/mail quality filters,
    /// full combat behaviors, death handling with spirit healer, and session timers.
    /// </summary>
    public class GatherbuddyBot : BotBase
    {
        // HB 4.3.4 API: HashSet<WoWPoint> — plugins (DruidHarvestHelper etc.) expect this exact type.
        public static readonly HashSet<WoWPoint> BlacklistNodes = new HashSet<WoWPoint>();

        // Internal timed expiry — parallel to BlacklistNodes, not exposed publicly.
        private static readonly Dictionary<WoWPoint, DateTime> _blacklistExpiry = new Dictionary<WoWPoint, DateTime>();

        private static bool IsNodeBlacklisted(WoWPoint pos)
        {
            if (!BlacklistNodes.Contains(pos)) return false;
            if (_blacklistExpiry.TryGetValue(pos, out DateTime expiry) && DateTime.Now >= expiry)
            {
                BlacklistNodes.Remove(pos);
                _blacklistExpiry.Remove(pos);
                return false;
            }
            return true;
        }

        // ═══════════════════════════════════════════════════════════
        // BOTBASE IMPLEMENTATION
        // ═══════════════════════════════════════════════════════════

        public override string Name => "GatherBuddy";
        public override bool IsPrimaryType => true;
        public override bool RequiresProfile => true;
        public override bool RequirementsMet => true;
        public override PulseFlags PulseFlags => PulseFlags.All;

        /// <summary>
        /// Returns the settings window shown when "Bot Config" is clicked.
        /// </summary>
        public override object ConfigurationForm => new GatherBuddySettingsWindow();

        private PrioritySelector? _root;
        private CircularQueue<WoWPoint>? _waypointQueue;
        private List<WoWPoint> _waypoints = new();
        private readonly Stopwatch _cleanupTimer = new();
        private static CombatRoutine? Routine => RoutineManager.Current;

        // HB 4.3.4 gather state (static — survives tree re-evaluation)
        private static WoWObject? _lockedNode;                              // woWObject_0: currently targeted node
        private static WoWPoint _lockedNodeLocation = WoWPoint.Zero;        // cached Location of _lockedNode (survives LootTargeting drop)
        private static WoWPoint _approachPoint = WoWPoint.Zero;            // woWPoint_0: raycast approach position
        private static int _gatherAttemptCount;                             // int_2: interact attempt counter
        private static readonly Stopwatch _gatherBlacklistTimer = new();   // stopwatch_0: timeout blacklist
        private static bool _combatSuppressed;                              // bool_1: HB 4.3.4 combat skip flag

        // Stats tracking
        private int _nodesGathered;
        private int _herbsGathered;
        private int _mineralsGathered;
        private readonly Stopwatch _sessionTimer = new();

        // Death tracking (corpse camp protection)
        private int _deathCount;
        private readonly Stopwatch _deathTimer = new();
        private bool _shouldUseSpiritHealer;

        // Loot tracking
        private int _lootAttemptCount;
        private int _lootFailCount;
        private ulong _lastLootGuid;

        // Session timer (BottingHours)
        private readonly Stopwatch _bottingTimer = new();

        // Random for hotspot shuffling and jiggle
        private static readonly Random _random = new();

        public override Composite Root => _root ??= CreateRootBehavior();

        /// <summary>
        /// Session statistics.
        /// </summary>
        public int NodesGathered => _nodesGathered;
        public int HerbsGathered => _herbsGathered;
        public int MineralsGathered => _mineralsGathered;
        public TimeSpan SessionTime => _sessionTimer.Elapsed;

        // ═══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════

        public override void Initialize()
        {
            NodeTracker.LoadBlacklist();
            Logging.Write("[GatherBuddy] Initialized (blacklist loaded)");
        }

        public override void Start()
        {
            Logging.Write("[GatherBuddy] Starting...");
            var settings = GatherBuddySettings.Instance;

            // Reset stats
            _nodesGathered = 0;
            _herbsGathered = 0;
            _mineralsGathered = 0;
            _deathCount = 0;
            _shouldUseSpiritHealer = false;
            _lootAttemptCount = 0;
            _lootFailCount = 0;
            _lastLootGuid = 0;
            _sessionTimer.Restart();
            _bottingTimer.Restart();

            // Load waypoints from ProfileManager
            if (ProfileManager.CurrentProfile == null)
                throw new Exception("[GatherBuddy] No profile loaded. Use 'Load Profile' button first.");

            _waypoints.Clear();
            float heightMod = settings.HeightModifier;

            if (ProfileManager.CurrentProfile.GrindArea?.Hotspots != null &&
                ProfileManager.CurrentProfile.GrindArea.Hotspots.Count > 0)
            {
                Logging.Write("[GatherBuddy] Loading waypoints from GrindArea");
                foreach (var hotspot in ProfileManager.CurrentProfile.GrindArea.Hotspots)
                    _waypoints.Add(hotspot.ToWoWPoint().Add(0f, 0f, heightMod));
            }
            else if (ProfileManager.CurrentProfile.HotspotManager?.Hotspots != null &&
                     ProfileManager.CurrentProfile.HotspotManager.Hotspots.Count > 0)
            {
                Logging.Write("[GatherBuddy] Loading waypoints from HotspotManager");
                foreach (var point in ProfileManager.CurrentProfile.HotspotManager.Hotspots)
                    _waypoints.Add(point.Add(0f, 0f, heightMod));
            }
            else
            {
                throw new Exception("[GatherBuddy] Profile has no hotspots.");
            }

            // Randomize hotspots if enabled
            if (settings.RandomizeHotspots)
            {
                ShuffleList(_waypoints);
                Logging.Write("[GatherBuddy] Hotspots randomized");
            }

            // Build circular queue
            _waypointQueue = new CircularQueue<WoWPoint>();
            foreach (var wp in _waypoints)
                _waypointQueue.Enqueue(wp);

            // Start at nearest waypoint
            if (StyxWoW.Me != null)
            {
                var nearest = _waypoints.OrderBy(w => w.DistanceSqr(StyxWoW.Me.Location)).First();
                _waypointQueue.CycleTo(nearest);
            }

            // Bounce mode
            if (settings.PathingType == PathType.Bounce)
                _waypointQueue.OnEndOfQueue += OnQueueCycle;

            Logging.Write($"[GatherBuddy] Loaded {_waypoints.Count} waypoints");

            // Load blackspots from profile
            if (ProfileManager.CurrentProfile.Blackspots != null &&
                ProfileManager.CurrentProfile.Blackspots.Count > 0)
            {
                BlackspotManager.AddBlackspots(ProfileManager.CurrentProfile.Blackspots);
                Logging.Write($"[GatherBuddy] Loaded {ProfileManager.CurrentProfile.Blackspots.Count} blackspots from profile");
            }
            BlackspotManager.EnsureBlackspotsMarked();

            // Targeting filters
            Targeting.Instance.IncludeTargetsFilter += IncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter += IncludeLootFilter;

            // Sync GatherBuddy's FindVendorsAutomatically to CharacterSettings
            // so VendorManager.GetClosestVendor's Data.bin fallback works
            if (GatherBuddySettings.Instance.FindVendorsAutomatically)
                CharacterSettings.Instance.FindVendorsAutomatically = true;

            // GameStats
            GameStats.Reset();
            GameStats.StartMeasuring();

            _cleanupTimer.Start();
            Logging.Write("[GatherBuddy] Started successfully!");

            // One-time diagnostic: test the full herb/mineral detection chain
            DiagnoseNodeDetection();
        }

        public override void Stop()
        {
            Logging.Write("[GatherBuddy] Stopping...");

            Targeting.Instance.IncludeTargetsFilter -= IncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter -= IncludeLootFilter;

            if (_waypointQueue != null)
                _waypointQueue.OnEndOfQueue -= OnQueueCycle;

            // Clear profile blackspots
            if (ProfileManager.CurrentProfile?.Blackspots != null &&
                ProfileManager.CurrentProfile.Blackspots.Count > 0)
            {
                BlackspotManager.RemoveBlackspots(ProfileManager.CurrentProfile.Blackspots);
            }

            NodeTracker.SaveBlacklist();
            GameStats.StopMeasuring();
            _sessionTimer.Stop();
            _bottingTimer.Stop();
            _lockedNode = null;
            _lockedNodeLocation = WoWPoint.Zero;
            _approachPoint = WoWPoint.Zero;
            _gatherAttemptCount = 0;
            _gatherBlacklistTimer.Reset();
            Logging.Write($"[GatherBuddy] Session: {_nodesGathered} nodes ({_herbsGathered} herbs, {_mineralsGathered} minerals) in {SessionTime:hh\\:mm\\:ss}");

            NodeTracker.Reset();
            _cleanupTimer.Stop();
        }

        public override void Pulse()
        {
            // PathPrecision scaling by speed (from LevelBot)
            float speed = StyxWoW.Me?.MovementInfo?.CurrentSpeed ?? 7f;
            Navigator.PathPrecision = Math.Clamp(speed * 0.15f, 1.5f, 10f);

            // Ensure blackspots are marked (tiles may load dynamically)
            BlackspotManager.EnsureBlackspotsMarked();

            // Periodic cleanup of expired node tracking
            if (_cleanupTimer.ElapsedMilliseconds > 30000)
            {
                NodeTracker.CleanupExpired();
                _cleanupTimer.Restart();
            }
        }

        private void OnQueueCycle(object? sender, EventArgs e)
        {
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
                // 0. Session timer check
                CreateSessionTimerBehavior(),

                // 1. Death management (full: release, spirit healer, corpse walk, rez sickness)
                CreateDeathBehavior(),

                // 2. Combat (full: Rest, PreCombatBuff, Heal, CombatBuff, Pull, Combat)
                CreateCombatBehavior(),

                // 3. Loot killed mobs (with LootFrame, retry, skinning)
                new Decorator(
                    ctx => GatherBuddySettings.Instance.LootMobs,
                    CreateLootBehavior()
                ),

                // 4. Vendor/Repair/Mail
                new Decorator(
                    ctx => NeedsVendorOrRepairOrMail(),
                    CreateVendorMailBehavior()
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
        // SESSION TIMER (BottingHours)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateSessionTimerBehavior()
        {
            return new Decorator(
                ctx =>
                {
                    float hours = GatherBuddySettings.Instance.BottingHours;
                    return hours > 0f && _bottingTimer.Elapsed.TotalHours >= hours;
                },
                new Action(ctx =>
                {
                    Logging.Write("[GatherBuddy] BottingHours limit reached, stopping!");

                    if (GatherBuddySettings.Instance.HearthAndExit)
                    {
                        Logging.Write("[GatherBuddy] Using Hearthstone...");
                        Lua.DoString("UseItemByName(GetItemInfo(6948))");
                        StyxWoW.Sleep(12000); // Wait for hearth cast
                    }

                    TreeRoot.Stop("GatherBuddy: BottingHours limit reached");
                    return RunStatus.Success;
                })
            );
        }

        // ═══════════════════════════════════════════════════════════
        // DEATH BEHAVIOR (Full — spirit healer, corpse camp, jiggle)
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
                            GameStats.Died();
                            TrackDeath();
                            Lua.DoString("RepopMe()");
                            SleepForLag();
                            return RunStatus.Success;
                        }),
                        new WaitContinue(5, ctx => StyxWoW.Me != null && StyxWoW.Me.IsGhost, new ActionAlwaysSucceed())
                    )
                ),

                // Spirit healer path (if setting enabled or 3+ deaths in 3 mins)
                new Decorator(
                    ctx => StyxWoW.Me != null && StyxWoW.Me.IsGhost &&
                           (_shouldUseSpiritHealer || GatherBuddySettings.Instance.UseSpiritHealer),
                    CreateSpiritHealerBehavior()
                ),

                // Can't navigate to corpse → use spirit healer
                new Decorator(
                    ctx => StyxWoW.Me != null && StyxWoW.Me.IsGhost &&
                           StyxWoW.Me.CorpsePoint != WoWPoint.Empty &&
                           StyxWoW.Me.Location.DistanceSqr(StyxWoW.Me.CorpsePoint) > 40 * 40 &&
                           !Navigator.CanNavigateFully(StyxWoW.Me.Location, StyxWoW.Me.CorpsePoint),
                    new Action(ctx =>
                    {
                        Logging.Write("[GatherBuddy] Can't navigate to corpse, using spirit healer");
                        _shouldUseSpiritHealer = true;
                        return RunStatus.Success;
                    })
                ),

                // Ghost - almost at corpse: jiggle + retrieve
                new Decorator(
                    ctx => StyxWoW.Me != null && StyxWoW.Me.IsGhost &&
                           StyxWoW.Me.CorpsePoint != WoWPoint.Empty &&
                           StyxWoW.Me.Location.DistanceSqr(StyxWoW.Me.CorpsePoint) < 40 * 40,
                    new Sequence(
                        // Jiggle 2-3y randomly before RetrieveCorpse (3.3.5a server bug fix)
                        new Action(ctx =>
                        {
                            float jx = (float)(_random.NextDouble() * 4 - 2);
                            float jy = (float)(_random.NextDouble() * 4 - 2);
                            var jigglePoint = StyxWoW.Me!.Location.Add(jx, jy, 0);
                            Navigator.MoveTo(jigglePoint);
                            StyxWoW.Sleep(500);
                            WoWMovement.MoveStop();
                            return RunStatus.Success;
                        }),
                        new Action(ctx =>
                        {
                            Lua.DoString("RetrieveCorpse()");
                            SleepForLag();
                            return RunStatus.Success;
                        })
                    )
                ),

                // Ghost - move to corpse
                new Decorator(
                    ctx => StyxWoW.Me != null && StyxWoW.Me.IsGhost &&
                           StyxWoW.Me.CorpsePoint != WoWPoint.Empty,
                    new Action(ctx =>
                    {
                        TreeRoot.StatusText = "Moving to corpse";
                        Navigator.MoveTo(StyxWoW.Me!.CorpsePoint);
                        return RunStatus.Running;
                    })
                ),

                // Rez sickness: wait it out if enabled
                new Decorator(
                    ctx => StyxWoW.Me != null && !StyxWoW.Me.IsDead && !StyxWoW.Me.IsGhost &&
                           GatherBuddySettings.Instance.WaitRezSickness &&
                           HasRezSickness(),
                    new Action(ctx =>
                    {
                        TreeRoot.StatusText = "Waiting out Resurrection Sickness";
                        Logging.WriteDiagnostic("[GatherBuddy] Waiting for Resurrection Sickness to expire...");
                        return RunStatus.Running;
                    })
                )
            );
        }

        private Composite CreateSpiritHealerBehavior()
        {
            return new PrioritySelector(
                // Find and move to spirit healer
                new Action(ctx =>
                {
                    var spiritHealer = ObjectManager.CachedUnits
                        .Where(u => u.IsSpiritHealer)
                        .OrderBy(u => u.DistanceSqr)
                        .FirstOrDefault();

                    if (spiritHealer == null)
                    {
                        Logging.WriteDebug("[GatherBuddy] No spirit healer nearby");
                        return RunStatus.Failure;
                    }

                    if (spiritHealer.DistanceSqr > 10 * 10)
                    {
                        TreeRoot.StatusText = "Moving to Spirit Healer";
                        Navigator.MoveTo(spiritHealer.Location);
                        return RunStatus.Running;
                    }

                    // Interact — accept rez
                    WoWMovement.MoveStop();
                    spiritHealer.Interact();
                    StyxWoW.Sleep(1000);
                    Lua.DoString("StaticPopup1Button1:Click()"); // Accept rez sickness
                    _shouldUseSpiritHealer = false;
                    _deathCount = 0;
                    Logging.Write("[GatherBuddy] Accepted Spirit Healer resurrection");
                    SleepForLag();
                    return RunStatus.Success;
                })
            );
        }

        /// <summary>
        /// Track deaths for corpse camp protection (3 deaths in 3 mins → spirit healer).
        /// </summary>
        private void TrackDeath()
        {
            if (_deathTimer.IsRunning && _deathTimer.Elapsed.TotalMinutes > 3)
            {
                _deathCount = 0;
                _deathTimer.Restart();
            }
            else if (!_deathTimer.IsRunning)
            {
                _deathTimer.Start();
            }

            _deathCount++;
            if (_deathCount >= 3)
            {
                Logging.Write("[GatherBuddy] 3 deaths in 3 minutes — switching to spirit healer");
                _shouldUseSpiritHealer = true;
            }
        }

        private static bool HasRezSickness()
        {
            var results = Lua.GetReturnValues("local name = UnitDebuff('player', 'Resurrection Sickness'); return name or ''");
            return results != null && results.Count > 0 && !string.IsNullOrEmpty(results[0]);
        }

        // ═══════════════════════════════════════════════════════════
        // COMBAT BEHAVIOR — HB 4.3.4 GatherBuddy exact logic
        // smethod_13/14: if mounted+combat+(no node or node>50y) → suppress combat, keep moving
        // smethod_19: gate → only allow combat subtree when NOT airborne
        // smethod_20: gate → only allow combat when !_combatSuppressed
        // smethod_23: reset _combatSuppressed each tick
        // LevelBot.CreateCombatBehavior: !Me.Mounted required for actual combat routine
        // ═══════════════════════════════════════════════════════════

        private Composite CreateCombatBehavior()
        {
            return new PrioritySelector(
                // [smethod_13/14] Suppress combat while mounted + in combat + no nearby node
                new Decorator(
                    ctx => StyxWoW.Me != null && StyxWoW.Me.Combat && StyxWoW.Me.Mounted,
                    new Action(ctx =>
                    {
                        var node = LootTargeting.Instance.FirstObject;
                        if (node == null || node.Distance > 50.0)
                            _combatSuppressed = true;
                        return RunStatus.Failure; // Continue to next children
                    })
                ),

                // [smethod_19+20] Combat subtree: NOT flying AND NOT suppressed
                new Decorator(
                    ctx => !_combatSuppressed && StyxWoW.Me != null && !StyxWoW.Me.IsFlying,
                    new PrioritySelector(
                        // Not in combat: Rest + PreCombatBuff
                        new Decorator(
                            ctx => !StyxWoW.Me!.Combat,
                            new PrioritySelector(
                                Routine?.RestBehavior ?? new ActionAlwaysFail(),
                                Routine?.PreCombatBuffBehavior ?? new ActionAlwaysFail()
                            )
                        ),

                        // In combat AND dismounted (HB 4.3.4: smethod_63 requires !Me.Mounted)
                        new Decorator(
                            ctx => !StyxWoW.Me!.Mounted &&
                                   (StyxWoW.Me.Combat || (StyxWoW.Me.GotAlivePet && StyxWoW.Me.Pet.Combat)),
                            new PrioritySelector(
                                // Release gather lock if we're fighting dismounted
                                new Decorator(
                                    ctx => _lockedNode != null,
                                    new Action(ctx =>
                                    {
                                        _lockedNode = null;
                                        _lockedNodeLocation = WoWPoint.Zero;
                                        _approachPoint = WoWPoint.Zero;
                                        _gatherAttemptCount = 0;
                                        _gatherBlacklistTimer.Reset();
                                        CycleToNearestWaypoint();
                                        return RunStatus.Failure; // Continue to combat actions
                                    })
                                ),
                                // If no target from Targeting system, find nearest hostile attacking us
                                new Decorator(
                                    ctx => Targeting.Instance.FirstUnit == null,
                                    new Action(ctx =>
                                    {
                                        var attacker = ObjectManager.GetObjectsOfType<WoWUnit>()
                                            .Where(u => u.IsValid && !u.IsDead && u.IsHostile &&
                                                        (u.IsTargetingMeOrPet || u.Aggro) &&
                                                        u.DistanceSqr < 40 * 40)
                                            .OrderBy(u => u.DistanceSqr)
                                            .FirstOrDefault();
                                        if (attacker != null)
                                            attacker.Target();
                                        return RunStatus.Failure; // Always Failure so PS reaches combat routine
                                    })
                                ),
                                Routine?.HealBehavior ?? new ActionAlwaysFail(),
                                Routine?.CombatBuffBehavior ?? new ActionAlwaysFail(),
                                Routine?.CombatBehavior ?? new ActionAlwaysFail(),
                                new ActionAlwaysSucceed()
                            )
                        )
                    )
                ),

                // [smethod_23] Reset combat suppression flag each tick
                new Action(ctx => { _combatSuppressed = false; return RunStatus.Failure; })
            );
        }

        // ═══════════════════════════════════════════════════════════
        // LOOT BEHAVIOR (Full — LootFrame, LOOT_OPENED, retry, skinning)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateLootBehavior()
        {
            return new Decorator(
                ctx => StyxWoW.Me != null && !StyxWoW.Me.Combat,
                new PrioritySelector(
                    // Loot a target mob
                    new Decorator(
                        ctx =>
                        {
                            var target = GetLootTarget();
                            return target != null;
                        },
                        new PrioritySelector(
                            // Move to lootable
                            new Decorator(
                                ctx =>
                                {
                                    var target = GetLootTarget();
                                    return target != null && target.DistanceSqr > 5 * 5;
                                },
                                new Action(ctx =>
                                {
                                    var target = GetLootTarget()!;
                                    TreeRoot.StatusText = $"Moving to loot {target.Name}";
                                    Navigator.MoveTo(target.Location);
                                    return RunStatus.Running;
                                })
                            ),
                            // Close enough — loot
                            new Action(ctx =>
                            {
                                var target = GetLootTarget();
                                if (target == null) return RunStatus.Failure;

                                // Track attempts for retry/blacklist
                                if (target.Guid != _lastLootGuid)
                                {
                                    _lastLootGuid = target.Guid;
                                    _lootAttemptCount = 0;
                                    _lootFailCount = 0;
                                }

                                _lootAttemptCount++;

                                // Too many failures → blacklist
                                if (_lootAttemptCount >= 5 || _lootFailCount >= 2)
                                {
                                    Logging.Write($"[GatherBuddy] Blacklisting loot target {target.Name} (too many attempts)");
                                    Blacklist.Add(target.Guid, TimeSpan.FromMinutes(5));
                                    _lastLootGuid = 0;
                                    return RunStatus.Success;
                                }

                                WoWMovement.MoveStop();
                                target.Interact();
                                SleepForLag();

                                // Wait for LOOT_OPENED
                                StyxWoW.Sleep(500);

                                // Auto-loot all via Lua
                                Lua.DoString(
                                    "for i=GetNumLootItems(),1,-1 do " +
                                    "  LootSlot(i); ConfirmBindOnUse(); " +
                                    "end; " +
                                    "CloseLoot()");

                                GameStats.LootedMob();
                                return RunStatus.Success;
                            })
                        )
                    ),

                    // Skinning (if enabled)
                    new Decorator(
                        ctx => GatherBuddySettings.Instance.SkinMobs,
                        new Decorator(
                            ctx =>
                            {
                                var skinTarget = GetSkinTarget();
                                return skinTarget != null;
                            },
                            new PrioritySelector(
                                new Decorator(
                                    ctx => GetSkinTarget()!.DistanceSqr > 5 * 5,
                                    new Action(ctx =>
                                    {
                                        Navigator.MoveTo(GetSkinTarget()!.Location);
                                        return RunStatus.Running;
                                    })
                                ),
                                new Action(ctx =>
                                {
                                    var skinTarget = GetSkinTarget()!;
                                    WoWMovement.MoveStop();
                                    skinTarget.Interact();
                                    Logging.Write($"[GatherBuddy] Skinning {skinTarget.Name}");
                                    SleepForLag();
                                    StyxWoW.Sleep(2000); // Wait for skinning cast
                                    return RunStatus.Success;
                                })
                            )
                        )
                    )
                )
            );
        }

        private WoWUnit? GetLootTarget()
        {
            float radius = GatherBuddySettings.Instance.LootRadius;
            float radiusSqr = radius * radius;
            return ObjectManager.GetObjectsOfType<WoWUnit>()
                .Where(u => u.IsDead && u.CanLoot && u.DistanceSqr < radiusSqr &&
                            !Blacklist.Contains(u.Guid))
                .OrderBy(u => u.DistanceSqr)
                .FirstOrDefault();
        }

        private WoWUnit? GetSkinTarget()
        {
            float radius = GatherBuddySettings.Instance.LootRadius;
            float radiusSqr = radius * radius;
            return ObjectManager.GetObjectsOfType<WoWUnit>()
                .Where(u => u.IsDead && u.CanSkin && !u.CanLoot && u.DistanceSqr < radiusSqr &&
                            u.KilledByMe && !Blacklist.Contains(u.Guid))
                .OrderBy(u => u.DistanceSqr)
                .FirstOrDefault();
        }

        // ═══════════════════════════════════════════════════════════
        // GATHER BEHAVIOR — HB 4.3.4 architecture
        // Source: .hb 4.3.4/.../GatherbuddyBot.cs method_0 (array25)
        // Uses LootTargeting.Instance.FirstObject as the node source.
        // Static state fields (_lockedNode, _approachPoint, _gatherAttemptCount,
        // _gatherBlacklistTimer) survive tree re-evaluation.
        // ═══════════════════════════════════════════════════════════

        private Composite CreateGatherBehavior()
        {
            return new Decorator(
                // Gate: enter if LootTargeting has a target OR we have a locked node we're still approaching
                ctx => LootTargeting.Instance.FirstObject != null ||
                       (_lockedNode != null && _lockedNodeLocation != WoWPoint.Zero &&
                        _gatherBlacklistTimer.IsRunning),

                new PrioritySelector(
                    // -------------------------------------------------------
                    // [0] Skip current hotspot if node is closer (smethod_39/40)
                    // -------------------------------------------------------
                    new Decorator(
                        ctx =>
                        {
                            if (_waypointQueue == null || _waypointQueue.Count < 3) return false;
                            var nextWp = _waypointQueue.Peek();
                            var node = LootTargeting.Instance.FirstObject;
                            if (node == null) return false;
                            // HB 4.3.4: skip hotspot if the second waypoint is closer to
                            // the node than to the first waypoint
                            return nextWp.Distance2D(StyxWoW.Me.Location) >
                                   nextWp.Distance2D(node.Location);
                        },
                        new Action(ctx =>
                        {
                            _waypointQueue!.Dequeue();
                            return RunStatus.Success;
                        })
                    ),

                    // -------------------------------------------------------
                    // [1] Reset state when LootTargeting target changes (smethod_41–46)
                    // -------------------------------------------------------
                    new Decorator(
                        ctx =>
                        {
                            var firstObj = LootTargeting.Instance.FirstObject;
                            if (firstObj == null) return false; // Keep existing lock if FirstObject dropped
                            return _lockedNode == null || _lockedNode != firstObj;
                        },
                        new Sequence(
                            new Action(ctx => { _gatherAttemptCount = 0; return RunStatus.Success; }),
                            new Action(ctx => { _gatherBlacklistTimer.Reset(); return RunStatus.Success; }),
                            new Action(ctx => { _gatherBlacklistTimer.Start(); return RunStatus.Success; }),
                            new Action(ctx => { _approachPoint = WoWPoint.Zero; return RunStatus.Success; }),
                            new Action(ctx =>
                            {
                                _lockedNode = LootTargeting.Instance.FirstObject;
                                _lockedNodeLocation = _lockedNode?.Location ?? WoWPoint.Zero;
                                Logging.Write($"[GatherBuddy] Targeting: {_lockedNode?.Name} ({_lockedNode?.Distance:F0}y)");
                                return RunStatus.Success;
                            })
                        )
                    ),

                    // -------------------------------------------------------
                    // [2] Blacklist after 3 failed interact attempts (smethod_47/48)
                    // -------------------------------------------------------
                    new Decorator(
                        ctx => _lockedNode != null &&
                               _gatherAttemptCount >= 3,
                        new Action(ctx =>
                        {
                            Logging.Write("[GatherBuddy] Blacklisting node after 3 failed attempts");
                            BlacklistNodes.Add(_lockedNodeLocation);
                            _blacklistExpiry[_lockedNodeLocation] = DateTime.Now.AddMinutes(5);
                            if (_lockedNode != null)
                                Blacklist.Add(_lockedNode.Guid, TimeSpan.FromMinutes(5));
                            _lockedNode = null;
                            _lockedNodeLocation = WoWPoint.Zero;
                            _gatherAttemptCount = 0;
                            _approachPoint = WoWPoint.Zero;
                            _gatherBlacklistTimer.Reset();
                            return RunStatus.Success;
                        })
                    ),

                    // -------------------------------------------------------
                    // [3] Blacklist after timeout (smethod_49/50)
                    // -------------------------------------------------------
                    new Decorator(
                        ctx => _gatherBlacklistTimer.IsRunning &&
                               _gatherBlacklistTimer.Elapsed.TotalSeconds > GatherBuddySettings.Instance.BlacklistTimer,
                        new Action(ctx =>
                        {
                            Logging.Write($"[GatherBuddy] Blacklisting node after {GatherBuddySettings.Instance.BlacklistTimer}s timeout");
                            if (_lockedNodeLocation != WoWPoint.Zero)
                            {
                                BlacklistNodes.Add(_lockedNodeLocation);
                                _blacklistExpiry[_lockedNodeLocation] = DateTime.Now.AddMinutes(5);
                            }
                            if (_lockedNode != null)
                                Blacklist.Add(_lockedNode.Guid, TimeSpan.FromMinutes(5));
                            _lockedNode = null;
                            _lockedNodeLocation = WoWPoint.Zero;
                            _approachPoint = WoWPoint.Zero;
                            _gatherBlacklistTimer.Reset();
                            return RunStatus.Success;
                        })
                    ),

                    // -------------------------------------------------------
                    // [4] Approach while far away (smethod_52–56)
                    // Fires when dist² >= 6.25 (2.5y) AND (flying OR !WithinInteractRange)
                    // Uses _lockedNodeLocation so we don't lose the node during long nav paths
                    // -------------------------------------------------------
                    new Decorator(
                        ctx =>
                        {
                            var node = LootTargeting.Instance.FirstObject ?? _lockedNode;
                            if (node == null) return _lockedNodeLocation != WoWPoint.Zero;
                            if (node.DistanceSqr < 6.25) return false;
                            return StyxWoW.Me.IsFlying || !node.WithinInteractRange;
                        },
                        new Sequence(
                            new Action(ctx =>
                            {
                                var node = LootTargeting.Instance.FirstObject ?? _lockedNode;
                                string name = node?.Name ?? "node";
                                double dist;
                                if (node != null)
                                    dist = node.Distance;
                                else
                                    dist = StyxWoW.Me.Location.Distance(_lockedNodeLocation);
                                TreeRoot.StatusText = $"Approaching {name} ({dist:F0}y)";
                                return RunStatus.Success;
                            }),
                            // Stop descend key if held (smethod_54/55)
                            new DecoratorContinue(
                                ctx => StyxWoW.Me.MovementInfo.IsDescending,
                                new Action(ctx =>
                                {
                                    WoWMovement.MoveStop(WoWMovement.MovementDirection.Descend);
                                    return RunStatus.Success;
                                })
                            ),
                            // Move to node (smethod_56: Flightor.MoveTo or Navigator.MoveTo)
                            new Action(ctx =>
                            {
                                var target = LootTargeting.Instance.FirstObject?.Location ?? _lockedNodeLocation;
                                if (StyxWoW.Me.IsFlying)
                                    Flightor.MoveTo(target);
                                else
                                    Navigator.MoveTo(target);
                                return RunStatus.Success;
                            })
                        )
                    ),

                    // -------------------------------------------------------
                    // [5] Dismount if flying (smethod_57–64)
                    // Descend → Wait(!Flying) → Dismount → MoveStop
                    // -------------------------------------------------------
                    new Decorator(
                        ctx => StyxWoW.Me.IsFlying,
                        new Sequence(
                            new Action(ctx =>
                            {
                                WoWMovement.Move(WoWMovement.MovementDirection.Descend);
                                return RunStatus.Success;
                            }),
                            new WaitContinue(1, ctx => !StyxWoW.Me.MovementInfo.IsFlying,
                                new Action(ctx =>
                                {
                                    WoWMovement.Move(WoWMovement.MovementDirection.Forward);
                                    return RunStatus.Success;
                                })
                            ),
                            new WaitContinue(1, ctx => !StyxWoW.Me.MovementInfo.IsFlying,
                                new ActionAlwaysSucceed()
                            ),
                            new DecoratorContinue(
                                ctx =>
                                {
                                    if (StyxWoW.Me.MovementInfo.IsFlying) return true;
                                    var node = LootTargeting.Instance.FirstObject;
                                    return node != null && node.DistanceSqr > 25.0;
                                },
                                new Action(ctx =>
                                {
                                    Flightor.MountHelper.Dismount();
                                    return RunStatus.Success;
                                })
                            ),
                            new Action(ctx =>
                            {
                                WoWMovement.MoveStop();
                                return RunStatus.Success;
                            })
                        )
                    ),

                    // -------------------------------------------------------
                    // [6] Ground approach if !WithinInteractRange (smethod_65–69)
                    // Navigator.MoveTo with ClickToMove fallback
                    // Uses _lockedNodeLocation when FirstObject dropped from LootTargeting
                    // -------------------------------------------------------
                    new Decorator(
                        ctx =>
                        {
                            var node = LootTargeting.Instance.FirstObject;
                            if (node != null) return !node.WithinInteractRange;
                            // FirstObject is null but we have a locked location → keep approaching
                            return _lockedNodeLocation != WoWPoint.Zero;
                        },
                        new PrioritySelector(
                            new Decorator(
                                ctx =>
                                {
                                    var target = LootTargeting.Instance.FirstObject?.Location ?? _lockedNodeLocation;
                                    return Navigator.CanNavigateFully(StyxWoW.Me.Location, target);
                                },
                                new Action(ctx =>
                                {
                                    var target = LootTargeting.Instance.FirstObject?.Location ?? _lockedNodeLocation;
                                    Navigator.MoveTo(target);
                                    return RunStatus.Success;
                                })
                            ),
                            new Action(ctx =>
                            {
                                var target = LootTargeting.Instance.FirstObject?.Location ?? _lockedNodeLocation;
                                WoWMovement.ClickToMove(target);
                                return RunStatus.Success;
                            })
                        )
                    ),

                    // -------------------------------------------------------
                    // [7] Interact sequence — within range (smethod_70–78)
                    // Wait(!Falling) → MoveStop → Dismount → Face → Interact →
                    // Wait(LootFrame/Combat/Gone) → LootAll
                    // -------------------------------------------------------
                    CreateInteractSequence()
                )
            );
        }

        /// <summary>
        /// HB 4.3.4 interact sequence (array55 / smethod_70–78).
        /// Context: LootTargeting.Instance.FirstObject.
        /// </summary>
        private Composite CreateInteractSequence()
        {
            return new Sequence(
                // Wait until not falling, then stop movement (smethod_71/72)
                new WaitContinue(5, ctx => !StyxWoW.Me.IsFalling,
                    new Action(ctx =>
                    {
                        WoWMovement.MoveStop();
                        return RunStatus.Success;
                    })
                ),

                // Dismount ground mount (safety — WoW auto-dismounts on Interact
                // but we do it explicitly for reliability on 3.3.5a private servers)
                new DecoratorContinue(
                    ctx => StyxWoW.Me.Mounted,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Mount.Dismount("Gathering");
                            return RunStatus.Success;
                        }),
                        new WaitContinue(2, ctx => !StyxWoW.Me.Mounted, new ActionAlwaysSucceed())
                    )
                ),

                // Increment attempt counter (smethod_73)
                new Action(ctx =>
                {
                    _gatherAttemptCount++;
                    return RunStatus.Success;
                }),

                // Face node if setting enabled (smethod_74/75)
                new DecoratorContinue(
                    ctx => LootTargeting.Instance.FirstObject is WoWGameObject &&
                           GatherBuddySettings.Instance.FaceNodes,
                    new Action(ctx =>
                    {
                        StyxWoW.Me.SetFacing((WoWGameObject)LootTargeting.Instance.FirstObject);
                        return RunStatus.Success;
                    })
                ),

                // Interact with node (smethod_76)
                new Action(ctx =>
                {
                    var node = LootTargeting.Instance.FirstObject;
                    node.Interact();
                    Logging.Write($"[GatherBuddy] Gathering {node.Name}");
                    return RunStatus.Success;
                }),

                // Wait up to 1s for the gathering cast to START (avoids false-complete when interact is slow)
                new WaitContinue(1,
                    ctx => StyxWoW.Me.IsCasting || StyxWoW.Me.IsChanneling || StyxWoW.Me.Combat,
                    new ActionAlwaysSucceed()),

                // Wait up to 8s for cast to COMPLETE, combat interrupt, or node gone
                new WaitContinue(8,
                    ctx =>
                    {
                        if (StyxWoW.Me.Combat) return true;
                        var node = LootTargeting.Instance.FirstObject;
                        if (node == null || node != _lockedNode) return true;
                        // Cast finished = gathering done
                        return !StyxWoW.Me.IsCasting && !StyxWoW.Me.IsChanneling;
                    },
                    new Action(ctx =>
                    {
                        OnGatherComplete();
                        return RunStatus.Success;
                    })
                )
            );
        }

        /// <summary>
        /// Called when the gathering cast completes or is interrupted.
        /// Loots via Lua, updates stats, blacklists the node briefly so LootTargeting moves on.
        /// </summary>
        private void OnGatherComplete()
        {
            _gatherBlacklistTimer.Reset();
            _gatherAttemptCount = 0;

            if (StyxWoW.Me.Combat)
            {
                _lockedNode = null;
                _lockedNodeLocation = WoWPoint.Zero;
                return;
            }

            // Loot via Lua — works regardless of whether the loot frame address is valid
            string nodeName = _lockedNode?.Name ?? "node";
            bool wasHerb = false;
            bool wasMineral = false;
            if (_lockedNode is WoWGameObject gatheredGo)
            {
                wasHerb = gatheredGo.IsHerb;
                wasMineral = gatheredGo.IsMineral;
                NodeTracker.MarkHarvested(gatheredGo);
            }

            Lua.DoString(
                "for i=GetNumLootItems(),1,-1 do " +
                "  LootSlot(i); ConfirmBindOnUse(); " +
                "end; " +
                "CloseLoot()");

            StyxWoW.SleepForLagDuration();

            _nodesGathered++;
            if (wasHerb) _herbsGathered++;
            if (wasMineral) _mineralsGathered++;
            Logging.Write($"[GatherBuddy] Harvested {nodeName} (total: {_nodesGathered})");

            // Brief blacklist so LootTargeting drops this node immediately
            if (_lockedNode != null)
                Blacklist.Add(_lockedNode.Guid, TimeSpan.FromSeconds(3));

            _lockedNode = null;
            _lockedNodeLocation = WoWPoint.Zero;
            _approachPoint = WoWPoint.Zero;

            CycleToNearestWaypoint();
        }

        /// <summary>
        /// Cycles the waypoint queue to the nearest waypoint from current position.
        /// Called after gather complete and after combat to avoid going back to a distant waypoint.
        /// HB 4.3.4: Area.CycleToNearest() — called at startup; we also call after interruptions.
        /// </summary>
        private void CycleToNearestWaypoint()
        {
            if (_waypointQueue == null || _waypointQueue.Count < 2 || StyxWoW.Me == null)
                return;

            var myLoc = StyxWoW.Me.Location;
            WoWPoint nearest = WoWPoint.Zero;
            float bestDist = float.MaxValue;

            // Peek through all waypoints in the circular queue
            var snapshot = _waypointQueue.ToArray();
            foreach (var wp in snapshot)
            {
                float dist = wp.Distance(myLoc);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = wp;
                }
            }

            if (nearest != WoWPoint.Zero)
                _waypointQueue.CycleTo(nearest);
        }

        // ═══════════════════════════════════════════════════════════
        // MOVEMENT BEHAVIOR (with mount logic)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateMovementBehavior()
        {
            return new PrioritySelector(
                new Decorator(
                    ctx => _waypointQueue == null || _waypointQueue.Count == 0,
                    new Action(ctx =>
                    {
                        Logging.Write("[GatherBuddy] No waypoints! Load a profile with hotspots.");
                        return RunStatus.Failure;
                    })
                ),

                // Arrived at waypoint - advance
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
                    TreeRoot.StatusText = $"Moving to waypoint ({_waypointQueue.Count} remaining)";

                    if (GatherBuddySettings.Instance.UseFlying && Flightor.MountHelper.CanMount)
                    {
                        float alt = GatherBuddySettings.Instance.FlyingAltitude;
                        var flyDest = new WoWPoint(targetWaypoint.X, targetWaypoint.Y, targetWaypoint.Z + alt);
                        Flightor.MoveTo(flyDest);
                        return RunStatus.Success;
                    }

                    // Ground mount if far
                    if (StyxWoW.Me != null && !StyxWoW.Me.Mounted &&
                        Mount.CanMount() &&
                        Mount.ShouldMount(targetWaypoint))
                    {
                        Mount.MountUp();
                        return RunStatus.Success;
                    }

                    Navigator.MoveTo(targetWaypoint);
                    return RunStatus.Success;
                })
            );
        }

        // ═══════════════════════════════════════════════════════════
        // VENDOR / MAIL BEHAVIOR (Full — Profile + Engine APIs)
        // ═══════════════════════════════════════════════════════════

        private bool NeedsVendorOrRepairOrMail()
        {
            if (StyxWoW.Me == null || StyxWoW.Me.Combat || StyxWoW.Me.IsDead || StyxWoW.Me.IsGhost)
                return false;

            return NeedsToSell() || NeedsToRepair() || NeedsToMail();
        }

        private bool NeedsToSell()
        {
            var settings = GatherBuddySettings.Instance;
            if (!settings.VendorWhenFull) return false;

            int freeSlots = GetFreeBagSlots();
            if (freeSlots > settings.MinFreeBagSlots) return false;

            // Profile vendor
            var vendor = GetBestVendor(Vendor.VendorType.Sell);
            if (vendor != null) return true;

            // Nearby vendor in ObjectManager
            if (HasNearbyVendorNpc()) return true;

            // Data.bin fallback — FindVendorsAutomatically
            if (settings.FindVendorsAutomatically)
                return FindVendorFromDatabase(UnitNPCFlags.Vendor) != null;

            return false;
        }

        private bool NeedsToRepair()
        {
            var settings = GatherBuddySettings.Instance;
            if (!settings.RepairAtVendor) return false;

            float durability = GetDurabilityPercent();
            if (durability <= 0f || durability >= settings.RepairDurabilityPercent) return false;

            var vendor = GetBestVendor(Vendor.VendorType.Repair);
            if (vendor != null) return true;

            if (HasNearbyRepairNpc()) return true;

            if (settings.FindVendorsAutomatically)
                return FindVendorFromDatabase(UnitNPCFlags.Repair) != null;

            return false;
        }

        private bool NeedsToMail()
        {
            var settings = GatherBuddySettings.Instance;
            if (!settings.MailToAlt || string.IsNullOrEmpty(settings.MailRecipient))
                return false;

            // Only mail if bags getting full
            int freeSlots = GetFreeBagSlots();
            if (freeSlots > settings.MinFreeBagSlots + 2) return false;

            // Profile mailbox
            var mailbox = ProfileManager.CurrentProfile?.MailboxManager?.GetClosestMailbox();
            if (mailbox != null) return true;

            // Nearby mailbox in ObjectManager
            if (FindNearbyMailbox() != null) return true;

            return false;
        }

        /// <summary>
        /// Full vendor/mail behavior. Priority: Sell/Repair → Mail → Return to route.
        /// Uses profile VendorManager/MailboxManager when available, falls back to ObjectManager scan.
        /// </summary>
        private Composite CreateVendorMailBehavior()
        {
            return new PrioritySelector(
                // === SELL / REPAIR ===
                new Decorator(
                    ctx => NeedsToSell() || NeedsToRepair(),
                    new PrioritySelector(
                        // Try profile vendor first
                        new Decorator(
                            ctx => GetBestVendor(NeedsToRepair() ? Vendor.VendorType.Repair : Vendor.VendorType.Sell) != null,
                            CreateProfileVendorBehavior()
                        ),
                        // Fallback: nearby NPC vendor
                        CreateNearbyVendorBehavior()
                    )
                ),

                // === MAIL ===
                new Decorator(
                    ctx => NeedsToMail(),
                    CreateMailBehavior()
                )
            );
        }

        /// <summary>
        /// Navigate to profile vendor, handle Gossip, Sell, Repair.
        /// Uses Vendors.SellAllItems() with profile quality filters + ProtectedItems.
        /// </summary>
        private Composite CreateProfileVendorBehavior()
        {
            return new Action(ctx =>
            {
                var vendorType = NeedsToRepair() ? Vendor.VendorType.Repair : Vendor.VendorType.Sell;
                var vendor = GetBestVendor(vendorType);
                if (vendor == null) return RunStatus.Failure;

                // Move to vendor
                if (StyxWoW.Me!.Location.DistanceSqr(vendor.Location) > 5 * 5)
                {
                    TreeRoot.StatusText = $"Moving to vendor {vendor.Name}";
                    Navigator.MoveTo(vendor.Location);
                    return RunStatus.Running;
                }

                // Find the NPC unit
                var vendorUnit = ObjectManager.GetObjectsOfType<WoWUnit>()
                    .Where(u => u.Entry == vendor.Entry && u.IsAlive)
                    .OrderBy(u => u.DistanceSqr)
                    .FirstOrDefault();

                if (vendorUnit == null)
                {
                    Logging.Write($"[GatherBuddy] Vendor {vendor.Name} not found at location, blacklisting");
                    ProfileManager.CurrentProfile?.VendorManager?.Blacklist.Add(vendor);
                    return RunStatus.Failure;
                }

                WoWMovement.MoveStop();
                vendorUnit.Interact();
                SleepForLag();

                // Handle GossipFrame if visible
                HandleGossipFrame();

                // Sell using proper API (respects profile quality filters + ProtectedItems)
                if (NeedsToSell())
                {
                    SellItemsWithQualityFilter();
                    Logging.Write("[GatherBuddy] Sold items at vendor");
                }

                // Repair
                if (NeedsToRepair() && vendorUnit.IsRepairMerchant)
                {
                    MerchantFrame.Instance.RepairAllItems();
                    Logging.Write("[GatherBuddy] Repaired equipment");
                }

                SleepForLag();
                MerchantFrame.Instance.Close();
                return RunStatus.Success;
            });
        }

        /// <summary>
        /// Fallback: scan ObjectManager for nearby vendor NPCs, or navigate to Data.bin vendor.
        /// </summary>
        private Composite CreateNearbyVendorBehavior()
        {
            return new Action(ctx =>
            {
                // 1. Try ObjectManager — nearby loaded vendor
                var vendor = ObjectManager.GetObjectsOfType<WoWUnit>()
                    .Where(u => u.IsVendor && u.IsAlive && !u.IsHostile)
                    .OrderBy(u => u.DistanceSqr)
                    .FirstOrDefault();

                if (vendor != null)
                {
                    if (vendor.DistanceSqr > 5 * 5)
                    {
                        TreeRoot.StatusText = $"Moving to vendor {vendor.Name}";
                        Navigator.MoveTo(vendor.Location);
                        return RunStatus.Running;
                    }

                    WoWMovement.MoveStop();
                    vendor.Interact();
                    SleepForLag();
                    HandleGossipFrame();

                    if (NeedsToSell())
                    {
                        SellItemsWithQualityFilter();
                        Logging.Write("[GatherBuddy] Sold items at vendor");
                    }

                    if (NeedsToRepair() && vendor.IsRepairMerchant)
                    {
                        MerchantFrame.Instance.RepairAllItems();
                        Logging.Write("[GatherBuddy] Repaired equipment");
                    }

                    SleepForLag();
                    MerchantFrame.Instance.Close();
                    return RunStatus.Success;
                }

                // 2. Data.bin fallback — FindVendorsAutomatically
                if (GatherBuddySettings.Instance.FindVendorsAutomatically)
                {
                    var flags = NeedsToRepair() ? UnitNPCFlags.Repair : UnitNPCFlags.Vendor;
                    var dbVendor = FindVendorFromDatabase(flags);
                    if (dbVendor != null)
                    {
                        TreeRoot.StatusText = $"Traveling to vendor {dbVendor.Name} (Data.bin)";
                        Logging.Write($"[GatherBuddy] Found vendor from database: {dbVendor.Name} ({dbVendor.Location.Distance(StyxWoW.Me.Location):F0}y)");
                        Navigator.MoveTo(dbVendor.Location);
                        return RunStatus.Running;
                    }
                }

                Logging.WriteDebug("[GatherBuddy] Bags full/low durability but no vendor found");
                return RunStatus.Failure;
            });
        }

        /// <summary>
        /// Mail behavior: navigate to mailbox → send items.
        /// Uses profile mailbox if available, otherwise scans ObjectManager for nearby mailboxes.
        /// </summary>
        private Composite CreateMailBehavior()
        {
            return new Action(ctx =>
            {
                var settings = GatherBuddySettings.Instance;

                // Determine mailbox target location
                WoWPoint mailboxLocation = WoWPoint.Zero;
                var profileMailbox = ProfileManager.CurrentProfile?.MailboxManager?.GetClosestMailbox();
                if (profileMailbox != null)
                {
                    mailboxLocation = profileMailbox.Location;
                }
                else
                {
                    // Scan ObjectManager for nearby mailbox GameObjects
                    var nearbyMailbox = FindNearbyMailbox();
                    if (nearbyMailbox != null)
                        mailboxLocation = nearbyMailbox.Location;
                }

                if (mailboxLocation == WoWPoint.Zero)
                {
                    Logging.WriteDebug("[GatherBuddy] No mailbox found (profile or nearby)");
                    return RunStatus.Failure;
                }

                // Move to mailbox
                if (StyxWoW.Me!.Location.DistanceSqr(mailboxLocation) > 5 * 5)
                {
                    TreeRoot.StatusText = "Moving to mailbox";
                    Navigator.MoveTo(mailboxLocation);
                    return RunStatus.Running;
                }

                // Find mailbox game object
                var mailboxObj = ObjectManager.GetObjectsOfType<WoWGameObject>()
                    .Where(g => g.SubType == WoWGameObjectType.Mailbox)
                    .OrderBy(g => g.DistanceSqr)
                    .FirstOrDefault();

                if (mailboxObj == null)
                {
                    Logging.Write("[GatherBuddy] Mailbox not found at location");
                    return RunStatus.Failure;
                }

                WoWMovement.MoveStop();
                mailboxObj.Interact();
                SleepForLag();
                StyxWoW.Sleep(1000); // Wait for mail frame

                // Collect items to mail based on quality settings
                var itemsToMail = GetItemsToMail();
                if (itemsToMail.Count > 0)
                {
                    MailFrame.Instance.SendMailWithManyAttachments(settings.MailRecipient, 0, itemsToMail.ToArray());
                    Logging.Write($"[GatherBuddy] Mailed {itemsToMail.Count} items to {settings.MailRecipient}");
                    SleepForLag();
                }

                MailFrame.Instance.Close();
                return RunStatus.Success;
            });
        }

        // ═══════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Finds the best node to harvest.
        /// Checks: distance, type, blacklist, anti-ninja, blackspot, AvoidMobs.
        /// </summary>
        // Diagnostic throttle for FindBestNode
        private static readonly Stopwatch _diagTimer = Stopwatch.StartNew();

        private WoWGameObject? FindBestNode()
        {
            var settings = GatherBuddySettings.Instance;
            float maxRangeSqr = settings.NodeDetectionRange * settings.NodeDetectionRange;
            var blacklistedEntries = settings.BlacklistedEntries;
            var profile = ProfileManager.CurrentProfile;

            // Diagnostic: log every 5s to help debug node detection
            bool shouldLog = _diagTimer.ElapsedMilliseconds > 5000;
            if (shouldLog) _diagTimer.Restart();

            var allGameObjects = ObjectManager.GetObjectsOfType<WoWGameObject>();
            if (shouldLog)
            {
                Logging.WriteDebug($"[GatherBuddy-DIAG] Total GameObjects: {allGameObjects.Count}, GatherHerbs={settings.GatherHerbs}, GatherMinerals={settings.GatherMinerals}");
                foreach (var go in allGameObjects.Take(10))
                {
                    try
                    {
                        Logging.WriteDebug($"[GatherBuddy-DIAG]   Entry={go.Entry} Name={go.Name} SubType={go.SubType} Dist={go.Distance:F0} IsHerb={go.IsHerb} IsMineral={go.IsMineral} CanHarvest={go.CanHarvest} CanMine={go.CanMine} State={go.State} LockType={go.LockType}");
                    }
                    catch (Exception ex)
                    {
                        Logging.WriteDebug($"[GatherBuddy-DIAG]   Entry={go.Entry} ERROR reading props: {ex.Message}");
                    }
                }
            }

            return allGameObjects
                .Where(obj =>
                {
                    // Distance check
                    if (obj.DistanceSqr > maxRangeSqr)
                        return false;

                    // Node type check
                    bool isValidType =
                        (settings.GatherHerbs && obj.IsHerb && obj.CanHarvest) ||
                        (settings.GatherMinerals && obj.IsMineral && obj.CanMine) ||
                        (settings.GatherChests && obj.IsChest && obj.CanLoot);

                    if (!isValidType)
                        return false;

                    // Node selection blacklist — unchecked entries in settings
                    if (blacklistedEntries.Count > 0 && blacklistedEntries.Contains(obj.Entry))
                        return false;

                    // Blacklist check (NodeTracker)
                    if (!NodeTracker.IsNodeValid(obj))
                        return false;

                    // Global blacklist check
                    if (Blacklist.Contains(obj.Guid))
                        return false;

                    // Blackspot check — skip nodes inside profile/global blackspots
                    if (BlackspotManager.IsBlackspotted(obj.Location))
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
        // TARGETING FILTERS (Faction, AvoidMobs, Blackspot-aware)
        // ═══════════════════════════════════════════════════════════

        private void IncludeTargetsFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
        {
            var settings = GatherBuddySettings.Instance;
            var profile = ProfileManager.CurrentProfile;

            for (int i = incoming.Count - 1; i >= 0; i--)
            {
                if (incoming[i] is not WoWUnit unit || incoming[i] is WoWPlayer)
                    continue;

                // Skip elites if configured
                if (settings.IgnoreElites && unit.Elite)
                    continue;

                // AvoidMobs from profile
                if (profile?.AvoidMobs != null && profile.AvoidMobs.Contains(unit.Entry))
                    continue;

                // Blackspot check
                if (BlackspotManager.IsBlackspotted(unit.Location))
                    continue;

                // Faction filtering from profile (bypass for hostile mobs attacking us)
                if (profile?.Factions != null && profile.Factions.Count > 0)
                {
                    bool isAttackingUs = unit.IsHostile && (unit.IsTargetingMeOrPet || unit.Aggro);
                    if (!isAttackingUs && !profile.Factions.Contains(unit.FactionId))
                        continue;
                }

                outgoing.Add(unit);
            }
        }

        private void IncludeLootFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
        {
            var settings = GatherBuddySettings.Instance;
            float lootRadius = settings.LootRadius;
            float lootRadiusSqr = lootRadius * lootRadius;
            // Nodes use NodeDetectionRange, not LootRadius
            float nodeRange = settings.NodeDetectionRange;
            float nodeRangeSqr = nodeRange * nodeRange;

            for (int i = 0; i < incoming.Count; i++)
            {
                if (incoming[i] is WoWUnit unit)
                {
                    if (settings.LootMobs && unit.IsDead && unit.DistanceSqr < lootRadiusSqr &&
                        !Blacklist.Contains(unit.Guid))
                    {
                        if ((unit.KilledByMe && unit.CanLoot) ||
                            (unit.CanSkin && settings.SkinMobs && unit.KilledByMe))
                        {
                            outgoing.Add(unit);
                        }
                    }
                }
                else if (incoming[i] is WoWGameObject gameObj)
                {
                    if (gameObj.DistanceSqr < nodeRangeSqr && !Blacklist.Contains(gameObj.Guid))
                    {
                        bool isTarget =
                            (gameObj.IsHerb && settings.GatherHerbs) ||
                            (gameObj.IsMineral && settings.GatherMinerals) ||
                            (gameObj.IsChest && settings.GatherChests);

                        if (!isTarget) continue;
                        if (!gameObj.CanLoot) continue;

                        // Position-based blacklist (failed gather attempts — HB 4.3.4 BlacklistNodes)
                        if (IsNodeBlacklisted(gameObj.Location)) continue;

                        // Node tracker (harvested/expired tracking)
                        if (!NodeTracker.IsNodeValid(gameObj)) continue;

                        // Settings blacklist (unchecked node entries in settings UI)
                        if (settings.BlacklistedEntries.Count > 0 &&
                            settings.BlacklistedEntries.Contains(gameObj.Entry))
                            continue;

                        // Blackspot check — permanent blacklist nodes inside blackspots
                        if (BlackspotManager.IsBlackspotted(gameObj.Location))
                        {
                            Blacklist.Add(gameObj.Guid, TimeSpan.FromDays(3));
                            continue;
                        }

                        // Anti-ninja: skip nodes with other players nearby
                        if (settings.NoNinja)
                        {
                            bool playerNearby = ObjectManager.GetObjectsOfType<WoWPlayer>()
                                .Any(p => !p.IsMe && p.IsAlive &&
                                          p.Location.DistanceSqr(gameObj.Location) < 15 * 15);
                            if (playerNearby) continue;
                        }

                        // Path distance ratio check — skip nodes where nav path > 3× straight-line
                        // Catches caves, deep mountain detours, etc. (WotLK ground-only pragmatic check)
                        float straightDist = gameObj.Distance > 0 ? (float)gameObj.Distance : gameObj.Location.Distance(StyxWoW.Me.Location);
                        if (straightDist > 30f) // Only check for distant nodes — nearby ones are fine
                        {
                            float? pathDist = Navigator.PathDistance(StyxWoW.Me.Location, gameObj.Location, straightDist * 3.5f);
                            if (pathDist == null)
                            {
                                // Path not found or exceeds 3.5× straight-line → skip this node
                                continue;
                            }
                        }

                        outgoing.Add(gameObj);
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        // VENDOR/MAIL HELPERS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Get best vendor from profile VendorManager.
        /// </summary>
        private static Vendor? GetBestVendor(Vendor.VendorType type)
        {
            return ProfileManager.CurrentProfile?.VendorManager?.GetClosestVendor(type);
        }

        private static bool HasNearbyVendorNpc()
        {
            return ObjectManager.GetObjectsOfType<WoWUnit>()
                .Any(u => u.IsVendor && u.IsAlive && !u.IsHostile && u.DistanceSqr < 200 * 200);
        }

        private static bool HasNearbyRepairNpc()
        {
            return ObjectManager.GetObjectsOfType<WoWUnit>()
                .Any(u => u.IsRepairMerchant && u.IsAlive && !u.IsHostile && u.DistanceSqr < 200 * 200);
        }

        /// <summary>
        /// Queries Data.bin for the nearest vendor with the specified NPC flags.
        /// Uses NpcQueries.GetNearestNpc — same path as VendorManager's fallback.
        /// </summary>
        private static NpcResult? FindVendorFromDatabase(UnitNPCFlags flags)
        {
            try
            {
                var faction = StyxWoW.Me?.Faction;
                if (faction == null) return null;
                return NpcQueries.GetNearestNpc(
                    faction,
                    StyxWoW.Me.MapId,
                    StyxWoW.Me.Location,
                    flags);
            }
            catch (Exception ex)
            {
                Logging.WriteDebug($"[GatherBuddy] Data.bin vendor lookup failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Scans ObjectManager for nearby mailbox GameObjects.
        /// </summary>
        private static WoWGameObject? FindNearbyMailbox()
        {
            return ObjectManager.GetObjectsOfType<WoWGameObject>()
                .Where(g => g.SubType == WoWGameObjectType.Mailbox && g.DistanceSqr < 500 * 500)
                .OrderBy(g => g.DistanceSqr)
                .FirstOrDefault();
        }

        /// <summary>
        /// Handle GossipFrame (some vendors require gossip selection first).
        /// </summary>
        private static void HandleGossipFrame()
        {
            StyxWoW.Sleep(500);
            var results = Lua.GetReturnValues("return GossipFrame and GossipFrame:IsVisible() and '1' or '0'");
            if (results != null && results.Count > 0 && results[0] == "1")
            {
                // Find the vendor gossip option (usually first one)
                Lua.DoString("SelectGossipOption(1)");
                StyxWoW.Sleep(500);
            }
        }

        /// <summary>
        /// Sell items respecting quality filters from GatherBuddy settings.
        /// Uses ProtectedItemsManager to protect specific items.
        /// </summary>
        private static void SellItemsWithQualityFilter()
        {
            var settings = GatherBuddySettings.Instance;

            // Build quality filter string for the Lua script
            var qualityList = new List<int>();
            if (settings.SellGrey) qualityList.Add(0);
            if (settings.SellWhite) qualityList.Add(1);
            if (settings.SellGreen) qualityList.Add(2);
            if (settings.SellBlue) qualityList.Add(3);
            if (settings.SellPurple) qualityList.Add(4);

            if (qualityList.Count == 0) return;

            // Build protected item IDs set
            var protectedIds = ProtectedItemsManager.GetAllItemIds();
            string protectedStr = protectedIds.Count > 0
                ? string.Join(",", protectedIds.Select(id => $"[{id}]=1"))
                : "";

            string qualFilter = string.Join(",", qualityList.Select(q => $"[{q}]=1"));

            Lua.DoString(
                $"local sell={{{qualFilter}}}; " +
                $"local prot={{{protectedStr}}}; " +
                "for bag=0,4 do " +
                "  for slot=1,GetContainerNumSlots(bag) do " +
                "    local link = GetContainerItemLink(bag,slot); " +
                "    if link then " +
                "      local name,_,quality,_,_,_,_,_,_,_,price = GetItemInfo(link); " +
                "      local id = tonumber(link:match('item:(%d+)')); " +
                "      if quality and sell[quality] and not prot[id] then " +
                "        UseContainerItem(bag,slot); " +
                "      end " +
                "    end " +
                "  end " +
                "end");
        }

        /// <summary>
        /// Get items to mail based on quality filters.
        /// Skips protected items and soulbound items.
        /// </summary>
        private static List<WoWItem> GetItemsToMail()
        {
            var settings = GatherBuddySettings.Instance;
            var items = new List<WoWItem>();

            foreach (var item in ObjectManager.GetObjectsOfType<WoWItem>())
            {
                if (item.IsSoulbound) continue;
                if (ProtectedItemsManager.Contains(item.Entry)) continue;

                bool shouldMail = item.Quality switch
                {
                    WoWItemQuality.Poor => settings.MailGrey,
                    WoWItemQuality.Common => settings.MailWhite,
                    WoWItemQuality.Uncommon => settings.MailGreen,
                    WoWItemQuality.Rare => settings.MailBlue,
                    WoWItemQuality.Epic => settings.MailPurple,
                    _ => false
                };

                if (shouldMail)
                    items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Returns the number of free bag slots via Lua.
        /// </summary>
        private static int GetFreeBagSlots()
        {
            var results = Lua.GetReturnValues("local free=0; for i=0,4 do free=free+GetContainerNumFreeSlots(i) end; return free");
            if (results != null && results.Count > 0 && int.TryParse(results[0], out int free))
                return free;
            return 999;
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

        /// <summary>
        /// Sleep for estimated latency (100 + server latency) ms.
        /// </summary>
        private static void SleepForLag()
        {
            var results = Lua.GetReturnValues("local _, _, lag = GetNetStats(); return lag");
            int lag = 100;
            if (results != null && results.Count > 0 && int.TryParse(results[0], out int serverLag))
                lag = serverLag;
            StyxWoW.Sleep(100 + lag);
        }

        /// <summary>
        /// Fisher-Yates shuffle for hotspot randomization.
        /// </summary>
        private static void ShuffleList<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// One-time diagnostic: tests the full herb/mineral detection chain at startup.
        /// Logs every step so we can identify exactly where it breaks.
        /// </summary>
        private void DiagnoseNodeDetection()
        {
            try
            {
                var gos = ObjectManager.GetObjectsOfType<WoWGameObject>();
                Logging.Write($"[GB-DIAG] === Node Detection Chain Test ===");
                Logging.Write($"[GB-DIAG] GameObjects in ObjectManager: {gos.Count}");
                Logging.Write($"[GB-DIAG] Settings: GatherHerbs={GatherBuddySettings.Instance.GatherHerbs}, GatherMinerals={GatherBuddySettings.Instance.GatherMinerals}");
                Logging.Write($"[GB-DIAG] BlacklistedEntries count: {GatherBuddySettings.Instance.BlacklistedEntries.Count}");

                if (gos.Count == 0)
                {
                    Logging.Write("[GB-DIAG] WARNING: No game objects found! ObjectManager may not see GOs.");
                    return;
                }

                // Test up to 5 game objects
                foreach (var go in gos.Take(5))
                {
                    Logging.Write($"[GB-DIAG] --- GO: Entry={go.Entry} Name=\"{go.Name}\" SubType={go.SubType} Dist={go.Distance:F0}y ---");

                    // Step 1: Cache entry
                    bool hasCacheEntry = go.GetCachedInfo(out var cacheEntry);
                    Logging.Write($"[GB-DIAG]   1. GetCachedInfo: {(hasCacheEntry ? "OK" : "FAILED")}");

                    if (!hasCacheEntry)
                    {
                        Logging.Write("[GB-DIAG]      Cache entry pointer is 0 or Memory is null");
                        continue;
                    }

                    Logging.Write($"[GB-DIAG]      Properties[0..3]: {cacheEntry.Properties[0]}, {cacheEntry.Properties[1]}, {cacheEntry.Properties[2]}, {cacheEntry.Properties[3]}");

                    // Step 2: Slot mapping table
                    bool hasLockId = go.GetDataSlot(GameObjectDataSlot.LockId, out int lockId);
                    Logging.Write($"[GB-DIAG]   2. GetDataSlot(LockId): {(hasLockId ? $"OK, lockId={lockId}" : "FAILED")}");

                    if (!hasLockId)
                    {
                        Logging.Write("[GB-DIAG]      Slot mapping table may not be loaded, or LockId slot not found for this SubType");
                        continue;
                    }

                    // Step 3: WoWDb Lock DBC lookup
                    var lockDb = StyxWoW.Db?[ClientDb.Lock];
                    Logging.Write($"[GB-DIAG]   3. StyxWoW.Db[Lock]: {(lockDb != null ? $"FOUND (rows={lockDb.NumRows}, min={lockDb.MinIndex}, max={lockDb.MaxIndex})" : "NOT FOUND — DBC KEY MISMATCH!")}");

                    if (lockDb == null)
                    {
                        Logging.Write("[GB-DIAG]      ClientDb enum values don't match WoW's DBC table IDs!");
                        Logging.Write("[GB-DIAG]      This is the root cause: Lock.dbc can't be looked up.");
                    }
                    else if (lockId > 0)
                    {
                        var row = lockDb.GetRow((uint)lockId);
                        Logging.Write($"[GB-DIAG]   4. GetRow({lockId}): {(row != null && row.IsValid ? "OK" : "FAILED")}");

                        if (row != null && row.IsValid)
                        {
                            var lockEntry = row.GetStruct<LockEntry>();
                            Logging.Write($"[GB-DIAG]   5. LockEntry: Type[0]={lockEntry.Type[0]}, LockProperties[0]={lockEntry.LockProperties[0]}");
                        }
                    }

                    // Final result
                    Logging.Write($"[GB-DIAG]   RESULT: IsHerb={go.IsHerb}, IsMineral={go.IsMineral}, CanHarvest={go.CanHarvest}, CanMine={go.CanMine}, State={go.State}, LockType={go.LockType}");
                }

                Logging.Write("[GB-DIAG] === End Chain Test ===");
            }
            catch (Exception ex)
            {
                Logging.Write($"[GB-DIAG] EXCEPTION: {ex.Message}");
            }
        }
    }
}
