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
        private readonly Dictionary<WoWPoint, (DateTime expiry, float radius)> _blacklist = new();
        // Death locations (NOT cleared on spot-install — we must remember regional deaths across spot-hops so the
        // escalation can recognise "this whole AREA keeps killing me" and blacklist the cluster, not one camp).
        private readonly List<(WoWPoint loc, DateTime when)> _deathSpots = new();
        private WoWPoint _packDeathSpot = WoWPoint.Empty;   // the camp that just killed us
        private bool _packDeathRelocatePending;             // relocate off it as soon as we're back up
        private bool _wasDead;
        private DateTime _emptySince = DateTime.MaxValue;

        // Freeze watchdog state (see StallWatchdog). _lastKillAt is unpruned (unlike the _kills window).
        private WoWPoint _lastPos;
        private DateTime _stillSince = DateTime.MaxValue;
        private DateTime _lastKillAt = DateTime.MaxValue;
        private bool _stallDiagged;

        // Hard dead-man's switch (see HardStallWatchdog): last time we made REAL progress — a kill, a
        // level-up, or covering ground — and the anchor we measure net travel from. No exemptions; this is
        // the unconditional floor under the soft freeze watchdog.
        private DateTime _lastProgressAt = DateTime.MaxValue;
        private WoWPoint _hardAnchor = WoWPoint.Empty;

        /// <summary>Set by VibeGrinder: drops its pull/peel/rest/vendor latches when a hard stall forces an escape.</summary>
        public System.Action OnForceEscape { get; set; }

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

        /// <summary>Active (non-expired) blacklisted areas (centroid + radius) — passed to SpotSelector.</summary>
        public List<(WoWPoint center, float radius)> ActiveBlacklist()
        {
            DateTime now = DateTime.UtcNow;
            // Evict expired entries so the dictionary doesn't grow unbounded over a long session.
            foreach (WoWPoint key in _blacklist.Where(kv => kv.Value.expiry <= now).Select(kv => kv.Key).ToList())
                _blacklist.Remove(key);
            return _blacklist.Select(kv => (kv.Key, kv.Value.radius)).ToList();
        }

        public void Reset()
        {
            _current = null;
            _intruderSince.Clear();
            _kills.Clear();
            _deaths.Clear();
            _deathSpots.Clear();
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
            _lastProgressAt = DateTime.MaxValue;
            _hardAnchor = WoWPoint.Empty;
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
            _lastProgressAt = DateTime.UtcNow;
            _hardAnchor = StyxWoW.Me?.Location ?? WoWPoint.Empty;
        }

        public void RecordKill()
        {
            _lastKillAt = DateTime.UtcNow;
            _lastProgressAt = DateTime.UtcNow;   // a kill is real progress — resets the hard dead-man's switch
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
                _lastProgressAt = DateTime.UtcNow;   // a level-up is real progress — resets the hard dead-man's switch
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
                    if (S.CrowdCautionStep > 0f) Escalate(_attackerPeak);
                    // ESCALATING death-cluster blacklist: don't just blacklist the one camp (then hop 90yd to the
                    // next swarm in the same hive — that's how one log hit 53 deaths). The radius grows with BOTH
                    // the swarm size and how many deaths have clustered in this region, so a couple of big-pack
                    // deaths blacklist the WHOLE area and force a far relocate out of the death trap.
                    if (S.EnableDeathLoopRelocate)
                    {
                        WoWPoint here = me.Location;
                        DateTime now2 = DateTime.UtcNow;
                        _deathSpots.Add((here, now2));
                        _deathSpots.RemoveAll(d => (now2 - d.when).TotalMinutes > S.DeathRegionWindowMin);

                        float regionR2 = S.DeathRegionRadius * S.DeathRegionRadius;
                        int regional = _deathSpots.Count(d => d.loc.DistanceSqr(here) <= regionR2);   // incl. this one
                        // crowd factor: an honest 2-mob death = 1×; a 20-mob swarm scales the radius up (clamped).
                        float crowdFactor = System.Math.Clamp(
                            (float)_attackerPeak / System.Math.Max(1, S.PackDeathAttackers), 1f, S.DeathCrowdRadiusMax);
                        float radius = System.Math.Min(S.GrindRadius * regional * crowdFactor, S.MaxDeathBlacklistRadius);

                        _packDeathSpot = here;
                        _blacklist[here] = (now2.AddMinutes(System.Math.Max(1, S.BlacklistMinutes)), radius);
                        _packDeathRelocatePending = true;

                        // Also blacklist the live mobs (by GUID) INSIDE the radius so nothing re-targets them as
                        // we leave. The spatial blacklist above only stops SpotSelector re-PICKING here; it doesn't
                        // stop the corpse-run / vendor-run target drivers re-grabbing the very swarm that killed us.
                        // That gap let the bot, post-abandon, thrash its Kill POI between two in-area mobs (a
                        // Silithid Swarm ⇄ a roaming Greater Thunderhawk, ~86 flips in 3 min) and wedge a repair run.
                        // OM-visible only (can't see the far edge of a 700yd ring) — i.e. exactly the swarm on/near
                        // us. Timed (BlacklistMinutes), so the area frees up once we've gone.
                        BlacklistMobsInRadius(here, radius);

                        bool abandon = regional >= S.DeathClusterAbandonCount;
                        Logging.Write(System.Drawing.Color.Orange,
                            "[VibeGrinder] Died to a {0}-mob pack ({1} death(s) in this region) — blacklisting {2:F0}yd{3} and relocating.",
                            _attackerPeak, regional, radius, abandon ? " — ABANDONING this whole area (it keeps killing us)" : "");
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
            HardStallWatchdog(me);
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
        /// GUID-blacklist every attackable mob currently within <paramref name="radius"/> of a death spot, so no
        /// target driver (corpse-run combat, vendor-run TransitPeel, grind commitment) re-engages the pack we're
        /// fleeing. Attackable = reaction ≤ Neutral — the swarm is hostile but the in-area grind mobs we'd
        /// oscillate onto are often neutral wildlife, so hostile-only would miss half the thrash. Object-manager
        /// scoped (we only see/avoid what's loaded near us, which is the immediate threat). Timed via BlacklistMinutes.
        /// </summary>
        private void BlacklistMobsInRadius(WoWPoint center, float radius)
        {
            try
            {
                var dur = TimeSpan.FromMinutes(System.Math.Max(1, S.BlacklistMinutes));
                float r2 = radius * radius;
                int n = 0;
                foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
                {
                    if (u == null || u is WoWPlayer || u.Dead || u.IsTotem) continue;
                    if (u.MyReaction > WoWUnitReaction.Neutral) continue;          // friendly NPCs/guards: never blacklist
                    if (u.Location.DistanceSqr(center) > r2) continue;
                    Blacklist.Add(u.Guid, dur);
                    n++;
                }
                if (n > 0)
                    Logging.Write(System.Drawing.Color.Orange,
                        "[VibeGrinder] Blacklisted {0} mob(s) within {1:F0}yd of the death spot for {2}min — won't re-engage them while leaving.",
                        n, radius, System.Math.Max(1, S.BlacklistMinutes));
            }
            catch (Exception ex)
            {
                Logging.Write(System.Drawing.Color.Orange, "[VibeGrinder] Blacklist-in-radius failed: {0}", ex.Message);
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

        /// <summary>
        /// Hard dead-man's switch — the unconditional floor under StallWatchdog. Real progress = a kill or
        /// level-up (both stamp _lastProgressAt directly) or covering ground (net HardProgressRadius from the
        /// anchor = we're actually travelling somewhere). If NONE happen for HardStallMinutes the bot is doing
        /// nothing productive — no matter WHY (mounted, resting, vendoring, a fight it can't close) — so force
        /// an escape. The soft watchdog exempts mounted/service/rest and resets on any jitter, which is exactly
        /// how the "mounted in water, oscillating forever" trap slipped through; this one has NO exemptions and
        /// measures NET travel, so a legit slow patch or long trek (which keeps covering ground) can't trip it —
        /// only a genuine no-progress lockup does. Dead/ghost is the one skip: a corpse run owns its own logic
        /// and force-grinding a ghost is nonsense.
        /// </summary>
        private void HardStallWatchdog(WoWUnit me)
        {
            if (!S.EnableStallRelocate || _current == null) return;
            if (me.IsDead || me.IsGhost)
            {
                _lastProgressAt = DateTime.UtcNow;
                _hardAnchor = me.Location;
                return;
            }

            // Covering ground is progress: a legit travel/vendor trek keeps moving so it can never false-trip;
            // oscillating in place — the water bob, a wedged POI — never leaves the radius and accumulates.
            if (_hardAnchor == WoWPoint.Empty || me.Location.Distance(_hardAnchor) > S.HardProgressRadius)
            {
                _hardAnchor = me.Location;
                _lastProgressAt = DateTime.UtcNow;
                return;
            }

            if ((DateTime.UtcNow - _lastProgressAt).TotalMinutes >= S.HardStallMinutes)
                ForceEscape(me);
        }

        /// <summary>
        /// Blow away everything and MOVE. Nuke VibeGrinder's latches + POI, big-blacklist the current area so
        /// selection can't re-pick the trap, and force-relocate anywhere else. Resets the clocks so it can't
        /// spin. If selection still finds nothing, we've at least cleared the wedge + blacklisted — the next
        /// window escalates (blacklisting more each time) until we're out.
        /// </summary>
        private void ForceEscape(WoWUnit me)
        {
            Logging.Write(System.Drawing.Color.Red,
                "[VibeGrinder] HARD STALL: no kill / level / travel for {0} min — FORCING escape (mounted={1}, combat={2}, poi={3}).",
                S.HardStallMinutes, me.Mounted, me.Combat, BotPoi.Current?.Type.ToString() ?? "null");

            OnForceEscape?.Invoke();                        // VibeGrinder drops its pull/peel/rest/vendor latches
            BotPoi.Clear("VibeGrinder hard-stall escape");
            ForceRelocate("hard stall");

            _lastProgressAt = DateTime.UtcNow;
            _hardAnchor = me.Location;
            _stillSince = DateTime.UtcNow;                  // settle the soft clock too so it can't pile on
            _lastKillAt = DateTime.UtcNow;
            _stallDiagged = false;
        }

        /// <summary>
        /// Force a relocation that does NOT defer to the "no better spot → stay put" rule: big-blacklist the
        /// current centroid first so SelectBest returns a DIFFERENT, away spot (null only if the whole reachable
        /// band is now excluded — logged, retried next window).
        /// </summary>
        private bool ForceRelocate(string reason)
        {
            var me = StyxWoW.Me;
            if (me == null || _current == null) return false;

            _blacklist[_current.Centroid] = (DateTime.UtcNow.AddMinutes(Math.Max(1, S.BlacklistMinutes)), S.HardStallBlacklistRadius);

            GrindSpot next = _selector.SelectBest(me.MapId, me.Location, me.Level, ActiveBlacklist(), _crowdCaution);
            if (next == null)
            {
                Logging.Write(System.Drawing.Color.Red,
                    "[VibeGrinder] {0}: no reachable spot after {1:F0}yd blacklist — cleared state, retrying next window.",
                    reason, S.HardStallBlacklistRadius);
                return false;
            }

            Logging.Write(System.Drawing.Color.Orange, "[VibeGrinder] Force-relocating ({0}) → {1}", reason, next);
            _synth.Install(next, me.Level);
            OnInstalled(next);
            EaseCaution(S.CrowdCautionEase, "hard-stall area switch");
            return true;
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

        private void Escalate(int attackers)
        {
            double before = _crowdCaution;
            // Scale the fear jump with the swarm size: a 2-mob death nudges (1× step), a 20-mob death slams it up
            // (clamped by DeathCrowdRadiusMax) so the bot turns spot-shy fast after a real swarm, not after ten.
            float crowdFactor = System.Math.Clamp((float)attackers / System.Math.Max(1, S.PackDeathAttackers), 1f, S.DeathCrowdRadiusMax);
            _crowdCaution = System.Math.Min(_crowdCaution + S.CrowdCautionStep * crowdFactor, System.Math.Max(1.0, S.CrowdCautionMax));
            if (_crowdCaution != before)
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder] Pack death ({0} attackers) — crowd caution {1:F2} → {2:F2}.",
                    attackers, before, _crowdCaution);
        }

        private void EaseCaution(double amount, string why)
        {
            if (_crowdCaution <= 1.0 || amount <= 0) return;
            double before = _crowdCaution;
            _crowdCaution = System.Math.Max(1.0, _crowdCaution - amount);
            if (_crowdCaution != before)
                Logging.Write("[VibeGrinder] Crowd caution eased ({0}): {1:F2} → {2:F2}.", why, before, _crowdCaution);
        }

        /// <summary>
        /// SURVIVAL-CRITICAL relocates (pack death / death loop) — fleeing a camp that keeps killing us. Split
        /// out from the discretionary triggers and **placed above the ENGAGING gate in Root** so it fires even
        /// while committed to a kill. The regression it fixes: after a pack death the bot rezzes at its corpse
        /// (in the swarm), re-commits to a silithid → ENGAGING → the abandon-relocate (which lived inside the
        /// `!Engaging`-gated RelocationCheck) was starved and never ran, so it re-fought the swarm and died on
        /// loop. Still gated !Combat/!Ghost (can't relocate mid-fight or as a corpse). Unthrottled — a flag +
        /// death-count check, and we want it responsive the first out-of-combat tick after a rez. See CLAUDE.md.
        /// </summary>
        public Composite EmergencyRelocationCheck()
        {
            return new Decorator(
                ctx => _current != null
                       && StyxWoW.Me != null && !StyxWoW.Me.Combat
                       && !StyxWoW.Me.IsDead && !StyxWoW.Me.IsGhost,
                new TreeSharp.Action(ctx => EvaluateEmergency()));
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
                    return EvaluateDiscretionary();
                }));
        }

        /// <summary>Death-trap flee. Runs regardless of ENGAGING (see EmergencyRelocationCheck).</summary>
        private RunStatus EvaluateEmergency()
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

        /// <summary>
        /// DISCRETIONARY relocates (intrusion / out-level / depletion) — quality-of-grind, not survival. These
        /// stay behind the ENGAGING gate (don't abandon a kill in progress for a nearer/denser/contested spot).
        /// </summary>
        private RunStatus EvaluateDiscretionary()
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
                {
                    // Before abandoning: Targeting collects around the TOON's parked position, so a spot whose
                    // remaining mobs sit at the far edge (or a wanderer that just drifted in) reads as empty.
                    // Re-scan the WHOLE spot from its centroid, extended by DepletionRecheckBuffer, with the
                    // exact grind-target filter. If anything valid is there the spot isn't dead — reset the
                    // empty timer and keep grinding. (Backstop: StallWatchdog still relocates if we genuinely
                    // can't make progress on them, so a stuck-on-edge mob can't wedge us here.)
                    int found = ValidTargetsNearSpot(S.DepletionRecheckBuffer);
                    if (found > 0)
                    {
                        Logging.Write("[VibeGrinder] Depletion recheck: {0} valid target(s) within +{1:F0}yd of spot — staying, not relocating.",
                            found, S.DepletionRecheckBuffer);
                        _emptySince = DateTime.MaxValue;   // give the spot a fresh empty-window before re-checking
                        return false;
                    }
                    return true;
                }
            }
            else
            {
                _emptySince = DateTime.MaxValue;
            }
            return false;
        }

        /// <summary>
        /// Count valid grind targets within (GrindRadius + extraRadius) of the spot centroid, using LevelBot's
        /// exact include-filter (faction/level-band/avoid/blackspot) so this can't drift from what the bot pulls.
        /// Excludes blacklisted mobs (ones we've already given up on) so they can't keep a dead spot "alive".
        /// </summary>
        private int ValidTargetsNearSpot(float extraRadius)
        {
            var me = StyxWoW.Me;
            if (me == null || _current == null) return 0;
            float r = S.GrindRadius + extraRadius;
            float rSqr = r * r;

            var incoming = new List<WoWObject>();
            foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
            {
                if (u == null || !u.IsAlive || u.Guid == me.Guid) continue;
                if (u.Location.DistanceSqr(_current.Centroid) > rSqr) continue;
                if (Blacklist.Contains(u.Guid)) continue;
                incoming.Add(u);
            }
            if (incoming.Count == 0) return 0;

            var outgoing = new HashSet<WoWObject>();
            Bots.Grind.LevelBot.LevelBotIncludeTargetsFilter(incoming, outgoing);
            return outgoing.Count;
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
            exclude.Add((_current.Centroid, S.GrindRadius));

            GrindSpot next = _selector.SelectBest(me.MapId, me.Location, me.Level, exclude, _crowdCaution);
            if (next == null || next.Centroid.DistanceSqr(_current.Centroid) <= S.GrindRadius * S.GrindRadius)
            {
                Logging.Write("[VibeGrinder] {0}: no better spot available — staying put.", reason);
                return false;
            }

            _blacklist[_current.Centroid] = (DateTime.UtcNow.AddMinutes(S.BlacklistMinutes), S.GrindRadius);
            Logging.Write("[VibeGrinder] Relocating ({0}) → {1}", reason, next);

            _synth.Install(next, me.Level);   // Install() calls EnsureProfile() itself (idempotent)
            OnInstalled(next);
            // A new area is a fresh layout — ease the fear earned at the abandoned spot.
            EaseCaution(S.CrowdCautionEase, "switched areas");
            return true;
        }
    }
}
