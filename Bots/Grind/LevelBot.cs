// LevelBot.cs - Ported from HB 4.3.4 (Cata)
// Main grinding bot - handles combat, looting, vendor, roaming behaviors
// Uses 3.3.5a offsets only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using CommonBehaviors;
using CommonBehaviors.Actions;
using CommonBehaviors.Decorators;
using Levelbot.Actions.Combat;
using Levelbot.Actions.Death;
using Levelbot.Decorators.Combat;
using Levelbot.Decorators.Death;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Common;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.AreaManagement;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Inventory;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.LootFrame;
using Styx.Logic.Inventory.Frames.MailBox;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.Logic.Inventory.Frames.Taxi;
using Styx.Logic.Inventory.Frames.Trainer;
using Styx.CommonBot;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Bots.Grind
{
    /// <summary>
    /// LevelBot - Main grinding bot ported from HB 4.3.4
    /// Handles: Death, Combat, Loot, Vendor, Roam behaviors
    /// </summary>
    public class LevelBot : BotBase
    {
        // LevelBot (Grind) requires a profile to function (HB 6.2.3 pattern)
        public override bool RequiresProfile => true;

        // Loot tracking
        private static PoiType _lastLootPoiType;
        private static ulong _lastLootGuid;
        private static bool _lootEventsAttached;
        private static int _lootAttemptCount;
        private static int _lootFailCount;

        // Death tracking  
        private static int _deathCount;
        private static readonly WaitTimer _deathTimer = new WaitTimer(new TimeSpan(0, 3, 0));
        private static Stopwatch _corpseWaitStopwatch = new Stopwatch();
        private static readonly Stopwatch _corpsePointWaitSw = new Stopwatch();   // ghost waiting for a valid CorpsePoint
        private static readonly Stopwatch _shDiagSw = new Stopwatch();            // throttle spirit-healer diagnostics
        private static bool _diedIndoors;
        private static readonly WaitTimer _releaseTimer = WaitTimer.FiveSeconds;
        private static WaitTimer _repairCostTimer = new WaitTimer(TimeSpan.FromMinutes(3.0));
        private static ulong _lastRepairCost;

        // Root behavior cache
        private PrioritySelector _rootBehavior;

        // HB 4.3.4 exact: LootAllItems helper
        private static void LootAllItems()
        {
            using (new FrameLock())
            {
                List<WoWItem> carriedItems = StyxWoW.Me.CarriedItems;
                for (int slot = 0; slot < LootFrame.Instance.LootItems; ++slot)
                {
                    uint itemId = LootFrame.Instance.GetItemId(slot);
                    foreach (WoWItem item in carriedItems)
                    {
                        if (item.Entry == itemId)
                        {
                            ItemInfo itemInfo = item.ItemInfo;
                            if (itemInfo != null && (itemInfo.UniqueCount == 1 || itemInfo.BeginQuestId != 0))
                            {
                                Blacklist.Add(BotPoi.Current.Guid, TimeSpan.FromHours(3.0));
                                break;
                            }
                        }
                    }
                    LootFrame.Instance.Loot(slot);
                }
                Lua.DoString("CloseLoot();");
            }
        }

        private static void OnLootEvent(object sender, LuaEventArgs e)
        {
            _lootAttemptCount = 0;
            _lootFailCount = 0;
        }

        #region BotBase Implementation

        public override string Name => "Grind";

        public override bool IsPrimaryType => true;

        public override bool RequirementsMet => true;

        public override Composite Root
        {
            get
            {
                if (_rootBehavior == null)
                {
                    _rootBehavior = new PrioritySelector(
                        CreateDeathBehavior(),
                        CreateCombatBehavior(),
                        CreateLootBehavior(),
                        CreateVendorBehavior(),
                        CreateRoamBehavior(),
                        new ActionIdle()
                    );
                }
                return _rootBehavior;
            }
        }

        public override PulseFlags PulseFlags => PulseFlags.All;

        public override void Start()
        {
            if (ProfileManager.CurrentOuterProfile == null)
                throw new HonorbuddyUnableToStartException("You haven't loaded a profile.");

            GrindArea currentGrindArea = StyxWoW.AreaManager?.CurrentGrindArea;
            if (currentGrindArea != null)
                currentGrindArea.CycleToNearest();

            Targeting.Instance.IncludeTargetsFilter += LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter += LevelbotIncludeLootsFilter;

            // HB 6.2.3 AvoidanceNavigationProvider pattern: register world obstacle avoidance
            // so the bot routes around forges, mailboxes, and similar navmesh-absent objects.
            Bots.DungeonBuddy.Avoidance.WorldObstacleManager.Initialize();
        }

        public override void Stop()
        {
            Targeting.Instance.IncludeTargetsFilter -= LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter -= LevelbotIncludeLootsFilter;
            Bots.DungeonBuddy.Avoidance.WorldObstacleManager.Shutdown();
        }

        private static CombatRoutine Routine => RoutineManager.Current;

        private float GetPathPrecision()
        {
            float speed = StyxWoW.Me.MovementInfo.CurrentSpeed;
            return MathEx.Clamp(speed * 0.15f, 1.5f, 10f);
        }

        public override void Pulse()
        {
            Navigator.PathPrecision = GetPathPrecision();
        }

        #endregion

        #region Combat Behavior

        /// <summary>
        /// HB 4.3.4 CreateCombatBehavior - handles dismount, target validation, rest, pull, combat
        /// </summary>
        public static Composite CreateCombatBehavior()
        {
            return new PrioritySelector(
                // Dismount for combat if needed
                new Decorator(
                    ctx => Mount.ShouldDismount(BotPoi.Current.Location),
                    new TreeSharp.Action(ctx => Mount.Dismount("Combat"))
                ),
                new PrioritySelector(
                    // Cancel skinning if not skinning POI
                    new Decorator(
                        ctx => BotPoi.Current.Type != PoiType.Skin && StyxWoW.Me.HasPendingSpell("Skinning"),
                        new TreeSharp.Action(ctx => Lua.DoString("SpellStopTargeting()"))
                    ),
                    // POI Kill sanity checks
                    new DecoratorIsPoiType(PoiType.Kill, new PrioritySelector(
                        new Decorator(
                            ctx => Targeting.Instance.TargetList.Count == 0,
                            new ActionClearPoi("No targets in target list - POI.Kill Sanity Checks")
                        ),
                        new Decorator(
                            ctx => BotPoi.Current.AsObject != null && BotPoi.Current.AsObject.ToUnit().Dead,
                            new TreeSharp.Action(ctx => BotPoi.Clear("POI is dead from Combat"))
                        )
                    )),
                    // Not in combat: Rest, PreCombatBuff, Pull
                    new Decorator(
                        ctx => !StyxWoW.Me.Combat,
                        new PrioritySelector(
                            Routine.RestBehavior,
                            Routine.PreCombatBuffBehavior,
                            new DecoratorIsPoiType(PoiType.Kill, new PrioritySelector(
                                // Switch target if better one available (HB 4.3.4: two nested decorators)
                                new Decorator(
                                    ctx => Targeting.Instance.TargetList.Count != 0,
                                    new Decorator(
                                        ctx => BotPoi.Current.AsObject != Targeting.Instance.FirstUnit &&
                                               BotPoi.Current.Type == PoiType.Kill,
                                        new Sequence(
                                            new ActionDebugString("Current POI is not the best pull target. Changing."),
                                            new ActionSetPoi(true, ctx => new BotPoi(Targeting.Instance.FirstUnit, PoiType.Kill)),
                                            new TreeSharp.Action(ctx => BotPoi.Current.AsObject.ToUnit().Target())
                                        )
                                    )
                                ),
                                // Pull if ready
                                new Decorator(
                                    ctx => CanPull(),
                                    Routine.PullBehavior
                                )
                            ))
                        )
                    ),
                    // In combat: Heal, CombatBuff, Combat
                    // combat branch: only run when we have a valid first target
                new Decorator(
                        ctx =>
                        {
                            bool combat = StyxWoW.Me.Combat || (StyxWoW.Me.GotAlivePet && StyxWoW.Me.Pet.Combat);
                            return !StyxWoW.Me.Mounted && combat &&
                                   Targeting.Instance.FirstUnit != null;
                        },
                        new PrioritySelector(
                            new Decorator(
                                ctx => StyxWoW.Me.Mounted,
                                new TreeSharp.Action(ctx => Mount.Dismount("Combat"))
                            ),
                            Routine.HealBehavior,
                            Routine.CombatBuffBehavior,
                            Routine.CombatBehavior,
                            new ActionAlwaysSucceed()
                        )
                    )
                )
            );
        }

        private static bool CanPull()
        {
            WoWUnit currentTarget = StyxWoW.Me.CurrentTarget;
            if (currentTarget == null)
                return false;
            if (!currentTarget.InLineOfSpellSight)
                return false;
            return currentTarget.Distance <= Targeting.PullDistance;
        }

        #endregion

        #region Death Behavior

        /// <summary>
        /// HB 4.3.4 CreateDeathBehavior - handles release, ghost movement, corpse retrieval
        /// </summary>
        public static PrioritySelector CreateDeathBehavior()
        {
            return new PrioritySelector(
                // Fresh start every life: a stale ShouldUseSpiritHealer / camp count from a PREVIOUS death must
                // not carry into the next one. Corpse-camp protection sets the flag, but if we then grab the
                // corpse anyway it never clears — so a later death with a perfectly reachable corpse wrongly ran
                // straight to the spirit healer (and into the SH↔corpse oscillation). Clear it whenever alive.
                new Decorator(
                    ctx => !StyxWoW.Me.IsDead && !StyxWoW.Me.IsGhost && (ShouldUseSpiritHealer || _deathCount != 0),
                    new TreeSharp.Action(ctx =>
                    {
                        ShouldUseSpiritHealer = false;
                        _deathCount = 0;
                        if (_corpsePointWaitSw.IsRunning) _corpsePointWaitSw.Reset();
                        if (_shDiagSw.IsRunning) _shDiagSw.Reset();
                        return RunStatus.Failure;
                    })
                ),
                // Dead - need to release
                new Decorator(
                    ctx => StyxWoW.Me.IsDead,
                    new Sequence(
                        new ActionSetActivity("Releasing from corpse"),
                        new TreeSharp.Action(ctx => ReleaseCorpse()),
                        new WaitContinue(5, ctx => StyxWoW.Me.IsGhost, 
                            new TreeSharp.Action(ctx => SleepForLag()))
                    )
                ),
                // Ghost but no VALID corpse location yet — CorpsePoint reads Empty (NaN) or 0,0,0 (a transient
                // memory read right after release; corpse runs work fine once the server sends the real point).
                // WAIT for it instead of falling through: a 0,0,0 sneaks past the "!= Empty" spirit-healer check
                // below and traps the bot in a permanent spirit-healer interact loop (the 4.5h ghost hang), and
                // the move-to-corpse branch would otherwise path to the world origin. Escalate to the spirit
                // healer only if it stays invalid for 60s (a genuine read failure, not the usual transient).
                new Decorator(
                    ctx => StyxWoW.Me.IsGhost && !ShouldUseSpiritHealer
                           && (StyxWoW.Me.CorpsePoint == WoWPoint.Empty || StyxWoW.Me.CorpsePoint == WoWPoint.Zero),
                    new TreeSharp.Action(ctx =>
                    {
                        if (!_corpsePointWaitSw.IsRunning) _corpsePointWaitSw.Restart();
                        if (_corpsePointWaitSw.ElapsedMilliseconds < 150 || _corpsePointWaitSw.ElapsedMilliseconds % 3000 < 150)
                            Logging.Write("[Death] Ghost waiting for a valid corpse location (CorpsePoint={0}, {1:0}s) — NOT diverting to spirit healer.",
                                StyxWoW.Me.CorpsePoint, _corpsePointWaitSw.Elapsed.TotalSeconds);
                        if (_corpsePointWaitSw.Elapsed.TotalSeconds > 60)
                        {
                            Logging.Write("[Death] CorpsePoint still invalid after 60s — genuine read failure, escalating to spirit healer.");
                            ShouldUseSpiritHealer = true;
                            _corpsePointWaitSw.Reset();
                        }
                        return RunStatus.Success;
                    })
                ),
                // Reset the wait clock once a valid corpse point shows up (normal corpse run takes over below).
                new Decorator(
                    ctx => _corpsePointWaitSw.IsRunning && StyxWoW.Me.CorpsePoint != WoWPoint.Empty
                           && StyxWoW.Me.CorpsePoint != WoWPoint.Zero,
                    new TreeSharp.Action(ctx => { _corpsePointWaitSw.Reset(); return RunStatus.Failure; })
                ),
                // Ghost - need to use spirit healer (if enabled and can't reach corpse). COMMIT once chosen:
                // wrap in AlwaysSucceed so this owns the tick and the corpse branches below NEVER run — that
                // stops the spirit-healer↔corpse ping-pong that hangs when neither recovery path completes
                // (corpse unreachable AND the SH res not finishing). Cleared only on an actual resurrection.
                new Decorator(
                    ctx => ShouldUseSpiritHealer && StyxWoW.Me.IsGhost,
                    new PrioritySelector(
                        CreateSpiritHealerBehavior(),
                        // Commit ONLY while a spirit healer actually exists (so walking to it can't ping-pong
                        // with the corpse). No healer present → fall through (Failure) and let the corpse
                        // branches run — never strand the ghost on a spirit healer that isn't there.
                        new Decorator(
                            ctx => ObjectManager.CachedUnits.Any(u => u.IsSpiritHealer),
                            new ActionAlwaysSucceed()
                        )
                    )
                ),
                // Ghost - can't navigate to corpse, use spirit healer
                new DecoratorIsNotPoiType(PoiType.Corpse, new Decorator(
                    ctx => CharacterSettings.Instance.RessAtSpiritHealers &&
                           StyxWoW.Me.IsGhost &&
                           StyxWoW.Me.CorpsePoint != WoWPoint.Empty &&
                           StyxWoW.Me.CorpsePoint != WoWPoint.Zero &&   // 0,0,0 = not-yet-read, NOT an unreachable corpse
                           StyxWoW.Me.Location.DistanceSqr(StyxWoW.Me.CorpsePoint) > 40.0 &&
                           !Navigator.CanNavigateFully(StyxWoW.Me.Location, StyxWoW.Me.CorpsePoint),
                    new Sequence(
                        new TreeSharp.Action(ctx => Logging.Write("Corpse point has no mesh. MapId: {0} Location: {1}", StyxWoW.Me.MapId, StyxWoW.Me.CorpsePoint)),
                        new TreeSharp.Action(ctx => Logging.Write("Can't navigate to our corpse. Trying the spirit healer instead! DEBUG: {0}", StyxWoW.Me.CorpsePoint)),
                        new TreeSharp.Action(ctx => ShouldUseSpiritHealer = true)
                    )
                )),
                // Ghost - far from corpse, need to move
                // HB 4.3.4 smethod_84: IsGhost && Distance > 40
                new DecoratorIsNotPoiType(PoiType.Corpse, new Decorator(
                    ctx => StyxWoW.Me.IsGhost && StyxWoW.Me.Location.Distance(StyxWoW.Me.CorpsePoint) > 40f,
                    new Sequence(
                        // HB 4.3.4 smethod_85: Wait up to 10 sec for server to send CorpsePoint
                        new Wait(10, ctx => StyxWoW.Me.CorpsePoint != WoWPoint.Empty, new ActionAlwaysSucceed()),
                        new ActionSetActivity("Moving to corpse"),
                        // HB 4.3.4 smethod_86/87/88: fly if !diedIndoors && (Mounted || CanFly), else walk
                        new PrioritySelector(
                            new Decorator(
                                ctx => !_diedIndoors && (StyxWoW.Me.Mounted || StyxWoW.Me.MovementInfo.CanFly),
                                new TreeSharp.Action(ctx => Flightor.MoveTo(StyxWoW.Me.CorpsePoint))
                            ),
                            new TreeSharp.Action(ctx => Navigator.MoveTo(StyxWoW.Me.CorpsePoint))
                        )
                    )
                )),
                // Ghost - near corpse, retrieve it.
                // HB 4.3.4 LevelBot.smethod_89: IsGhost && Distance(CorpsePoint) < 40f.
                new Decorator(
                    ctx => StyxWoW.Me.IsGhost && StyxWoW.Me.Location.Distance(StyxWoW.Me.CorpsePoint) < 40f,
                    CreateCorpseRetrievalBehavior()
                ),
                // Succeed if dead or ghost (to prevent other behaviors from running)
                new ActionSuceedIfDeadOrGhost()
            );
        }

        public static bool ShouldUseSpiritHealer { get; set; }

        private static Composite CreateSpiritHealerBehavior()
        {
            return new PrioritySelector(
                ctx => ObjectManager.CachedUnits.FirstOrDefault(u => u.IsSpiritHealer),
                // No spirit healer in range — log it (we don't know where to walk). Keeps committed (parent
                // AlwaysSucceed owns the tick) instead of ping-ponging back to the corpse.
                new Decorator(
                    ctx => ctx == null,
                    new TreeSharp.Action(ctx =>
                    {
                        // No spirit healer to use → don't sit committed to nothing. Drop the flag and let the
                        // corpse branches retry (camp-protection won't re-set it without a SH present).
                        ShDiag("no spirit healer nearby — abandoning SH, retrying the corpse run");
                        ShouldUseSpiritHealer = false;
                        return RunStatus.Failure;
                    })
                ),
                // Move to spirit healer
                new Decorator(
                    ctx => ctx != null && ((WoWObject)ctx).DistanceSqr > 16.0,
                    new TreeSharp.Action(ctx =>
                    {
                        var sh = (WoWObject)ctx;
                        ShDiag(string.Format("moving to spirit healer '{0}' d={1:0.#}", sh.Name, sh.Distance));
                        Navigator.MoveTo(sh.Location);
                        return RunStatus.Success;
                    })
                ),
                // At the spirit healer — interact, capture what the server presents, then resurrect.
                new Decorator(
                    ctx => ctx != null && ((WoWObject)ctx).DistanceSqr < 16.0,
                    new Sequence(
                        new TreeSharp.Action(ctx => { ((WoWObject)ctx).Interact(); SleepForLag(); }),
                        // DIAGNOSTIC: dump exactly what's on screen so we can match the right resurrect path on
                        // THIS server (the default StaticPopup1/Healer-gossip detection wasn't completing the res).
                        new TreeSharp.Action(ctx =>
                        {
                            try
                            {
                                bool popup = Lua.GetReturnVal<bool>("return (StaticPopup1 and StaticPopup1:IsVisible()) and true or false", 0);
                                bool gossip = Lua.GetReturnVal<bool>("return (GossipFrame and GossipFrame:IsVisible()) and true or false", 0);
                                int gNum = Lua.GetReturnVal<int>("return GetNumGossipOptions() or 0", 0);
                                int delay = Lua.GetReturnVal<int>("return GetCorpseRecoveryDelay() or 0", 0);
                                string popText = Lua.GetReturnVal<string>("return (StaticPopup1Text and StaticPopup1Text:GetText()) or ''", 0);
                                Logging.Write("[Death/SH] interacted: popup={0} text='{1}' gossip={2} gossipOpts={3} corpseDelay={4}",
                                    popup, popText, gossip, gNum, delay);
                            }
                            catch (System.Exception ex) { Logging.Write("[Death/SH] diag err: {0}", ex.Message); }
                            return RunStatus.Success;
                        }),
                        new Wait(5,
                            ctx => Lua.GetReturnVal<bool>("return ((StaticPopup1 and StaticPopup1:IsVisible()) or (GossipFrame and GossipFrame:IsVisible())) and true or false", 0),
                            new Sequence(
                                // Gossip path: Healer-typed option, else fall back to the first option (some
                                // servers don't tag the resurrect option as type Healer).
                                new TreeSharp.Action(ctx =>
                                {
                                    try
                                    {
                                        if (Lua.GetReturnVal<bool>("return (GossipFrame and GossipFrame:IsVisible()) and true or false", 0))
                                        {
                                            var entry = GossipFrame.Instance.GossipOptionEntries
                                                .FirstOrDefault(e => e.Type == GossipEntry.GossipEntryType.Healer);
                                            if (entry.Index != 0) GossipFrame.Instance.SelectGossipOption(entry.Index);
                                            else Lua.DoString("SelectGossipOption(1)");
                                        }
                                    }
                                    catch { }
                                }),
                                // Popup path + generic accept-resurrect, then XP-loss confirm.
                                new TreeSharp.Action(ctx => Lua.DoString("if StaticPopup1Button1 then StaticPopup1Button1:Click() end")),
                                new TreeSharp.Action(ctx => Lua.DoString("if AcceptResurrect then AcceptResurrect() end")),
                                new TreeSharp.Action(ctx =>
                                {
                                    SleepForLag();
                                    Lua.DoString("if AcceptXPLoss then AcceptXPLoss() end");
                                    // Only consider it done if we actually came back to life — otherwise stay
                                    // committed and keep trying (with the diagnostic above showing why).
                                    if (StyxWoW.Me.IsAlive && !StyxWoW.Me.IsGhost)
                                    {
                                        Logging.Write("[Death/SH] Resurrected at spirit healer.");
                                        ShouldUseSpiritHealer = false;
                                        _deathCount = 0;
                                        _shDiagSw.Reset();
                                    }
                                })
                            )
                        )
                    )
                )
            );
        }

        // Throttled spirit-healer diagnostic (≈ every 3s).
        private static void ShDiag(string msg)
        {
            if (!_shDiagSw.IsRunning || _shDiagSw.Elapsed.TotalSeconds >= 3)
            {
                Logging.Write("[Death/SH] {0}", msg);
                _shDiagSw.Restart();
            }
        }

        private static Composite CreateCorpseRetrievalBehavior()
        {
            return new Sequence(
                // Set POI to corpse if not already
                new DecoratorContinue(
                    ctx => BotPoi.Current.Type != PoiType.Corpse,
                    new ActionSetPoi(ctx => new BotPoi(FindSafeResPoint(), PoiType.Corpse))
                ),
                new DecoratorIsPoiType(PoiType.Corpse, new Sequence(
                    // If we're alive now (not ghost), clear POI
                    new DecoratorContinue(
                        ctx => StyxWoW.Me.IsAlive && !StyxWoW.Me.IsGhost,
                        new Sequence(
                            new TreeSharp.Action(ctx => _corpseWaitStopwatch.Reset()),
                            new ActionClearPoi("Resurrected"),
                            new ActionAlwaysFail()
                        )
                    ),
                    // Start stopwatch if not running
                    new DecoratorContinue(
                        ctx => !_corpseWaitStopwatch.IsRunning,
                        new TreeSharp.Action(ctx => _corpseWaitStopwatch.Start())
                    ),
                    // If POI is at corpse point exactly, grab corpse immediately
                    new DecoratorContinue(
                        ctx => BotPoi.Current.Type == PoiType.Corpse && 
                               BotPoi.Current.Location == StyxWoW.Me.CorpsePoint,
                        new Sequence(
                            new ActionSetActivity("Safespot is invalid. Grabbing corpse..."),
                            new TreeSharp.Action(ctx => GrabCorpse()),
                            new TreeSharp.Action(ctx => _corpseWaitStopwatch.Reset()),
                            new ActionClearPoi("Grabbed our corpse.")
                        )
                    ),
                    // Safe res timer expired (40 seconds)
                    new DecoratorContinue(
                        ctx => _corpseWaitStopwatch.Elapsed.Seconds > 40,
                        new Sequence(
                            new ActionSetActivity("SafeRes timer expired - Grabbing our corpse where we are."),
                            new TreeSharp.Action(ctx => GrabCorpse()),
                            new TreeSharp.Action(ctx => _corpseWaitStopwatch.Reset()),
                            new WaitContinue(5, ctx => StyxWoW.Me.IsAlive, null),
                            new ActionClearPoi("Res timer expired. Grabbed our corpse.")
                        )
                    ),
                    // Near safe spot - grab corpse
                    new Sequence(
                        new DecoratorContinue(
                            ctx => _corpseWaitStopwatch.Elapsed.Seconds < 40 && IsNearCurrentPoi(),
                            new Sequence(
                                new ActionSetActivity("Grabbing corpse"),
                                new TreeSharp.Action(ctx => GrabCorpse()),
                                new TreeSharp.Action(ctx => _corpseWaitStopwatch.Reset()),
                                new WaitContinue(5, ctx => StyxWoW.Me.IsAlive, null),
                                new ActionClearPoi("Grabbed corpse at safe spot")
                            )
                        )
                    )
                )),
                // Instance corpse - move to portal
                new DecoratorContinue(
                    ctx => StyxWoW.Me.InstanceCorpseLocation != WoWPoint.Empty,
                    new Sequence(
                        new ActionSetActivity("Moving to instance portal, since we died inside."),
                        new NavigationAction(ctx => StyxWoW.Me.InstanceCorpseLocation)
                    )
                ),
                // Move to POI
                new Decorator(
                    ctx => BotPoi.Current.Location != WoWPoint.Zero,
                    new ActionMoveToPoi()
                )
            );
        }

        private static bool IsNearCurrentPoi()
        {
            return BotPoi.Current != null && 
                   BotPoi.Current.Location != WoWPoint.Empty &&
                   StyxWoW.Me.Location.Distance2DSqr(BotPoi.Current.Location) < 25.0;
        }

        /// <summary>
        /// HB 4.3.4 smethod_6 — Attempt to retrieve corpse.
        /// Returns Running while waiting for recovery delay, Success after RetrieveCorpse().
        /// </summary>
        private static RunStatus GrabCorpse()
        {
            if (Lua.GetReturnVal<int>("return GetCorpseRecoveryDelay()", 0) != 0)
            {
                Logging.Write("Waiting for corpse recovery delay to expire.");
                return RunStatus.Running;
            }

            Logging.Write("Clicking corpse popup...");
            Lua.DoString("RetrieveCorpse()");

            // Corpse-camp protection routes to a spirit healer — only engage it if one ACTUALLY exists nearby.
            // Otherwise it strands the ghost committed to a healer that isn't there, ignoring a corpse that's
            // perfectly grabbable (e.g. once the camp clears or you clear it manually). No SH → keep trying the
            // corpse (bounded by the 40s force-grab). NOTE: this counter increments per GrabCorpse call (≈ per
            // tick), so with a SH present it still trips fast — acceptable since it then has a real healer to use.
            if (CharacterSettings.Instance.RessAtSpiritHealers && !Battlegrounds.IsInsideBattleground
                && ObjectManager.CachedUnits.Any(u => u.IsSpiritHealer))
            {
                if (!_deathTimer.IsFinished)
                {
                    ++_deathCount;
                    Logging.Write("Corpse possibly being camped. Camp count: {0}/3", _deathCount);
                }
                _deathTimer.Reset();
                if (_deathCount >= 3)
                {
                    Logging.Write("Corpse camp protection tripped. Attempting to resurrect at a spirit healer.");
                    ShouldUseSpiritHealer = true;
                    _deathCount = 0;
                }
            }

            return RunStatus.Success;
        }

        private static void ReleaseCorpse()
        {
            if (!_releaseTimer.IsFinished)
                return;

            _releaseTimer.Reset();
            GameStats.Died();
            Navigator.Clear();
            Logging.Write("I died.");
            _diedIndoors = StyxWoW.Me.IsIndoors;
            Lua.DoString("RepopMe()");
        }

        /// <summary>
        /// HB 4.3.4 Class635.smethod_0 — Simple safe res point: tries direct nav, then raycasts
        /// around corpse with FindHeight validation.
        /// </summary>
        private static WoWPoint FindCorpsePoint()
        {
            WoWPoint corpsePoint = StyxWoW.Me.CorpsePoint;
            WoWPoint myLocation = StyxWoW.Me.Location;

            if (Navigator.CanNavigateFully(myLocation, corpsePoint))
                return corpsePoint;

            for (float degrees = 0.0f; degrees < 360.0f; degrees += 15f)
            {
                for (float distance = 0.0f; distance <= 35.0f; distance += 5f)
                {
                    var vector = corpsePoint.RayCast(WoWMathHelper.DegreesToRadians(degrees), distance);
                    float originalZ = vector.Z;
                    if (Navigator.FindHeight(vector.X, vector.Y, out float newZ) &&
                        Math.Abs(originalZ - newZ) <= 15f &&
                        Navigator.CanNavigateFully(myLocation, new WoWPoint(vector.X, vector.Y, newZ)))
                    {
                        return new WoWPoint(vector.X, vector.Y, newZ);
                    }
                }
            }

            return corpsePoint;
        }

        /// <summary>
        /// HB 4.3.4 Class635.smethod_1 — Full safe res point algorithm with hostile mob avoidance.
        /// Uses MassTraceLine LOS checks and scores points by distance from nearest hostile.
        /// </summary>
        private static WoWPoint FindSafeResPoint()
        {
            // HB 4.3.4: woWPoint_0 = first read of CorpsePoint (used for safety check & fallback)
            WoWPoint originalCorpse = StyxWoW.Me.CorpsePoint;

            // Gather hostile NPC positions
            List<WoWPoint> hostilePositions = ObjectManager.GetObjectsOfType<WoWUnit>(true, false)
                .Where(u => !u.Dead && u.IsHostile)
                .Select(u => u.Location)
                .ToList();

            Logging.Write("There are {0} hostile mobs near our corpse.", hostilePositions.Count);

            // HB 4.3.4: second read of CorpsePoint (used for raycasting, raised by 2.132)
            WoWPoint corpsePoint = StyxWoW.Me.CorpsePoint;
            WoWPoint myLocation = StyxWoW.Me.Location;

            // If corpse hasn't moved significantly and no hostiles within 25yd, use original point directly
            if (corpsePoint.Distance2D(originalCorpse) < 39f && IsPointSafeFromHostiles(originalCorpse, hostilePositions))
                return originalCorpse;

            // Build raycast lines from corpse outward
            WoWPoint raisedCorpse = corpsePoint;
            raisedCorpse.Z += 2.132f;

            var traceLines = new List<WorldLine>();
            for (float degrees = 0.0f; degrees < 360.0f; degrees += 15f)
            {
                for (float distance = 0.0f; distance <= 35.0f; distance += 5f)
                {
                    WoWPoint endPoint = raisedCorpse.RayCast((float)(degrees * Math.PI / 180.0), distance);
                    traceLines.Add(new WorldLine(raisedCorpse, endPoint));
                }
            }

            // MassTraceLine for LOS — points that hit geometry are blocked
            GameWorld.MassTraceLine(traceLines.ToArray(), GameWorld.CGWorldFrameHitFlags.HitTestLOS, out bool[] hitResults);

            WoWPoint bestPoint = corpsePoint;
            float bestDistance = 0f;

            for (int i = 0; i < traceLines.Count; i++)
            {
                // Skip points blocked by LOS
                if (hitResults != null && hitResults[i])
                    continue;

                WoWPoint candidate = traceLines[i].End;

                // Validate path to candidate
                WoWPoint[]? path = Navigator.GeneratePath(myLocation, candidate);
                if (path == null || path.Length == 0)
                    continue;

                // The candidate Z is the corpse height + 2.132 (raised for the LOS raycast), so the old
                // |pathEnd.Z - candidate.Z| >= 3 check rejected almost everything: 2.132 of the 3yd tolerance
                // was eaten by the raise, so any spot even ~1yd below the corpse was discarded → on real
                // (non-flat) terrain it found nothing and rezzed on the mobs. Use the reachable ground point
                // (pathEnd) as the res spot and only require the path to actually reach the candidate's XY
                // (i.e. didn't stop short at a cliff/wall).
                WoWPoint pathEnd = path[path.Length - 1];
                if (pathEnd.Distance2DSqr(candidate) > Navigator.PathPrecision * Navigator.PathPrecision)
                    continue;

                // Score: distance from nearest hostile (higher = safer)
                float distFromHostile = GetDistanceToNearestHostile(pathEnd, hostilePositions);
                if (distFromHostile > bestDistance)
                {
                    bestPoint = pathEnd;
                    bestDistance = distFromHostile;
                }
            }

            return bestPoint;
        }

        /// <summary>
        /// HB 4.3.4 Class635.smethod_3 — Check if no hostile is within 25 yards of a point.
        /// </summary>
        private static bool IsPointSafeFromHostiles(WoWPoint point, IEnumerable<WoWPoint> hostilePositions)
        {
            // 625f = 25 * 25 (25 yard radius check)
            return !hostilePositions.Any(h => h.DistanceSqr(point) < 625f);
        }

        /// <summary>
        /// HB 4.3.4 Class635.smethod_4 — Get distance to the nearest hostile point.
        /// Returns float.MaxValue if no hostiles exist.
        /// </summary>
        private static float GetDistanceToNearestHostile(WoWPoint point, IEnumerable<WoWPoint> hostilePositions)
        {
            WoWPoint nearest = hostilePositions
                .OrderBy(h => h.Distance(point))
                .FirstOrDefault();

            if (nearest == default)
                return float.MaxValue;

            return nearest.Distance2D(point);
        }

        #endregion

        #region Loot Behavior

        /// <summary>
        /// HB 4.3.4 CreateLootBehavior - handles looting, skinning, harvesting
        /// </summary>
        public static Composite CreateLootBehavior()
        {
            // Attach loot events once
            if (!_lootEventsAttached)
            {
                Lua.Events.AttachEvent("CHAT_MSG_LOOT", OnLootEvent);
                BotEvents.Player.OnMobKilled += args =>
                {
                    if (!CharacterSettings.Instance.LootMobs ||
                        RaFHelper.Leader != null ||
                        Battlegrounds.IsInsideBattleground ||
                        StyxWoW.Me.IsInInstance ||
                        Targeting.GetAggroOnMeWithin(StyxWoW.Me.Location, 30f) != 0)
                        return;
                };
                _lootEventsAttached = true;
            }

            return new Decorator(
                ctx => CanLoot() && !StyxWoW.Me.IsActuallyInCombat,
                new PrioritySelector(
                    // Handle loot/skin/harvest POI
                    new DecoratorIsPoiType(new[] { PoiType.Loot, PoiType.Skin, PoiType.Harvest },
                        new PrioritySelector(
                            // Check for enemies while looting
                            new DecoratorIsNotPoiType(PoiType.Kill,
                                new DecoratorNeedToFindTarget(new PrioritySelector(
                                    new ActionDebugString("[LB] DNTFT -> S"),
                                    new Decorator(
                                        ctx => Targeting.Instance.FirstUnit != null &&
                                               Targeting.Instance.FirstUnit.IsHostile &&
                                               Targeting.Instance.FirstUnit.Distance < 
                                               Targeting.Instance.FirstUnit.MyAggroRange + 2.0,
                                        new Sequence(
                                            new TreeSharp.Action(ctx => Targeting.Instance.FirstUnit.Target()),
                                            new ActionDebugString("[LB] SetTarget Finished. Waiting."),
                                            new Wait(5, ctx => StyxWoW.Me.GotTarget, new ActionIdle()),
                                            new ActionDebugString("[LB] Finished waiting, we got a target."),
                                            new ActionSetPoi(ctx => new BotPoi(StyxWoW.Me.CurrentTarget, PoiType.Kill))
                                        )
                                    )
                                ))
                            ),
                            // Already looted check
                            new Decorator(
                                ctx => _lastLootPoiType == BotPoi.Current.Type && _lastLootGuid == BotPoi.Current.Guid,
                                new TreeSharp.Action(ctx =>
                                {
                                    if (++_lootAttemptCount >= 5)
                                    {
                                        if (++_lootFailCount >= 2)
                                        {
                                            Logging.Write("Blacklisting lootable to avoid useless POI spam, tried looting twice but we still can't loot.");
                                            Blacklist.Add(BotPoi.Current.Guid, TimeSpan.FromMinutes(15.0));
                                            _lootFailCount = 0;
                                            BotPoi.Clear("Tried to loot more than 2 times");
                                        }
                                        else
                                        {
                                            _lastLootGuid = 0;
                                            _lootAttemptCount = 0;
                                        }
                                    }
                                    else
                                    {
                                        BotPoi.Clear("Already looted");
                                    }
                                })
                            ),
                            // HB 4.3.4 smethod_25/26: "Can't generate a path to lootable" blacklist.
                            // REMOVED: In HB 4.3.4 + Tripper navmesh, CanNavigateFully() never returned false
                            // for reachable WotLK terrain, so this check never fired in practice.
                            // Our Detour navmesh returns DT_PARTIAL_RESULT for corpses slightly off-mesh
                            // (slopes, geometry edges) — false positives that incorrectly blacklist real loot.
                            // HB-parity fallback: loot sequence tries Interact, WaitLuaEvent("LOOT_OPENED")
                            // times out after 3s, fallback action checks CanLoot and handles the blacklist.
                            // Stale loot POI: object despawned and no longer in ObjectManager
                            new Decorator(
                                ctx => BotPoi.Current.AsObject == null,
                                new TreeSharp.Action(ctx =>
                                {
                                    Logging.Write("[LB] Loot object 0x{0:X016} no longer in world (despawned), clearing stale POI.", BotPoi.Current.Guid);
                                    Blacklist.Add(BotPoi.Current.Guid, TimeSpan.FromMinutes(5.0));
                                    BotPoi.Clear("Loot object despawned");
                                })
                            ),
                            // Move to lootable
                            new Decorator(
                                ctx => BotPoi.Current.AsObject != null && !BotPoi.Current.AsObject.WithinInteractRange,
                                new ActionMoveToPoi()
                            ),
                            // Stop descending if flying
                            new Decorator(
                                ctx => StyxWoW.Me.IsFlying,
                                new TreeSharp.Action(ctx => WoWMovement.Move(WoWMovement.MovementDirection.Descend))
                            ),
                            new Decorator(
                                ctx => StyxWoW.Me.MovementInfo.IsDescending,
                                new TreeSharp.Action(ctx => WoWMovement.MoveStop(WoWMovement.MovementDirection.Descend))
                            ),
                            // Loot sequence
                            new PrioritySelector(
                                new Sequence(
                                    new DecoratorContinue(
                                        ctx => StyxWoW.Me.IsMoving,
                                        new Sequence(
                                            new TreeSharp.Action(ctx => WoWMovement.MoveStop()),
                                            new TreeSharp.Action(ctx => SleepForLag())
                                        )
                                    ),
                                    new TreeSharp.Action(ctx => BotPoi.Current.AsObject.Interact()),
                                    new WaitLuaEvent("LOOT_OPENED", 
                                        () => BotPoi.Current.Type != PoiType.Loot ? 10 : 3,
                                        new TreeSharp.Action(ctx =>
                                        {
                                            WoWObject lootObj = BotPoi.Current.AsObject;
                                            if (lootObj != null)
                                            {
                                                Logging.Write("Looting {0} Guid 0x{1:X016}", lootObj.Name, lootObj.Guid);
                                            }
                                            // HB 4.3.4 smethod_37 → smethod_0 (LootAllItems)
                                            LootAllItems();
                                        })
                                    ),
                                    // Skinning check
                                    new DecoratorContinue(
                                        ctx => (CharacterSettings.Instance.SkinMobs || CharacterSettings.Instance.NinjaSkin) &&
                                               BotPoi.Current.AsObject != null &&
                                               BotPoi.Current.AsObject is WoWUnit unit &&
                                               unit.SkinType == WoWCreatureSkinType.Leather &&
                                               unit.Level < StyxWoW.Me.CanSkinLevel,
                                        new WaitContinue(5,
                                            ctx => BotPoi.Current.AsObject.ToUnit().CanSkin &&
                                                   LootTargeting.Instance.FirstObject != null &&
                                                   LootTargeting.Instance.FirstObject.Guid == BotPoi.Current.Guid,
                                            new ActionAlwaysSucceed()
                                        )
                                    ),
                                    // Update stats
                                    new DecoratorContinue(
                                        ctx => BotPoi.Current.Type == PoiType.Loot,
                                        new TreeSharp.Action(ctx => GameStats.LootedMob())
                                    ),
                                    // Track last loot
                                    new TreeSharp.Action(ctx =>
                                    {
                                        _lastLootPoiType = BotPoi.Current.Type;
                                        _lastLootGuid = BotPoi.Current.Guid;
                                    }),
                                    new ActionClearPoi("Waiting for loot flag"),
                                    new TreeSharp.Action(ctx => SleepForLag())
                                ),
                                // Fallback - check if we can still loot
                                new TreeSharp.Action(ctx =>
                                {
                                    Logging.Write("Loot timer exceeded, blacklisting lootable.");
                                    SleepForLag();
                                    bool canStillLoot = BotPoi.Current.Type switch
                                    {
                                        PoiType.Harvest => BotPoi.Current.AsObject.ToGameObject().CanLoot,
                                        PoiType.Skin => BotPoi.Current.AsObject.ToUnit().CanSkin,
                                        _ => BotPoi.Current.AsObject.ToUnit().CanLoot
                                    };
                                    if (canStillLoot)
                                    {
                                        Logging.Write("I can't tell if we looted, blacklisting it just to be safe.");
                                        Blacklist.Add(BotPoi.Current.Guid, new TimeSpan(0, 10, 0));
                                    }
                                    else
                                    {
                                        Logging.Write("Lootable isn't lootable, blacklisting.");
                                        Blacklist.Add(BotPoi.Current.Guid, TimeSpan.FromMinutes(5));
                                    }
                                    BotPoi.Clear("Done looting");
                                })
                            )
                        )
                    ),
                    // Not currently looting - find something to loot
                    new DecoratorIsNotPoiType(new[] { PoiType.Loot, PoiType.Skin, PoiType.Harvest, PoiType.Kill },
                        new PrioritySelector(
                            // Skinnable
                            new Decorator(
                                ctx => BotPoi.Current.Type != PoiType.Skin &&
                                       LootTargeting.Instance.FirstObject != null &&
                                       LootTargeting.SkinMobs &&
                                       LootTargeting.Instance.FirstObject is WoWUnit unit &&
                                       unit.SkinType == WoWCreatureSkinType.Leather &&
                                       unit.CanSkin,
                                new ActionSetPoi(ctx => new BotPoi(LootTargeting.Instance.FirstObject, PoiType.Skin))
                            ),
                            // Harvestable (herbs/minerals only — not chests)
                            new Decorator(
                                ctx => BotPoi.Current.Type != PoiType.Harvest &&
                                       LootTargeting.Instance.FirstObject is WoWGameObject harvestObj &&
                                       (harvestObj.IsHerb && LootTargeting.HarvestHerbs ||
                                        harvestObj.IsMineral && LootTargeting.HarvestMinerals),
                                new ActionSetPoi(ctx => new BotPoi(LootTargeting.Instance.FirstObject, PoiType.Harvest))
                            ),
                            // Lootable (units and chests)
                            new Decorator(
                                ctx => BotPoi.Current.Type != PoiType.Skin &&
                                       BotPoi.Current.Type != PoiType.Loot &&
                                       LootTargeting.Instance.FirstObject != null,
                                new ActionSetPoi(ctx => new BotPoi(LootTargeting.Instance.FirstObject, PoiType.Loot))
                            )
                        )
                    )
                )
            );
        }

        private static bool IsPathBlocked(WoWObject target)
        {
            WoWPoint myLocation = ObjectManager.Me.Location;
            var path = Navigator.GeneratePath(myLocation, target.Location);

            if (path != null && path.Length > 0)
            {
                // Check if any path point is too far from target (blocked)
                if (path.Any(p => p.Distance(myLocation) > 80f))
                    return true;

                // Check if path end is too far from target
                if (path[path.Length - 1].Distance(target.Location) > 5.0)
                {
                    Blacklist.Add(target.Guid, new TimeSpan(1, 1, 1));
                    return true;
                }
            }

            return false;
        }

        private static bool CanLoot()
        {
            if (ProfileManager.CurrentProfile == null)
                return true;

            uint freeSlots = LevelbotSettings.Instance.GroundMountFarmingMode
                ? StyxWoW.Me.FreeBagSlots
                : StyxWoW.Me.FreeNormalBagSlots;

            if (freeSlots <= 1 || freeSlots < ProfileManager.CurrentProfile.MinFreeBagSlots)
            {
                // HB 4.3.4 uses Trace.WriteLine here (invisible in bot log), not Logging.WriteDebug.
                return false;
            }

            return true;
        }

        #endregion

        #region Vendor Behavior

        /// <summary>
        /// HB 4.3.4 CreateVendorBehavior - handles selling, repairing, mailing, training
        /// </summary>
        public static PrioritySelector CreateVendorBehavior()
        {
            return new PrioritySelector(
                // Handle vendor POI types
                new DecoratorIsPoiType(new[] { PoiType.Sell, PoiType.Repair, PoiType.Mail, PoiType.Buy, PoiType.Train, PoiType.Fly },
                    new PrioritySelector(
                        // Move to vendor
                        new Decorator(
                            ctx => BotPoi.Current.Location.Distance(StyxWoW.Me.Location) > 5.0,
                            new ActionMoveToPoi()
                        ),
                        // At vendor
                        new Decorator(
                            ctx => BotPoi.Current.Location.Distance(StyxWoW.Me.Location) <= 5.0,
                            new PrioritySelector(
                            // Vendor/mailbox not found
                                new Decorator(
                                    ctx => BotPoi.Current.AsObject == null,
                                    new Sequence(
                                        new TreeSharp.Action(ctx => Logging.Write(System.Drawing.Color.Red, 
                                            "Could not find {0} {1}[{2}], blacklisting.",
                                            BotPoi.Current.Type == PoiType.Mail ? "mailbox" : "vendor",
                                            BotPoi.Current.Name, BotPoi.Current.Entry)),
                                        new DecoratorContinue(
                                            ctx => BotPoi.Current.AsVendor != null,
                                            new TreeSharp.Action(ctx => 
                                                ProfileManager.CurrentProfile.VendorManager.Blacklist.Add(BotPoi.Current.AsVendor))
                                        ),
                                        new ActionClearPoi("Vendor/mailbox was blacklisted")
                                    )
                                ),
                                // Interact with vendor
                                new Decorator(
                                    ctx => !IsVendorFrameOpen() && BotPoi.Current.AsObject != null,
                                    new Sequence(
                                        new TreeSharp.Action(ctx => Navigator.PlayerMover.MoveStop()),
                                        new TreeSharp.Action(ctx => SleepForLag()),
                                        new TreeSharp.Action(ctx => BotPoi.Current.AsObject.Interact()),
                                        new WaitContinue(5, ctx => IsVendorFrameOpen(),
                                            new PrioritySelector(
                                                new DecoratorFrameIsVisible<GossipFrame>(new Sequence(
                                                    new TreeSharp.Action(ctx =>
                                                    {
                                                        var entry = GossipFrame.Instance.GossipOptionEntries
                                                            .FirstOrDefault(e => e.Type == BotPoi.Current.Type.GetGossipType());
                                                        if (entry.Index >= 0)
                                                            GossipFrame.Instance.SelectGossipOption(entry.Index);
                                                    }),
                                                    // HB 6.2.3 fix: delay after gossip selection to let the game
                                                    // process the request and open the correct frame
                                                    new ActionSleep(500),
                                                    new Wait(5, ctx => !GossipFrame.Instance.IsVisible, new ActionIdle())
                                                )),
                                                new ActionIdle()
                                            )
                                        ),
                                        // Fly POI: if TaxiFrame never opened after 5 seconds, blacklist the flight master
                                        new DecoratorContinue(
                                            ctx => BotPoi.Current.Type == PoiType.Fly && !TaxiFrame.Instance.IsVisible,
                                            new Sequence(
                                                new TreeSharp.Action(ctx => Logging.Write("Taximap failed to open. Blacklisting the flight master.")),
                                                new TreeSharp.Action(ctx =>
                                                {
                                                    if (BotPoi.Current.AsObject != null)
                                                        Blacklist.Add(BotPoi.Current.AsObject.Guid, TimeSpan.FromMinutes(30));
                                                }),
                                                new ActionClearPoi("Flight master blacklisted")
                                            )
                                        )
                                    )
                                ),
                                // Vendor frame is open - do actions
                                new Decorator(
                                    ctx => IsVendorFrameOpen(),
                                    new PrioritySelector(
                                        // Sell/Repair — HB 6.2.3 pattern: require MerchantFrame visible
                                        new DecoratorIsPoiType(new[] { PoiType.Sell, PoiType.Repair },
                                            new Decorator(ctx => MerchantFrame.Instance.IsVisible, new Sequence(
                                            new DecoratorContinue(
                                                ctx => BotPoi.Current.AsObject?.ToUnit()?.IsVendor == true,
                                                new Sequence(
                                                    new ActionDebugString("Selling items"),
                                                    new ActionSetActivity("Selling Items"),
                                                    new TreeSharp.Action(ctx => Vendors.SellAllItems()),
                                                    new ActionSleep(2000),
                                                    new DecoratorContinue(
                                                        ctx => StyxWoW.Me.FreeBagSlots < 2,
                                                        new Sequence(
                                                            new TreeSharp.Action(ctx => Logging.Write(System.Drawing.Color.Red,
                                                                "We have just done a sell run and bags are still full. Stopping the bot.")),
                                                            new TreeSharp.Action(ctx => TreeRoot.Stop())
                                                        )
                                                    )
                                                )
                                            ),
                                            new DecoratorContinue(
                                                ctx => BotPoi.Current.AsObject?.ToUnit()?.IsRepairMerchant == true,
                                                new Sequence(
                                                    new ActionDebugString("Repairing items"),
                                                    new ActionSetActivity("Repairing Items"),
                                                    new TreeSharp.Action(ctx =>
                                                    {
                                                        // Frame is OPEN here, so GetRepairAllCost() is accurate. NeedToRepair runs it
                                                        // while grinding (frame CLOSED → returns 0), so _lastRepairCost was stuck at 0 and
                                                        // the affordability guard never fired → endless repair loop when broke. Capture
                                                        // the real cost here and only repair if we can actually afford it.
                                                        var cost = StyxWoW.Me.GetEstimatedRepairCost();
                                                        if (cost.TotalCoppers > 0)
                                                            _lastRepairCost = (ulong)cost.TotalCoppers;
                                                        if (StyxWoW.Me.Coinage >= _lastRepairCost)
                                                            Vendors.RepairAllItems();
                                                        else
                                                            Logging.Write(System.Drawing.Color.Orange,
                                                                "Can't afford repair ({0}c, have {1}c) — backing off to grind for money.",
                                                                _lastRepairCost, StyxWoW.Me.Coinage);
                                                    }),
                                                    new ActionSleep(2000)
                                                )
                                            ),
                                            // Check if need to mail
                                            new DecoratorContinue(
                                                ctx =>
                                                {
                                                    if (!NeedToMail())
                                                        return false;
                                                    var mailbox = ProfileManager.CurrentProfile?.MailboxManager?.GetClosestMailbox();
                                                    if (mailbox == null)
                                                        return false;
                                                    // Store mailbox in context for ActionSetPoi
                                                    if (ctx is Dictionary<string, object> dict)
                                                        dict["_mailbox"] = mailbox;
                                                    return true;
                                                },
                                                new Sequence(
                                                    new ActionSetPoi(ctx =>
                                                    {
                                                        if (ctx is Dictionary<string, object> dict && dict.TryGetValue("_mailbox", out var mb) && mb is Mailbox mailbox)
                                                            return new BotPoi(mailbox.Location, PoiType.Mail);
                                                        return BotPoi.Current;
                                                    })
                                                )
                                            ),
                                            new DecoratorContinue(
                                                ctx => BotPoi.Current.Type != PoiType.Mail,
                                                new ActionClearPoi("POI is not Mail")
                                            ),
                                            new TreeSharp.Action(ctx => MerchantFrame.Instance.Close()),
                                            new TreeSharp.Action(ctx => StyxWoW.Me.ClearTarget()),
                                            new ActionAlwaysFail()
                                        ))),
                                        // Mail
                                        new DecoratorIsPoiType(PoiType.Mail, new Sequence(
                                            new ActionDebugString("Mailing items"),
                                            new ActionSetActivity("Mailing Items"),
                                            new TreeSharp.Action(ctx => Vendors.MailAllItems()),
                                            new DecoratorContinue(
                                                ctx => StyxWoW.Me.FreeBagSlots < 2,
                                                new Sequence(
                                                    new TreeSharp.Action(ctx => Logging.Write(System.Drawing.Color.Red,
                                                        "We have just done a mail run and bags are still full. Stopping the bot.")),
                                                    new TreeSharp.Action(ctx => TreeRoot.Stop())
                                                )
                                            ),
                                            new ActionClearPoi("Done mailing")
                                        )),
                                        // Buy — like Sell/Repair, require the MERCHANT frame (not just any
                                        // vendor/gossip frame) to be open + populated before buying. Without
                                        // this the bot ran BuyItems a tick after Interact, while only the
                                        // gossip frame was up (or the item list hadn't arrived), read an empty
                                        // merchant → "no food/water" → blacklisted good food vendors.
                                        new DecoratorIsPoiType(PoiType.Buy,
                                            new Decorator(ctx => MerchantFrame.Instance.IsVisible &&
                                                                 MerchantFrame.Instance.MerchantNumItems > 0,
                                            new Sequence(
                                            new ActionDebugString("Buying items"),
                                            new ActionSetActivity("Buying Items"),
                                            new TreeSharp.Action(ctx => Vendors.BuyItems()),
                                            new ActionClearPoi("Done buying")
                                        ))),
                                        // Train
                                        new DecoratorIsPoiType(PoiType.Train, new Sequence(
                                            new Wait(3, ctx => TrainerFrame.Instance.IsVisible, null),
                                            new ActionDebugString("Training Skills"),
                                            new ActionSetActivity("Training Skills"),
                                            new TreeSharp.Action(ctx => Vendors.TrainSkills()),
                                            new TreeSharp.Action(ctx => Lua.DoString("CloseTrainer()")),
                                            new ActionClearPoi("Done training")
                                        ))
                                    )
                                )
                            )
                        )
                    )
                ),
                // Check if need to sell
                new Decorator(
                    ctx => NeedToSell(),
                    new ActionSetPoi(ctx => new BotPoi(
                        ProfileManager.CurrentProfile.VendorManager.GetClosestVendor(Vendor.VendorType.Sell), 
                        PoiType.Sell))
                ),
                // Check if need to repair
                new Decorator(
                    ctx => NeedToRepair(),
                    new ActionSetPoi(ctx => new BotPoi(
                        ProfileManager.CurrentProfile.VendorManager.GetClosestVendor(Vendor.VendorType.Repair), 
                        PoiType.Repair))
                ),
                // Check if need to train
                new Decorator(
                    ctx => NeedToTrain(),
                    new ActionSetPoi(ctx => new BotPoi(
                        ProfileManager.CurrentProfile.VendorManager.GetClosestVendor(Vendor.VendorType.Train), 
                        PoiType.Train))
                ),
                // Check if need to buy
                new Decorator(
                    ctx => NeedToBuy(),
                    new ActionSetPoi(ctx => new BotPoi(
                        ProfileManager.CurrentProfile.VendorManager.GetClosestVendor(Vendor.VendorType.Food), 
                        PoiType.Buy))
                ),
                // Check flight paths
                new Decorator(
                    ctx => FlightPaths.Reason != FlightPathReason.None || 
                           FlightPaths.NeedFlightPath || 
                           FlightPaths.NeedNearbyUpdate(),
                    new TreeSharp.Action(ctx => FlightPaths.SetPoi())
                )
            );
        }

        private static bool NeedToSell()
        {
            if (StyxWoW.Me == null) return false;
            // HB 4.3.4 smethod_11 — no FindVendorsAutomatically check
            if (ProfileManager.CurrentProfile?.VendorManager?.GetClosestVendor(Vendor.VendorType.Sell) == null)
                return false;
            return Vendors.ForceSell || StyxWoW.Me.FreeNormalBagSlots <= ProfileManager.CurrentProfile.MinFreeBagSlots;
        }

        private static bool NeedToTrain()
        {
            // HB 4.3.4 smethod_12
            if (!CharacterSettings.Instance.TrainNewSkills && !Vendors.ForceTrainer)
                return false;
            if (ProfileManager.CurrentProfile?.VendorManager?.GetClosestVendor(Vendor.VendorType.Train) == null)
                return false;
            return Vendors.ForceTrainer || Vendors.NeedClassTraining;
        }

        private static bool NeedToRepair()
        {
            if (StyxWoW.Me == null) return false;
            // HB 4.3.4 smethod_13 — no FindVendorsAutomatically check
            if (Vendors.RepairDisabled)
                return false;
            if (ProfileManager.CurrentProfile?.VendorManager?.GetClosestVendor(Vendor.VendorType.Repair) == null)
                return false;

            // HB 4.3.4: update repair cost periodically
            if (_repairCostTimer.IsFinished)
            {
                var cost = StyxWoW.Me.GetEstimatedRepairCost();
                if (cost.TotalCoppers != 0L)
                {
                    Logging.WriteDebug("Updating repair cost for current equipped items. New value: [{0}]", cost);
                    _lastRepairCost = (ulong)cost.TotalCoppers;
                }
                _repairCostTimer.Reset();
            }

            if (StyxWoW.Me.Coinage <= _lastRepairCost)
            {
                if (Vendors.ForceRepair)
                {
                    Logging.Write(System.Drawing.Color.Red, "WARNING! You have no money to repair! Cancelling forced repair run.");
                    Vendors.ForceRepair = false;
                }
                return false;
            }

            return Vendors.ForceRepair || StyxWoW.Me.LowestDurabilityPercent <= ProfileManager.CurrentProfile.MinDurability;
        }

        /// <summary>
        /// HB 4.3.4 smethod_14 - Check if need to buy food/drink.
        /// Note: Unlike Sell/Repair, HB 4.3.4 does NOT check FindVendorsAutomatically for buying.
        /// The FoodAmount/DrinkAmount sliders are the explicit opt-in.
        /// </summary>
        private static bool NeedToBuy()
        {
            // HB 4.3.4: Minimum 1 gold required to buy
            if (StyxWoW.Me.Coinage < 10000)
            {
                if (Vendors.ForceBuy)
                {
                    Logging.Write(System.Drawing.Color.Red, "WARNING! You have no money to restock! Cancelling forced restock run.");
                    Vendors.ForceBuy = false;
                }
                return false;
            }

            if (Vendors.ForceBuy)
                return true;

            // HB 4.3.4: Check if food vendor exists (from profile or NPC database)
            var foodVendor = ProfileManager.CurrentProfile?.VendorManager?.GetClosestVendor(Vendor.VendorType.Food);
            if (foodVendor == null)
                return false;

            // HB 4.3.4: Check if need drink (mana users only)
            bool usesMana = StyxWoW.Me.PowerType == WoWPowerType.Mana || StyxWoW.Me.Class == WoWClass.Druid;
            if (usesMana && Consumable.GetBestDrink(false) == null && CharacterSettings.Instance.DrinkAmount > 0)
            {
                Logging.WriteDebug("[NeedToBuy] Need drink: DrinkAmount={0}, Vendor={1}", 
                    CharacterSettings.Instance.DrinkAmount, foodVendor.Name);
                return true;
            }
            
            // HB 4.3.4: Check if need food
            if (Consumable.GetBestFood(false) == null && CharacterSettings.Instance.FoodAmount > 0)
            {
                Logging.WriteDebug("[NeedToBuy] Need food: FoodAmount={0}, Vendor={1}", 
                    CharacterSettings.Instance.FoodAmount, foodVendor.Name);
                return true;
            }

            return false;
        }

        private static bool NeedToMail()
        {
            LocalPlayer me = StyxWoW.Me;
            Profile currentProfile = ProfileManager.CurrentProfile;

            if (string.IsNullOrEmpty(CharacterSettings.Instance.MailRecipient) ||
                currentProfile == null ||
                ProfileManager.CurrentProfile?.MailboxManager == null)
                return false;

            Mailbox closestMailbox = ProfileManager.CurrentProfile.MailboxManager.GetClosestMailbox();
            if (closestMailbox == null)
                return false;

            return me.Level >= currentProfile.MinMailLevel &&
                   (closestMailbox.Location.Distance(me.Location) < 200.0 || StyxWoW.Me.FreeBagSlots < 30);
        }

        private static bool IsVendorFrameOpen()
        {
            return MerchantFrame.Instance.IsVisible ||
                   GossipFrame.Instance.IsVisible ||
                   MailFrame.Instance.IsVisible ||
                   TrainerFrame.Instance.IsVisible ||
                   TaxiFrame.Instance.IsVisible;
        }

        #endregion

        #region Roam Behavior

        /// <summary>
        /// HB 4.3.4 CreateRoamBehavior - handles movement between hotspots
        /// </summary>
        public static PrioritySelector CreateRoamBehavior()
        {
            return new PrioritySelector(
                // Find target if not looting/killing/vendoring
                // HB 6.2.3 fix: also exclude Sell/Repair/Train/Buy/Mail to prevent
                // pulling mobs during vendor runs (overwrites Sell POI with Kill)
                    new DecoratorIsNotPoiType(new[] { PoiType.Kill, PoiType.Loot, PoiType.Skin, PoiType.Harvest,
                        PoiType.Sell, PoiType.Repair, PoiType.Train, PoiType.Buy, PoiType.Mail },
                    new DecoratorNeedToFindTarget(new Sequence(
                        new TreeSharp.Action(ctx =>
                    {
                        // HB 4.3.4 smethod_113 — no dead check, trusts Targeting pulse
                        Targeting.Instance.FirstUnit.Target();
                    }),
                        new Wait(5, ctx => StyxWoW.Me.GotTarget, new ActionIdle()),
                        // HB 4.3.4 smethod_115 — always Kill POI, no dead/loot logic
                        new ActionSetPoi(ctx => new BotPoi(StyxWoW.Me.CurrentTarget, PoiType.Kill))
                    ))
                ),
                // Move to hotspot if needed
                new DecoratorIsNotPoiType(PoiType.Kill, new Decorator(
                    ctx => ShouldMoveToHotspot(),
                    new TreeSharp.Action(ctx =>
                    {
                        GrindArea grindArea = StyxWoW.AreaManager?.CurrentGrindArea;
                        if (grindArea == null)
                            return RunStatus.Failure;

                        Hotspot currentHotSpot = grindArea.CurrentHotSpot;
                        WoWPoint hotspot = currentHotSpot.Position;
                        if (Mount.ShouldMount(hotspot))
                            Mount.MountUp(() => hotspot);

                        TreeRoot.StatusText = "Moving to hotspot";
                        return Navigator.GetRunStatusFromMoveResult(Navigator.MoveTo(hotspot));
                    })
                )),
                // Move closer to target or clear POI if better target
                new PrioritySelector(
                    new Decorator(
                        ctx => RoutineManager.Current?.MoveToTargetBehavior != null,
                        RoutineManager.Current?.MoveToTargetBehavior
                    ),
                    new Decorator(
                        ctx => ShouldMoveCloserToTarget(),
                        new ActionMoveToTarget()
                    ),
                    new Decorator(
                        ctx => ShouldClearPoiForBetterTarget(),
                        new ActionClearPoi("NeedToClearPOI is true #2")
                    )
                )
            );
        }

        private static bool ShouldClearPoiForBetterTarget()
        {
            // HB 4.3.4 smethod_8 — no dead check
            WoWUnit firstUnit = Targeting.Instance.FirstUnit;
            WoWUnit currentTarget = StyxWoW.Me.CurrentTarget;

            if (BotPoi.Current.Type == PoiType.Kill &&
                firstUnit != null &&
                firstUnit.Distance < Targeting.PullDistance &&
                currentTarget == null)
                return true;

            if (currentTarget != null && firstUnit != null)
                return currentTarget.Guid != firstUnit.Guid;

            return false;
        }

        private static bool ShouldMoveToHotspot()
        {
            GrindArea grindArea = StyxWoW.AreaManager?.CurrentGrindArea;
            if (grindArea == null)
            {
                Logging.WriteDebug("StyxWoW.AreaManager.CurrentGrindArea is null");
                return false;
            }
            return grindArea.HotspotChanged;
        }

        private static bool ShouldMoveCloserToTarget()
        {
            WoWUnit firstUnit = Targeting.Instance.FirstUnit;
            if (firstUnit == null)
                return false;

            return firstUnit.DistanceSqr >= Targeting.PullDistance * Targeting.PullDistance ||
                   !firstUnit.InLineOfSpellSight;
        }

        #endregion

        #region Target Filters

        /// <summary>
        /// HB 4.3.4 LevelbotIncludeLootsFilter - filters loot targets
        /// </summary>
        public static void LevelbotIncludeLootsFilter(List<WoWObject> incomingObjects, HashSet<WoWObject> outgoingObjects)
        {
            for (int i = 0; i < incomingObjects.Count; i++)
            {
                if (incomingObjects[i] is WoWUnit unit)
                {
                    if (LootTargeting.LootMobs &&
                        unit.Distance <= LootTargeting.LootRadius &&
                        unit.Dead &&
                        !Blacklist.Contains(unit.Guid) &&
                        (unit.KilledByMe && unit.CanLoot ||
                         unit.CanSkin && LootTargeting.SkinMobs && (CharacterSettings.Instance.NinjaSkin || unit.KilledByMe)))
                    {
                        outgoingObjects.Add(unit);
                    }
                }
                else if (incomingObjects[i] is WoWGameObject gameObj)
                {
                    WoWPoint location = StyxWoW.Me.Location;
                    if (gameObj.Distance <= LootTargeting.LootRadius &&
                        (gameObj.IsHerb && LootTargeting.HarvestHerbs ||
                         gameObj.IsMineral && LootTargeting.HarvestMinerals ||
                         gameObj.IsChest && LootTargeting.LootChests) &&
                        gameObj.CanLoot &&
                        !Blacklist.Contains(gameObj.Guid))
                    {
                        if (IsTooNearBlackspot(ProfileManager.CurrentProfile?.Blackspots, gameObj.Location))
                            Blacklist.Add(gameObj.Guid, TimeSpan.FromDays(3.0));
                        else
                            outgoingObjects.Add(gameObj);
                    }
                }
            }
        }

        /// <summary>
        /// HB 4.3.4 LevelBotIncludeTargetsFilter - filters combat targets by faction
        /// </summary>
        public static void LevelBotIncludeTargetsFilter(List<WoWObject> incomingUnits, HashSet<WoWObject> outgoingUnits)
        {
            Profile currentProfile = ProfileManager.CurrentProfile;
            if (currentProfile == null || StyxWoW.Me.Combat)
                return;

            // HB 6.2.3: Don't add faction targets when traveling to vendor/trainer/mail
            // Only aggro mobs (handled by DefaultIncludeTargetsFilter) should be included
            PoiType poiType = BotPoi.Current.Type;
            bool isVendorRun = poiType == PoiType.Sell || poiType == PoiType.Repair ||
                               poiType == PoiType.Train || poiType == PoiType.Buy ||
                               poiType == PoiType.Mail;
            if (isVendorRun)
                return;

            HashSet<uint> validFactions = new HashSet<uint>();
            GrindArea grindArea = StyxWoW.AreaManager?.CurrentGrindArea;

            // Grind-area level band. Default is [0, int.MaxValue] (no limit) so HB profiles that don't set
            // it are unaffected; VibeGrinder sets it, so we don't kill grossly under/over-level mobs of the
            // same faction (e.g. gray level 5-6 Prairie Wolves at a level-9 beast spot — faction matched but
            // way below band). Previously targeting was faction-only and ignored level entirely.
            int bandMin = grindArea?.TargetMinLevel ?? 0;
            int bandMax = grindArea?.TargetMaxLevel ?? int.MaxValue;

            if (grindArea != null && grindArea.Factions.Count > 0)
            {
                foreach (uint faction in grindArea.Factions)
                    validFactions.Add(faction);
            }
            else
            {
                foreach (uint faction in currentProfile.Factions)
                    validFactions.Add(faction);
            }

            foreach (WoWObject obj in incomingUnits)
            {
                if (obj is WoWUnit unit && !(obj is WoWPlayer))
                {
                    if (!currentProfile.AvoidMobs.Contains(unit.Entry) &&
                        !IsTooNearBlackspot(currentProfile.Blackspots, unit.Location) &&
                        validFactions.Contains(unit.FactionId) &&
                        unit.Level >= bandMin && unit.Level <= bandMax)
                    {
                        outgoingUnits.Add(obj);
                    }
                }
            }
        }

        /// <summary>
        /// HB 4.3.4 IsTooNearBlackspot - checks if point is within any blackspot
        /// </summary>
        public static bool IsTooNearBlackspot(IEnumerable<Blackspot> blackspots, WoWPoint point)
        {
            if (blackspots == null)
                return false;
            return blackspots.Any(b => point.Distance2D(b.Location) < b.Radius);
        }

        #endregion

        #region Helpers

        private static void SleepForLag()
        {
            // Sleep for estimated latency
            StyxWoW.Sleep(100 + (int)(StyxWoW.WoWClient?.Latency ?? 100));
        }

        /// <summary>
        /// VibeGrinder: clean slate for Stop→Start within one app session. Resets the static
        /// loot/death/corpse/repair state these reused subtrees carry, so a restart behaves like
        /// a cold boot. Loot events stay attached (guarded by _lootEventsAttached) to avoid
        /// double-subscribing.
        /// </summary>
        internal static void ResetState()
        {
            _lastLootPoiType = default;
            _lastLootGuid = 0;
            _lootAttemptCount = 0;
            _lootFailCount = 0;
            _deathCount = 0;
            _deathTimer.Reset();
            _corpseWaitStopwatch.Reset();
            _diedIndoors = false;
            _releaseTimer.Reset();
            _repairCostTimer.Reset();
            _lastRepairCost = 0;
            ShouldUseSpiritHealer = false;
        }

        /// <summary>
        /// HB 4.3.4 SetDefaultQueryFilter — Resets the mesh navigator query filter.
        /// Called when navigation parameters need to be restored to defaults.
        /// </summary>
        public static void SetDefaultQueryFilter()
        {
            if (Navigator.IsNavigatorLoaded)
            {
                Navigator.TripperNavigator.ResetQueryFilter();
            }
        }

        #endregion
    }
}
