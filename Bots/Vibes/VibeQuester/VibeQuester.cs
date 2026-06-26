using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Styx;
using Styx.Logic.POI;
using Styx.Helpers;
using Styx.Logic.AreaManagement;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Inventory;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Bots.Quest.QuestOrder;
using Bots.Vibes.Shared;
using TreeSharp;
using Action = TreeSharp.Action;
using Timer = System.Windows.Forms.Timer;   // disambiguate from System.Threading.Timer in the DLL

namespace VibeQuester
{
    public class VibeQuester : Bots.Quest.QuestBot
    {
        private DataLoader _dataLoader;
        private VendorDataLoader _vendorLoader;
        private QuestScheduler _scheduler;
        private ProfileBuilder _profileBuilder;
        private VibeQuesterSettings _settings = new VibeQuesterSettings();
        private bool _initialized;
        private bool _dataReady;
        private bool _vendorDataReady;
        private DateTime _lastScanTime = DateTime.MinValue;
        private HashSet<int> _lastLoadedQuestIds = new HashSet<int>();
        private string _profilePath;
        private string _vendorBlacklistPath;
        private Timer _restartTimer;
        private double _lastX, _lastY, _lastZ;
        private double _anchorX, _anchorY, _anchorZ;
        private bool _anchorSet;
        private DateTime _lastMovedTime = DateTime.Now;
        private DateTime _lastProgressTime = DateTime.Now;
        private DateTime _lastBlackspotTime = DateTime.MinValue;
        private bool _wasStuck;
        private bool _stuckLogged;
        private volatile int _workerThreadId = -1;   // bot worker thread, refreshed each Pulse
        private volatile bool _userStopRequested;    // a Stop raised off the worker thread = real user/UI Stop
        private bool _stopHookSubscribed;
        // Faction-safe mailing (shared with VibeGrinder). Off unless EnableMailing.
        private readonly MailboxService _mailboxes = new MailboxService();
        private readonly ConsumableProtection _consumables = new ConsumableProtection();
        private Profile _mailboxedProfile;          // last profile we populated mailboxes for
        private uint _mailboxedMap = uint.MaxValue;  // map we populated them for
        private bool _diedHookSubscribed;
        private volatile bool _forceRescan;           // event-driven prompt rescan (death / turn-in), serviced in Pulse
        private readonly Dictionary<int, int> _deathCountByQuest = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _noHotspotCount = new Dictionary<int, int>();
        private DateTime _lastNoHotspotCheck = DateTime.MinValue;
        private HashSet<int> _lastReadyInLog;
        private DateTime _lastReadyCheck = DateTime.MinValue;
        private const int DeathBlacklistThreshold = 5;
        private const int NoHotspotAbandonThreshold = 5;

        public override string Name => "VibeQuester";
        public override bool RequiresProfile => false;

        public override Composite Root => base.Root;

        public override Form ConfigurationForm
        {
            get { return new SettingsForm(_settings, Log); }
        }

        public override void Start()
        {
            _profilePath = FindProfilePath();
            _profileBuilder = new ProfileBuilder(_profilePath);
            _dataLoader = new DataLoader();
            _dataReady = _dataLoader.Load() != null;
            _vendorLoader = new VendorDataLoader();
            _vendorDataReady = _vendorLoader.Load();
            _vendorBlacklistPath = Path.Combine(Path.GetDirectoryName(_profilePath), "vendor_blacklist.txt");
            LoadVendorBlacklist();
            _scheduler = new QuestScheduler(_dataLoader, _profileBuilder, _settings);
            _initialized = true;
            _lastScanTime = DateTime.MinValue;

            // Watchdog must respect a real user Stop. We classify each stop by the thread that
            // raised it: internal recovery stops fire from the worker thread, the UI Stop button
            // from the UI thread (see OnStopRequested).
            if (!_stopHookSubscribed)
            {
                BotEvents.OnBotStopRequested += OnStopRequested;
                _stopHookSubscribed = true;
            }
            if (!_diedHookSubscribed)
            {
                BotEvents.Player.OnPlayerDied += OnPlayerDied;
                _diedHookSubscribed = true;
            }
            _userStopRequested = false;

            if (_restartTimer == null)
            {
                _restartTimer = new Timer { Interval = 1000 };
                _restartTimer.Tick += (_, _) =>
                {
                    try
                    {
                        if (!TreeRoot.IsRunning)
                        {
                            if (_userStopRequested)
                                return; // user pressed Stop — stay down until they Start again

                            if (StyxWoW.Me.Combat)
                            {
                                Log("Stopped while in combat — resuming instantly");
                                TreeRoot.Start();
                            }
                            DoScan();
                        }
                        else if (_lastScanTime != DateTime.MinValue
                              && (DateTime.Now - _lastScanTime).TotalSeconds > 30
                              && !StyxWoW.Me.Combat)
                        {
                            DoScan(); // periodic re-eval; reloads only if the quest set changed
                        }
                    }
                    catch { }
                };
            }
            // Regenerate the profile from the current position BEFORE base.Start() builds the
            // QuestOrder. The framework auto-loads the previous session's VIBEQUESTER.xml;
            // without this, QuestBot.Start() initializes its order from that stale set and the tree
            // starts a pickup for a quest no longer selected (the in-place reload can't dislodge an
            // already-running behavior). Don't start the tree yet — base.Start() must run first.
            DoScan(allowTreeStart: false);

            base.Start();

            _restartTimer.Start();
            DoScan();

            Log(_dataReady ? "Started with quest data loaded." : "Started. No quest data loaded.");
        }

        public override void Stop()
        {
            _consumables.Clear();
            base.Stop();
            Log("Stopped.");
        }

        // Populate the loaded profile's ForcedMailboxes with faction-safe boxes for the current map.
        // VibeQuester regenerates + LoadNews its profile as the quest set changes, each time replacing
        // the MailboxManager with an empty one, so re-apply whenever the profile instance or map
        // changes (rather than hooking every LoadNew site).
        private void RefreshMailboxes()
        {
            var prof = ProfileManager.CurrentProfile;
            var mgr = prof?.MailboxManager;
            if (mgr == null) return;
            uint map = StyxWoW.Me.MapId;
            if (prof == _mailboxedProfile && map == _mailboxedMap) return;
            mgr.ForcedMailboxes = _mailboxes.LoadSafeMailboxes(map);
            _mailboxedProfile = prof;
            _mailboxedMap = map;
        }

        // Distinguish a genuine user/UI Stop from the bot's own recovery stops. Internal stops
        // (Pulse stuck-handlers, engine OnNoMoreNodes, ForcedBehaviorExecutor create-failures) all
        // call TreeRoot.Stop() on the worker thread; the UI Stop button and window-close call it on
        // the UI thread. RaiseBotStopRequested fires synchronously on the calling thread, so a
        // thread-id mismatch means the user stopped us — and the watchdog must not resurrect.
        private void OnStopRequested(EventArgs args)
        {
            _userStopRequested = Environment.CurrentManagedThreadId != _workerThreadId;
        }

        // Repeated deaths on the same objective = the hotspot is a deathtrap (over-tough pack, pathing
        // into mobs). Session-blacklist after a few so a set-and-forget run stops corpse-looping. Don't
        // stop the tree here — OnPlayerDied's thread is unknown, and a Stop off the worker thread reads
        // as a user Stop; flag a rescan that Pulse services on the worker thread instead.
        private void OnPlayerDied()
        {
            var qa = (QuestOrder.Instance?.CurrentBehavior as ForcedQuestObjective)?.Objective?.QuestArea;
            if (qa?.Quest == null) return;

            int qId = (int)qa.Quest.Id;
            _deathCountByQuest.TryGetValue(qId, out int count);
            count++;
            _deathCountByQuest[qId] = count;

            if (count >= DeathBlacklistThreshold)
            {
                Log($"Died {count}x on {qa.Quest.Name} ({qId}) — auto-blacklisting (TTL) + rescanning");
                _scheduler.AutoBlacklistQuest((uint)qId);
                _forceRescan = true;
            }
            else
            {
                Log($"Died on {qa.Quest.Name} ({qId}) — death #{count}/{DeathBlacklistThreshold}");
            }
        }

        private void DoScan(bool allowTreeStart = true)
        {
            if (!_initialized || !_dataReady) return;
            if (!StyxWoW.IsInGame || StyxWoW.Me == null) return;

            _lastScanTime = DateTime.Now;

            try
            {
                _scheduler.SyncBlacklist();

                if (_vendorDataReady && StyxWoW.Me != null)
                {
                    var bl = _settings.BlacklistedVendors;
                    _scheduler.CurrentVendors = _vendorLoader.GetNearestVendors(StyxWoW.Me, "Repair", 3, bl)
                        .Concat(_vendorLoader.GetNearestVendors(StyxWoW.Me, "Food", 3, bl))
                        .Concat(_vendorLoader.GetNearestVendors(StyxWoW.Me, "Train", 2, bl))
                        .ToList();
                }

                int ignored = _scheduler.IgnoreUndoableLogQuests(StyxWoW.Me);
                if (ignored > 0)
                    Log($"Ignoring {ignored} un-completable quest(s) (escort/unsupported) — left in log, not abandoned");

                if (_scheduler.ScanAndRefresh(StyxWoW.Me, GetActiveQuestId()))
                {
                    bool hasQuests = _scheduler.ActiveQuestIds != null
                                  && _scheduler.ActiveQuestIds.Count > 0;

                    if (hasQuests)
                    {
                        if (StyxWoW.Me.Combat)
                        {
                            Log("In combat — deferring profile refresh");
                            return;
                        }

                        // skip no-op reloads: LoadNew resets QuestOrder to node 0, interrupting in-flight quests
                        bool changed = !_scheduler.ActiveQuestIds.SetEquals(_lastLoadedQuestIds);
                        // Load when the set changed, the tree isn't running, OR a foreign profile is
                        // currently loaded (e.g. a manually-loaded WAQ profile we must take over).
                        if (changed || !TreeRoot.IsRunning || !IsOwnProfileLoaded())
                        {
                            // LoadNew reinitializes QuestState.Order in place, so a running tree picks up the
                            // new order without a stop. A TreeRoot.Stop() here would run on the watchdog/UI
                            // thread, which OnStopRequested reads as a user Stop — the bot would stay down.
                            // So only Start() when not running.
                            ProfileManager.LoadNew(_scheduler.CurrentProfilePath);
                            _lastLoadedQuestIds = new HashSet<int>(_scheduler.ActiveQuestIds);
                            if (allowTreeStart && !TreeRoot.IsRunning)
                                TreeRoot.Start();
                            Log($"Profile {(changed ? "changed" : "loaded")} - {_scheduler.LastStatus} (completed known: {_scheduler.CompletedCount})");
                        }
                        return;   // our own quest profile is loaded
                    }
                }

                // No eligible quests in range (still expanding, or none within max range). VibeQuester is
                // self-feeding — own an empty profile so QuestBot idles here instead of running whatever
                // was last loaded (a manually-loaded WAQ profile, a stale session profile), and so
                // base.Start() never throws "You haven't loaded a profile".
                EnsureOwnEmptyProfileLoaded(allowTreeStart);
            }
            catch (Exception ex)
            {
                Log($"Scan error: {ex.Message}");
            }
        }

        // VibeQuester (RequiresProfile=false) must never let a profile it didn't generate be the loaded
        // one. Replaces any foreign/stale profile with our own empty profile; no-ops if ours is already
        // loaded (avoids per-scan churn).
        private void EnsureOwnEmptyProfileLoaded(bool allowTreeStart)
        {
            var me = StyxWoW.Me;
            if (me == null) return;
            if (IsOwnProfileLoaded())
            {
                if (allowTreeStart && !TreeRoot.IsRunning) TreeRoot.Start();
                return;
            }
            string path = _profileBuilder.BuildEmptyProfile(me.ZoneText, me.Level, _scheduler.CurrentVendors);
            if (path == null) return;
            ProfileManager.LoadNew(path);
            _lastLoadedQuestIds = new HashSet<int>();
            if (allowTreeStart && !TreeRoot.IsRunning) TreeRoot.Start();
            Log($"No eligible quests near here ({_scheduler.LastStatus}) — loaded empty profile (won't run a foreign profile). Move to a level-appropriate quest area.");
        }

        // Is the framework's currently-loaded profile one WE generated? Our profiles are named
        // "VibeQuester - <zone>" / "... (empty)"; anything else (e.g. "WholesomeAQ - The Barrens") is foreign.
        private static bool IsOwnProfileLoaded()
        {
            string n = ProfileManager.CurrentProfile?.Name;
            return n != null && n.StartsWith("VibeQuester", StringComparison.Ordinal);
        }

        public override void Pulse()
        {
            base.Pulse();

            // Refresh every tick: TreeRoot.Start() spins up a new worker thread on each restart.
            _workerThreadId = Environment.CurrentManagedThreadId;

            // Faction-safe mailing: keep the loaded profile's mailboxes populated + consumables
            // protected, and run the live backstop near a mailbox. The engine's mail behaviour does
            // the rest (needs MailRecipient + MailWhite/MailGreen set). Off unless EnableMailing.
            if (_settings.EnableMailing && StyxWoW.IsInGame && StyxWoW.Me != null)
            {
                RefreshMailboxes();
                _consumables.Sync();
                _mailboxes.CheckCurrentMailboxSafety();
            }

            // Service event-driven rescans (death blacklist, turn-in) on the worker thread, so the
            // Stop()/Start() is classified as recovery (not a user Stop) and the watchdog cooperates.
            if (_forceRescan && StyxWoW.IsInGame && StyxWoW.Me != null
                && !StyxWoW.Me.Combat && !StyxWoW.Me.Dead && !StyxWoW.Me.IsGhost)
            {
                _forceRescan = false;
                if (TreeRoot.IsRunning) TreeRoot.Stop();
                DoScan();
            }

            if (StyxWoW.IsInGame && StyxWoW.Me != null && !StyxWoW.Me.Combat && TreeRoot.IsRunning)
            {
                var loc = StyxWoW.Me.Location;

                if (!_anchorSet)
                {
                    _anchorX = loc.X;
                    _anchorY = loc.Y;
                    _anchorZ = loc.Z;
                    _lastProgressTime = DateTime.Now;
                    _anchorSet = true;
                }

                double dx = loc.X - _anchorX;
                double dy = loc.Y - _anchorY;
                double dz = loc.Z - _anchorZ;
                if (dx * dx + dy * dy + dz * dz > 25.0)
                {
                    _anchorX = loc.X;
                    _anchorY = loc.Y;
                    _anchorZ = loc.Z;
                    _lastProgressTime = DateTime.Now;
                }

                if (Math.Abs(loc.X - _lastX) > 0.1 || Math.Abs(loc.Y - _lastY) > 0.1 || Math.Abs(loc.Z - _lastZ) > 0.1)
                {
                    _lastX = loc.X;
                    _lastY = loc.Y;
                    _lastZ = loc.Z;
                    _lastMovedTime = DateTime.Now;
                    _wasStuck = false;
                    _stuckLogged = false;
                }
                else
                {
                    double stuckSec = (DateTime.Now - _lastMovedTime).TotalSeconds;
                    double progressSec = (DateTime.Now - _lastProgressTime).TotalSeconds;

                    if (StuckDetector.IsStuck && progressSec >= 10 && (DateTime.Now - _lastBlackspotTime).TotalSeconds > 10)
                    {
                        _lastBlackspotTime = DateTime.Now;
                        BlackspotManager.AddBlackspot(loc, 10f, 5f);
                        BlackspotManager.EnsureBlackspotsMarked();
                        Navigator.Clear();
                        Log($"No progress for 10s — added 10yd blackspot at ({loc.X:F1}, {loc.Y:F1}, {loc.Z:F1})");
                    }

                    if (stuckSec > 30 && !_wasStuck)
                    {
                        var poi = BotPoi.Current;
                        bool poiIsVendor = poi != null
                            && (poi.Type == PoiType.Sell
                             || poi.Type == PoiType.Repair
                             || poi.Type == PoiType.Buy
                             || poi.Type == PoiType.Train);

                        if (poiIsVendor)
                        {
                            _wasStuck = true;
                            _stuckLogged = true;
                            _settings.BlacklistedVendors.Add((int)poi.Entry);
                            SaveVendorBlacklist();
                            Log($"Blacklisted vendor {poi.Name} (Entry:{poi.Entry}) — stuck for {stuckSec:F0}s, forcing re-scan");
                            TreeRoot.Stop();
                        }
                        else if (MerchantFrame.Instance.IsVisible
                            && _scheduler?.CurrentVendors != null
                            && _scheduler.CurrentVendors.Any(v =>
                                Math.Sqrt(Math.Pow(v.X - loc.X, 2) + Math.Pow(v.Y - loc.Y, 2)) < 50))
                        {
                            _wasStuck = true;
                            _stuckLogged = true;
                            var stuckVendor = _scheduler.CurrentVendors
                                .Where(v => Math.Sqrt(Math.Pow(v.X - loc.X, 2) + Math.Pow(v.Y - loc.Y, 2)) < 50)
                                .OrderBy(v => Math.Sqrt(Math.Pow(v.X - loc.X, 2) + Math.Pow(v.Y - loc.Y, 2)))
                                .First();
                            _settings.BlacklistedVendors.Add(stuckVendor.Entry);
                            SaveVendorBlacklist();
                            Log($"Blacklisted vendor {stuckVendor.Name} (Entry:{stuckVendor.Entry}) — stuck for {stuckSec:F0}s (fallback), forcing re-scan");
                            TreeRoot.Stop();
                        }
                        else if (stuckSec > 180)
                        {
                            _wasStuck = true;
                            _stuckLogged = true;
                            if (_scheduler?.ActiveQuestIds != null && _scheduler.ActiveQuestIds.Count > 0)
                            {
                                int stuckQuest = FindStuckQuestByNoHotspots();
                                if (stuckQuest == 0)
                                    stuckQuest = FindStuckQuestByPoi();
                                if (stuckQuest == 0)
                                {
                                    var qpoi = BotPoi.Current;
                                    if (qpoi == null || qpoi.Type == PoiType.None)
                                    {
                                        Log($"No POI set — can't determine stuck quest ({stuckSec:F0}s). No quest blacklisted.");
                                        _wasStuck = true;
                                        _stuckLogged = true;
                                        return;
                                    }
                                    stuckQuest = _scheduler.ActiveQuestIds.First();
                                }
                                _scheduler.AutoBlacklistQuest((uint)stuckQuest);
                                Log($"Auto-blacklisted quest {stuckQuest} (TTL) — stuck for {stuckSec:F0}s, forcing re-scan");
                                TreeRoot.Stop();
                            }
                        }
                        else if (!_stuckLogged)
                        {
                            _stuckLogged = true;
                            Log($"Bot running but not moving for {stuckSec:F0}s");
                        }
                    }
                }
            }

            // Proactive no-hotspot abandon: an objective area that produced no usable hotspot
            // (degenerate/zeroed) will never path anywhere — the bot just sits. Abandon it (frees the
            // log slot) rather than waiting for the 180s physical-stuck path, which only catches the
            // never-created case. Counts per quest so a transient blip doesn't trip it.
            if (StyxWoW.IsInGame && StyxWoW.Me != null && TreeRoot.IsRunning && !StyxWoW.Me.Combat
                && (DateTime.Now - _lastNoHotspotCheck).TotalSeconds >= 2
                && (DateTime.Now - _lastMovedTime).TotalSeconds >= 10)
            {
                _lastNoHotspotCheck = DateTime.Now;
                var qa = (QuestOrder.Instance?.CurrentBehavior as ForcedQuestObjective)?.Objective?.QuestArea;
                // Read Hotspots.Count (no side effects). Do NOT poll qa.CurrentHotSpot — its getter calls
                // UpdateCurrentHotspot(), which can Dequeue() and would race the engine's own progression.
                // HotspotsCreated + empty list == the engine's "No hotspots created for quest" case.
                if (qa != null && qa.HotspotsCreated && qa.Quest != null)
                {
                    int qId = (int)qa.Quest.Id;
                    if (qa.Hotspots.Count == 0)
                    {
                        _noHotspotCount.TryGetValue(qId, out int count);
                        count++;
                        _noHotspotCount[qId] = count;
                        if (count >= NoHotspotAbandonThreshold)
                        {
                            Log($"No usable hotspot {count}x for {qa.Quest.Name} ({qId}) — abandoning + auto-blacklisting (TTL)");
                            StyxWoW.Me.QuestLog.AbandonQuestById((uint)qId);
                            _scheduler.AutoBlacklistQuest((uint)qId);
                            TreeRoot.Stop();   // worker thread → watchdog rescans without it
                        }
                        else
                        {
                            Log($"No usable hotspot for {qa.Quest.Name} ({qId}) — #{count}/{NoHotspotAbandonThreshold}");
                        }
                    }
                    else if (_noHotspotCount.TryGetValue(qId, out int prev) && prev > 0)
                    {
                        _noHotspotCount[qId] = 0;   // a later multi-step area produced hotspots — reset
                    }
                }
            }

            // Immediate rescan on turn-in: when a quest leaves the "complete in log" set it was just
            // handed in — rescan now so follow-ups get picked up instead of idling to the 30s watchdog.
            if (StyxWoW.IsInGame && StyxWoW.Me != null && TreeRoot.IsRunning
                && (DateTime.Now - _lastReadyCheck).TotalSeconds >= 1)
            {
                _lastReadyCheck = DateTime.Now;
                var readyNow = new HashSet<int>(
                    StyxWoW.Me.QuestLog.GetAllQuests().Where(q => q.IsCompleted).Select(q => (int)q.Id));
                if (_lastReadyInLog != null && _lastReadyInLog.Any(id => !readyNow.Contains(id)))
                    _forceRescan = true;
                _lastReadyInLog = readyNow;
            }

            if (_settings.SellGrey || _settings.SellWhite || _settings.SellGreen || _settings.SellBlue)
                SellByQuality();
        }

        private bool _lastFrameVisible;

        private void SellByQuality()
        {
            if (!MerchantFrame.Instance.IsVisible)
            {
                _lastFrameVisible = false;
                return;
            }

            if (!_lastFrameVisible)
            {
                _lastFrameVisible = true;
                Log("Arrived at vendor — merchant frame opened");
            }

            ItemQuality mask = ItemQuality.None;
            if (_settings.SellGrey) mask |= ItemQuality.Poor;
            if (_settings.SellWhite) mask |= ItemQuality.Common;
            if (_settings.SellGreen) mask |= ItemQuality.Uncommon;
            if (_settings.SellBlue) mask |= ItemQuality.Rare;

            if (mask == ItemQuality.None) return;

            var protectedIds = new HashSet<uint>();

            if (_dataLoader.Database != null && _scheduler?.ActiveQuestIds != null)
            {
                foreach (int qId in _scheduler.ActiveQuestIds)
                {
                    var quest = _dataLoader.Database.Quests.FirstOrDefault(q => q.Id == qId);
                    if (quest == null) continue;
                    if (quest.StartItem > 0)
                        protectedIds.Add((uint)quest.StartItem);
                    foreach (var obj in quest.Objectives)
                    {
                        if (obj.ItemId > 0)
                            protectedIds.Add((uint)obj.ItemId);
                    }
                }
            }

            // SellItemQualities has no safety net — it vendors everything in the mask except these ids.
            // White/Common is NOT junk (food, ammo, reagents, class items), so protect: best food/drink
            // (don't sell what Rest just bought), class essentials by item class, and the user keep-list
            // (ids or name substrings — catches Totems / Soul Shards that aren't reliably class Reagent).
            var food = Consumable.GetBestFood(false);
            if (food != null) protectedIds.Add(food.Entry);
            var drink = Consumable.GetBestDrink(false);
            if (drink != null) protectedIds.Add(drink.Entry);

            var keepTokens = _settings.SellKeepTokens.ToList();
            foreach (var item in StyxWoW.Me.BagItems)
            {
                var cls = item.ItemClass;
                if (cls == WoWItemClass.Projectile || cls == WoWItemClass.Quiver
                 || cls == WoWItemClass.Reagent || cls == WoWItemClass.Key)
                {
                    protectedIds.Add(item.Entry);
                    continue;
                }

                string name = item.ItemInfo?.Name ?? "";   // ItemInfo.Name is reliable for items (base.Name uses a wrong vtable)
                foreach (string tok in keepTokens)
                {
                    if (uint.TryParse(tok, out uint tokId))
                    {
                        if (item.Entry == tokId) { protectedIds.Add(item.Entry); break; }
                    }
                    else if (name.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        protectedIds.Add(item.Entry);
                        break;
                    }
                }
            }

            MerchantFrame.Instance.SellItemQualities(mask, null, protectedIds);
        }

        private static string FindProfilePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(baseDir, "Bots", "VibeQuester", "VIBEQUESTER.xml");
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return path;
        }

        internal void Log(string message)
        {
            Logging.Write(System.Drawing.Color.CornflowerBlue, $"[{Name}] {message}");
        }

        // Quest the QuestOrder is currently executing — kept in the proximity-selected set (sticky).
        private int GetActiveQuestId()
        {
            switch (QuestOrder.Instance?.CurrentBehavior)
            {
                case ForcedQuestObjective o:
                    return o.Objective?.QuestArea?.Quest != null ? (int)o.Objective.QuestArea.Quest.Id : 0;
                case ForcedQuestPickUp p:
                    return (int)p.QuestId;
                case ForcedQuestTurnIn t:
                    return (int)t.QuestId;
                default:
                    return 0;
            }
        }

        private int FindStuckQuestByNoHotspots()
        {
            if (_scheduler?.ActiveQuestIds == null || _scheduler.ActiveQuestIds.Count == 0)
                return 0;

            var currentBehavior = QuestOrder.Instance?.CurrentBehavior as ForcedQuestObjective;
            if (currentBehavior?.Objective?.QuestArea == null)
                return 0;

            var qa = currentBehavior.Objective.QuestArea;
            if (qa.HotspotsCreated)
                return 0;

            int qId = (int)qa.Quest.Id;
            if (_scheduler.ActiveQuestIds.Contains(qId))
            {
                Log($"No hotspots created for quest {qId} ({qa.Quest.Name}) — will blacklist");
                return qId;
            }

            return 0;
        }

        private int FindStuckQuestByPoi()
        {
            var poi = BotPoi.Current;
            if (poi == null) return 0;
            if (poi.Type != PoiType.Quest && poi.Type != PoiType.QuestPickUp && poi.Type != PoiType.QuestTurnIn
                && poi.Type != PoiType.Kill && poi.Type != PoiType.Loot)
                return 0;
            if (_dataLoader?.Database == null || _scheduler?.ActiveQuestIds == null)
                return 0;

            int entry = (int)poi.Entry;
            if (entry <= 0) return 0;

            foreach (int qId in _scheduler.ActiveQuestIds)
            {
                var quest = _dataLoader.Database.Quests.FirstOrDefault(q => q.Id == qId);
                if (quest == null) continue;

                if (quest.Objectives.Any(o => o.MobId == entry || o.GameObjectId == entry))
                    return qId;

                if (_dataLoader.Database.QuestGivers.Any(g => g.QuestId == qId && g.GiverId == entry))
                    return qId;

                if (_dataLoader.Database.QuestEnders.Any(e => e.QuestId == qId && e.EnderId == entry))
                    return qId;
            }

            return 0;
        }

        private void LoadVendorBlacklist()
        {
            if (!File.Exists(_vendorBlacklistPath)) return;
            try
            {
                string text = File.ReadAllText(_vendorBlacklistPath).Trim();
                _settings.VendorBlacklistText = text;
                Log($"Loaded {_settings.BlacklistedVendors.Count} blacklisted vendors");
            }
            catch (Exception ex)
            {
                Log($"Failed to load vendor blacklist: {ex.Message}");
            }
        }

        private void SaveVendorBlacklist()
        {
            try
            {
                string dir = Path.GetDirectoryName(_vendorBlacklistPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_vendorBlacklistPath, _settings.VendorBlacklistText);
            }
            catch (Exception ex)
            {
                Log($"Failed to save vendor blacklist: {ex.Message}");
            }
        }
    }
}
