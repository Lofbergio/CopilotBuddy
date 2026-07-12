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
        private readonly Dictionary<string, DateTime> _goBlacklist = new Dictionary<string, DateTime>();   // key: rounded position

        private const float InteractRange = 4.5f;
        private const float GoInteractRange = 4.0f;
        private const int InteractRetryMs = 2000;
        private const int NotOfferedAbandonStrikes = 2;
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
            {
                // Objectives not finished (plan ordering guarantees the objective tasks came first —
                // this catches drop-rate stragglers): push the turn-in back by re-queueing after the
                // objective work is done; simplest correct form: skip forward, replan restores order.
                Advance("not ready to turn in yet");
                _requestReplan();
                return RunStatus.Running;
            }

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
            using (var lootWait = new LuaEventWait("LOOT_OPENED"))
            {
                go.Interact();
                if (!lootWait.Wait(4000))
                {
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
            if (HasWork)
                Logging.Write("[VQ2-Task] START {0} ({1}).", CurrentTask, why);
        }
    }
}
