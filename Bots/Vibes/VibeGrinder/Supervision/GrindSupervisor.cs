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
        private bool _wasDead;
        private DateTime _emptySince = DateTime.MaxValue;

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
            return _blacklist.Where(kv => kv.Value > now).Select(kv => kv.Key).ToList();
        }

        public void Reset()
        {
            _current = null;
            _intruderSince.Clear();
            _kills.Clear();
            _deaths.Clear();
            _blacklist.Clear();
            _wasDead = false;
            _emptySince = DateTime.MaxValue;
            _crowdCaution = 1.0;
            _attackerPeak = 0;
            _attackerPeakAt = DateTime.MinValue;
            _lastLevel = 0;
            _cleanKills = 0;
            _throttle.Reset();
            _sinceInstall.Reset();
        }

        public void OnInstalled(GrindSpot spot)
        {
            _current = spot;
            _intruderSince.Clear();
            _kills.Clear();
            _emptySince = DateTime.MaxValue;
            _throttle.Restart();
            _sinceInstall.Restart();
        }

        public void RecordKill()
        {
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
            // death tick mobs have already dropped target, so we sample the run-up (last ~5s).
            if (!me.IsDead)
            {
                int attackers = AttackersOnMe(me);
                if (attackers >= _attackerPeak || (DateTime.UtcNow - _attackerPeakAt).TotalSeconds > 5)
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
                if (S.CrowdCautionStep > 0f && _attackerPeak >= S.PackDeathAttackers)
                    Escalate();
                _attackerPeak = 0;
            }
            _wasDead = dead;

            DateTime now = DateTime.UtcNow;
            while (_kills.Count > 0 && (now - _kills.Peek()).TotalSeconds > 60)
                _kills.Dequeue();
            while (_deaths.Count > 0 && (now - _deaths.Peek()).TotalMinutes > S.DeathLoopWindowMin)
                _deaths.Dequeue();
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

            if (S.EnableIntrusionRelocate && IntrusionTripped())
                return Relocate("player intrusion") ? RunStatus.Success : RunStatus.Failure;

            if (S.EnableLevelDriftRelocate && _current.DominantMaxLevel > 0 &&
                _current.DominantMaxLevel < me.Level - S.LevelBandBelow)
                return Relocate("out-leveled spot") ? RunStatus.Success : RunStatus.Failure;

            if (S.EnableDepletionRelocate && _sinceInstall.Elapsed.TotalSeconds > 90 && Depleted())
                return Relocate("spot depleted") ? RunStatus.Success : RunStatus.Failure;

            if (S.EnableDeathLoopRelocate && _deaths.Count >= S.DeathLoopCount)
                return Relocate("death loop") ? RunStatus.Success : RunStatus.Failure;

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

            if (_synth.MapChanged(next.Map))
                _synth.EnsureProfile();
            _synth.Install(next, me.Level);
            OnInstalled(next);
            // A new area is a fresh layout — ease the fear earned at the abandoned spot.
            EaseCaution(S.CrowdCautionEase, "switched areas");
            return true;
        }
    }
}
