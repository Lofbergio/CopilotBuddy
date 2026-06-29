using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bots.VibeGrinder.Selection;
using Bots.VibeGrinder.Synthesis;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Bots.VibeGrinder.Supervision
{
    /// <summary>
    /// Owns relocation: watches the four triggers (player intrusion, level drift, depletion,
    /// death-loop) and swaps the GrindArea in place when one fires. Lives below combat/loot in the
    /// tree, so a relocation can only happen while roaming. Leniency: it won't abandon a contested
    /// camp unless SpotSelector can offer something better.
    /// </summary>
    public class GrindSupervisor
    {
        private readonly SpotSelector _selector;
        private readonly GrindAreaSynthesizer _synth;
        private readonly FactionResolver _factions;

        private GrindSpot _current;
        private readonly Stopwatch _throttle = new Stopwatch();
        private readonly Stopwatch _sinceInstall = new Stopwatch();
        private readonly Dictionary<ulong, DateTime> _intruderSince = new();
        private readonly Queue<DateTime> _kills = new();
        private readonly Queue<DateTime> _deaths = new();
        private readonly Dictionary<WoWPoint, DateTime> _blacklist = new();
        private WoWPoint _packDeathSpot = WoWPoint.Empty;   // the camp that just killed us
        private bool _packDeathRelocatePending;             // relocate off it as soon as we're back up
        private bool _wasDead;
        private DateTime _emptySince = DateTime.MaxValue;

        // Freeze watchdog state (see StallWatchdog). _lastKillAt is unpruned (unlike the _kills window).
        private WoWPoint _lastPos;
        private DateTime _stillSince = DateTime.MaxValue;
        private DateTime _lastKillAt = DateTime.MaxValue;
        private bool _stallDiagged;

        // Adaptive add-fear: rises on pack deaths, eased by real progress (level-ups, area switches,
        // clean kill streaks) — never by idle wall-clock. Multiplies VibeGrinder's pull penalty.
        private double _crowdCaution = 1.0;
        private int _attackerPeak;
        private DateTime _attackerPeakAt = DateTime.MinValue;
        private int _lastLevel;
        private int _cleanKills;

        public GrindSupervisor(SpotSelector selector, GrindAreaSynthesizer synth, FactionResolver factions)
        {
            _selector = selector;
            _synth = synth;
            _factions = factions;
        }

        private static VibeGrinderSettings S => VibeGrinderSettings.Instance;

        /// <summary>Adaptive multiplier (≥1) on the pull add-avoidance penalty. Grows with pack deaths.</summary>
        public double CrowdCautionFactor => _crowdCaution;

        /// <summary>Active (non-expired) blacklisted centroids — passed to SpotSelector at Start.</summary>
        public List<WoWPoint> ActiveBlacklist()
        {
            DateTime now = DateTime.UtcNow;
            // Evict expired entries so the dictionary doesn't grow unbounded over a long session.
            foreach (WoWPoint key in _blacklist.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                _blacklist.Remove(key);
            return _blacklist.Keys.ToList();
        }

        public void Reset()
        {
            _current = null;
            _intruderSince.Clear();
            _kills.Clear();
            _deaths.Clear();
            _blacklist.Clear();
            _packDeathSpot = WoWPoint.Empty;
            _packDeathRelocatePending = false;
            _wasDead = false;
            _emptySince = DateTime.MaxValue;
            _crowdCaution = 1.0;
            _attackerPeak = 0;
            _attackerPeakAt = DateTime.MinValue;
            _lastLevel = 0;
            _cleanKills = 0;
            _throttle.Reset();
            _sinceInstall.Reset();
            _lastPos = WoWPoint.Empty;
            _stillSince = DateTime.MaxValue;
            _lastKillAt = DateTime.MaxValue;
            _stallDiagged = false;
        }

        public void OnInstalled(GrindSpot spot)
        {
            _current = spot;
            _intruderSince.Clear();
            _kills.Clear();
            // Clear death/streak counters too: a fresh spot must be judged on its own. Leaving
            // _deaths set re-tripped the death-loop at the new spot before it had been tried; leaving
            // _cleanKills set credited the new spot for a streak earned at the old one.
            _deaths.Clear();
            _cleanKills = 0;
            _emptySince = DateTime.MaxValue;
            _throttle.Restart();
            _sinceInstall.Restart();
            // Give the fresh spot a clean freeze clock so the travel-to-spot leg can't trip it.
            _lastKillAt = DateTime.UtcNow;
            _stillSince = DateTime.UtcNow;
            _lastPos = StyxWoW.Me?.Location ?? WoWPoint.Empty;
            _stallDiagged = false;
        }

        public void RecordKill()
        {
            _lastKillAt = DateTime.UtcNow;
            _kills.Enqueue(DateTime.UtcNow);
            // A clean streak (no death) is evidence we're handling the area — ease the fear.
            if (S.CrowdCautionKillStreak > 0 && ++_cleanKills >= S.CrowdCautionKillStreak)
            {
                _cleanKills = 0;
                EaseCaution(S.CrowdCautionEase, "clean kill streak");
            }
        }

        /// <summary>Called every botbase Pulse: death edge-detection + rolling-window pruning.</summary>
        public void Pulse()
        {
            var me = StyxWoW.Me;
            if (me == null) return;

            // Level-up eases the fear: you got stronger / picked up new tools.
            if (_lastLevel == 0) _lastLevel = me.Level;
            else if (me.Level > _lastLevel)
            {
                EaseCaution(S.CrowdCautionEase * (me.Level - _lastLevel), "leveled up");
                _lastLevel = me.Level;
            }

            // Track the peak attackers-on-us so a death can be classed pack vs honest 1v1 — at the
            // death tick mobs have already dropped target, so we sample the run-up (last ~5s). Only
            // raise on a new high; the 5s branch lets a stale peak age out but must NOT lower the
            // peak just because a mob in the current pull died (that masked real pack deaths).
            if (!me.IsDead)
            {
                int attackers = AttackersOnMe(me);
                if (attackers > _attackerPeak)
                {
                    _attackerPeak = attackers;
                    _attackerPeakAt = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - _attackerPeakAt).TotalSeconds > 5)
                {
                    _attackerPeak = attackers;
                    _attackerPeakAt = DateTime.UtcNow;
                }
            }

            bool dead = me.IsDead;
            if (dead && !_wasDead)
            {
                _deaths.Enqueue(DateTime.UtcNow);
                _cleanKills = 0;
                if (_attackerPeak >= S.PackDeathAttackers)   // a real pack death (not an honest 1v1)
                {
                    if (S.CrowdCautionStep > 0f) Escalate();
                    // Don't grind the camp that just killed us. Blacklist the death spot and flag a relocate for
                    // the moment we're back up — DON'T wait for the 3-deaths-in-5-min loop (camp deaths are
                    // minutes apart, so that never trips). Bounded by the scarcity rule: if nothing better
                    // exists we stay, but with the camp blacklisted so SelectBest won't re-pick it.
                    if (S.EnableDeathLoopRelocate)
                    {
                        _packDeathSpot = me.Location;
                        _blacklist[_packDeathSpot] = DateTime.UtcNow.AddMinutes(System.Math.Max(1, S.BlacklistMinutes));
                        _packDeathRelocatePending = true;
                        Logging.Write(System.Drawing.Color.Orange,
                            "[VibeGrinder] Died to a {0}-mob pack — blacklisting this camp and relocating when back up.", _attackerPeak);
                    }
                }
                _attackerPeak = 0;
            }
            _wasDead = dead;

            DateTime now = DateTime.UtcNow;
            while (_kills.Count > 0 && (now - _kills.Peek()).TotalSeconds > 60)
                _kills.Dequeue();
            while (_deaths.Count > 0 && (now - _deaths.Peek()).TotalMinutes > S.DeathLoopWindowMin)
                _deaths.Dequeue();

            StallWatchdog(me);
        }

        /// <summary>
        /// Freeze watchdog. The relocation triggers live in a tree branch BELOW combat/roam, so when a
        /// branch wedges returning Running (e.g. Singular's Rest gating Pull forever because there's no
        /// drink, or a target the routine never pulls), the supervisor never ticks and the bot stands
        /// still for hours. This runs from Pulse() — unconditional, immune to that starvation. A genuine
        /// freeze = alive, out of combat, not eating/drinking, not on a vendor trek, body not moving, AND
        /// no kills — for StallSeconds. A struggling-but-roaming bot resets the clock every time it moves,
        /// so this only fires on a true lockup, never on a rough patch.
        /// </summary>
        private void StallWatchdog(WoWUnit me)
        {
            if (!S.EnableStallRelocate || _current == null) return;

            bool exempt = me.IsDead || me.IsGhost || me.Combat || me.Mounted
                          || me.HasAura("Food") || me.HasAura("Drink")
                          || OnServiceRun();
            if (exempt)
            {
                _stillSince = DateTime.UtcNow;
                _lastPos = me.Location;
                _stallDiagged = false;
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (me.Location.DistanceSqr(_lastPos) > S.StallMoveEpsilon * S.StallMoveEpsilon)
            {
                _lastPos = me.Location;
                _stillSince = now;
                _stallDiagged = false;
                return;
            }

            // Stationary. Require BOTH no-move and no-kill so a brief stand (pull wind-up, looting) or a
            // spot mid-clear can't trip it. Diagnostic fires EARLY (StallDiagSeconds) and once per episode
            // — it captures the "standing with a valid target, not pulling" symptom the user sees, which
            // resolves long before the 180s break. The relocate break stays at the longer StallSeconds.
            double stillSec = (now - _stillSince).TotalSeconds;
            double sinceKill = (now - _lastKillAt).TotalSeconds;
            if (stillSec < S.StallDiagSeconds || sinceKill < S.StallDiagSeconds)
                return;

            if (!_stallDiagged)
            {
                LogStallDiagnostic(me, stillSec, sinceKill);
                _stallDiagged = true;
            }

            if (stillSec >= S.StallSeconds && sinceKill >= S.StallSeconds)
                BreakStall(me);
        }

        /// <summary>
        /// One-shot snapshot of the pull-gate state at the moment a stall is detected — pins WHY the tree
        /// isn't pulling. Logs the POI/FirstUnit/CurrentTarget GUID triple (a mismatch, or a null
        /// CurrentTarget while FirstUnit is in range, is the classic "POI Kill set but never pulls"
        /// deadlock — CanPull keys off CurrentTarget, which the not-in-combat branch only re-Targets on a
        /// POI≠FirstUnit *switch*) plus the CanPull inputs (dist/los) and reachability.
        /// </summary>
        private void LogStallDiagnostic(WoWUnit me, double stillSec, double sinceKill)
        {
            try
            {
                WoWUnit fu = Targeting.Instance.FirstUnit;
                WoWUnit ct = me.CurrentTarget;
                WoWObject poiObj = BotPoi.Current?.AsObject;
                double pull = Targeting.PullDistance;
                string poi = BotPoi.Current?.Type.ToString() ?? "null";
                double poiDist = BotPoi.Current != null ? BotPoi.Current.Location.Distance(me.Location) : -1;

                // GUID triple — reveals target-mismatch / null-CurrentTarget deadlocks at a glance.
                ulong fuGuid = fu?.Guid ?? 0, ctGuid = ct?.Guid ?? 0, poiGuid = poiObj?.Guid ?? 0;

                string fuStr = "none";
                bool reachable = false;
                if (fu != null)
                {
                    double d = fu.Location.Distance(me.Location);
                    bool los = false;
                    try { los = fu.InLineOfSpellSight; } catch { /* world query can throw on bad state */ }
                    try { reachable = Navigator.CanNavigateFully(me.Location, fu.Location); } catch { }
                    fuStr = string.Format("{0} d={1:F1} los={2} dead={3} reaction={4} canPull(fu)={5}",
                        fu.Name, d, los, fu.Dead, fu.MyReaction, d <= pull && los);
                }

                // CanPull as LevelBot evaluates it — on CurrentTarget, not FirstUnit.
                string ctStr = "null";
                if (ct != null)
                {
                    double cd = ct.Location.Distance(me.Location);
                    bool clos = false;
                    try { clos = ct.InLineOfSpellSight; } catch { }
                    ctStr = string.Format("{0} d={1:F1} los={2} canPull={3}", ct.Name, cd, clos, cd <= pull && clos);
                }

                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder] STALL {0:F0}s no-move/{1:F0}s no-kill. combat={2} moving={3} hp={4}% mana={5} " +
                    "pull={6:F0} targets={7} poi={8}(d={9:F1}) | guids poi={10:X} fu={11:X} ct={12:X} | " +
                    "firstUnit=[{13}] reachable={14} curTarget=[{15}] drink={16} food={17}",
                    stillSec, sinceKill, me.Combat, me.IsMoving, me.HealthPercent,
                    me.PowerType == WoWPowerType.Mana ? me.ManaPercent + "%" : "n/a",
                    pull, Targeting.Instance.TargetList.Count, poi, poiDist,
                    poiGuid, fuGuid, ctGuid, fuStr, reachable, ctStr,
                    me.HasAura("Drink"), me.HasAura("Food"));
            }
            catch (Exception ex)
            {
                Logging.Write(System.Drawing.Color.Orange, "[VibeGrinder] STALL diag failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Unwedge a freeze: drop + briefly blacklist the target/POI we're stuck on (so combat/roam don't
        /// re-grab it next tick) and relocate forward. Resets the freeze clock either way so the break
        /// can't spin every tick — if no better spot exists, the POI clear alone re-floats the bot.
        /// </summary>
        private void BreakStall(WoWUnit me)
        {
            WoWUnit cur = me.CurrentTarget ?? Targeting.Instance.FirstUnit;
            if (cur != null)
                Blacklist.Add(cur.Guid, TimeSpan.FromMinutes(System.Math.Max(1, S.BlacklistMinutes)));
            BotPoi.Clear("VibeGrinder stall watchdog");

            bool moved = Relocate("stall watchdog (frozen: no move, no kills)");
            if (!moved)
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder] Stall watchdog: no better spot — cleared POI + blacklisted target, retrying here.");

            _stillSince = DateTime.UtcNow;
            _lastKillAt = DateTime.UtcNow;
            _lastPos = me.Location;
            _stallDiagged = false;
        }

        /// <summary>True while travelling to service a vendor/mailbox/trainer/flightmaster (legit minutes-long idle).</summary>
        private static bool OnServiceRun()
        {
            switch (BotPoi.Current.Type)
            {
                case PoiType.Sell:
                case PoiType.Repair:
                case PoiType.Mail:
                case PoiType.Buy:
                case PoiType.Train:
                case PoiType.Fly:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Hostile mobs currently targeting me within engage range — my live add count.</summary>
        private static int AttackersOnMe(WoWUnit me)
        {
            try
            {
                int n = 0;
                foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
                {
                    if (u == null || u is WoWPlayer || u.IsDead) continue;
                    if (!u.IsHostile || u.CurrentTargetGuid != me.Guid) continue;
                    if (u.Location.Distance(me.Location) > 40f) continue;
                    n++;
                }
                return n;
            }
            catch { return 0; }
        }

        private void Escalate()
        {
            double before = _crowdCaution;
            _crowdCaution = System.Math.Min(_crowdCaution + S.CrowdCautionStep, System.Math.Max(1.0, S.CrowdCautionMax));
            if (_crowdCaution != before)
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder] Pack death ({0} attackers) — crowd caution {1:F2} → {2:F2}.",
                    _attackerPeak, before, _crowdCaution);
        }

        private void EaseCaution(double amount, string why)
        {
            if (_crowdCaution <= 1.0 || amount <= 0) return;
            double before = _crowdCaution;
            _crowdCaution = System.Math.Max(1.0, _crowdCaution - amount);
            if (_crowdCaution != before)
                Logging.Write("[VibeGrinder] Crowd caution eased ({0}): {1:F2} → {2:F2}.", why, before, _crowdCaution);
        }

        public Composite RelocationCheck()
        {
            return new Decorator(
                ctx => _current != null
                       && _throttle.Elapsed.TotalSeconds >= S.SupervisorIntervalSec
                       && StyxWoW.Me != null && !StyxWoW.Me.Combat
                       && !StyxWoW.Me.IsDead && !StyxWoW.Me.IsGhost,
                new TreeSharp.Action(ctx =>
                {
                    _throttle.Restart();
                    return Evaluate();
                }));
        }

        private RunStatus Evaluate()
        {
            var me = StyxWoW.Me;
            if (me == null || _current == null)
                return RunStatus.Failure;

            // Highest priority: we died to a pack — get OFF that camp (the death spot is already blacklisted).
            if (_packDeathRelocatePending)
            {
                _packDeathRelocatePending = false;
                return Relocate("died to a pack") ? RunStatus.Success : RunStatus.Failure;
            }

            if (S.EnableIntrusionRelocate && IntrusionTripped())
                return Relocate("player intrusion") ? RunStatus.Success : RunStatus.Failure;

            if (S.EnableLevelDriftRelocate && _current.DominantMaxLevel > 0 &&
                _current.DominantMaxLevel < me.Level - S.LevelBandBelow)
                return Relocate("out-leveled spot") ? RunStatus.Success : RunStatus.Failure;

            if (S.EnableDepletionRelocate && _sinceInstall.Elapsed.TotalSeconds > 90 && Depleted())
                return Relocate("spot depleted") ? RunStatus.Success : RunStatus.Failure;

            if (S.EnableDeathLoopRelocate && _deaths.Count >= S.DeathLoopCount)
            {
                bool moved = Relocate("death loop");
                // Acknowledge the signal either way: if nothing better exists we stay, but clearing
                // the window stops a per-interval "no better spot" log spin until it ages out.
                _deaths.Clear();
                return moved ? RunStatus.Success : RunStatus.Failure;
            }

            return RunStatus.Failure;
        }

        private bool IntrusionTripped()
        {
            DateTime now = DateTime.UtcNow;
            var seen = new HashSet<ulong>();
            bool tripped = false;

            try
            {
                foreach (WoWPlayer p in ObjectManager.GetObjectsOfType<WoWPlayer>(false, false))
                {
                    if (p == null || p.Guid == StyxWoW.Me.Guid)
                        continue;
                    if (p.Location.Distance(_current.Centroid) > S.GrindRadius)
                        continue;

                    seen.Add(p.Guid);
                    if (!_intruderSince.ContainsKey(p.Guid))
                        _intruderSince[p.Guid] = now;
                    if ((now - _intruderSince[p.Guid]).TotalSeconds >= S.IntrusionSeconds)
                        tripped = true;
                }
            }
            catch
            {
                return false;
            }

            // Forget intruders who left.
            foreach (ulong guid in _intruderSince.Keys.Where(g => !seen.Contains(g)).ToList())
                _intruderSince.Remove(guid);

            return tripped;
        }

        private bool Depleted()
        {
            // Depletion = the spot stops offering mobs, NOT a low kill rate. At low level kills/min
            // is bound by how slowly the toon kills (long casts + loot + drink), not by mob supply,
            // so a healthy camp reads as "depleted" and the bot thrashes. Drive off a sustained
            // empty target list; kills/min only confirms (don't flee a camp we were just clearing —
            // respawns are coming).
            if (Targeting.Instance.TargetList.Count == 0)
            {
                if (_emptySince == DateTime.MaxValue)
                    _emptySince = DateTime.UtcNow;
                else if ((DateTime.UtcNow - _emptySince).TotalSeconds >= S.EmptyTargetSeconds
                         && _kills.Count < S.MinKillsPerMin)
                    return true;
            }
            else
            {
                _emptySince = DateTime.MaxValue;
            }
            return false;
        }

        /// <summary>
        /// Try to move to a better spot. Returns false (stay put) when nothing better exists — the
        /// scarcity rule: don't wander off a contested-but-workable camp to nowhere.
        /// </summary>
        private bool Relocate(string reason)
        {
            var me = StyxWoW.Me;

            // Exclude the current centroid plus the active blacklist from the search.
            var exclude = ActiveBlacklist();
            exclude.Add(_current.Centroid);

            GrindSpot next = _selector.SelectBest(me.MapId, me.Location, me.Level, exclude, _crowdCaution);
            if (next == null || next.Centroid.DistanceSqr(_current.Centroid) <= S.GrindRadius * S.GrindRadius)
            {
                Logging.Write("[VibeGrinder] {0}: no better spot available — staying put.", reason);
                return false;
            }

            _blacklist[_current.Centroid] = DateTime.UtcNow.AddMinutes(S.BlacklistMinutes);
            Logging.Write("[VibeGrinder] Relocating ({0}) → {1}", reason, next);

            _synth.Install(next, me.Level);   // Install() calls EnsureProfile() itself (idempotent)
            OnInstalled(next);
            // A new area is a fresh layout — ease the fear earned at the abandoned spot.
            EaseCaution(S.CrowdCautionEase, "switched areas");
            return true;
        }
    }
}
