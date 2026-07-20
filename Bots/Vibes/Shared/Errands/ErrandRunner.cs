using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bots.Vibes.Shared.GrindData;
using CommonBehaviors.Actions;
using Styx;
using Styx.Logic;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.MailBox;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.Logic.Inventory.Frames.Trainer;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;
using Styx.Logic.Inventory.Frames.Taxi;
using Styx.Logic.BehaviorTree;
using Styx.Helpers;
using Styx.Combat.CombatRoutine;

namespace Bots.Vibes.Shared.Errands
{
    /// <summary>
    /// Owns an errand trip end to end: what we owe, where that gets cleared, the route between, and the
    /// conditions that call the whole thing off. It replaces the arrangement where the deciding chain and
    /// the servicing subtree were SIBLINGS in one selector — any tick servicing returned Failure the tick
    /// fell through to the decide chain, which re-resolved a vendor from the player's current position,
    /// so a "trip" was only ever whatever POI survived the tick.
    ///
    /// The commitment here is the doctrine's lookahead kind, not the banned latch kind: it stores the
    /// current CHOICE, re-validates it against its cancellers every tick, and self-releases the moment
    /// the choice stops being valid. It stores no memory of what went wrong.
    ///
    /// Ownership boundary: this decides and services Sell/Repair/Buy/Ammo/Train/Mail. It only SERVICES
    /// a Fly POI — flight learning and flight travel decide those, and they are not demand-derived.
    /// </summary>
    public sealed class ErrandRunner
    {
        private readonly FactionResolver _factions;
        private readonly string _tag;

        /// <summary>Called at the COMMIT edge with the destination, for trek-hazard marking and for
        /// releasing the grind commitment (the give-up clock must be stopped by not having one).</summary>
        public System.Action<WoWPoint> OnCommit;
        /// <summary>Called when the trip ends, however it ends.</summary>
        public System.Action OnEnd;

        private List<ErrandStop> _tour;
        private int _at;
        private readonly HashSet<ErrandKind> _outstanding = new HashSet<ErrandKind>();
        private readonly Stopwatch _tripClock = new Stopwatch();

        // Cost throttles. Both apply whether the last answer was yes or no — they bound work, they do
        // not remember a verdict. Planning touches SQLite; the demand scan reads durability and bags.
        private static readonly TimeSpan PlanEvery = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DemandEvery = TimeSpan.FromSeconds(1);
        private DateTime _planAt = DateTime.MinValue;
        private DateTime _demandAt = DateTime.MinValue;

        // Interacting faster than the server answers achieves nothing. A cadence, not an attempt tally:
        // nothing reads how many times it elapsed.
        private static readonly TimeSpan InteractEvery = TimeSpan.FromSeconds(2);
        private DateTime _interactAt = DateTime.MinValue;

        private int _screenedEntry = -1;   // stop already passed the location screen (re-screened per DISTINCT stop)

        // A trip that aborts twice on the same stop has a problem with the STOP, not with retrying:
        // change target rather than re-walk the same wall all night. Survives the trip's own teardown
        // so it can count consecutive failures; a completed trip proves the target works and clears it.
        private int _abortEntry = -1;
        private int _abortStreak;

        public ErrandRunner(FactionResolver factions, string tag)
        {
            _factions = factions;
            _tag = tag;
            Reset();   // ErrandDemands' census is static; a fresh run must not inherit the last one's polls
        }

        /// <summary>True while an errand owns the bot — the trip, or a Fly POI someone else set.</summary>
        public bool Active => _tour != null || BotPoi.Current.Type == PoiType.Fly;

        /// <summary>The errand currently being served, for logs.</summary>
        public ErrandStop CurrentStop => _tour != null && _at < _tour.Count ? _tour[_at] : null;

        public void Reset()
        {
            _tour = null;
            _at = 0;
            _outstanding.Clear();
            _tripClock.Reset();
            _screenedEntry = -1;
            _abortEntry = -1;
            _abortStreak = 0;
            _planAt = DateTime.MinValue;
            _demandAt = DateTime.MinValue;
            ErrandDemands.Reset();
        }

        /// <summary>
        /// The branch gate: true while an errand owns the tick. Runs the whole trip lifecycle —
        /// cancellers first, then completion, then (off-trip) the decision to start one.
        /// </summary>
        public bool Update()
        {
            LocalPlayer me = StyxWoW.Me;
            if (me == null) return false;

            // Dying is a canceller, not a pause: the corpse run owns the bot from here, and a trip left
            // standing would keep rest and looting suppressed for the whole ghost walk and beyond.
            if (me.IsDead)
            {
                Cancel();
                return false;
            }

            if (_tour != null)
                return ValidateTrip(me);

            // A Fly POI is another owner's decision; service it and do not plan over it.
            if (BotPoi.Current.Type == PoiType.Fly)
                return true;

            if (DateTime.UtcNow < _planAt) return false;
            _planAt = DateTime.UtcNow.Add(PlanEvery);

            List<ErrandDemand> demands = ErrandDemands.Scan();
            ErrandDemands.WarnIfStuckWithFullBags(demands);

            // Flight learning / taxi travel want a Fly POI when nothing else is going on. Carried from
            // the tail of LevelBot's vendor chain, which is what GrindSupervisor's flight learning hands
            // off to; without it the learn detour walks up to a master and never records anything.
            if (demands.Count == 0)
            {
                if (FlightPaths.Reason != FlightPathReason.None || FlightPaths.NeedFlightPath || FlightPaths.NeedNearbyUpdate())
                {
                    FlightPaths.SetPoi();
                    return BotPoi.Current.Type == PoiType.Fly;
                }
                return false;
            }

            List<ErrandStop> tour = ErrandPlanner.Plan(me.Location, demands);
            tour = Screen(tour);
            if (tour.Count == 0)
            {
                // Loud, and it repeats: a demand nothing can serve means the night is degrading.
                Logging.Write(System.Drawing.Color.Orange,
                    "[{0}/Errand] {1} outstanding but no reachable, safe place to clear it — carrying on grinding.",
                    _tag, string.Join(", ", demands.Select(d => d.ToString())));
                return false;
            }

            Commit(tour, demands);
            return true;
        }

        private void Commit(List<ErrandStop> tour, List<ErrandDemand> demands)
        {
            _tour = tour;
            _at = 0;
            _screenedEntry = -1;
            _outstanding.Clear();
            foreach (ErrandDemand d in demands) _outstanding.Add(d.Kind);
            _tripClock.Restart();
            _demandAt = DateTime.UtcNow.Add(DemandEvery);

            Logging.Write(System.Drawing.Color.Khaki,
                "[{0}/Errand] COMMIT — {1}; tour: {2}. Grind/rest/roam suspended until done.",
                _tag,
                string.Join(", ", demands.Select(d => d.Kind + " (" + d.Why + ")")),
                string.Join(" → ", tour.Select(s => s.ToString())));

            AssertPoi();
            OnCommit?.Invoke(tour[0].Location);
        }

        /// <summary>
        /// Every canceller, every tick. A commitment that is not re-validated is the one thing the
        /// doctrine's carve-out forbids. GeneratePath sees neither a transaction the server refuses nor
        /// a world wedge, which is why the trip also carries a clock.
        /// </summary>
        private bool ValidateTrip(LocalPlayer me)
        {
            var tuning = VibeTuning.Current;

            // Airborne on a taxi: the flight latch owns the tick and a long hop is legitimate.
            if (me.OnTaxi) return true;

            if (_tripClock.Elapsed.TotalSeconds > tuning.VendorRunAbortSeconds)
            {
                Abort(string.Format("didn't complete in {0}s", tuning.VendorRunAbortSeconds));
                return false;
            }

            // Demand is the trip's reason to exist. Re-derived, so a demand that resolved itself (or one
            // that appeared during a peel fight en route) is picked up by the stop we are already at.
            if (DateTime.UtcNow >= _demandAt)
            {
                _demandAt = DateTime.UtcNow.Add(DemandEvery);
                _outstanding.Clear();
                foreach (ErrandDemand d in ErrandDemands.Scan()) _outstanding.Add(d.Kind);
            }

            // Skip stops whose business is already done.
            while (_at < _tour.Count && !_tour[_at].Serves.Overlaps(_outstanding))
                Advance();

            if (_at >= _tour.Count)
            {
                if (RePlanRemainder(me)) return true;
                Complete();
                return false;
            }

            // The mailbox service withdraws a mailbox it finds a live hostile standing at (reputation
            // guards and roamers the static DB cannot see). It clears the POI to do it; without this the
            // trip would simply re-assert the same point next tick and walk back into the guard.
            if (_tour[_at].IsMailbox && !MailboxStillOffered(_tour[_at]))
            {
                DropStop(_tour[_at], "the mailbox was withdrawn as unsafe");
                return _tour != null;
            }

            // Re-screen every DISTINCT stop, mid-trip swaps included: the initial pick is not the only
            // one that can sit in enemy territory or behind a canyon (Razbo → Zulrg).
            ErrandStop stop = _tour[_at];
            if (stop.Entry != _screenedEntry)
            {
                if (!ScreenStop(stop, me))
                {
                    DropStop(stop, "failed the location screen");
                    return _tour != null;
                }
                _screenedEntry = stop.Entry;
            }

            // Repair is the one demand whose satisfiability can change while we walk (we spend coin).
            if (stop.Serves.Contains(ErrandKind.Repair) && _outstanding.Contains(ErrandKind.Repair)
                && ErrandDemands.RepairCost > 0 && me.Coinage <= ErrandDemands.RepairCost)
            {
                _outstanding.Remove(ErrandKind.Repair);
                Logging.Write(System.Drawing.Color.Orange,
                    "[{0}/Errand] can no longer afford the repair ({1}c, have {2}c) — dropping it from this trip.",
                    _tag, ErrandDemands.RepairCost, me.Coinage);
            }

            return true;
        }

        private static bool MailboxStillOffered(ErrandStop stop)
        {
            Mailbox mb = ProfileManager.CurrentProfile?.MailboxManager?.GetClosestMailbox(stop.Location);
            return mb != null && mb.Location.DistanceSqr(stop.Location) < 1f;
        }

        /// <summary>A stop that fails removes itself and the tour re-plans its REMAINDER — it never
        /// restarts the trip, which is how a bot ends up re-walking legs it already completed.</summary>
        private void DropStop(ErrandStop stop, string why)
        {
            Logging.Write(System.Drawing.Color.Orange, "[{0}/Errand] dropping {1} — {2}.", _tag, stop, why);
            _tour.Remove(stop);
            if (_at > _tour.Count) _at = _tour.Count;
            _screenedEntry = -1;
            BotPoi.Clear("errand stop dropped");

            LocalPlayer me = StyxWoW.Me;
            if (me != null && _at >= _tour.Count && !RePlanRemainder(me))
                Complete();
        }

        private void Advance()
        {
            _at++;
            _screenedEntry = -1;
        }

        /// <summary>
        /// Outstanding demands with no stop left to serve them: plan a fresh tail from where we stand.
        /// True when there is more to do. Bounded by the trip clock, and by candidates running out —
        /// every failure path above blacklists, so the candidate set shrinks monotonically.
        /// </summary>
        private bool RePlanRemainder(LocalPlayer me)
        {
            if (_outstanding.Count == 0) return false;

            var demands = ErrandDemands.Scan();
            _outstanding.Clear();
            foreach (ErrandDemand d in demands) _outstanding.Add(d.Kind);
            if (demands.Count == 0) return false;

            List<ErrandStop> tail = Screen(ErrandPlanner.Plan(me.Location, demands));
            if (tail.Count == 0) return false;

            _tour = tail;
            _at = 0;
            _screenedEntry = -1;
            Logging.Write(System.Drawing.Color.Khaki, "[{0}/Errand] re-planned the remainder: {1}.",
                _tag, string.Join(" → ", tail.Select(s => s.ToString())));
            AssertPoi();
            return true;
        }

        private void Complete()
        {
            Logging.Write(System.Drawing.Color.Khaki, "[{0}/Errand] DONE — trip complete in {1:F0}s; resuming grind.",
                _tag, _tripClock.Elapsed.TotalSeconds);
            _abortEntry = -1;
            _abortStreak = 0;   // a completed trip proves its targets work
            End();
        }

        private void Abort(string why)
        {
            ErrandStop stuck = CurrentStop;
            Logging.Write(System.Drawing.Color.Orange, "[{0}/Errand] ABORT — {1} (stuck at {2}); resuming grind.",
                _tag, why, stuck == null ? "no stop" : stuck.ToString());

            if (stuck != null)
            {
                _abortStreak = stuck.Entry == _abortEntry ? _abortStreak + 1 : 1;
                _abortEntry = stuck.Entry;
                if (_abortStreak >= 2)
                {
                    _abortStreak = 0;
                    // Twice is about the PLACE (unreachable, wedged approach), not about one errand.
                    BlacklistStop(stuck, "aborted twice", Styx.Logic.Profiles.Vendor.VendorType.Unknown);
                }
            }
            // "Resuming grind" is a lie while the POI still points at the target we just abandoned —
            // it would be re-adopted on the very next tick.
            BotPoi.Clear("errand aborted");
            End();
        }

        private void End()
        {
            _tour = null;
            _at = 0;
            _outstanding.Clear();
            _tripClock.Reset();
            _screenedEntry = -1;
            _planAt = DateTime.UtcNow.Add(PlanEvery);
            OnEnd?.Invoke();
        }

        /// <summary>Force-escape / activity teardown: drop the trip without ceremony.</summary>
        public void Cancel()
        {
            if (_tour == null) return;
            Logging.Write(System.Drawing.Color.Orange, "[{0}/Errand] trip cancelled.", _tag);
            BotPoi.Clear("errand cancelled");
            End();
        }

        /// <summary>
        /// Records a refusal against a stop. <paramref name="purpose"/> is Unknown for anything wrong
        /// with the PLACE (unreachable, enemy territory) — that is true of every errand there — and the
        /// specific errand when only that one failed. A forged purpose makes the record and the log lie
        /// about what went wrong, and bans a working vendor from the wrong list.
        /// </summary>
        private void BlacklistStop(ErrandStop stop, string why, Styx.Logic.Profiles.Vendor.VendorType purpose)
        {
            Profile profile = ProfileManager.CurrentProfile;
            if (stop.IsMailbox)
            {
                if (profile?.MailboxManager == null) return;
                profile.MailboxManager.Blacklist.Add(new Mailbox(stop.Location));
                Logging.Write(System.Drawing.Color.Orange,
                    "[{0}/Errand] mailbox at {1} {2} — blacklisting it for this session; the planner picks another.",
                    _tag, stop.Location, why);
                return;
            }
            if (profile?.VendorManager == null) return;
            profile.VendorManager.Blacklist.Add(
                new Styx.Logic.Profiles.Vendor(stop.Entry, stop.Name, purpose, stop.Location), purpose);
            Logging.Write(System.Drawing.Color.Orange,
                "[{0}/Errand] {1} {2} — blacklisting it for {3}; the planner picks another.",
                _tag, stop.Name, why,
                purpose == Styx.Logic.Profiles.Vendor.VendorType.Unknown ? "every errand" : purpose + " runs");
        }

        // ---- Screening: reachable, and somewhere we can actually survive shopping ----

        private List<ErrandStop> Screen(List<ErrandStop> tour)
        {
            LocalPlayer me = StyxWoW.Me;
            var kept = new List<ErrandStop>();
            if (me == null) return kept;
            foreach (ErrandStop s in tour)
                if (ScreenStop(s, me)) kept.Add(s);
            return kept;
        }

        /// <summary>
        /// Vendor resolution is distance-and-faction based over the whole CONTINENT, so it sends us into
        /// trouble three ways, all visible before we walk: a knot of player-hostile spawns around the NPC
        /// (enemy territory), surrounding wild mobs well above our level (the next zone up), and a
        /// straight line that is nothing like the walk (the Caverns-of-Time canyon). Distance itself is
        /// NOT a gate — a far same-level vendor is fine.
        /// </summary>
        private bool ScreenStop(ErrandStop stop, LocalPlayer me)
        {
            var s = VibeTuning.Current;
            if (_factions == null) return true;   // no faction data yet → fail-safe, don't block the errand

            if (!stop.IsMailbox)
            {
                if (s.VendorHostileThreshold > 0)
                {
                    int hostiles = GrindMobsRepository.HostileSpawnCountNear(me.MapId, stop.Location, s.VendorHostileRadius, _factions);
                    if (hostiles >= s.VendorHostileThreshold)
                        return Reject(stop, "{0} hostile spawns within {1:F0}yd (enemy territory)", hostiles, s.VendorHostileRadius);
                }

                if (s.VendorAreaLevelMargin > 0)
                {
                    float areaLevel = GrindMobsRepository.AverageAttackableLevelNear(
                        me.MapId, stop.Location, s.VendorAreaScanRadius, _factions, EngagementGovernor.ImmuneUnitFlagMask);
                    if (areaLevel > me.Level + s.VendorAreaLevelMargin)
                        return Reject(stop, "area avg level {0:F0} >> mine {1} (higher-level zone)", areaLevel, me.Level);
                }
            }

            // Reachability is asked ONCE per stop, at the moment we commit to walking to it — the
            // lookahead the doctrine asks for, without paying a pathfind per tick for an answer that
            // cannot change while we walk a static mesh.
            float straight = (float)me.Location.Distance(stop.Location);
            if (straight > 50f)
            {
                WoWPoint[] path = Navigator.GeneratePath(me.Location, stop.Location);
                if (path == null || path.Length == 0)
                    return Reject(stop, "no nav path to it");
                float shortfall = (float)path[path.Length - 1].Distance(stop.Location);
                if (shortfall > 25f)
                    return Reject(stop, "partial path (ends {0:F0}yd short)", shortfall);
                if (s.VendorDetourFactor > 0)
                {
                    float walk = 0f;
                    for (int i = 1; i < path.Length; i++)
                        walk += (float)path[i - 1].Distance(path[i]);
                    if (walk > Math.Max(s.VendorDetourMinYd, straight * s.VendorDetourFactor))
                        return Reject(stop, "walk {0:F0}yd vs {1:F0}yd straight (canyon/cave detour)", walk, straight);
                }
            }
            return true;
        }

        private bool Reject(ErrandStop stop, string fmt, params object[] args)
        {
            Logging.Write(System.Drawing.Color.Orange, "[{0}/Errand] SKIP {1} — {2}; blacklisting, re-routing.",
                _tag, stop, string.Format(fmt, args));
            BlacklistStop(stop, "was rejected", Styx.Logic.Profiles.Vendor.VendorType.Unknown);
            return false;
        }

        // ---- Servicing ----

        /// <summary>
        /// Travel and transact. Sits BELOW combat and the transit peel in the errand branch, so a peel
        /// fight owns the tick and this re-asserts its POI when the fight ends.
        /// </summary>
        public Composite Behavior()
        {
            return new PrioritySelector(
                // A Fly POI is serviced, never planned — see the class doc.
                new Decorator(ctx => BotPoi.Current.Type == PoiType.Fly,
                    new PrioritySelector(
                        new Decorator(ctx => BotPoi.Current.Location.Distance(StyxWoW.Me.Location) > 5.0,
                            new ActionMoveToPoi()),
                        new Action(ctx => InteractTick(null)))),
                new Decorator(ctx => _tour != null && _at < _tour.Count,
                    new PrioritySelector(
                        new Action(ctx => AssertPoi()),
                        new Decorator(ctx => BotPoi.Current.Location.Distance(StyxWoW.Me.Location) > 5.0,
                            new ActionMoveToPoi()),
                        new Action(ctx => ServeStop()))));
        }

        /// <summary>Keeps the POI pointed at our stop and returns Failure so the branch continues. The
        /// trip owns the slot; combat and looting borrow it and this takes it back.</summary>
        private RunStatus AssertPoi()
        {
            ErrandStop stop = CurrentStop;
            if (stop == null) return RunStatus.Failure;
            ErrandKind kind = stop.PrimaryKind(_outstanding);
            BotPoi poi = BotPoi.Current;
            if (poi.Type != ErrandKinds.Poi(kind) || poi.Location.Distance(stop.Location) > 5.0)
                BotPoi.Current = stop.ToPoi(kind);
            return RunStatus.Failure;
        }

        private RunStatus ServeStop()
        {
            ErrandStop stop = CurrentStop;
            LocalPlayer me = StyxWoW.Me;
            if (stop == null || me == null) return RunStatus.Failure;

            // A frame is open: clear EVERY demand this stop can serve while we are standing here. That
            // coordination is the free win of the flat merchant branch this replaces — sell and repair
            // against ONE open frame, one approach, one interact.
            if (Transact(stop)) return RunStatus.Running;

            if (stop.IsMailbox)
            {
                WoWGameObject box = stop.LiveMailbox();
                if (box == null)
                {
                    DropStop(stop, "no mailbox object at the recorded point");
                    return RunStatus.Running;
                }
                return InteractTick(box);
            }

            WoWUnit npc = stop.LiveUnit();
            if (npc == null)
            {
                DropStop(stop, "the NPC isn't here");
                return RunStatus.Running;
            }

            // Capability re-validated from the live unit — the server's own answer, which outranks the
            // data.bin flag that planned the trip. A kind it cannot serve is removed here rather than
            // re-attempted; there is then nothing to remember about the attempt.
            if (!ConfirmCapability(stop, npc)) return RunStatus.Running;

            return InteractTick(npc);
        }

        /// <summary>Drops any kind the live NPC does not actually serve, dropping the whole stop if
        /// nothing is left. False when the stop is gone and the caller must not go on using it.</summary>
        private bool ConfirmCapability(ErrandStop stop, WoWUnit npc)
        {
            var lost = new List<ErrandKind>();
            foreach (ErrandKind kind in stop.Serves)
            {
                if (!_outstanding.Contains(kind)) continue;
                bool can = kind switch
                {
                    ErrandKind.Repair => npc.IsRepairMerchant,
                    ErrandKind.Train => npc.IsTrainer,
                    _ => npc.IsVendor
                };
                if (!can) lost.Add(kind);
            }
            foreach (ErrandKind kind in lost)
            {
                stop.Serves.Remove(kind);
                Logging.Write(System.Drawing.Color.Orange,
                    "[{0}/Errand] {1} does not serve {2} (the NPC's own flags say so) — dropping that errand from this stop.",
                    _tag, stop.Name, kind);
                ProfileManager.CurrentProfile?.VendorManager?.Blacklist.AddPermanent(
                    new Styx.Logic.Profiles.Vendor(stop.Entry, stop.Name, ErrandKinds.Vendor(kind), stop.Location),
                    ErrandKinds.Vendor(kind));
            }
            if (stop.Serves.Overlaps(_outstanding)) return true;
            DropStop(stop, "serves none of what we came for");
            return false;
        }

        /// <summary>
        /// One interact per cadence, then read what came back next tick. A gossip window that presents no
        /// option of the kind we need is the server saying this NPC does not do that — act on what it
        /// presents rather than counting how often it refused.
        /// </summary>
        private RunStatus InteractTick(WoWObject target)
        {
            if (GossipFrame.Instance.IsVisible && !ActionFrameOpen())
            {
                GossipEntry.GossipEntryType want = BotPoi.Current.Type.GetGossipType();
                if (want != GossipEntry.GossipEntryType.Unknown)
                {
                    var entries = GossipFrame.Instance.GossipOptionEntries;
                    int idx = -1;
                    if (entries != null)
                        foreach (GossipEntry e in entries)
                            if (e.Type == want) { idx = e.Index; break; }

                    if (idx >= 0)
                    {
                        GossipFrame.Instance.SelectGossipOption(idx);
                        return RunStatus.Running;
                    }
                    ErrandStop stop = CurrentStop;
                    if (stop != null)
                    {
                        // The presented list is authoritative and it does not offer this.
                        GossipFrame.Instance.Close();
                        DropStop(stop, "its gossip offers no " + want + " option");
                        return RunStatus.Running;
                    }
                }
                GossipFrame.Instance.Close();
                return RunStatus.Running;
            }

            if (DateTime.UtcNow < _interactAt) return RunStatus.Running;
            _interactAt = DateTime.UtcNow.Add(InteractEvery);

            WoWObject obj = target ?? BotPoi.Current.AsObject;
            if (obj == null) return RunStatus.Failure;
            Navigator.PlayerMover.MoveStop();
            obj.Interact();
            return RunStatus.Running;
        }

        private static bool ActionFrameOpen()
            => MerchantFrame.Instance.IsVisible || MailFrame.Instance.IsVisible
               || TrainerFrame.Instance.IsVisible || TaxiFrame.Instance.IsVisible;

        /// <summary>
        /// Every transaction this stop can serve, against the frame that is already open. True when
        /// something was done this tick.
        /// </summary>
        private bool Transact(ErrandStop stop)
        {
            LocalPlayer me = StyxWoW.Me;
            bool did = false;

            if (MerchantFrame.Instance.IsVisible)
            {
                if (Wanted(stop, ErrandKind.Sell))
                {
                    TreeRoot.StatusText = "Selling items";
                    Vendors.SellAllItems();
                    _outstanding.Remove(ErrandKind.Sell);
                    did = true;
                }
                if (Wanted(stop, ErrandKind.Repair))
                {
                    // The frame is open, so this cost is the real one — outside it the estimate reads 0,
                    // which is how a broke character used to repair-loop forever.
                    var cost = me.GetEstimatedRepairCost();
                    ulong price = cost.TotalCoppers > 0 ? (ulong)cost.TotalCoppers : ErrandDemands.RepairCost;
                    if (me.Coinage >= price)
                    {
                        TreeRoot.StatusText = "Repairing items";
                        Vendors.RepairAllItems();
                    }
                    else
                    {
                        Logging.Write(System.Drawing.Color.Orange,
                            "[{0}/Errand] can't afford the repair ({1}c, have {2}c) — grinding for money instead.",
                            _tag, price, me.Coinage);
                    }
                    _outstanding.Remove(ErrandKind.Repair);
                    did = true;
                }
                if (Wanted(stop, ErrandKind.Buy) || Wanted(stop, ErrandKind.Ammo))
                {
                    if (MerchantFrame.Instance.MerchantNumItems > 0)
                    {
                        TreeRoot.StatusText = "Buying items";
                        // BuyItems reads BotPoi.Current.AsVendor and skips the food branch for anything
                        // that isn't a Food/Restock vendor — so at a stop whose POI wears its Sell face,
                        // it would buy ammo and silently walk away without the food we came for.
                        BotPoi.Current = stop.ToPoi(Wanted(stop, ErrandKind.Buy) ? ErrandKind.Buy : ErrandKind.Ammo);
                        Vendors.BuyItems();
                        _outstanding.Remove(ErrandKind.Buy);
                        _outstanding.Remove(ErrandKind.Ammo);
                        did = true;
                    }
                    // An empty item list is the frame still filling in — next tick.
                }
                if (did)
                {
                    MerchantFrame.Instance.Close();
                    me.ClearTarget();
                }
            }

            if (TrainerFrame.Instance.IsVisible && Wanted(stop, ErrandKind.Train))
            {
                TreeRoot.StatusText = "Training skills";
                Vendors.TrainSkills();
                _outstanding.Remove(ErrandKind.Train);
                // CloseGossip too: a gossip-only "trainer" would otherwise leave gossip pinned open.
                Lua.DoString("CloseTrainer() CloseGossip()");
                did = true;
            }

            if (MailFrame.Instance.IsVisible && Wanted(stop, ErrandKind.Mail))
            {
                TreeRoot.StatusText = "Mailing items";
                Vendors.MailAllItems();
                _outstanding.Remove(ErrandKind.Mail);
                // Closed on purpose: an open MailFrame counts as "a vendor frame is open" everywhere,
                // so leaving it up makes the tree believe it is still transacting.
                MailFrame.Instance.Close();
                did = true;
            }

            // Deliberately does NOT advance: a stop that also trains is not finished because the merchant
            // half is. ValidateTrip advances past a stop once nothing outstanding is left for it.
            if (did) ErrandDemands.Invalidate();
            return did;
        }

        private bool Wanted(ErrandStop stop, ErrandKind kind)
            => stop.Serves.Contains(kind) && _outstanding.Contains(kind);
    }
}
