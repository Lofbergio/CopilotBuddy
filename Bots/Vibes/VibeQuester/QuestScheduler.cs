using System;
using System.Collections.Generic;
using System.Linq;
using Styx.Logic.Questing;
using Styx.WoWInternals.WoWObjects;

namespace VibeQuester
{
    public class QuestScheduler
    {
        private readonly DataLoader _dataLoader;
        private readonly ProfileBuilder _profileBuilder;
        private readonly VibeQuesterSettings _settings;
        private int _scanThreshold;
        private HashSet<uint> _completedQuests = new HashSet<uint>();
        private HashSet<uint> _blacklistedQuests = new HashSet<uint>();   // manual (UI) — no expiry
        private readonly Dictionary<uint, DateTime> _autoBlacklist = new Dictionary<uint, DateTime>(); // auto (death/no-hotspot/stuck) — TTL'd
        private readonly TimeSpan _autoBlacklistTtl;
        private readonly HashSet<uint> _autoIgnored = new HashSet<uint>();
        private readonly Dictionary<uint, DateTime> _firstSeenIncomplete = new Dictionary<uint, DateTime>();
        private static readonly TimeSpan UndoableGrace = TimeSpan.FromSeconds(120);
        private DateTime _lastScan = DateTime.MinValue;
        private static readonly TimeSpan ScanCooldown = TimeSpan.FromSeconds(10);

        public int ScanThreshold => _scanThreshold;
        public string CurrentProfilePath { get; private set; }
        public int LastQuestCount { get; private set; }
        public string LastStatus { get; private set; }
        public HashSet<int> ActiveQuestIds { get; private set; }
        public List<VendorEntry> CurrentVendors { get; set; }

        public QuestScheduler(DataLoader dataLoader, ProfileBuilder profileBuilder, VibeQuesterSettings settings)
        {
            _dataLoader = dataLoader;
            _profileBuilder = profileBuilder;
            _settings = settings;
            _scanThreshold = settings.ScanStartDistance;
            _autoBlacklistTtl = TimeSpan.FromMinutes(Math.Max(1, settings.AutoBlacklistMinutes));
            ApplyBlacklist();
        }

        public void SyncBlacklist()
        {
            _blacklistedQuests.Clear();
            ApplyBlacklist();
        }

        private void ApplyBlacklist()
        {
            foreach (int id in _settings.BlacklistedQuests)
                _blacklistedQuests.Add((uint)id);
        }

        // Auto-blacklist (death / no-hotspot / stuck) expires after a TTL so a transient failure
        // (server lag, a momentary bad hotspot) doesn't bury a quest for the whole session — it
        // gets retried once the window passes. Manual UI blacklist entries never expire.
        public void AutoBlacklistQuest(uint questId)
        {
            _autoBlacklist[questId] = DateTime.Now + _autoBlacklistTtl;
        }

        private bool IsBlacklisted(uint id)
        {
            if (_blacklistedQuests.Contains(id))
                return true;
            if (_autoBlacklist.TryGetValue(id, out DateTime expiry))
            {
                if (DateTime.Now < expiry)
                    return true;
                _autoBlacklist.Remove(id);   // expired — allow retry
            }
            return false;
        }

        public int CompletedCount => _completedQuests.Count;

        // Keep "what have I already done" correct AND monotonic — the single source of truth feeding
        // both quest exclusion (FilterAvailableQuests) and prereq-gating (CheckPrerequisites).
        // Use QuestLog.GetCompletedQuests(): it queries the server AND waits for QUEST_QUERY_COMPLETE
        // before reading, so the list is fully populated (it's what the quest engine itself trusts).
        // The other API, Questing.GetCompletedQuestIDs(), reads memory before the response lands and
        // returns a partial set (~2 ids) — which made the scheduler re-select already-done quests and
        // idle. Union so a quest seen done never flaps back to "available" within a session.
        private void RefreshCompleted(LocalPlayer me)
        {
            var fresh = me?.QuestLog?.GetCompletedQuests();
            if (fresh != null)
                foreach (uint id in fresh)
                    _completedQuests.Add(id);
        }

        // Visibility into the picker's choices. Format: pool size, the nearest few candidates as
        // id(questLevel)@distance, and the per-reason rejection tally — so a bad pick (e.g. reaching
        // far for a scaling QuestLevel=-1 quest) is obvious in the log.
        private static void PickerLog(string msg) =>
            Styx.Helpers.Logging.Write(System.Drawing.Color.MediumPurple, "[VQ-Picker] " + msg);

        public bool NeedNewScan()
        {
            return DateTime.Now - _lastScan >= ScanCooldown;
        }

        public bool ScanAndBuildProfile(LocalPlayer me)
        {
            RefreshCompleted(me);
            _lastScan = DateTime.Now;

            QuestDatabase db = _dataLoader.Database;
            if (db == null)
            {
                LastStatus = "No quest data loaded";
                return false;
            }

            List<QuestEntry> available = FilterAvailableQuests(db, me);
            LastQuestCount = available.Count;

            if (available.Count == 0)
            {
                _scanThreshold += _settings.ScanStep;
                LastStatus = _scanThreshold >= _settings.ScanMaxDistance
                    ? $"No quests at max range ({_scanThreshold}yd)"
                    : $"No quests within {_scanThreshold}yd, expanding...";
                return false;
            }

            _scanThreshold = _settings.ScanStartDistance;
            ActiveQuestIds = new HashSet<int>(available.Select(q => q.Id));

            string xml = _profileBuilder.BuildProfileXml(available, db, me.ZoneText, me.Name, me.Level, CurrentVendors, (int)me.MapId);
            CurrentProfilePath = _profileBuilder.WriteProfile(xml);
            string ids = string.Join(",", available.Select(q => q.Id));
            LastStatus = $"Found {available.Count} quests within {_scanThreshold}yd [{ids}]";

            return true;
        }

        public bool BuildForLogQuests(LocalPlayer me)
        {
            QuestDatabase db = _dataLoader.Database;
            if (db == null)
            {
                LastStatus = "No quest data loaded";
                return false;
            }

            _lastScan = DateTime.Now;

            List<int> logQuestIds = me.QuestLog.GetAllQuests().Select(q => (int)q.Id).ToList();
            if (logQuestIds.Count == 0)
            {
                LastStatus = "No quests in log";
                return false;
            }

            List<QuestEntry> logQuests = db.Quests
                .Where(q => logQuestIds.Contains(q.Id))
                .ToList();

            if (logQuests.Count == 0)
            {
                LastStatus = "No log quests match quest data";
                return false;
            }

            List<QuestEntry> withObjectives = logQuests
                .Where(q => HasObjectivesWithSpawnsInRange(q, db, me))
                .ToList();

            if (withObjectives.Count == 0)
            {
                LastStatus = "No log quests with objectives in range";
                return false;
            }

            _scanThreshold = _settings.ScanStartDistance * 2;
            ActiveQuestIds = new HashSet<int>(withObjectives.Select(q => q.Id));

            string xml = _profileBuilder.BuildProfileXml(withObjectives, db, me.ZoneText, me.Name, me.Level, CurrentVendors, (int)me.MapId);
            CurrentProfilePath = _profileBuilder.WriteProfile(xml);
            LastStatus = $"Building profile for {withObjectives.Count} log quests with objectives in range";
            LastQuestCount = withObjectives.Count;

            return true;
        }

        private List<QuestEntry> FilterAvailableQuests(QuestDatabase db, LocalPlayer me)
        {
            List<QuestEntry> result = new List<QuestEntry>();
            int rBlack = 0, rDone = 0, rInLog = 0, rMinLvl = 0, rLvlWin = 0, rMobLvl = 0, rRace = 0, rClass = 0, rPrereq = 0, rGiverFar = 0, rUnsupp = 0, rNoKill = 0;

            foreach (QuestEntry qe in db.Quests)
            {
                if (IsBlacklisted((uint)qe.Id)) { rBlack++; continue; }
                if (_completedQuests.Contains((uint)qe.Id)) { rDone++; continue; }
                if (me.QuestLog.ContainsQuest((uint)qe.Id)) { rInLog++; continue; }
                if (me.Level < qe.MinLevel) { rMinLvl++; continue; }

                // QuestLevel is the *recommended* level and is routinely above the player — most level-1
                // starting quests are QuestLevel 2-5 ("The Hunt Begins" QL2). Capping the window at
                // me.Level rejected nearly every startable quest for low characters (a fresh lvl-1 Tauren
                // in Camp Narache got 0 kept). Acceptance reach is its OWN concern, NOT mob difficulty:
                // the floor of 3 guarantees the starter chain is always reachable even if MaxMobOverLevel
                // is set low for cautious play. MinLevel (hard gate, above) and the objective mob-level
                // gate (below) govern feasibility/difficulty; this window only trims grey + over-reach.
                int qMin = Math.Max(1, me.Level - 2);
                int qMax = me.Level + Math.Max(3, _settings.MaxMobOverLevel);
                if (qe.QuestLevel > 0 && (qe.QuestLevel < qMin || qe.QuestLevel > qMax)) { rLvlWin++; continue; }

                // Level safety: skip quests whose objective mobs are too high (mob_level = creature
                // maxlevel). Catches scaling (QuestLevel=-1) / MinLevel=1 quests the QuestLevel window
                // misses — e.g. the lvl 48-52 'Spawn of Jubjub' a lvl-5 was pulling. 0 = no mob, never gated.
                if (qe.Objectives.Any(o => o.MobLevel > me.Level + _settings.MaxMobOverLevel)) { rMobLvl++; continue; }

                if (!CheckRaceAllowed(qe.AllowableRaces, me)) { rRace++; continue; }
                if (!CheckClassAllowed(qe.AllowableClasses, me)) { rClass++; continue; }
                if (!CheckPrerequisites(qe)) { rPrereq++; continue; }
                if (!IsGiverWithinThreshold(qe, db, me)) { rGiverFar++; continue; }
                if (!IsSupportedType(qe)) { rUnsupp++; continue; }
                if (!HasResolvableKillTargets(qe, db)) { rNoKill++; continue; }

                result.Add(qe);
            }

            var kept = result.OrderBy(q => q.QuestLevel).ThenBy(q => q.Id).Take(_settings.MaxQuestsPerProfile).ToList();
            PickerLog($"lvl={me.Level} map={(int)me.MapId} avail @ {_scanThreshold}yd: {kept.Count} kept ({result.Count} passed) | rejected done={rDone} inLog={rInLog} minLvl={rMinLvl} lvlWin={rLvlWin} mobLvl={rMobLvl} race={rRace} class={rClass} prereq={rPrereq} giverFar={rGiverFar} unsupp={rUnsupp} noKill={rNoKill} black={rBlack}");
            return kept;
        }

        private bool IsGiverWithinThreshold(QuestEntry qe, QuestDatabase db, LocalPlayer me)
        {
            int playerMap = (int)me.MapId;
            List<QuestGiverEntry> givers = db.QuestGivers
                .Where(g => g.QuestId == qe.Id)
                .ToList();

            foreach (QuestGiverEntry giver in givers)
            {
                string giverKey = giver.GiverId.ToString();
                if (db.CreatureSpawns.TryGetValue(giverKey, out List<SpawnPoint> spawns))
                {
                    foreach (SpawnPoint sp in spawns)
                    {
                        if (sp.Map != playerMap) continue;
                        double dx = sp.X - me.Location.X;
                        double dy = sp.Y - me.Location.Y;
                        if (Math.Sqrt(dx * dx + dy * dy) <= _scanThreshold)
                            return true;
                    }
                }
                else if (db.GameObjectSpawns.TryGetValue(giverKey, out List<SpawnPoint> goSpawns))
                {
                    foreach (SpawnPoint sp in goSpawns)
                    {
                        if (sp.Map != playerMap) continue;
                        double dx = sp.X - me.Location.X;
                        double dy = sp.Y - me.Location.Y;
                        if (Math.Sqrt(dx * dx + dy * dy) <= _scanThreshold)
                            return true;
                    }
                }
            }

            return false;
        }

        private bool HasObjectivesWithSpawnsInRange(QuestEntry qe, QuestDatabase db, LocalPlayer me)
        {
            int playerMap = (int)me.MapId;
            foreach (QuestObjective obj in qe.Objectives)
            {
                if (obj.Type == ObjectiveType.KillMob && obj.MobId > 0)
                {
                    string mobKey = obj.MobId.ToString();
                    if (db.CreatureSpawns.TryGetValue(mobKey, out List<SpawnPoint> spawns))
                    {
                        foreach (SpawnPoint sp in spawns)
                        {
                            if (sp.Map != playerMap) continue;
                            double dx = sp.X - me.Location.X;
                            double dy = sp.Y - me.Location.Y;
                            if (Math.Sqrt(dx * dx + dy * dy) <= _scanThreshold)
                                return true;
                        }
                    }
                }

                if (obj.Type == ObjectiveType.CollectFromGameObject && obj.GameObjectId > 0)
                {
                    string goKey = obj.GameObjectId.ToString();
                    if (db.GameObjectSpawns.TryGetValue(goKey, out List<SpawnPoint> spawns))
                    {
                        foreach (SpawnPoint sp in spawns)
                        {
                            if (sp.Map != playerMap) continue;
                            double dx = sp.X - me.Location.X;
                            double dy = sp.Y - me.Location.Y;
                            if (Math.Sqrt(dx * dx + dy * dy) <= _scanThreshold)
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool CheckRaceAllowed(int allowableRaces, LocalPlayer me)
        {
            if (allowableRaces == 0 || allowableRaces == -1)
                return true;

            int playerRaceBit = GetRaceBit((int)me.Race);
            return (allowableRaces & playerRaceBit) != 0;
        }

        private int GetRaceBit(int raceId)
        {
            return 1 << (raceId - 1);
        }

        private bool CheckClassAllowed(int allowableClasses, LocalPlayer me)
        {
            if (allowableClasses == 0 || allowableClasses == -1)
                return true;

            int playerClassBit = 1 << ((int)me.Class - 1);
            return (allowableClasses & playerClassBit) != 0;
        }

        private bool CheckPrerequisites(QuestEntry qe)
        {
            if (qe.PrevQuestID > 0)
            {
                if (!_completedQuests.Contains((uint)qe.PrevQuestID))
                    return false;
            }

            foreach (int prevId in qe.PreviousQuestsIds)
            {
                if (prevId > 0 && !_completedQuests.Contains((uint)prevId))
                    return false;
            }

            return true;
        }

        private bool IsSupportedType(QuestEntry qe)
        {
            if (qe.Objectives.Count == 0)
                return false;

            bool hasStartItem = qe.StartItem > 0;

            foreach (QuestObjective obj in qe.Objectives)
            {
                if (obj.Type == ObjectiveType.KillMob && obj.MobId <= 0)
                    return false;
                if (obj.Type == ObjectiveType.CollectItem && obj.ItemId <= 0)
                    return false;
                if (obj.Type == ObjectiveType.CollectFromGameObject && obj.GameObjectId <= 0)
                    return false;

                if (hasStartItem && obj.Type == ObjectiveType.CollectItem && obj.MobId == 0)
                    return false;
            }

            return true;
        }

        public void BlacklistQuest(uint questId)
        {
            _blacklistedQuests.Add(questId);
        }

        // Reject kill quests whose mobs have no static spawn at all (script/pool/event-summoned ~15%):
        // no extract can place them, so the bot would travel out, find nothing, and stall. Only gate
        // when EVERY kill target is unspawned — a mixed quest with one resolvable mob is still worth
        // taking, and the runtime no-hotspot abandon catches what slips through. CollectItem stays
        // un-gated (sourceless gathered/provided items are intentionally pickup+turnin only).
        private bool HasResolvableKillTargets(QuestEntry qe, QuestDatabase db)
        {
            var killMobs = qe.Objectives
                .Where(o => o.Type == ObjectiveType.KillMob && o.MobId > 0)
                .ToList();
            if (killMobs.Count == 0)
                return true;
            return killMobs.Any(o => db.CreatureSpawns.ContainsKey(o.MobId.ToString()));
        }

        public bool ScanAndRefresh(LocalPlayer me, int stickyQuestId = 0)
        {
            RefreshCompleted(me);
            _lastScan = DateTime.Now;

            QuestDatabase db = _dataLoader.Database;
            if (db == null)
            {
                LastStatus = "No quest data loaded";
                return false;
            }

            List<QuestEntry> allQuests = _settings.PreferNearbyQuests
                ? SelectNearbyQuests(db, me, stickyQuestId)
                : SelectLogFirst(db, me);

            if (allQuests.Count == 0)
            {
                _scanThreshold += _settings.ScanStep;
                if (_scanThreshold >= _settings.ScanMaxDistance)
                {
                    _scanThreshold = _settings.ScanStartDistance;
                    LastStatus = "No quests available";
                    CurrentProfilePath = _profileBuilder.BuildEmptyProfile(me.ZoneText, me.Level, CurrentVendors);
                    ActiveQuestIds = new HashSet<int>();
                    return true;
                }
                LastStatus = $"No quests within {_scanThreshold}yd";
                return false;
            }

            _scanThreshold = _settings.ScanStartDistance;
            ActiveQuestIds = new HashSet<int>(allQuests.Select(q => q.Id));

            HashSet<int> priorityTurnins = ComputePriorityTurnins(me, allQuests);
            string xml = _profileBuilder.BuildProfileXml(allQuests, db, me.ZoneText, me.Name, me.Level, CurrentVendors, (int)me.MapId, priorityTurnins, me.Location.X, me.Location.Y);
            CurrentProfilePath = _profileBuilder.WriteProfile(xml);
            string ids = string.Join(",", allQuests.Select(q => q.Id));
            LastStatus = $"{allQuests.Count} quests [{ids}]";
            LastQuestCount = allQuests.Count;

            return true;
        }

        // Ignore (don't abandon) log quests the bot can't finish: all-TurnInOnly quests that never
        // become turn-in-able are unsupported types (escort/use/interact). Report/delivery quests are
        // turn-in-able on accept, so they're never caught. Grace avoids racing completion registration.
        public int IgnoreUndoableLogQuests(LocalPlayer me)
        {
            QuestDatabase db = _dataLoader.Database;
            if (db == null) return 0;

            var completable = me.QuestLog.GetCompletedQuests();
            HashSet<uint> logIds = me.QuestLog.GetAllQuests().Select(q => q.Id).ToHashSet();
            int newlyIgnored = 0;

            foreach (uint id in logIds)
            {
                if (_autoIgnored.Contains(id)) continue;
                QuestEntry q = db.Quests.FirstOrDefault(x => x.Id == (int)id);
                if (q == null || q.Objectives.Count == 0) continue;
                if (!q.Objectives.All(o => o.Type == ObjectiveType.TurnInOnly)) continue;

                if (completable.Contains(id))
                {
                    _firstSeenIncomplete.Remove(id); // ready to turn in — a real report/delivery quest
                    continue;
                }
                if (!_firstSeenIncomplete.TryGetValue(id, out DateTime first))
                    _firstSeenIncomplete[id] = DateTime.Now;
                else if (DateTime.Now - first >= UndoableGrace)
                {
                    _autoIgnored.Add(id);
                    _firstSeenIncomplete.Remove(id);
                    newlyIgnored++;
                }
            }

            foreach (uint gone in _firstSeenIncomplete.Keys.Where(k => !logIds.Contains(k)).ToList())
                _firstSeenIncomplete.Remove(gone);

            return newlyIgnored;
        }

        // Log-first: all log quests, then fill remaining slots with nearby new quests.
        private List<QuestEntry> SelectLogFirst(QuestDatabase db, LocalPlayer me)
        {
            List<QuestEntry> logQuests = GetLogQuestsWithObjectives(me, db);
            int remaining = _settings.MaxQuestsPerProfile - logQuests.Count;
            List<QuestEntry> newQuests = remaining > 0
                ? FilterAvailableQuests(db, me).Take(remaining).ToList()
                : new List<QuestEntry>();
            return logQuests.Concat(newQuests).Take(_settings.MaxQuestsPerProfile).ToList();
        }

        // Proximity-first: rank log + new quests by distance to their nearest actionable spawn and take
        // the closest N. Keeps the actively-executing quest in the set (sticky) so a rescan can't pull
        // the bot off work mid-flight.
        private List<QuestEntry> SelectNearbyQuests(QuestDatabase db, LocalPlayer me, int stickyQuestId)
        {
            var pool = new List<(QuestEntry q, double dist)>();
            int logCount = 0, availCount = 0;
            foreach (QuestEntry q in GetLogQuestsWithObjectives(me, db))
            { pool.Add((q, NearestWorkDistance(q, db, me, inLog: true))); logCount++; }
            foreach (QuestEntry q in FilterAvailableQuests(db, me))
            { pool.Add((q, NearestWorkDistance(q, db, me, inLog: false))); availCount++; }

            var ordered = pool.OrderBy(p => p.dist).ToList();
            PickerLog($"pool={pool.Count} (log={logCount} avail={availCount}); nearest: " +
                string.Join("  ", ordered.Take(6).Select(p => $"{p.q.Id}({p.q.QuestLevel})@{p.dist:F0}")));

            List<QuestEntry> selected = ordered
                .Select(p => p.q)
                .Take(_settings.MaxQuestsPerProfile)
                .ToList();

            if (stickyQuestId > 0 && selected.All(q => q.Id != stickyQuestId))
            {
                QuestEntry active = db.Quests.FirstOrDefault(q => q.Id == stickyQuestId);
                if (active != null)
                {
                    if (selected.Count >= _settings.MaxQuestsPerProfile && selected.Count > 0)
                        selected.RemoveAt(selected.Count - 1); // drop the farthest to make room
                    selected.Insert(0, active);
                }
            }
            return selected;
        }

        // Distance to the nearest spawn the player interacts with for this quest: objectives + turn-in
        // for log quests, the giver for new ones. MaxValue if no spawn data (ranked last).
        private double NearestWorkDistance(QuestEntry qe, QuestDatabase db, LocalPlayer me, bool inLog)
        {
            var ids = new List<int>();
            if (inLog)
            {
                foreach (QuestObjective obj in qe.Objectives)
                {
                    if (obj.MobId > 0) ids.Add(obj.MobId);
                    if (obj.GameObjectId > 0) ids.Add(obj.GameObjectId);
                }
                ids.AddRange(db.QuestEnders.Where(e => e.QuestId == qe.Id).Select(e => e.EnderId));
            }
            else
            {
                ids.AddRange(db.QuestGivers.Where(g => g.QuestId == qe.Id).Select(g => g.GiverId));
            }
            return MinSpawnDistance(ids, db, me);
        }

        private double MinSpawnDistance(IEnumerable<int> ids, QuestDatabase db, LocalPlayer me)
        {
            int playerMap = (int)me.MapId;
            double best = double.MaxValue;
            foreach (int id in ids)
            {
                string key = id.ToString();
                if (db.CreatureSpawns.TryGetValue(key, out List<SpawnPoint> cs))
                    best = Math.Min(best, NearestOnMap(cs, playerMap, me));
                if (db.GameObjectSpawns.TryGetValue(key, out List<SpawnPoint> gs))
                    best = Math.Min(best, NearestOnMap(gs, playerMap, me));
            }
            return best;
        }

        private static double NearestOnMap(List<SpawnPoint> spawns, int playerMap, LocalPlayer me)
        {
            double best = double.MaxValue;
            foreach (SpawnPoint sp in spawns)
            {
                if (sp.Map != playerMap) continue;
                double dx = sp.X - me.Location.X;
                double dy = sp.Y - me.Location.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < best) best = d;
            }
            return best;
        }

        private List<QuestEntry> GetLogQuestsWithObjectives(LocalPlayer me, QuestDatabase db)
        {
            List<int> logIds = me.QuestLog.GetAllQuests().Select(q => (int)q.Id).ToList();
            if (logIds.Count == 0)
                return new List<QuestEntry>();

            List<QuestEntry> matches = db.Quests
                .Where(q => logIds.Contains(q.Id))
                .ToList();

            List<QuestEntry> withObjectives = matches
                .Where(q => q.Objectives.Count > 0 && IsSupportedType(q)
                         && !IsBlacklisted((uint)q.Id)
                         && !_autoIgnored.Contains((uint)q.Id))
                .ToList();

            return withObjectives;
        }

        // Quests already complete in the log AND well below the player's level: hand these in first so
        // stale completions don't sit occupying profile/log slots. Gated low-level so the proximity
        // ranker still governs current-tier quests (we only force-drain the ones we're leaving behind).
        private const int StaleTurnInLevelGap = 3;

        private HashSet<int> ComputePriorityTurnins(LocalPlayer me, IEnumerable<QuestEntry> selected)
        {
            int staleBelow = Math.Max(1, me.Level - StaleTurnInLevelGap);
            HashSet<uint> readyInLog = me.QuestLog.GetAllQuests()
                .Where(q => q.IsCompleted)
                .Select(q => q.Id)
                .ToHashSet();
            return new HashSet<int>(selected
                .Where(q => readyInLog.Contains((uint)q.Id)
                         && q.QuestLevel > 0 && q.QuestLevel < staleBelow)
                .Select(q => q.Id));
        }

        public void Reset()
        {
            _scanThreshold = _settings.ScanStartDistance;
            _completedQuests.Clear();
            ActiveQuestIds = null;
            CurrentProfilePath = null;
            _lastScan = DateTime.MinValue;
        }
    }
}
