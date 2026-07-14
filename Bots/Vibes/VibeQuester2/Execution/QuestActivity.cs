using System;
using System.Collections.Generic;
using System.Linq;
using Bots.VibeGrinder.Selection;
using Bots.VibeGrinder.Synthesis;
using Bots.Vibes.Shared;
using Bots.Vibes.VibeQuester2.Planning;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using VibeQuester;
using Action = System.Action;

namespace Bots.Vibes.VibeQuester2.Execution
{
    /// <summary>
    /// The quest task executor — runs the current QuestPlan head-to-tail from v2's activity slot
    /// (below the shared vendor/rest/combat/loot shell, above the grind branches). One task at a
    /// time, commitment-latched: travel → interact / install an objective grind area → verify by
    /// quest-log state → advance. Failure paths are typed and bounded (NotOffered strikes,
    /// per-task stall clock, per-quest death count → TTL auto-blacklist + replan) — the executor
    /// never loops silently on a task the server refuses (the 2026-07-06 lesson).
    /// </summary>
    public class QuestActivity
    {
        private readonly QuestPlanner _planner;
        private readonly Func<GrindAreaSynthesizer> _synth;
        private readonly Func<FactionResolver> _factions;
        private readonly Func<QuestDatabase> _db;
        private readonly Action _onAreaReplaced;    // host invalidates its grind-spot bootstrap
        private readonly Action _requestReplan;     // host rescans at the next Pulse

        private QuestPlan _plan;
        private int _taskIndex;
        private DateTime _taskStartedAt;
        private DateTime _nextInteractAt = DateTime.MinValue;
        private int _notOfferedStrikes;
        private int _deathsOnCurrentQuest;
        private bool _objectiveAreaInstalled;
        private WoWPoint _trekMarkedFrom = WoWPoint.Empty;
        private int _useItemAttempts;
        private DateTime _nextUseItemAt = DateTime.MinValue;
        private bool _npcInteracted;
        private int _npcBefore;
        private DateTime _npcProvokeDeadline;
        private readonly Dictionary<string, DateTime> _goBlacklist = new Dictionary<string, DateTime>();   // key: rounded position

        private const float InteractRange = 4.5f;
        private const float GoInteractRange = 4.0f;
        private const int InteractRetryMs = 2000;
        private const int NotOfferedAbandonStrikes = 2;
        private const int MaxUseItemAttempts = 3;
        private const int UseItemPaceMs = 2500;
        private const float FriendlyNpcDetectRange = 100f;   // divert to a friendly objective NPC within this
        private const float NpcApproachRange = 2.75f;        // stop solidly INSIDE interact range, not on its edge
        private const float FollowRange = 6f;                // stay this close while the NPC walks its lure/escort script
        private const int ProvokeWaitSeconds = 45;           // a scripted talk/provoke can run many seconds
        private const int ProvokeReInteractMs = 1500;        // re-open FAST — multi-step gossips advance one line per interact
        private const int FollowExtendSeconds = 20;          // keep extending patience while the NPC is actively moving
        private const float ObjectiveAreaRadius = 250f;   // spawns near the task anchor that form the work area
        private const int GoBlacklistMinutes = 3;

        public QuestActivity(QuestPlanner planner, Func<GrindAreaSynthesizer> synth, Func<FactionResolver> factions,
                             Func<QuestDatabase> db, Action onAreaReplaced, Action requestReplan)
        {
            _planner = planner;
            _synth = synth;
            _factions = factions;
            _db = db;
            _onAreaReplaced = onAreaReplaced;
            _requestReplan = requestReplan;
        }

        public bool HasWork => _plan != null && _taskIndex < _plan.Tasks.Count;

        public QuestTask CurrentTask => HasWork ? _plan.Tasks[_taskIndex] : null;

        /// <summary>Adopt a fresh plan — called by the host at task boundaries only (never mid-task).</summary>
        public void AdoptPlan(QuestPlan plan)
        {
            _plan = plan;
            _taskIndex = 0;
            StartTask("plan adopted");
        }

        /// <summary>Safe to swap plans? (no in-flight task, or the current task just ended)</summary>
        public bool AtBoundary => !HasWork;

        /// <summary>Host forwards OnPlayerDied — deaths count against the CURRENT task's quest.</summary>
        public void NotifyDeath()
        {
            var t = CurrentTask;
            if (t == null) return;
            _deathsOnCurrentQuest++;
            if (_deathsOnCurrentQuest >= VibeQuester2Settings.Instance.DeathBlacklistThreshold)
            {
                Logging.Write("[VQ2-Task] {0} deaths on q{1} '{2}' — abandoning it.",
                    _deathsOnCurrentQuest, t.QuestId, t.QuestName);
                AbandonQuest(t, "deaths");
            }
        }

        public void Reset()
        {
            _plan = null;
            _taskIndex = 0;
            _goBlacklist.Clear();
            _objectiveAreaInstalled = false;
            _trekMarkedFrom = WoWPoint.Empty;
        }

        /// <summary>The activity slot body. Running/Success = we own this tick; Failure = fall through
        /// (no work — the arbiter's supply signal will flip to grind).</summary>
        public RunStatus Tick()
        {
            var me = StyxWoW.Me;
            if (me == null || me.IsDead || me.IsGhost) return RunStatus.Failure;
            if (!HasWork) return RunStatus.Failure;

            QuestTask task = CurrentTask;

            // Task-level stall clock: whatever the reason (unreachable NPC, empty node field, contested
            // mobs), a task making no progress for TaskStallMinutes abandons its quest (TTL'd — retried
            // after the window). Objective progress resets the clock via ObjectiveDone advancing tasks.
            if ((DateTime.UtcNow - _taskStartedAt).TotalMinutes >= VibeQuester2Settings.Instance.TaskStallMinutes)
            {
                Logging.Write("[VQ2-Task] {0} stalled {1}min — abandoning q{2}.",
                    task, VibeQuester2Settings.Instance.TaskStallMinutes, task.QuestId);
                AbandonQuest(task, "task stall");
                return RunStatus.Running;
            }

            switch (task.Kind)
            {
                case QuestTaskKind.PickUp:
                case QuestTaskKind.TurnIn:
                    return TickInteraction(task, me);
                case QuestTaskKind.DoObjective:
                    return TickObjective(task, me);
                default:
                    Advance("unknown task kind");
                    return RunStatus.Running;
            }
        }

        private RunStatus TickInteraction(QuestTask task, LocalPlayer me)
        {
            // Already satisfied (manual play, chained accept, previous pass)?
            bool inLog = me.QuestLog.ContainsQuest((uint)task.QuestId);
            if (task.Kind == QuestTaskKind.PickUp && inLog) { Advance("already in log"); return RunStatus.Running; }
            if (task.Kind == QuestTaskKind.TurnIn && !inLog)
            {
                _planner.NotifyTurnedIn((uint)task.QuestId);
                Advance("already turned in");
                _requestReplan();   // turn-in edge — supply changed
                return RunStatus.Running;
            }
            if (task.Kind == QuestTaskKind.TurnIn && !QuestReadyToTurnIn(task, me))
                return TickNotReadyTurnIn(task, me);

            if (!TravelTo(task.Position, InteractRange, task.ToString()))
                return RunStatus.Running;   // travelling owns the tick

            if (DateTime.UtcNow < _nextInteractAt)
                return RunStatus.Running;   // pace interaction attempts
            _nextInteractAt = DateTime.UtcNow.AddMilliseconds(InteractRetryMs);

            InteractionResult result = task.Kind == QuestTaskKind.PickUp
                ? QuestInteraction.TryPickUp(task, _plan, _planner)
                : QuestInteraction.TryTurnIn(task, _plan, _planner);

            switch (result)
            {
                case InteractionResult.Success:
                    if (task.Kind == QuestTaskKind.TurnIn) _requestReplan();   // turn-in edge
                    Advance("done");
                    return RunStatus.Running;
                case InteractionResult.NotOffered:
                    _notOfferedStrikes++;
                    if (_notOfferedStrikes >= NotOfferedAbandonStrikes)
                    {
                        Logging.Write("[VQ2-Task] q{0} refused by the server ×{1} — blacklisting + replanning.",
                            task.QuestId, _notOfferedStrikes);
                        AbandonQuest(task, "not offered");
                    }
                    return RunStatus.Running;
                default:
                    return RunStatus.Running;   // Retry — the stall clock bounds it
            }
        }

        private RunStatus TickObjective(QuestTask task, LocalPlayer me)
        {
            if (ObjectiveDone(task, me))
            {
                Logging.Write("[VQ2-Task] DoObjective q{0}/{1} complete.", task.QuestId, task.ObjectiveIndex);
                Advance("objective complete");
                return RunStatus.Running;
            }

            if (task.Objective.Type == ObjectiveType.CollectFromGameObject)
                return TickGameObjectCollect(task, me);

            // Explore: walk into the areatrigger radius — the server fires the discovery credit on entry.
            // Advance on arrival (not on descriptor) so mixed explore+kill quests don't deadlock; the
            // real completion is verified downstream by the turn-in readiness / quest-complete check.
            if (task.Objective.Type == ObjectiveType.Explore)
            {
                if (task.Position == WoWPoint.Empty) { Advance("explore area not on this map"); return RunStatus.Running; }
                float arrive = Math.Max((float)(task.Objective.Radius * 0.9), 5f);
                if (TravelTo(task.Position, arrive, string.Format("explore q{0}", task.QuestId)))
                {
                    StyxWoW.Sleep(600);   // dwell inside the trigger so it fires server-side
                    Logging.Write("[VQ2-Task] q{0}: reached explore area.", task.QuestId);
                    Advance("explore area reached");
                }
                return RunStatus.Running;
            }

            // Friendly objective mob (runtime-observed: the kill target is present but not attackable)
            // needs a talk (→ credit) or a gossip-provoke (→ turns hostile) before the grind can engage
            // it. Once it flips hostile / gives credit, this returns null next tick and the grind runs.
            if (task.Objective.MobId > 0)
            {
                WoWUnit friendly = FindFriendlyObjectiveUnit(task, me);
                if (friendly != null) return TickFriendlyNpc(task, me, friendly);
            }

            // Kill / collect-from-mob: install the objective-scoped grind area once; the inherited
            // chassis (roam → governor commit → LevelBot pull/combat/loot) does the actual killing —
            // returning Failure hands the tick down to exactly those branches. Progress is read from
            // the quest descriptor each pass; deaths/stall abandon via the shared clocks.
            if (!_objectiveAreaInstalled)
            {
                InstallObjectiveArea(task, me);
                return RunStatus.Running;
            }
            return RunStatus.Failure;   // grind the area (combat/loot/roam below us)
        }

        private RunStatus TickGameObjectCollect(QuestTask task, LocalPlayer me)
        {
            PruneGoBlacklist();
            WoWPoint node = NearestGoNode(task, me);
            if (node == WoWPoint.Empty)
            {
                // Every known node blacklisted/contested — wait out the shortest blacklist rather than
                // wedge; the task stall clock still bounds the whole objective.
                TreeRoot.StatusText = string.Format("VQ2: waiting for {0} nodes to free up", task.Objective.GameObjectName ?? "objective");
                return RunStatus.Running;
            }

            if (!TravelTo(node, GoInteractRange, string.Format("GO node q{0}", task.QuestId)))
                return RunStatus.Running;

            // At the node: resolve the live GO (loaded + right entry + at this spot).
            WoWGameObject go = null;
            double best = double.MaxValue;
            foreach (WoWGameObject g in ObjectManager.GetObjectsOfType<WoWGameObject>(false, false))
            {
                if (g == null || g.Entry != (uint)task.Objective.GameObjectId) continue;
                double d = g.Location.Distance(node);
                if (d <= 8f && d < best) { best = d; go = g; }
            }
            if (go == null)
            {
                // Despawned / taken — blacklist the position and move to the next node.
                BlacklistGoNode(node, "not spawned");
                return RunStatus.Running;
            }

            if (DateTime.UtcNow < _nextInteractAt) return RunStatus.Running;
            _nextInteractAt = DateTime.UtcNow.AddMilliseconds(InteractRetryMs);

            if (me.IsMoving) WoWMovement.MoveStop();
            int before = ObjectiveCount(task, me);
            using (var lootWait = new LuaEventWait("LOOT_OPENED"))
            {
                go.Interact();
                if (!lootWait.Wait(4000))
                {
                    // No loot window. Many GOs give objective CREDIT on use (douse the fire, ring the
                    // bell, free the prisoner) rather than loot — check the descriptor before calling it a dud.
                    StyxWoW.Sleep(400);
                    if (ObjectiveCount(task, me) > before)
                    {
                        Logging.Write("[VQ2-Task] q{0}: used {1} for objective credit (no loot).",
                            task.QuestId, task.Objective.GameObjectName ?? "GO");
                        BlacklistGoNode(node, null);   // consumed — let it respawn before revisiting
                        return RunStatus.Running;
                    }
                    BlacklistGoNode(node, "no loot window");
                    return RunStatus.Running;
                }
            }
            // Loot every slot; autoloot settings vary, so drive it explicitly.
            Lua.DoString("for i = 1, GetNumLootItems() do LootSlot(i) end CloseLoot()");
            StyxWoW.Sleep(300);
            BlacklistGoNode(node, null);   // looted (or emptied) — let it respawn before revisiting
            return RunStatus.Running;
        }

        /// <summary>Nearest alive spawn of the objective mob that is NOT attackable (reaction ≥ Friendly)
        /// within reach — i.e. one that must be talked to / provoked rather than grinded.</summary>
        private WoWUnit FindFriendlyObjectiveUnit(QuestTask task, LocalPlayer me)
        {
            WoWUnit best = null;
            double bestDist = double.MaxValue;
            foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
            {
                if (u == null || u.Dead || u.Entry != (uint)task.Objective.MobId) continue;
                if (me.GetReactionTowards(u) < WoWUnitReaction.Friendly) continue;   // attackable → normal grind
                double d = me.Location.Distance(u.Location);
                if (d <= FriendlyNpcDetectRange && d < bestDist) { bestDist = d; best = u; }
            }
            return best;
        }

        /// <summary>
        /// The objective mob is standing there friendly — resolve it patiently, covering four shapes:
        /// use-item-on-target (a held quest item used on it), talk-to-complete (gossip → credit),
        /// gossip-provoke (a multi-step taunt → turns hostile → the grind kills it), and FOLLOW-provoke
        /// (talk → the NPC walks a scripted path you follow → turns hostile; the dwarf-up-the-spire /
        /// Blood Elf traitor pattern). Rules that made it work live: SURVIVAL FIRST — the instant we're
        /// in combat or it flips hostile, close the gossip frame (an open frame blocks casting) and yield
        /// (Failure) so the shell's combat/rest branches (above the activity) engage/heal, and NEVER
        /// abandon mid-fight (the quest must stay in the log for the kill to credit). While the NPC is
        /// moving we FOLLOW and don't re-interact (that interrupts the walk), extending the patience
        /// window. Only after <see cref="ProvokeWaitSeconds"/> stationary with no credit/flip do we
        /// blacklist + abandon.
        /// </summary>
        private RunStatus TickFriendlyNpc(QuestTask task, LocalPlayer me, WoWUnit npc)
        {
            // SURVIVAL FIRST: the moment we're in combat (provoke worked / an add) or the mob flips
            // hostile, STOP gossiping — close the frame (an open gossip window blocks casting) and yield
            // the tick (return Failure) so the shell's combat + rest branches, which sit ABOVE the quest
            // activity, engage/heal. The quest stays in the log so the kill credits; never abandon here.
            if (me.Combat || me.GetReactionTowards(npc) < WoWUnitReaction.Friendly)
            {
                if (GossipFrame.Instance.IsVisible) GossipFrame.Instance.Close();
                Logging.WriteDebug("[VQ2-Task] q{0}: {1} provoked/aggro — yielding to combat.", task.QuestId, npc.Name);
                return RunStatus.Failure;
            }
            if (_npcInteracted && ObjectiveCount(task, me) > _npcBefore)
            {
                Logging.Write("[VQ2-Task] q{0}: {1} — objective credit.", task.QuestId, npc.Name);
                return RunStatus.Running;
            }

            // Approach / FOLLOW: the NPC may walk a scripted path (talk → "follow me" → turns hostile).
            // Stay close while it moves (follow range); only plant solidly inside interact range once it's
            // standing still, since interacting needs us stopped.
            float approach = npc.IsMoving ? FollowRange : NpcApproachRange;
            if (!TravelTo(npc.Location, approach, string.Format("friendly NPC q{0}", task.QuestId)))
                return RunStatus.Running;
            if (!npc.IsMoving && me.IsMoving) WoWMovement.MoveStop();

            // First contact: use-item-on-target if we hold a candidate, else interact to open gossip.
            if (!_npcInteracted)
            {
                _npcInteracted = true;
                _npcBefore = ObjectiveCount(task, me);
                _npcProvokeDeadline = DateTime.UtcNow.AddSeconds(ProvokeWaitSeconds);
                _nextInteractAt = DateTime.UtcNow.AddMilliseconds(ProvokeReInteractMs);
                npc.Target();

                QuestEntry qe = _db()?.Quests.FirstOrDefault(x => x.Id == task.QuestId);
                WoWItem item = CandidateQuestItems(qe)
                    .Select(id => me.CarriedItems.FirstOrDefault(i => i != null && i.Entry == (uint)id))
                    .FirstOrDefault(i => i != null);
                if (item != null)
                {
                    Logging.Write("[VQ2-Task] q{0}: using quest item {1} on {2}.", task.QuestId, item.Entry, npc.Name);
                    item.Use(npc.Guid);
                }
                else
                {
                    Logging.Write("[VQ2-Task] q{0}: interacting with friendly {1} (waiting up to {2}s for talk/provoke).",
                        task.QuestId, npc.Name, ProvokeWaitSeconds);
                    npc.Interact();
                    StyxWoW.Sleep(400);
                    SelectLoneGossipOption();
                }
                return RunStatus.Running;
            }

            // Waiting for the scripted talk/provoke to resolve.
            if (npc.IsMoving)
            {
                // The NPC is walking its lure/escort script — FOLLOW (the travel above does) and stay
                // patient; re-interacting would interrupt it. Keep extending the window while it moves.
                DateTime moveFloor = DateTime.UtcNow.AddSeconds(FollowExtendSeconds);
                if (_npcProvokeDeadline < moveFloor) _npcProvokeDeadline = moveFloor;
            }
            else if (GossipFrame.Instance.IsVisible)
                SelectLoneGossipOption();
            else if (DateTime.UtcNow >= _nextInteractAt)
            {
                npc.Target();
                npc.Interact();
                StyxWoW.Sleep(400);
                SelectLoneGossipOption();
                _nextInteractAt = DateTime.UtcNow.AddMilliseconds(ProvokeReInteractMs);
            }

            // Re-check AFTER the click — the flip lands ON the provoking line, so checking only at the top
            // of the tick raced the deadline (the NPC turned hostile during the interact and we abandoned
            // him the same tick). Provoked/in-combat → close the frame and yield; never abandon.
            if (me.Combat || me.GetReactionTowards(npc) < WoWUnitReaction.Friendly)
            {
                if (GossipFrame.Instance.IsVisible) GossipFrame.Instance.Close();
                Logging.Write("[VQ2-Task] q{0}: {1} provoked — yielding to combat.", task.QuestId, npc.Name);
                return RunStatus.Failure;
            }
            if (ObjectiveCount(task, me) > _npcBefore)
            {
                Logging.Write("[VQ2-Task] q{0}: {1} — objective credit.", task.QuestId, npc.Name);
                return RunStatus.Running;
            }

            if (DateTime.UtcNow > _npcProvokeDeadline && !me.Combat)
            {
                _planner.RecordPermanentBlacklist(task.QuestId, "friendly-npc-interact", "friendly-npc-interact",
                    "Objective mob is friendly; use-item/talk/provoke via gossip did not credit it or turn it hostile within the patience window — a path v2 couldn't resolve (ambiguous/absent option or a mechanic we don't model).",
                    string.Format("mob {0} friendly; waited {1}s after interact; no credit, no reaction flip", task.Objective.MobId, ProvokeWaitSeconds));
                AbandonQuest(task, "unresolved friendly-npc interact");
            }
            return RunStatus.Running;
        }

        /// <summary>Click a lone plain dialogue option in the open gossip frame (the provoke/talk line).
        /// Conservative: only an unambiguous single Gossip-type option is selected.</summary>
        private static void SelectLoneGossipOption()
        {
            if (!GossipFrame.Instance.IsVisible) return;
            var opts = GossipFrame.Instance.GossipOptionEntries?
                .Where(e => e.Type == GossipEntry.GossipEntryType.Gossip).ToList();
            if (opts != null && opts.Count == 1)
                GossipFrame.Instance.SelectGossipOption(opts[0].Index);
        }

        /// <summary>Quest-provided items worth trying ON a friendly objective mob: the start item and any
        /// collect-item ids (only those actually in the bags are used).</summary>
        private static IEnumerable<int> CandidateQuestItems(QuestEntry qe)
        {
            if (qe == null) yield break;
            if (qe.StartItem > 0) yield return qe.StartItem;
            foreach (QuestObjective o in qe.Objectives)
                if (o.Type == ObjectiveType.CollectItem && o.ItemId > 0) yield return o.ItemId;
        }

        /// <summary>Raw per-objective credit count from the player descriptor (-1 if unreadable).</summary>
        private static int ObjectiveCount(QuestTask task, LocalPlayer me)
        {
            var pq = me.QuestLog.GetAllQuests().FirstOrDefault(q => q.Id == (uint)task.QuestId);
            if (pq != null && pq.GetData(out var data) && data.ObjectivesDone != null
                && task.ObjectiveIndex >= 0 && task.ObjectiveIndex < data.ObjectivesDone.Length)
                return data.ObjectivesDone[task.ObjectiveIndex];
            return -1;
        }

        // --- helpers ---

        /// <summary>Mount-aware travel with TrekSafety hazard marking per leg. True = arrived.</summary>
        private bool TravelTo(WoWPoint target, float range, string legName)
        {
            var me = StyxWoW.Me;
            double dist = me.Location.Distance(target);
            if (dist <= range)
            {
                if (me.IsMoving) WoWMovement.MoveStop();
                return true;
            }

            // Mark hazards once per leg start (re-marked when the target changes meaningfully).
            if (_trekMarkedFrom == WoWPoint.Empty || _trekMarkedFrom.Distance(target) > 25f)
            {
                var f = _factions();
                if (f != null)
                    TrekSafety.MarkLeg(f, me.Location, target, me.Level, me.MapId, legName);
                _trekMarkedFrom = target;
            }

            if (!me.Mounted && dist > 60)
                Mount.MountUp(() => target);   // internally gated (imminent fight / short trip / CD)
            Navigator.MoveTo(target);
            TreeRoot.StatusText = string.Format("VQ2: {0} ({1:F0}yd)", legName, dist);
            return false;
        }

        private void InstallObjectiveArea(QuestTask task, LocalPlayer me)
        {
            var db = _db();
            var spawns = new List<WoWPoint>();
            if (db != null && task.Objective.MobId > 0
                && db.CreatureSpawns.TryGetValue(task.Objective.MobId.ToString(), out List<SpawnPoint> sps))
            {
                foreach (SpawnPoint sp in sps)
                {
                    if (sp.Map != (int)me.MapId) continue;
                    var p = new WoWPoint((float)sp.X, (float)sp.Y, (float)sp.Z);
                    if (p.Distance2D(task.Position) <= ObjectiveAreaRadius)
                        spawns.Add(p);
                }
            }
            if (spawns.Count == 0) spawns.Add(task.Position);

            var spot = new GrindSpot
            {
                Centroid = task.Position,
                Map = me.MapId,
                Hotspots = spawns.Take(12).ToList(),
                MobIds = new List<int> { task.Objective.MobId },   // precise targeting: the objective mob only
                Classification = SpotClass.Safe,                    // pre-screened by DangerScreen at selection
            };
            _synth().Install(spot, me.Level);
            _onAreaReplaced();   // the grind bootstrap must re-pick if/when grind-mode resumes
            _objectiveAreaInstalled = true;
            Logging.Write("[VQ2-Task] objective area installed for q{0}/{1}: {2} spawn point(s), mob {3}.",
                task.QuestId, task.ObjectiveIndex, spot.Hotspots.Count, task.Objective.MobId);
        }

        /// <summary>
        /// At the turn-in NPC but the server says the quest isn't complete. If the quest handed us an
        /// item on accept and every objective is report-only, it's a "use the provided item" quest (read
        /// a book, blow a horn) — USE the item; a location-free use completes it on the spot and the
        /// turn-in proceeds next tick. If it still won't complete after a few tries the mechanic needs
        /// more than a bare use (a location/target/sequence v2 can't model) → record durable blacklist
        /// knowledge and abandon so no run wastes the trip. Otherwise (no provided item, or a real
        /// objective genuinely lagging) defer as before — the plan re-tours, the stall clock still bounds
        /// a true wedge.
        /// </summary>
        private RunStatus TickNotReadyTurnIn(QuestTask task, LocalPlayer me)
        {
            QuestEntry qe = _db()?.Quests.FirstOrDefault(x => x.Id == task.QuestId);
            bool reportOnly = qe != null && qe.Objectives.Count > 0
                              && qe.Objectives.All(o => o.Type == ObjectiveType.TurnInOnly);
            int startItem = qe?.StartItem ?? 0;
            WoWItem carried = reportOnly && startItem > 0
                ? me.CarriedItems.FirstOrDefault(i => i != null && i.Entry == (uint)startItem)
                : null;

            if (carried == null)
            {
                Advance("not ready to turn in yet");
                _requestReplan();
                return RunStatus.Running;
            }

            if (_useItemAttempts < MaxUseItemAttempts)
            {
                if (DateTime.UtcNow < _nextUseItemAt) return RunStatus.Running;   // pace attempts
                if (me.IsMoving) WoWMovement.MoveStop();
                Logging.Write("[VQ2-Task] q{0}: using provided item {1} to complete '{2}' (attempt {3}/{4}).",
                    task.QuestId, startItem, task.QuestName, _useItemAttempts + 1, MaxUseItemAttempts);
                carried.Use();
                _useItemAttempts++;
                _nextUseItemAt = DateTime.UtcNow.AddMilliseconds(UseItemPaceMs);
                return RunStatus.Running;   // re-check readiness next tick
            }

            _planner.RecordPermanentBlacklist(task.QuestId, "use-item-location", "use-item-location",
                "Provided item used at the turn-in but the quest stayed incomplete — needs a location/target/scripted use v2 can't model.",
                string.Format("used item {0} x{1}; IsCompleteInLog stayed false", startItem, _useItemAttempts));
            AbandonQuest(task, "unsupported use-item");
            return RunStatus.Running;
        }

        private static bool QuestReadyToTurnIn(QuestTask task, LocalPlayer me)
        {
            // Lua-log truth, NOT PlayerQuest.IsCompleted (false-positives on in-progress quests —
            // docs/gotchas.md); the set only ever contains quests currently in the log.
            return QuestLogTruth.IsCompleteInLog((uint)task.QuestId);
        }

        /// <summary>Per-objective progress from the player descriptor (server truth in memory).</summary>
        private static bool ObjectiveDone(QuestTask task, LocalPlayer me)
        {
            var pq = me.QuestLog.GetAllQuests().FirstOrDefault(q => q.Id == (uint)task.QuestId);
            if (pq == null) return true;    // left the log (turned in / abandoned) — nothing to do
            if (QuestLogTruth.IsCompleteInLog((uint)task.QuestId)) return true;   // never PlayerQuest.IsCompleted (gotchas)

            int required = task.Objective.Type == ObjectiveType.KillMob
                ? task.Objective.KillCount
                : task.Objective.CollectCount;
            if (required <= 0) return false;   // unknown requirement — rely on IsCompleted / stall clock

            if (pq.GetData(out var data) && data.ObjectivesDone != null
                && task.ObjectiveIndex >= 0 && task.ObjectiveIndex < data.ObjectivesDone.Length)
                return data.ObjectivesDone[task.ObjectiveIndex] >= required;
            return false;
        }

        private WoWPoint NearestGoNode(QuestTask task, LocalPlayer me)
        {
            var db = _db();
            if (db == null || !db.GameObjectSpawns.TryGetValue(task.Objective.GameObjectId.ToString(), out List<SpawnPoint> sps))
                return WoWPoint.Empty;
            WoWPoint best = WoWPoint.Empty;
            double bestDist = double.MaxValue;
            foreach (SpawnPoint sp in sps)
            {
                if (sp.Map != (int)me.MapId) continue;
                var p = new WoWPoint((float)sp.X, (float)sp.Y, (float)sp.Z);
                if (_goBlacklist.ContainsKey(GoKey(p))) continue;
                double d = me.Location.Distance2D(p);
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        private void BlacklistGoNode(WoWPoint node, string reason)
        {
            _goBlacklist[GoKey(node)] = DateTime.UtcNow.AddMinutes(GoBlacklistMinutes);
            if (reason != null)
                Logging.WriteDebug("[VQ2-Task] GO node at {0} blacklisted {1}m ({2}).", node, GoBlacklistMinutes, reason);
        }

        private void PruneGoBlacklist()
        {
            if (_goBlacklist.Count == 0) return;
            DateTime now = DateTime.UtcNow;
            foreach (string key in _goBlacklist.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                _goBlacklist.Remove(key);
        }

        private static string GoKey(WoWPoint p) => string.Format("{0:F0}:{1:F0}:{2:F0}", p.X, p.Y, p.Z);

        private void Advance(string reason)
        {
            Logging.WriteDebug("[VQ2-Task] advance past {0} ({1}).", CurrentTask, reason);
            _taskIndex++;
            StartTask(reason);
        }

        /// <summary>Drop every task of a failing quest, TTL-blacklist it, abandon it in the log, replan.</summary>
        private void AbandonQuest(QuestTask task, string reason)
        {
            _planner.AutoBlacklist((uint)task.QuestId);
            try
            {
                var me = StyxWoW.Me;
                if (me != null && me.QuestLog.ContainsQuest((uint)task.QuestId))
                    Lua.DoString(
                        "for i=1,GetNumQuestLogEntries() do local _,_,_,_,_,_,_,_,qid = GetQuestLogTitle(i) " +
                        "if qid == " + task.QuestId + " then SelectQuestLogEntry(i) SetAbandonQuest() AbandonQuest() break end end");
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[VQ2-Task] abandon-in-log failed for q{0}: {1}", task.QuestId, ex.Message);
            }
            if (_plan != null)
                _plan.Tasks.RemoveAll(t => t.QuestId == task.QuestId);
            StartTask("abandoned: " + reason);
            _requestReplan();
        }

        private void StartTask(string why)
        {
            _taskStartedAt = DateTime.UtcNow;
            _nextInteractAt = DateTime.MinValue;
            _notOfferedStrikes = 0;
            _deathsOnCurrentQuest = 0;
            _objectiveAreaInstalled = false;
            _trekMarkedFrom = WoWPoint.Empty;
            _useItemAttempts = 0;
            _nextUseItemAt = DateTime.MinValue;
            _npcInteracted = false;
            _npcProvokeDeadline = DateTime.MinValue;
            if (HasWork)
                Logging.Write("[VQ2-Task] START {0} ({1}).", CurrentTask, why);
        }
    }
}
