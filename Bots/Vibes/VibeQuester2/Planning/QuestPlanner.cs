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
        private readonly QuestBlacklist _blacklist = new QuestBlacklist();   // persistent, learned "can't do it" set

        // Monotonic per session: server set (event-waited GetCompletedQuests) ∪ our own turn-in edges.
        private readonly HashSet<uint> _completed = new HashSet<uint>();
        private readonly Dictionary<uint, DateTime> _autoBlacklist = new Dictionary<uint, DateTime>();
        private int _scanThreshold;
        private FactionResolver _lastFactions;   // captured each scan for on-the-spot chained-offer screening

        public QuestPlanner(DataLoader loader)
        {
            _loader = loader;
            _scanThreshold = VibeQuester2Settings.Instance.ScanStartDistance;
        }

        public int LastDoableSupply { get; private set; }

        /// <summary>A turn-in landed (quest left the log) — completed NOW, don't wait out the cache.</summary>
        public void NotifyTurnedIn(uint questId) => _completed.Add(questId);

        /// <summary>
        /// A turn-in pushed the server's chained follow-up detail frame. Accept it if it clears the
        /// SAME eligibility gates a normal scan applies (level window, mob-level, race/class, prereqs —
        /// now satisfied since the predecessor was just unioned into <c>_completed</c> — type whitelist,
        /// resolvable kill, danger); proximity is intentionally NOT gated because the giver is standing
        /// in front of us (free pickup). This is why a valid next-in-chain quest that couldn't be in the
        /// pre-computed plan (its prereq was still pending at scan time) is taken on the spot instead of
        /// declined and walked back for. Screening — not blind accept — still refuses a follow-up that is
        /// over-level, wrong-type (escort/script), or lands in a Dangerous area.
        /// </summary>
        public bool ScreenChainedOffer(int questId, LocalPlayer me)
        {
            if (me == null) return false;
            QuestDatabase db = _loader.Database;
            if (db == null) return false;
            QuestEntry qe = db.Quests.FirstOrDefault(x => x.Id == questId);
            if (qe == null) return false;

            var s = VibeQuester2Settings.Instance;
            if (IsBlacklisted((uint)qe.Id) || _blacklist.IsBlocked(qe.Id)) return false;
            if (_completed.Contains((uint)qe.Id)) return false;
            if (me.QuestLog.ContainsQuest((uint)qe.Id)) return false;   // accept already landed it

            if (me.Level < qe.MinLevel) return false;
            int qMin = Math.Max(1, me.Level - 2);
            int qMax = me.Level + Math.Max(3, s.MaxMobOverLevel);
            if (qe.QuestLevel > 0 && (qe.QuestLevel < qMin || qe.QuestLevel > qMax)) return false;

            if (qe.Objectives.Any(o => o.MobLevel > me.Level + s.MaxMobOverLevel)) return false;
            if (!RaceAllowed(qe.AllowableRaces, me)) return false;
            if (!ClassAllowed(qe.AllowableClasses, me)) return false;
            if (!PrereqsMet(qe)) return false;
            if (!SupportedType(qe)) return false;
            if (!HasResolvableKillTargets(qe, db)) return false;
            if (_lastFactions != null && _danger.Reject(qe, db, me, _lastFactions) != null) return false;
            return true;
        }

        /// <summary>Runtime failure (deaths / no progress) — TTL'd so a transient failure retries later.</summary>
        public void AutoBlacklist(uint questId)
        {
            _autoBlacklist[questId] = DateTime.UtcNow.AddMinutes(
                Math.Max(1, VibeQuester2Settings.Instance.AutoBlacklistMinutes));
            Logging.Write("[VQ2-Plan] quest {0} auto-blacklisted for {1}m.",
                questId, VibeQuester2Settings.Instance.AutoBlacklistMinutes);
        }

        /// <summary>
        /// Structural failure (a mechanic v2 can't perform) — record durable, reasoned knowledge in the
        /// persistent blacklist so no FUTURE run re-accepts it, and a future build that ships
        /// <paramref name="capability"/> can auto-re-enable it. Snapshots the quest's shape so the entry
        /// is triageable offline without re-deriving from the DB.
        /// </summary>
        public void RecordPermanentBlacklist(int questId, string category, string capability, string reason, string evidence)
        {
            QuestDatabase db = _loader.Database;
            QuestEntry qe = db?.Quests.FirstOrDefault(x => x.Id == questId);
            _blacklist.Record(new QuestBlacklistEntry
            {
                QuestId = questId,
                QuestName = qe?.Name,
                Category = category,
                RequiresCapability = capability,
                BlockClass = "structural",
                Action = "never-accept",
                Reason = reason,
                Evidence = evidence,
                DetectedByVersion = DetectorVersion,
                Source = "learned",
                StartItem = qe?.StartItem ?? 0,
                GiverId = db?.QuestGivers.FirstOrDefault(g => g.QuestId == questId)?.GiverId ?? 0,
                EnderId = db?.QuestEnders.FirstOrDefault(e => e.QuestId == questId)?.EnderId ?? 0,
                ObjectiveSummary = qe != null ? string.Join(", ", qe.Objectives.Select(o => o.Type.ToString())) : null,
                ObjectiveDetail = qe?.Objectives.Select(DescribeObjective).ToList(),
                Map = StyxWoW.Me != null ? (int)StyxWoW.Me.MapId : 0,
            });
        }

        private const string DetectorVersion = "1.5.2";

        private static string DescribeObjective(QuestObjective o)
        {
            string what = o.Type switch
            {
                ObjectiveType.KillMob => "mob=" + o.MobId + (o.MobLevel > 0 ? " lvl" + o.MobLevel : ""),
                ObjectiveType.CollectItem => "item=" + o.ItemId,
                ObjectiveType.CollectFromGameObject => "go=" + o.GameObjectId,
                _ => "",
            };
            int count = Math.Max(o.KillCount, o.CollectCount);
            return string.Format("[{0}] {1}{2} {3}", o.Index, o.Type, string.IsNullOrEmpty(what) ? "" : " " + what,
                count > 0 ? "x" + count : "").TrimEnd();
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

            _lastFactions = factions;
            RefreshCompleted(me);

            // --- available (not-in-log) quests through the full gate chain ---
            int rBlack = 0, rDone = 0, rInLog = 0, rMinLvl = 0, rLvlWin = 0, rMobLvl = 0,
                rRace = 0, rClass = 0, rPrereq = 0, rGiverFar = 0, rType = 0, rNoKill = 0, rDanger = 0;
            var available = new List<(QuestEntry q, double dist)>();
            WoWPoint futureHub = WoWPoint.Empty;
            double futureHubDist = double.MaxValue;

            foreach (QuestEntry qe in db.Quests)
            {
                if (IsBlacklisted((uint)qe.Id) || _blacklist.IsBlocked(qe.Id)) { rBlack++; continue; }
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
                if (IsBlacklisted((uint)qe.Id) || _blacklist.IsBlocked(qe.Id)) continue;
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
        /// One geography-first tour over per-quest DEPENDENCY CHAINS (pickup → objectives → turn-in;
        /// a ready quest = just its turn-in). A single greedy NN walk over the chain HEADS, chained
        /// from the player's real position, interleaves quests by actual distance while a quest's
        /// turn-in can never precede its own pickup/objectives. Rigid phase batching (all ready
        /// turn-ins first, each phase re-toured from the start point) sent the bot 500yd to drain a
        /// lone completed quest, then 700yd back for pickups that were at its feet — geography wins.
        /// </summary>
        private void BuildTasks(QuestPlan plan, List<QuestEntry> selected, QuestDatabase db, LocalPlayer me)
        {
            // Lua-log truth, NOT PlayerQuest.IsCompleted — that false-positives on in-progress quests
            // and plans turn-ins the server then refuses, striking innocent quests toward abandon
            // (docs/gotchas.md).
            var readyIds = new HashSet<uint>(QuestLogTruth.CompleteSet());
            var inLog = me.QuestLog.GetAllQuests().Select(q => (uint)q.Id).ToHashSet();

            var chains = new List<Queue<QuestTask>>();

            foreach (QuestEntry q in selected)
            {
                var chain = new Queue<QuestTask>();

                if (readyIds.Contains((uint)q.Id))
                {
                    // Already complete in the log — the turn-in is the whole chain, no objective work.
                    QuestTask ti = InteractionTask(QuestTaskKind.TurnIn, q, db, me, givers: false);
                    if (ti != null) chain.Enqueue(ti);
                    if (chain.Count > 0) chains.Add(chain);
                    continue;
                }

                if (!inLog.Contains((uint)q.Id))
                {
                    QuestTask pu = InteractionTask(QuestTaskKind.PickUp, q, db, me, givers: true);
                    if (pu != null) chain.Enqueue(pu);
                }
                foreach (QuestObjective obj in q.Objectives)
                {
                    if (obj.Type == ObjectiveType.TurnInOnly) continue;
                    chain.Enqueue(new QuestTask
                    {
                        Kind = QuestTaskKind.DoObjective,
                        QuestId = q.Id,
                        QuestName = q.Name,
                        Objective = obj,
                        ObjectiveIndex = obj.Index,
                        Position = NearestObjectivePoint(obj, db, me),
                    });
                }
                QuestTask turnIn = InteractionTask(QuestTaskKind.TurnIn, q, db, me, givers: false);
                if (turnIn != null) chain.Enqueue(turnIn);

                if (chain.Count > 0) chains.Add(chain);
            }

            plan.Tasks.AddRange(GreedyChainTour(chains, me.Location));
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

        /// <summary>
        /// Greedy NN tour over dependency chains: the frontier is every chain's current head; the
        /// nearest head to the cursor is emitted and its chain advances by one. Dependencies hold
        /// (each quest's pickup/objectives precede its turn-in) while quests interleave by REAL
        /// distance from where the character actually stands — spatially-close work still clusters
        /// (NN locality), but no phase front-loads a far task ahead of near ones.
        /// </summary>
        private static List<QuestTask> GreedyChainTour(List<Queue<QuestTask>> chains, WoWPoint from)
        {
            var result = new List<QuestTask>();
            var active = chains.Where(c => c.Count > 0).ToList();
            WoWPoint cur = from;
            while (active.Count > 0)
            {
                int bestIdx = 0;
                double best = double.MaxValue;
                for (int i = 0; i < active.Count; i++)
                {
                    QuestTask head = active[i].Peek();
                    double d = head.Position == WoWPoint.Empty
                        ? double.MaxValue - 1   // positionless heads go last, stable
                        : cur.Distance2D(head.Position);
                    if (d < best) { best = d; bestIdx = i; }
                }
                QuestTask chosen = active[bestIdx].Dequeue();
                result.Add(chosen);
                if (chosen.Position != WoWPoint.Empty)
                    cur = chosen.Position;
                if (active[bestIdx].Count == 0)
                    active.RemoveAt(bestIdx);
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
                    case ObjectiveType.Explore when obj.X == 0 && obj.Y == 0 && obj.Z == 0:
                        return false;   // explore objective with no coordinate is unrunnable
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
                if (obj.Type == ObjectiveType.Explore && obj.Map == (int)me.MapId)
                    best = Math.Min(best, me.Location.Distance2D(new WoWPoint((float)obj.X, (float)obj.Y, (float)obj.Z)));
            }
            foreach (QuestEnderEntry e in db.QuestEnders)
                if (e.QuestId == qe.Id)
                    best = Math.Min(best, NearestSpawnDistance(e.EnderId, db, me, e.EnderType == QuestObjectType.GameObject));
            return best;
        }

        private static WoWPoint NearestObjectivePoint(QuestObjective obj, QuestDatabase db, LocalPlayer me)
        {
            if (obj.Type == ObjectiveType.Explore)
                return obj.Map == (int)me.MapId
                    ? new WoWPoint((float)obj.X, (float)obj.Y, (float)obj.Z)
                    : WoWPoint.Empty;

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
