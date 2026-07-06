using System;
using System.Collections.Generic;
using System.Linq;
using Bots.VibeGrinder.Selection;
using Styx;
using Styx.Logic.Pathing;
using Styx.Helpers;
using Styx.WoWInternals.WoWObjects;
using VibeQuester;

namespace Bots.Vibes.VibeQuester2.Planning
{
    /// <summary>
    /// Builds the QuestPlan: eligibility gates (ported from the legacy QuestScheduler — every carried
    /// gate keeps its documented semantics), the strict whitelist, the DangerScreen, and hub-batched
    /// nearest-neighbour ordering. Two hard lessons from the 2026-07-06 Grull Hawkwind incident are
    /// baked in here:
    ///  - prereqs are satisfied ONLY by the completed set, never by log state;
    ///  - a quest that leaves the log via turn-in is unioned into the completed set IMMEDIATELY
    ///    (NotifyTurnedIn) — GetCompletedQuests() caches ~1 min, and during that window a freshly
    ///    turned-in quest otherwise re-selects as "available" and loops an unofferable pickup.
    /// </summary>
    public class QuestPlanner
    {
        private readonly DataLoader _loader;
        private readonly DangerScreen _danger = new DangerScreen();

        // Monotonic per session: server set (event-waited GetCompletedQuests) ∪ our own turn-in edges.
        private readonly HashSet<uint> _completed = new HashSet<uint>();
        private readonly Dictionary<uint, DateTime> _autoBlacklist = new Dictionary<uint, DateTime>();
        private int _scanThreshold;

        public QuestPlanner(DataLoader loader)
        {
            _loader = loader;
            _scanThreshold = VibeQuester2Settings.Instance.ScanStartDistance;
        }

        public int LastDoableSupply { get; private set; }

        /// <summary>A turn-in landed (quest left the log) — completed NOW, don't wait out the cache.</summary>
        public void NotifyTurnedIn(uint questId) => _completed.Add(questId);

        /// <summary>Runtime failure (deaths / no progress) — TTL'd so a transient failure retries later.</summary>
        public void AutoBlacklist(uint questId)
        {
            _autoBlacklist[questId] = DateTime.UtcNow.AddMinutes(
                Math.Max(1, VibeQuester2Settings.Instance.AutoBlacklistMinutes));
            Logging.Write("[VQ2-Plan] quest {0} auto-blacklisted for {1}m.",
                questId, VibeQuester2Settings.Instance.AutoBlacklistMinutes);
        }

        private bool IsBlacklisted(uint id)
        {
            if (_autoBlacklist.TryGetValue(id, out DateTime until))
            {
                if (DateTime.UtcNow < until) return true;
                _autoBlacklist.Remove(id);
            }
            return false;
        }

        private void RefreshCompleted(LocalPlayer me)
        {
            var fresh = me?.QuestLog?.GetCompletedQuests();   // event-waited; the only trustworthy source
            if (fresh == null) return;
            foreach (uint id in fresh)
                _completed.Add(id);
        }

        public QuestPlan BuildPlan(LocalPlayer me, FactionResolver factions)
        {
            var s = VibeQuester2Settings.Instance;
            var plan = new QuestPlan();
            QuestDatabase db = _loader.Database;
            if (db == null || me == null)
                return plan;

            RefreshCompleted(me);

            // --- available (not-in-log) quests through the full gate chain ---
            int rBlack = 0, rDone = 0, rInLog = 0, rMinLvl = 0, rLvlWin = 0, rMobLvl = 0,
                rRace = 0, rClass = 0, rPrereq = 0, rGiverFar = 0, rType = 0, rNoKill = 0, rDanger = 0;
            var available = new List<(QuestEntry q, double dist)>();
            WoWPoint futureHub = WoWPoint.Empty;
            double futureHubDist = double.MaxValue;

            foreach (QuestEntry qe in db.Quests)
            {
                if (IsBlacklisted((uint)qe.Id)) { rBlack++; continue; }
                if (_completed.Contains((uint)qe.Id)) { rDone++; continue; }
                if (me.QuestLog.ContainsQuest((uint)qe.Id)) { rInLog++; continue; }

                bool levelGated = me.Level < qe.MinLevel;
                if (!levelGated)
                {
                    // QuestLevel window with the floor-of-3 upper bound (acceptance reach is not mob
                    // difficulty — see the legacy CLAUDE.md; the floor is load-bearing for fresh lvl-1s).
                    int qMin = Math.Max(1, me.Level - 2);
                    int qMax = me.Level + Math.Max(3, s.MaxMobOverLevel);
                    levelGated = qe.QuestLevel > 0 && (qe.QuestLevel < qMin || qe.QuestLevel > qMax);
                    if (levelGated) rLvlWin++;
                }
                else rMinLvl++;

                if (levelGated)
                {
                    // Failing ONLY on level = tomorrow's quest — its giver is the hub grinding should
                    // drift toward. Track the nearest such giver for the arbiter.
                    double hd = NearestGiverDistance(qe, db, me);
                    if (hd < futureHubDist && hd <= s.MaxTravelDistance * 2)
                    {
                        WoWPoint hub = NearestGiverPoint(qe, db, me);
                        if (hub != WoWPoint.Empty) { futureHub = hub; futureHubDist = hd; }
                    }
                    continue;
                }

                // Objective mob-level gate — the REAL difficulty guard (QuestLevel misses scaling quests).
                if (qe.Objectives.Any(o => o.MobLevel > me.Level + s.MaxMobOverLevel)) { rMobLvl++; continue; }
                if (!RaceAllowed(qe.AllowableRaces, me)) { rRace++; continue; }
                if (!ClassAllowed(qe.AllowableClasses, me)) { rClass++; continue; }
                // Prereqs vs the COMPLETED set only — log state never satisfies a prereq.
                if (!PrereqsMet(qe)) { rPrereq++; continue; }
                if (!SupportedType(qe)) { rType++; continue; }
                if (!HasResolvableKillTargets(qe, db)) { rNoKill++; continue; }

                double giverDist = NearestGiverDistance(qe, db, me);
                if (giverDist > _scanThreshold) { rGiverFar++; continue; }

                string dangerReason = _danger.Reject(qe, db, me, factions);
                if (dangerReason != null)
                {
                    rDanger++;
                    Logging.WriteDebug("[VQ2-Plan] quest {0} skipped: danger({1})", qe.Id, dangerReason);
                    continue;
                }

                available.Add((qe, giverDist));
            }

            // --- log quests with workable objectives (danger-screened too: the area may have been
            //     fine at pickup and lethal now — the runtime backstop still owns mid-task failures) ---
            var logQuests = new List<QuestEntry>();
            foreach (var lq in me.QuestLog.GetAllQuests())
            {
                QuestEntry qe = db.Quests.FirstOrDefault(x => x.Id == (int)lq.Id);
                if (qe == null || qe.Objectives.Count == 0) continue;
                if (IsBlacklisted((uint)qe.Id)) continue;
                if (!SupportedType(qe)) continue;
                logQuests.Add(qe);
            }

            // --- selection: proximity-ranked union, capped ---
            var selected = logQuests
                .Select(q => (q, dist: NearestWorkDistance(q, db, me)))
                .Concat(available)
                .OrderBy(p => p.Item2)
                .Select(p => p.Item1)
                .Distinct()
                .Take(s.MaxQuestsPerPlan)
                .ToList();

            LastDoableSupply = available.Count;
            plan.DoableSupply = available.Count;
            plan.NextFutureHub = futureHub;
            foreach (var q in selected) plan.QuestIds.Add(q.Id);

            BuildTasks(plan, selected, db, me);

            Logging.Write(System.Drawing.Color.MediumPurple,
                "[VQ2-Plan] lvl={0} map={1} @{2}yd: {3} doable, {4} planned ({5} tasks) | rejected done={6} inLog={7} minLvl={8} lvlWin={9} mobLvl={10} race={11} class={12} prereq={13} giverFar={14} type={15} noKill={16} danger={17} black={18}{19}",
                me.Level, (int)me.MapId, _scanThreshold, available.Count, selected.Count, plan.Tasks.Count,
                rDone, rInLog, rMinLvl, rLvlWin, rMobLvl, rRace, rClass, rPrereq, rGiverFar, rType, rNoKill, rDanger, rBlack,
                plan.HasFutureHub ? string.Format(" | futureHub@{0:F0}yd", futureHubDist) : "");

            // Adaptive scan reach: nothing found → widen next scan; found → reset.
            _scanThreshold = available.Count == 0 && logQuests.Count == 0
                ? Math.Min(_scanThreshold + s.ScanStep, s.MaxTravelDistance)
                : s.ScanStartDistance;

            return plan;
        }

        /// <summary>
        /// Hub-batched phases with NN tours from the player: ready turn-ins first (drain), then
        /// pickups, then objectives, then remaining turn-ins. Per-quest interleaving is deliberately
        /// NOT done — batching is what makes hub-and-spoke questing efficient (carried rule).
        /// </summary>
        private void BuildTasks(QuestPlan plan, List<QuestEntry> selected, QuestDatabase db, LocalPlayer me)
        {
            var readyIds = me.QuestLog.GetAllQuests().Where(q => q.IsCompleted).Select(q => (uint)q.Id).ToHashSet();
            var inLog = me.QuestLog.GetAllQuests().Select(q => (uint)q.Id).ToHashSet();

            var turnInsReady = new List<QuestTask>();
            var pickups = new List<QuestTask>();
            var objectives = new List<QuestTask>();
            var turnInsLater = new List<QuestTask>();

            foreach (QuestEntry q in selected)
            {
                bool logQuest = inLog.Contains((uint)q.Id);
                if (!logQuest)
                {
                    QuestTask pu = InteractionTask(QuestTaskKind.PickUp, q, db, me, givers: true);
                    if (pu != null) pickups.Add(pu);
                }
                if (readyIds.Contains((uint)q.Id))
                {
                    QuestTask ti = InteractionTask(QuestTaskKind.TurnIn, q, db, me, givers: false);
                    if (ti != null) turnInsReady.Add(ti);
                    continue;   // ready quests need no objective work
                }
                foreach (QuestObjective obj in q.Objectives)
                {
                    if (obj.Type == ObjectiveType.TurnInOnly) continue;
                    objectives.Add(new QuestTask
                    {
                        Kind = QuestTaskKind.DoObjective,
                        QuestId = q.Id,
                        QuestName = q.Name,
                        Objective = obj,
                        ObjectiveIndex = obj.Index,
                        Position = NearestObjectivePoint(obj, db, me),
                    });
                }
                QuestTask later = InteractionTask(QuestTaskKind.TurnIn, q, db, me, givers: false);
                if (later != null) turnInsLater.Add(later);
            }

            plan.Tasks.AddRange(NnTour(turnInsReady, me.Location));
            plan.Tasks.AddRange(NnTour(pickups, me.Location));
            plan.Tasks.AddRange(NnTour(objectives, me.Location));
            plan.Tasks.AddRange(NnTour(turnInsLater, me.Location));
        }

        private QuestTask InteractionTask(QuestTaskKind kind, QuestEntry q, QuestDatabase db, LocalPlayer me, bool givers)
        {
            // Nearest same-map spawn among all givers/enders; entity carried WITH type + coords so the
            // executor never free-resolves an entry (the id-space collision gotcha).
            QuestTask best = null;
            double bestDist = double.MaxValue;
            if (givers)
            {
                foreach (QuestGiverEntry g in db.QuestGivers)
                {
                    if (g.QuestId != q.Id) continue;
                    Consider(g.GiverId, g.GiverName, g.GiverType == QuestObjectType.GameObject);
                }
            }
            else
            {
                foreach (QuestEnderEntry e in db.QuestEnders)
                {
                    if (e.QuestId != q.Id) continue;
                    Consider(e.EnderId, e.EnderName, e.EnderType == QuestObjectType.GameObject);
                }
            }
            return best;

            void Consider(int id, string name, bool isGo)
            {
                var source = isGo ? db.GameObjectSpawns : db.CreatureSpawns;
                if (!source.TryGetValue(id.ToString(), out List<SpawnPoint> spawns)) return;
                foreach (SpawnPoint sp in spawns)
                {
                    if (sp.Map != (int)me.MapId) continue;
                    var p = new WoWPoint((float)sp.X, (float)sp.Y, (float)sp.Z);
                    double d = me.Location.Distance2D(p);
                    if (d >= bestDist) continue;
                    bestDist = d;
                    best = new QuestTask
                    {
                        Kind = kind,
                        QuestId = q.Id,
                        QuestName = q.Name,
                        EntityId = id,
                        EntityName = name,
                        IsGameObject = isGo,
                        Position = p,
                    };
                }
            }
        }

        private static List<QuestTask> NnTour(List<QuestTask> tasks, WoWPoint from)
        {
            var result = new List<QuestTask>(tasks.Count);
            var remaining = new List<QuestTask>(tasks);
            WoWPoint cur = from;
            while (remaining.Count > 0)
            {
                int bestIdx = 0;
                double best = double.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    double d = remaining[i].Position == WoWPoint.Empty
                        ? double.MaxValue - 1   // positionless tasks go last, stable
                        : cur.Distance2D(remaining[i].Position);
                    if (d < best) { best = d; bestIdx = i; }
                }
                result.Add(remaining[bestIdx]);
                if (remaining[bestIdx].Position != WoWPoint.Empty)
                    cur = remaining[bestIdx].Position;
                remaining.RemoveAt(bestIdx);
            }
            return result;
        }

        // --- carried gates (semantics frozen from the legacy scheduler) ---

        private bool PrereqsMet(QuestEntry qe)
        {
            if (qe.PrevQuestID > 0 && !_completed.Contains((uint)qe.PrevQuestID))
                return false;
            foreach (int prevId in qe.PreviousQuestsIds)
                if (prevId > 0 && !_completed.Contains((uint)prevId))
                    return false;
            return true;
        }

        private static bool RaceAllowed(int mask, LocalPlayer me)
            => mask == 0 || mask == -1 || (mask & (1 << ((int)me.Race - 1))) != 0;

        private static bool ClassAllowed(int mask, LocalPlayer me)
            => mask == 0 || mask == -1 || (mask & (1 << ((int)me.Class - 1))) != 0;

        /// <summary>
        /// Strict whitelist: every objective is executable (kill / collect-from-mob / collect-from-GO),
        /// or the quest is all-TurnInOnly (deliver/report — free XP; the runtime grace-ignore still
        /// backstops the escort/script quests AC data can't distinguish).
        /// </summary>
        private static bool SupportedType(QuestEntry qe)
        {
            if (qe.Objectives.Count == 0)
                return false;
            bool hasStartItem = qe.StartItem > 0;
            foreach (QuestObjective obj in qe.Objectives)
            {
                switch (obj.Type)
                {
                    case ObjectiveType.KillMob when obj.MobId <= 0:
                    case ObjectiveType.CollectItem when obj.ItemId <= 0:
                    case ObjectiveType.CollectFromGameObject when obj.GameObjectId <= 0:
                        return false;
                    case ObjectiveType.CollectItem when hasStartItem && obj.MobId == 0:
                        return false;   // provided-item collect with a start item = use-item quest in disguise
                }
            }
            return true;
        }

        private static bool HasResolvableKillTargets(QuestEntry qe, QuestDatabase db)
        {
            var killMobs = qe.Objectives.Where(o => o.Type == ObjectiveType.KillMob && o.MobId > 0).ToList();
            if (killMobs.Count == 0) return true;
            return killMobs.Any(o => db.CreatureSpawns.ContainsKey(o.MobId.ToString()));
        }

        // --- distance helpers ---

        private static double NearestGiverDistance(QuestEntry qe, QuestDatabase db, LocalPlayer me)
        {
            double best = double.MaxValue;
            foreach (QuestGiverEntry g in db.QuestGivers)
            {
                if (g.QuestId != qe.Id) continue;
                best = Math.Min(best, NearestSpawnDistance(g.GiverId, db, me, g.GiverType == QuestObjectType.GameObject));
            }
            return best;
        }

        private static WoWPoint NearestGiverPoint(QuestEntry qe, QuestDatabase db, LocalPlayer me)
        {
            WoWPoint best = WoWPoint.Empty;
            double bestDist = double.MaxValue;
            foreach (QuestGiverEntry g in db.QuestGivers)
            {
                if (g.QuestId != qe.Id) continue;
                var source = g.GiverType == QuestObjectType.GameObject ? db.GameObjectSpawns : db.CreatureSpawns;
                if (!source.TryGetValue(g.GiverId.ToString(), out List<SpawnPoint> spawns)) continue;
                foreach (SpawnPoint sp in spawns)
                {
                    if (sp.Map != (int)me.MapId) continue;
                    var p = new WoWPoint((float)sp.X, (float)sp.Y, (float)sp.Z);
                    double d = me.Location.Distance2D(p);
                    if (d < bestDist) { bestDist = d; best = p; }
                }
            }
            return best;
        }

        private static double NearestSpawnDistance(int entityId, QuestDatabase db, LocalPlayer me, bool gameObject)
        {
            double best = double.MaxValue;
            var source = gameObject ? db.GameObjectSpawns : db.CreatureSpawns;
            if (!source.TryGetValue(entityId.ToString(), out List<SpawnPoint> spawns)) return best;
            foreach (SpawnPoint sp in spawns)
            {
                if (sp.Map != (int)me.MapId) continue;
                double dx = sp.X - me.Location.X, dy = sp.Y - me.Location.Y;
                best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
            }
            return best;
        }

        private static double NearestWorkDistance(QuestEntry qe, QuestDatabase db, LocalPlayer me)
        {
            double best = double.MaxValue;
            foreach (QuestObjective obj in qe.Objectives)
            {
                if (obj.MobId > 0) best = Math.Min(best, NearestSpawnDistance(obj.MobId, db, me, gameObject: false));
                if (obj.GameObjectId > 0) best = Math.Min(best, NearestSpawnDistance(obj.GameObjectId, db, me, gameObject: true));
            }
            foreach (QuestEnderEntry e in db.QuestEnders)
                if (e.QuestId == qe.Id)
                    best = Math.Min(best, NearestSpawnDistance(e.EnderId, db, me, e.EnderType == QuestObjectType.GameObject));
            return best;
        }

        private static WoWPoint NearestObjectivePoint(QuestObjective obj, QuestDatabase db, LocalPlayer me)
        {
            WoWPoint best = WoWPoint.Empty;
            double bestDist = double.MaxValue;
            bool isGo = obj.Type == ObjectiveType.CollectFromGameObject;
            int id = isGo ? obj.GameObjectId : obj.MobId;
            if (id <= 0) return best;
            var source = isGo ? db.GameObjectSpawns : db.CreatureSpawns;
            if (!source.TryGetValue(id.ToString(), out List<SpawnPoint> spawns)) return best;
            foreach (SpawnPoint sp in spawns)
            {
                if (sp.Map != (int)me.MapId) continue;
                var p = new WoWPoint((float)sp.X, (float)sp.Y, (float)sp.Z);
                double d = me.Location.Distance2D(p);
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }
    }
}
