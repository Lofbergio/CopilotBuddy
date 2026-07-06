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
using Styx.Logic.Profiles;
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
        private DateTime _packDeathAt = DateTime.MinValue;  // freshness + radius for the post-res GUID re-arm:
        private float _packDeathRadius;                     // the death-tick sweep is an OM snapshot from BEFORE the ghost run
        private bool _wasDead;
        // Camp-wall detector (see RecordExposureReject): exposure-rejected mobs, pruned to
        // CampWallWindowSeconds. Kills deliberately do NOT clear it — the camp-edge 1v1 nibble
        // loop it exists to break IS a stream of kills.
        private readonly List<(ulong guid, WoWPoint loc, DateTime when)> _exposureRejects = new();
        private bool _campWallRelocatePending;
        private WoWPoint _campWallSpot = WoWPoint.Empty;
        private float _campWallRadius;                  // centroid-spread-derived; set when the wall trips
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

        // Terminal unstuck ladder (see ForceEscape / ProbeAndUnstuckTick): consecutive IN-PLACE hard-stall
        // fires escalate to an 8s movement probe, then a hearthstone teleport — the only recovery that works
        // when the BODY physically cannot move. 2026-07-06 Tanaris: force-relocate fired 30× over 5h against
        // a geometry wedge, and a full client restart put the char straight back inside it; only an actual
        // server-side teleport (hearth / Stuck()) breaks the class.
        private WoWPoint _escapePos = WoWPoint.Empty;      // where the last hard-stall fired
        private DateTime _escapeAt = DateTime.MinValue;    // when ("pinned" requires recency, else a stale pos
                                                           // from hours ago false-terminals a healthy camp)
        private int _zeroMoveEscapes;                      // consecutive fires without leaving _escapePos
        private WoWPoint _probeFrom = WoWPoint.Empty;      // movement probe: position at probe start
        private DateTime _probeUntil = DateTime.MinValue;  // probe measurement deadline (MinValue = no probe)
        private WoWPoint _unstuckFrom = WoWPoint.Empty;    // set when hearth/Stuck() invoked → reselect after the jump
        private DateTime _unstuckHoldUntil = DateTime.MinValue; // Root owns the tick here so the 10s hearth cast survives
        private int _unstuckAttempts;                      // for the log — how long we've been fighting this wedge

        // Wedge-blackspot state (see WedgeBlackspotWatchdog): net-travel anchor + the marks we've placed this
        // session (persist across relocations, removed on Stop via ClearWedgeBlackspots).
        private WoWPoint _wedgeAnchor = WoWPoint.Empty;
        private DateTime _wedgeSince = DateTime.MaxValue;
        private readonly List<(Blackspot mark, DateTime placed)> _wedgeMarks = new();   // .placed drives the TTL self-heal

        // ---- Flight-path learning (see FlightLearnCheck) ----
        // The built-in FlightPaths.NeedNearbyUpdate only sees masters in the object cache (~100yd). To learn at
        // FlightLearnRadius (400yd) we do the APPROACH ourselves (Navigator.MoveTo), then hand off to that
        // built-in flow once the master is in cache — which sets a Fly POI the vendor-run latch already services.
        private WoWPoint _learnTarget = WoWPoint.Empty;   // node world-loc we're walking to
        private string _learnName;
        private DateTime _learnAbortAt;                   // approach give-up
        private DateTime _learnNextScan = DateTime.MinValue;
        private DateTime _learnLogNext = DateTime.MinValue;   // throttles the approach-progress debug line
        private readonly Dictionary<string, DateTime> _learnBan = new();  // node name -> don't retry until (attempted/unreachable)
        private List<TaxiNodeInfo> _continentNodes;       // cached DBC nodes for the current continent
        private uint _continentNodesMap = uint.MaxValue;

        // ---- Flight travel (see FlightTravelCheck) ----
        // When a relocation target is faster by taxi (or only reachable by air), we set the FlightPaths Fly POI
        // and let the vendor-run latch walk us to the start master + open the taxi (takeoff). This latch then
        // OWNS the tick while airborne (OnTaxi) and detects the landing to hand back to the grind.
        private bool _flightActive;                       // a taxi hop is in progress (pre-takeoff or airborne)
        private bool _flightTookOff;                      // we've been on the taxi this hop (→ next ground tick = landed)
        private DateTime _flightPreTakeoffAbortAt;        // give up if we never reach the master / open the map
        private DateTime _flightLogNext = DateTime.MinValue;
        private string _flightDestName;                   // end node, for logs

        /// <summary>Set by VibeGrinder: drops its pull/peel/rest/vendor latches when a hard stall forces an escape.</summary>
        public System.Action OnForceEscape { get; set; }

        /// <summary>
        /// Mirrors VibeGrinder's rest latch (set each Pulse). The zero-consumable design rests by SITTING with
        /// NO Food/Drink aura, so the soft stall watchdog's aura exemptions couldn't see it — a natural-regen
        /// rest accumulated "12s no-move" and fired a scary STALL diagnostic the instant rest exited (log
        /// 2026-07-02_1403: rest EXIT → ACQUIRE → STALL within 4ms). The HARD watchdog deliberately does NOT
        /// honor this (no exemptions, ever — rest is capped at 60s, far under its 10min fuse).
        /// </summary>
        public bool RestingLatch { get; set; }

        // Water-trap detector (see SwimTrapCheck / RecordSwimBlocked). Swim-blocks = commit→swim cycles at the
        // current spot (the bot chasing a mob into water); trips a fast relocate only if we've landed ZERO kills
        // here — so a workable shoreline camp (any kill) is never abandoned. Both reset on install.
        private int _swimBlocks;
        private int _killsSinceInstall;

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

        /// <summary>
        /// A survival flee is latched and waiting for its tick. Rest/vendor ENTRY must not start a new
        /// commitment over it (audit 2026-07-05: the post-res low-HP rest sat down AT the death camp, and
        /// a post-death durability errand latched vendor mode — both starved the flee for minutes).
        /// </summary>
        public bool HasPendingEmergencyRelocate =>
            _packDeathRelocatePending || _campWallRelocatePending
            || (S.EnableDeathLoopRelocate && _deaths.Count >= S.DeathLoopCount);


        /// <summary>Active (non-expired) blacklisted areas (centroid + radius) — passed to SpotSelector.</summary>
        public List<(WoWPoint center, float radius)> ActiveBlacklist()
        {
            DateTime now = DateTime.UtcNow;
            // Evict expired entries so the dictionary doesn't grow unbounded over a long session.
            foreach (WoWPoint key in _blacklist.Where(kv => kv.Value.expiry <= now).Select(kv => kv.Key).ToList())
                _blacklist.Remove(key);
            return _blacklist.Select(kv => (kv.Key, kv.Value.radius)).ToList();
        }

        // NOTE: no Reset() method on purpose — VibeGrinder.Start() constructs a NEW GrindSupervisor every
        // session (the reset primitive is instance-per-Start). A hand-maintained Reset() existed, was never
        // called, and had already drifted (missing _wedgeMarks) — a future instance-reuse refactor must not
        // resurrect that pattern (audit 2026-07-05).

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
            _wedgeAnchor = StyxWoW.Me?.Location ?? WoWPoint.Empty;   // fresh wedge clock — the travel leg re-anchors as it covers ground
            _wedgeSince = DateTime.UtcNow;
            _swimBlocks = 0;
            _killsSinceInstall = 0;
            // Camp-wall counting is position-ungated (see RecordExposureReject) — a fresh spot must start
            // from zero or a previous far spot's rejects would combine with this one's.
            _exposureRejects.Clear();
            _preSelected = null;   // a new spot invalidates any pre-pick made from the old one
            _nextPreSelectAt = DateTime.UtcNow.AddSeconds(60);

            // Trek safety: mark red/pack aggro bubbles along the travel leg to this spot so the navigator
            // routes AROUND them (soft cost) instead of beelining through lethal country. Every relocate
            // funnels through OnInstalled, so this covers initial install, depletion, water-trap, hard-stall.
            var me2 = StyxWoW.Me;
            if (me2 != null && me2.IsValid)
                TrekSafety.MarkLeg(_factions, me2.Location, spot.Centroid, me2.Level, me2.MapId, "grind leg");
        }

        public void RecordKill()
        {
            _lastKillAt = DateTime.UtcNow;
            _lastProgressAt = DateTime.UtcNow;   // a kill is real progress — resets the hard dead-man's switch
            _kills.Enqueue(DateTime.UtcNow);
            _killsSinceInstall++;
            // A kill proves this spot is workable — even a watery one. Clear any swim-block suspicion so the
            // water-trap relocate never abandons a shoreline camp we CAN grind.
            if (_swimBlocks > 0)
            {
                Logging.Write(System.Drawing.Color.MediumSpringGreen,
                    "[VibeGrinder/Water] kill landed — spot is workable, clearing {0} swim-block(s) and keeping it.", _swimBlocks);
                _swimBlocks = 0;
            }
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
                        _packDeathAt = now2;
                        _packDeathRadius = radius;
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
            else if (!dead && _wasDead && S.EnableDeathRearm && _packDeathRadius > 0f
                     && (DateTime.UtcNow - _packDeathAt).TotalMinutes < S.BlacklistMinutes)
            {
                // Res edge: re-arm the pack-death GUID blacklist. The death-tick sweep is an OM snapshot
                // from BEFORE the ghost run — a mob that loaded/respawned since isn't in it (the Dock
                // Worker ACQUIRE'd d=26 from the corpse, 77s after an 11-mob death). One shot per death.
                BlacklistMobsInRadius(_packDeathSpot, _packDeathRadius);
                _packDeathRadius = 0f;
            }
            _wasDead = dead;

            DateTime now = DateTime.UtcNow;
            while (_kills.Count > 0 && (now - _kills.Peek()).TotalSeconds > 60)
                _kills.Dequeue();
            while (_deaths.Count > 0 && (now - _deaths.Peek()).TotalMinutes > S.DeathLoopWindowMin)
                _deaths.Dequeue();

            if (!me.IsDead && !me.IsGhost && StyxWoW.IsInWorld)
                ProbeAndUnstuckTick(me);   // wedge-probe verdicts + the post-teleport reselect
            StallWatchdog(me);
            HardStallWatchdog(me);
            WedgeBlackspotWatchdog(me);
            SwimTrapCheck(me);
        }

        /// <summary>
        /// EXPERIMENTAL persistent wedge-blackspot (2026-07-04, Den of Flame timber fence). Styx's stuck handler
        /// only blackspots as the LAST step of its unstick escalation, and ResetUnstickAttempts() wipes that
        /// escalation on any ≥10yd jitter — so a RECURRING terrain wedge (a protruding timber the mesh thinks is
        /// passable) never gets marked and the bot re-walks into it forever (the same jitter-defeats-the-escalation
        /// failure the pull give-up clock has). This is a jitter-robust NET-travel detector: while NOT resting/
        /// transacting-at-a-service-target/mounted/in combat, with no kill and no meaningful ground covered (WedgeMoveRadius) for
        /// WedgeSeconds, we're stuck on geometry — a real move goal that isn't progressing. Drop a SOFT (60× path
        /// cost) session-length blackspot so the router bends around it; soft so a wedge that IS the only way in/out
        /// stays traversable rather than walling us in. Kills/travel/level re-anchor the fuse, so a productive tight
        /// camp or a long cross-zone trek never trips it. Marks persist across relocations, self-heal after
        /// WedgeBlackspotTtlMinutes (a misfire can't outlive that), get wiped by the hard-stall break-glass, and
        /// are removed on Stop.
        /// </summary>
        private void WedgeBlackspotWatchdog(WoWUnit me)
        {
            if (!S.EnableWedgeBlackspot || _current == null) return;
            PruneExpiredWedgeMarks();   // TTL self-heal: a misplaced mark can't persist longer than the TTL

            // NO Mounted exemption, deliberately (audit 2026-07-05): this list was seeded from StallWatchdog's,
            // but Mounted is exactly how the "mounted in water" trap dodged that watchdog for 30+ min — and
            // approach legs are MOUNTED, so a mounted terrain wedge waited 10 min for the blunt hard-stall
            // instead of 40s for the soft mark. A wedge doesn't care about the mount; net-travel detection is
            // mount-safe (a legit ride covers ground and re-anchors). Rest/combat/service hold position on
            // purpose and stay exempt.
            // Service runs are only exempt while AT the target (transacting/queueing) — the TRAVEL legs get
            // wedge detection (2026-07-06: a repair-run wedge was invisible all night because the vendor POI
            // was up the whole time; the one system built for terrain wedges never armed).
            bool exempt = me.IsDead || me.IsGhost || me.Combat
                          || me.HasAura("Food") || me.HasAura("Drink")
                          || RestingLatch
                          || (OnServiceRun() && BotPoi.Current.Location.Distance(me.Location) < 20f);
            if (exempt) { _wedgeAnchor = me.Location; _wedgeSince = DateTime.UtcNow; return; }

            DateTime now = DateTime.UtcNow;
            // Covering ground OR real progress (a kill/level bumps _lastProgressAt) since we anchored = not wedged.
            if (_wedgeAnchor == WoWPoint.Empty
                || me.Location.Distance(_wedgeAnchor) > S.WedgeMoveRadius
                || _lastProgressAt > _wedgeSince)
            {
                _wedgeAnchor = me.Location;
                _wedgeSince = now;
                return;
            }

            if ((now - _wedgeSince).TotalSeconds < S.WedgeSeconds) return;

            PlaceWedgeBlackspot(me.Location);
            _wedgeAnchor = me.Location;   // re-anchor so we don't re-fire every tick while still stuck
            _wedgeSince = now;
        }

        private void PlaceWedgeBlackspot(WoWPoint at)
        {
            // Already marked here and STILL wedging this session — the soft cost wasn't enough (or the wedge
            // point IS the destination). Two full WedgeSeconds windows stuck on the same geometry is strong
            // evidence of a real, static terrain trap, so PROMOTE it to a persisted global blackspot
            // (GlobalStuckBlackspots.xml) and retire the session copy — restarts then skip this trap instead
            // of re-learning it. Still soft cost, so the hard-stall watchdog stays the backstop if it walls us in.
            for (int i = 0; i < _wedgeMarks.Count; i++)
            {
                if (_wedgeMarks[i].mark.Location.Distance(at) < S.WedgeMinSeparation)
                {
                    var mark = _wedgeMarks[i].mark;
                    _wedgeMarks.RemoveAt(i);
                    BlackspotManager.RemoveBlackspots(new[] { mark });
                    BlackspotManager.PromoteToGlobalBlackspot(at, S.WedgeBlackspotRadius, S.WedgeBlackspotHeight);
                    Navigator.Clear();
                    Logging.Write(System.Drawing.Color.Orange,
                        "[VibeGrinder] WEDGE promoted to PERSISTENT at {0} (re-hit) — saved so restarts skip this trap; hard-stall still backstops if it walls us in.", at);
                    return;
                }
            }

            // Already a persisted global blackspot here (promoted earlier, or loaded from disk) and still stuck —
            // don't stack a redundant session mark on it; let the hard-stall watchdog force us out.
            if (BlackspotManager.IsBlackspotted(at))
            {
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder] Wedge at {0} is already blackspotted but we're stuck here again — router still routes through it (hard-stall will back us out).", at);
                return;
            }

            if (_wedgeMarks.Count >= S.MaxWedgeBlackspots)
            {
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder] Wedge blackspot cap ({0}) reached — not marking more.", S.MaxWedgeBlackspots);
                return;
            }

            var bs = new Blackspot(at, S.WedgeBlackspotRadius, S.WedgeBlackspotHeight);
            BlackspotManager.AddBlackspots(new[] { bs });
            _wedgeMarks.Add((bs, DateTime.UtcNow));
            Navigator.Clear();   // drop the cached path so it regenerates around the new blackspot
            Logging.Write(System.Drawing.Color.Orange,
                "[VibeGrinder] WEDGE (experimental): no kill and no net travel (>{0:F0}yd) for {1}s at {2} — blackspotting {3:F0}yd and rerouting.",
                S.WedgeMoveRadius, S.WedgeSeconds, at, S.WedgeBlackspotRadius);
        }

        /// <summary>
        /// Remove ALL wedge blackspots. Two callers: VibeGrinder.Stop (session teardown) and the hard-stall
        /// break-glass (ForceEscape) — if we've made zero progress for HardStallMinutes despite everything, our
        /// own marks might be part of the trap, so the emergency nuke tears them down too.
        /// </summary>
        public void ClearWedgeBlackspots()
        {
            if (_wedgeMarks.Count == 0) return;
            BlackspotManager.RemoveBlackspots(_wedgeMarks.Select(m => m.mark));
            _wedgeMarks.Clear();
        }

        /// <summary>TTL self-heal: drop wedge marks older than WedgeBlackspotTtlMinutes so a misfire (or a spot
        /// that's since become passable) can't persist the whole session. The real timber just re-marks in ~40s.</summary>
        private void PruneExpiredWedgeMarks()
        {
            if (_wedgeMarks.Count == 0) return;
            DateTime now = DateTime.UtcNow;
            var expired = _wedgeMarks.Where(m => (now - m.placed).TotalMinutes >= S.WedgeBlackspotTtlMinutes).ToList();
            if (expired.Count == 0) return;
            BlackspotManager.RemoveBlackspots(expired.Select(m => m.mark));
            _wedgeMarks.RemoveAll(m => (now - m.placed).TotalMinutes >= S.WedgeBlackspotTtlMinutes);
            Navigator.Clear();   // the freed area may now offer a shorter path
        }

        /// <summary>
        /// One exposure-reject (acquire or preempt-denial) — feeds the camp-wall detector: CampWallRejects
        /// DISTINCT mobs rejected inside CampWallWindowSeconds (position-ungated, cleared on install) = the
        /// camp is a wall we keep bouncing off. Survival-relocate away via EvaluateEmergency, which runs
        /// ABOVE the ENGAGING gate — the camp-edge 1v1s (rejected mobs that body-pull anyway) keep
        /// ENGAGING lit, so the discretionary depletion relocate can never fire: that's the
        /// durability-bleed nibble loop this breaks. Also kills the 45s re-reject log spam (respawns
        /// outlive the per-guid blacklist; the knot is spatial, the guid incidental).
        /// </summary>
        public void RecordExposureReject(WoWUnit u)
        {
            if (!S.EnableCampWallRelocate || _current == null || u == null) return;
            DateTime now = DateTime.UtcNow;
            _exposureRejects.RemoveAll(r => (now - r.when).TotalSeconds > S.CampWallWindowSeconds);
            if (_exposureRejects.All(r => r.guid != u.Guid))
                _exposureRejects.Add((u.Guid, u.Location, now));

            // Distinct mobs in the window, POSITION-UNGATED: the old 30yd nearest-anchor test only counted
            // neighbors of the LATEST reject, so a camp wider than one knot never summed (Lost Rigger spans
            // 100+yd — 12 rejects in 10 min, zero trips; audit 2026-07-05). Rejects only ever fire where
            // we're currently grinding, and the list clears on every install, so a previous far spot's
            // rejects can't leak into this count.
            if (_exposureRejects.Count >= S.CampWallRejects && !_campWallRelocatePending)
            {
                // Blacklist the CAMP: centroid of the rejects, radius covering their spread + our roam ring.
                float cx = 0, cy = 0, cz = 0;
                foreach (var r in _exposureRejects) { cx += r.loc.X; cy += r.loc.Y; cz += r.loc.Z; }
                var centroid = new WoWPoint(cx / _exposureRejects.Count, cy / _exposureRejects.Count, cz / _exposureRejects.Count);
                float spread = 0;
                foreach (var r in _exposureRejects)
                    spread = System.Math.Max(spread, (float)r.loc.Distance(centroid));
                _campWallSpot = centroid;
                _campWallRadius = System.Math.Max(S.CampWallBlacklistRadius, spread + S.GrindRadius);
                _campWallRelocatePending = true;
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder] CAMP WALL: {0} distinct mob(s) exposure-rejected within {1}s — can't be nibbled; blacklisting {2:F0}yd and relocating away.",
                    _exposureRejects.Count, S.CampWallWindowSeconds, _campWallRadius);
            }
        }

        /// <summary>
        /// One commit→swim cycle at the current spot — the bot committed to a mob and had to abandon it because
        /// reaching it means swimming. Logged loudly (this is the water-trap evidence the user watches). Cleared
        /// by any kill (RecordKill), so it only accumulates while we're catching NOTHING here.
        /// </summary>
        public void RecordSwimBlocked()
        {
            if (_current == null) return;
            _swimBlocks++;
            Logging.Write(System.Drawing.Color.LightSkyBlue,
                "[VibeGrinder/Water] swim-blocked ({0}/{1}) — {2} kill(s) here, {3:F0}s since install. Targets need swimming.",
                _swimBlocks, S.SwimTrapDrops, _killsSinceInstall, _sinceInstall.Elapsed.TotalSeconds);
        }

        /// <summary>
        /// Water-trap relocate — the fast, reactive fix for the coastal-belt waste (log 2026-07-01_2023: 30+ min
        /// swimming after Daggerspine mobs that sit in the water). NOT a selection-time ban: it abandons a spot
        /// only once the bot has PROVEN it can't work it — `SwimTrapDrops` swim-blocks with ZERO kills since
        /// arriving (any kill clears the count in RecordKill, so a grindable shoreline camp is kept). Bounds the
        /// waste to ~`SwimTrapSeconds` instead of the 10-min hard switch, and blacklists a wide-enough radius
        /// (`SwimTrapBlacklistRadius`) to step off the shoreline strip; repeated traps walk it out of the belt.
        /// </summary>
        private void SwimTrapCheck(WoWUnit me)
        {
            if (!S.EnableStallRelocate || _current == null) return;
            if (_swimBlocks < S.SwimTrapDrops || _killsSinceInstall > 0) return;
            if (_sinceInstall.Elapsed.TotalSeconds < S.SwimTrapSeconds) return;

            Logging.Write(System.Drawing.Color.Orange,
                "[VibeGrinder/Water] WATER TRAP: {0} swim-blocks, 0 kills in {1:F0}s at {2} — blacklisting {3:F0}yd and relocating off the shoreline.",
                _swimBlocks, _sinceInstall.Elapsed.TotalSeconds, _current.Centroid, S.SwimTrapBlacklistRadius);

            if (!Relocate("water trap", S.SwimTrapBlacklistRadius))
                // Nowhere better in range — reset the count so we re-arm (and the 10-min hard switch still backstops).
                _swimBlocks = 0;
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
                          || RestingLatch
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
            if (_probeUntil != DateTime.MinValue || DateTime.UtcNow < _unstuckHoldUntil) return;   // probe/hearth in flight — ProbeAndUnstuckTick owns this
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
                _escapePos = WoWPoint.Empty;   // real travel: stand down the in-place escalation
                _zeroMoveEscapes = 0;
                return;
            }

            // High alert after an in-place escape: a freed char re-anchors/kills within a couple of minutes,
            // a wedged body doesn't — re-evaluate at 3 min instead of waiting out the full window again.
            bool highAlert = _escapePos != WoWPoint.Empty
                             && (DateTime.UtcNow - _escapeAt).TotalMinutes < 10
                             && me.Location.Distance(_escapePos) < 10f;
            double windowMinutes = highAlert ? 3 : S.HardStallMinutes;

            if ((DateTime.UtcNow - _lastProgressAt).TotalMinutes >= windowMinutes)
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
            // In-place fire tracking: an escape that didn't move us means latches/relocation weren't the
            // problem. Time-bounded (a stale _escapePos from hours ago must not false-terminal a healthy
            // camp lull) and tight (10yd — a working bot roams/fights tens of yards between fires).
            bool pinned = _escapePos != WoWPoint.Empty
                          && (DateTime.UtcNow - _escapeAt).TotalMinutes < 10
                          && me.Location.Distance(_escapePos) < 10f;
            _zeroMoveEscapes = pinned ? _zeroMoveEscapes + 1 : 0;
            _escapePos = me.Location;
            _escapeAt = DateTime.UtcNow;

            if (_zeroMoveEscapes >= 1)
            {
                // Second consecutive in-place fire: dropping latches + relocating didn't move us — suspect a
                // GEOMETRY wedge (the body can't walk). Confirm with an 8s movement probe before teleporting:
                // "nothing to kill at a thin camp" and "physically cannot move" must not be conflated — a
                // false positive here burns the hearthstone and persists a global blackspot on a healthy spot.
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder] HARD STALL fire #{0} is IN-PLACE (<10yd from the last one) — probing movement for 8s before declaring a geometry wedge.",
                    _zeroMoveEscapes + 1);
                OnForceEscape?.Invoke();
                EndFlightTravel();
                BotPoi.Clear("VibeGrinder wedge probe");
                Navigator.Clear();
                WoWPoint probeTarget = _current != null && _current.Centroid.Distance(me.Location) > 25f
                    ? _current.Centroid
                    : new WoWPoint(me.Location.X + 20f, me.Location.Y, me.Location.Z);
                WoWMovement.ClickToMove(probeTarget);
                _probeFrom = me.Location;
                _probeUntil = DateTime.UtcNow.AddSeconds(8);
                // settle every clock so nothing else fires mid-probe; the probe verdict continues the ladder
                _lastProgressAt = DateTime.UtcNow;
                _hardAnchor = me.Location;
                _stillSince = DateTime.UtcNow;
                _lastKillAt = DateTime.UtcNow;
                _stallDiagged = false;
                return;
            }

            Logging.Write(System.Drawing.Color.Red,
                "[VibeGrinder] HARD STALL: no kill / level / travel for {0} min — FORCING escape (mounted={1}, combat={2}, poi={3}).",
                S.HardStallMinutes, me.Mounted, me.Combat, BotPoi.Current?.Type.ToString() ?? "null");

            OnForceEscape?.Invoke();                        // VibeGrinder drops its pull/peel/rest/vendor latches
            EndFlightTravel();                              // a hung taxi hop is a latch too — without this, _flightActive kept owning the tick post-escape (audit 2026-07-05)
            ClearWedgeBlackspots();                         // break-glass: our own wedge marks may be part of the trap — tear them down
            BotPoi.Clear("VibeGrinder hard-stall escape");
            ForceRelocate("hard stall");

            _lastProgressAt = DateTime.UtcNow;
            _hardAnchor = me.Location;
            _stillSince = DateTime.UtcNow;                  // settle the soft clock too so it can't pile on
            _lastKillAt = DateTime.UtcNow;
            _stallDiagged = false;
        }

        /// <summary>
        /// Runs every Pulse (alive + in-world only): delivers the movement-probe verdict and, after a
        /// hearth/Stuck() teleport actually jumps us, re-selects a grind spot from the NEW position.
        /// Gated on alive/in-world by the caller — a ghost corpse-run covering 100yd must not install a
        /// spot mid-death-behavior, and a loading-screen position must not feed SelectBest.
        /// </summary>
        private void ProbeAndUnstuckTick(WoWUnit me)
        {
            if (_probeUntil != DateTime.MinValue && DateTime.UtcNow >= _probeUntil)
            {
                float moved = me.Location.Distance(_probeFrom);
                _probeUntil = DateTime.MinValue;
                if (moved >= 8f)
                {
                    // Legs work — this was a logical stall (thin camp, starved branch), not geometry.
                    Logging.Write(System.Drawing.Color.Orange,
                        "[VibeGrinder] Probe moved {0:F1}yd — legs work, not a geometry wedge. Forcing a normal relocation.", moved);
                    _zeroMoveEscapes = 0;
                    BotPoi.Clear("VibeGrinder probe: legs work");
                    ForceRelocate("post-probe hard stall");
                }
                else
                {
                    InvokeUnstuck(me, moved);
                }
                _lastProgressAt = DateTime.UtcNow;
                _hardAnchor = me.Location;
                return;
            }

            if (_unstuckFrom != WoWPoint.Empty && me.Location.Distance(_unstuckFrom) > 100f)
            {
                Logging.Write(System.Drawing.Color.MediumSpringGreen,
                    "[VibeGrinder] Unstuck teleport landed {0:F0}yd away — re-selecting a grind spot from here.",
                    me.Location.Distance(_unstuckFrom));
                _unstuckFrom = WoWPoint.Empty;
                _unstuckHoldUntil = DateTime.MinValue;
                _escapePos = WoWPoint.Empty;
                _zeroMoveEscapes = 0;
                _unstuckAttempts = 0;
                // Flee evidence gathered at the wedge position is stale at the hearth destination.
                _packDeathRelocatePending = false;
                _campWallRelocatePending = false;
                ForceRelocate("post-unstuck");
            }
        }

        /// <summary>
        /// The confirmed-geometry-wedge teleport. Alive path = the hearthstone ITEM (fork- and locale-
        /// independent; the client's Stuck() has server-fork-dependent alive semantics, so it is only the
        /// fallback while the hearth is on cooldown — on AzerothCore it nudge-teleports 5yd, which walks
        /// small pockets out). The 03:15 client restart on 2026-07-06 proved relogin does NOT break this
        /// class — the char logs back in inside the wedge; only a teleport moves the body.
        /// </summary>
        private void InvokeUnstuck(WoWUnit me, float probeMoved)
        {
            _unstuckAttempts++;
            // Persist the trap so future routes bend around this pocket (soft cost — never walls anything in).
            BlackspotManager.PromoteToGlobalBlackspot(me.Location, S.WedgeBlackspotRadius, S.WedgeBlackspotHeight);

            WoWItem hearth = null;
            try { hearth = StyxWoW.Me.BagItems?.FirstOrDefault(i => i != null && i.Entry == 6948); } catch { }
            bool hearthReady = false;
            try { hearthReady = hearth != null && hearth.CooldownTimeLeft <= TimeSpan.Zero; } catch { }

            if (hearthReady)
            {
                Logging.Write(System.Drawing.Color.Red,
                    "[VibeGrinder] GEOMETRY WEDGE CONFIRMED at {0} (probe moved {1:F1}yd, attempt #{2}) — casting Hearthstone; holding all movement for the 10s cast.",
                    me.Location, probeMoved, _unstuckAttempts);
                _unstuckFrom = me.Location;
                _unstuckHoldUntil = DateTime.UtcNow.AddSeconds(13);   // cast 10s + teleport slack; Root owns these ticks
                hearth.Use();
            }
            else
            {
                Logging.Write(System.Drawing.Color.Red,
                    "[VibeGrinder] GEOMETRY WEDGE CONFIRMED at {0} (probe moved {1:F1}yd, attempt #{2}) — hearthstone {3}; trying the server auto-unstuck (Stuck()) and re-evaluating in ~3 min.",
                    me.Location, probeMoved, _unstuckAttempts,
                    hearth == null ? "not in bags" : "on cooldown " + FormatHearthCd(hearth));
                _unstuckFrom = me.Location;
                Lua.DoString("if Stuck then pcall(Stuck) end");
            }

            // Keep the high-alert window armed: no jump → next fire in ~3 min retries (hearth CD ≤ 30 min
            // bounds the whole ladder; every attempt is a RED log line, never a silent wait).
            _escapePos = me.Location;
            _escapeAt = DateTime.UtcNow;
        }

        private static string FormatHearthCd(WoWItem hearth)
        {
            try { return hearth.CooldownTimeLeft.TotalMinutes.ToString("F0") + "min"; }
            catch { return "unknown"; }
        }

        /// <summary>True while the wedge-escape hearth cast is in flight — VibeGrinder's Root owns the tick
        /// so roam/vendor movement (and the stuck-handler jitter it causes) can't interrupt the cast.</summary>
        public bool UnstuckHoldActive => DateTime.UtcNow < _unstuckHoldUntil;

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
                return Relocate("died to a pack", survival: true) ? RunStatus.Success : RunStatus.Failure;
            }

            // Camp wall: we keep exposure-rejecting the same knot — blacklist the knot itself (it's not the
            // spot centroid) and leave. Survival-class on purpose: the nibble 1v1s keep ENGAGING lit, so
            // this can never fire from the discretionary path.
            if (_campWallRelocatePending)
            {
                _campWallRelocatePending = false;
                float wallR = _campWallRadius > 0 ? _campWallRadius : S.CampWallBlacklistRadius;
                _blacklist[_campWallSpot] =
                    (DateTime.UtcNow.AddMinutes(System.Math.Max(1, S.BlacklistMinutes)), wallR);
                _exposureRejects.Clear();
                return Relocate("camp wall", wallR, survival: true) ? RunStatus.Success : RunStatus.Failure;
            }

            if (S.EnableDeathLoopRelocate && _deaths.Count >= S.DeathLoopCount)
            {
                bool moved = Relocate("death loop", survival: true);
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

            // Fluid doctrine (pre-compute the next action): SelectBest blocks the worker thread 1-2.4s, and
            // running it AT relocation time froze the bot mid-world at every camp switch. When the spot is
            // ALMOST depleted (few valid targets left) and we're calm (this method only runs out of combat),
            // pre-pick the next spot now — the eventual Relocate() then consumes a ready answer instantly.
            MaybePreSelectNextSpot(me);
            return RunStatus.Failure;
        }

        // Pre-selected next spot (see EvaluateDiscretionary) — consumed by Relocate(), invalidated on install.
        // Stamped with the caution/level it was SCORED under: a pack death raises caution right before the
        // flee calls Relocate, and serving it a spot ranked under yesterday's fear defeats the escalation
        // (audit 2026-07-05). TakePreSelected rejects on drift.
        private GrindSpot _preSelected;
        private DateTime _preSelectedAt = DateTime.MinValue;
        private double _preSelectedCaution;
        private int _preSelectedLevel;
        private DateTime _nextPreSelectAt = DateTime.MinValue;

        private void MaybePreSelectNextSpot(LocalPlayer me)
        {
            if (!S.EnableDepletionRelocate || _preSelected != null) return;
            if (DateTime.UtcNow < _nextPreSelectAt) return;
            if (_sinceInstall.Elapsed.TotalSeconds < 60) return;
            if (ValidTargetsNearSpot(S.DepletionRecheckBuffer) > 2) return;   // spot still has legs — no need yet
            _nextPreSelectAt = DateTime.UtcNow.AddSeconds(45);                // bound the scan cost while it stays thin

            var exclude = ActiveBlacklist();
            exclude.Add((_current.Centroid, S.GrindRadius));
            GrindSpot next = _selector.SelectBest(me.MapId, me.Location, me.Level, exclude, _crowdCaution);
            if (next == null || next.Centroid.DistanceSqr(_current.Centroid) <= S.GrindRadius * S.GrindRadius)
                return;
            _preSelected = next;
            _preSelectedAt = DateTime.UtcNow;
            _preSelectedCaution = _crowdCaution;
            _preSelectedLevel = (int)me.Level;
            Logging.Write("[VibeGrinder] Pre-selected the next spot ({0:F0}yd away) — relocation will be instant.",
                me.Location.Distance(next.Centroid));
        }

        /// <summary>Fresh, still-valid pre-selected spot or null. Consumes the cache.</summary>
        private GrindSpot TakePreSelected(float minDistFromCurrent)
        {
            GrindSpot cached = _preSelected;
            _preSelected = null;
            if (cached == null) return null;
            if ((DateTime.UtcNow - _preSelectedAt).TotalMinutes > 3) return null;             // stale — world moved on
            // Scored under a different fear level or character level? The pack-death flee ALWAYS raises
            // caution just before consuming this — a calm-scored spot must never satisfy a panic relocate.
            if (System.Math.Abs(_crowdCaution - _preSelectedCaution) > 0.25) return null;
            if (StyxWoW.Me != null && (int)StyxWoW.Me.Level != _preSelectedLevel) return null;
            if (cached.Centroid.DistanceSqr(_current.Centroid) <= minDistFromCurrent * minDistFromCurrent) return null;
            foreach (var (center, radius) in ActiveBlacklist())                                // blacklisted since picked?
                if (cached.Centroid.Distance(center) <= radius) return null;
            return cached;
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
        // ---- Flight-path learning ----------------------------------------------------------------
        // Opportunistic: when a flight master we haven't recorded is within FlightLearnRadius and reachable,
        // detour to it and let the built-in learn flow record it. Root gates this on !Engaging/!OnVendorRun/
        // !Combat, so it only fires during free travel; it OWNS the tick while approaching (returns Success)
        // and hands off (returns Failure) the moment the master is in cache range so the vendor-run servicing
        // + TAXIMAP_OPENED handler do the actual interact/record. Kill/level/travel keep the hard-stall happy.
        public Composite FlightLearnCheck()
        {
            // Both standard checkboxes: the record handler (HandleTaxiMapOpened) and NeedNearbyUpdate
            // hard-require UseFlightPaths, so a Learn-only detour would walk up and never record.
            return new Decorator(
                ctx => CharacterSettings.Instance.UseFlightPaths && CharacterSettings.Instance.LearnFlightPaths,
                new TreeSharp.Action(ctx => FlightLearnTick()));
        }

        private RunStatus FlightLearnTick()
        {
            var me = StyxWoW.Me;
            if (me == null || me.IsDead || me.IsGhost)
                return RunStatus.Failure;

            // A Fly POI is up → the vendor-run branch owns the interact/record now; stop driving the approach.
            if (BotPoi.Current.Type == PoiType.Fly)
                return RunStatus.Failure;

            DateTime now = DateTime.UtcNow;

            if (_learnTarget != WoWPoint.Empty)
            {
                // Success: the vendor-run servicing recorded the master while we were here.
                if (IsNodeLearned(_learnName))
                {
                    Logging.Write(System.Drawing.Color.SkyBlue,
                        "[VibeGrinder/Flight] LEARNED '{0}' ✔ — {1} flight nodes now known.", _learnName, FlightPaths.XmlNodes?.Count ?? 0);
                    ClearLearnTarget();
                    return RunStatus.Failure;
                }

                // Approach give-up: couldn't reach / interact in time. Ban it so we don't thrash on it.
                if (now > _learnAbortAt)
                {
                    Logging.Write(System.Drawing.Color.Orange,
                        "[VibeGrinder/Flight] gave up approaching '{0}' after {1}s ({2:F0}yd out) — banning {3}min.",
                        _learnName, S.FlightLearnApproachSeconds, me.Location.Distance(_learnTarget), S.FlightLearnBanMinutes);
                    _learnBan[_learnName] = now.AddMinutes(S.FlightLearnBanMinutes);
                    ClearLearnTarget();
                    return RunStatus.Failure;
                }

                // Master now in object-cache range → hand off to the built-in learn flow (sets the Fly POI the
                // vendor-run latch services). Ban it up front so a failed taxi-map open can't loop us back here.
                if (FlightPaths.NeedNearbyUpdate())
                {
                    var fm = FlightPaths.NearestFlightMerchant;
                    Logging.Write(System.Drawing.Color.SkyBlue,
                        "[VibeGrinder/Flight] Reached '{0}' (master '{1}' entry {2}) — handing off to open taxi map & record it.",
                        _learnName, fm?.Name ?? "?", fm?.Entry ?? 0);
                    _learnBan[_learnName] = now.AddMinutes(S.FlightLearnBanMinutes);
                    FlightPaths.SetPoi();
                    ClearLearnTarget();
                    return RunStatus.Failure;   // vendor-run branch takes it from here
                }

                // Still walking up — periodic progress so the detour is visible in the log.
                if (now >= _learnLogNext)
                {
                    _learnLogNext = now.AddSeconds(3);
                    Logging.WriteDebug("[VibeGrinder/Flight] approaching '{0}': {1:F0}yd to go.", _learnName, me.Location.Distance(_learnTarget));
                }
                Navigator.MoveTo(_learnTarget, "flight master (learn)");
                return RunStatus.Success;       // own the tick during the walk-up
            }

            // Idle: throttled scan for the nearest unlearned, reachable master within range.
            if (now < _learnNextScan)
                return RunStatus.Failure;
            _learnNextScan = now.AddSeconds(S.FlightLearnScanSeconds);

            TaxiNodeInfo node = NearestUnlearnedNode(me);
            if (node == null)
                return RunStatus.Failure;

            _learnTarget = node.Location;
            _learnName = node.Name;
            _learnLogNext = DateTime.MinValue;
            _learnAbortAt = now.AddSeconds(S.FlightLearnApproachSeconds);
            Logging.Write(System.Drawing.Color.SkyBlue,
                "[VibeGrinder/Flight] Unlearned flight master '{0}' {1:F0}yd away (<= {2}yd) — detouring to learn it.",
                node.Name, me.Location.Distance(node.Location), S.FlightLearnRadius);
            Navigator.MoveTo(_learnTarget, "flight master (learn)");
            return RunStatus.Success;
        }

        private void ClearLearnTarget() { _learnTarget = WoWPoint.Empty; _learnName = null; }

        // Scripted/quest/internal taxi nodes you can't walk up to and learn — they still carry a mount creature,
        // so they're excluded by NAME. Real masters look like "Orgrimmar, Durotar"; junk is "Quest - …" / "(DND)".
        private static bool IsScriptedTaxiName(string name)
        {
            return name.StartsWith("Quest", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("(DND)", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("[Unused]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Learned = we've recorded the master (MasterEntry set). A node present only as a connection target
        // (MasterEntry 0, backfilled loc) is NOT learned — we still want to visit it.
        private bool IsNodeLearned(string name)
        {
            var nodes = FlightPaths.XmlNodes;
            if (nodes == null || string.IsNullOrEmpty(name)) return false;
            XmlFlightNode n = nodes.FirstOrDefault(x => x.Name == name);
            return n != null && n.MasterEntry != 0;
        }

        private TaxiNodeInfo NearestUnlearnedNode(WoWUnit me)
        {
            uint map = StyxWoW.Me.MapId;
            if (_continentNodes == null || _continentNodesMap != map)
            {
                // Only REAL, faction-usable flight masters. Scripted/quest taxi nodes DO carry a mount creature
                // (so a mount!=0 test isn't enough — "Quest - Caverns of Time (Intro Flight Path)" passed it),
                // but they're name-tagged "Quest -" / "(DND)" / bracketed. Filter those, plus opposite-faction-only.
                bool horde = StyxWoW.Me.IsHorde;
                _continentNodes = TaxiNodeInfo.GetAll()
                    .Where(n => n.IsValid && (uint)n.MapId == map && !string.IsNullOrEmpty(n.Name)
                                && (horde ? n.HordeMountCreatureId != 0 : n.AllianceMountCreatureId != 0)
                                && !IsScriptedTaxiName(n.Name))
                    .ToList();
                _continentNodesMap = map;
                Logging.WriteDebug("[VibeGrinder/Flight] {0} usable {1} flight master(s) on map {2}.",
                    _continentNodes.Count, horde ? "Horde" : "Alliance", map);
            }

            float radius = S.FlightLearnRadius;
            DateTime now = DateTime.UtcNow;
            TaxiNodeInfo best = null;
            float bestDist = float.MaxValue;
            int unlearned = 0, inRange = 0;
            foreach (TaxiNodeInfo n in _continentNodes)
            {
                if (IsNodeLearned(n.Name)) continue;
                unlearned++;
                if (_learnBan.TryGetValue(n.Name, out DateTime until) && now < until) continue;
                float d = me.Location.Distance(n.Location);
                if (d > radius) continue;
                inRange++;
                if (d >= bestDist) continue;
                bestDist = d; best = n;
            }

            if (best == null)
            {
                // Only chatter when there's something to report (unlearned nodes exist but none in range).
                if (unlearned > 0)
                    Logging.WriteDebug("[VibeGrinder/Flight] scan: {0} unlearned node(s) on continent, none within {1}yd (nearest in-range: {2}).",
                        unlearned, radius, inRange);
                return null;
            }

            // Reachability gate on the pick only (bounded — 1 pathfind). Partial/empty path = can't walk there;
            // ban it so next scan tries the next-nearest instead of re-picking the unreachable one every tick.
            WoWPoint[] path = Navigator.GeneratePath(me.Location, best.Location);
            if (path == null || path.Length == 0 || path[path.Length - 1].Distance(best.Location) > 12f)
            {
                Logging.WriteDebug("[VibeGrinder/Flight] '{0}' {1:F0}yd away is unreachable by ground (partial path) — banning {2}min.",
                    best.Name, bestDist, S.FlightLearnBanMinutes);
                _learnBan[best.Name] = now.AddMinutes(S.FlightLearnBanMinutes);
                return null;
            }
            return best;
        }

        // ---- Flight travel ------------------------------------------------------------------------
        // Placed ABOVE the vendor-run branch in Root: while airborne it must own the tick so the vendor
        // servicing doesn't try to walk us back to the (now behind us) start-node POI. Pre-takeoff it returns
        // Failure so the vendor-run servicing does the walk-to-master + taxi-open (takeoff).
        public Composite FlightTravelCheck()
        {
            // Deliberately NOT gated on the UseFlightPaths setting: an in-progress hop must run to its end
            // (landing detection + EndFlightTravel) even if the checkbox is unticked mid-air — the setting
            // gates STARTING a flight (MaybeFlyTo), never abandoning one (adversarial review 2026-07-06).
            return new Decorator(ctx => _flightActive, new TreeSharp.Action(ctx => FlightTravelTick()));
        }

        private RunStatus FlightTravelTick()
        {
            var me = StyxWoW.Me;
            if (me == null) return RunStatus.Failure;
            DateTime now = DateTime.UtcNow;

            // Airborne on the taxi — let the server fly us; own the tick so nothing else drives movement.
            if (me.OnTaxi || me.IsOnTransport)
            {
                if (!_flightTookOff)
                    Logging.Write(System.Drawing.Color.SkyBlue, "[VibeGrinder/Flight] Airborne → {0}.", _flightDestName ?? "destination");
                _flightTookOff = true;
                if (now >= _flightLogNext)
                {
                    _flightLogNext = now.AddSeconds(5);
                    Logging.WriteDebug("[VibeGrinder/Flight] in flight to '{0}'…", _flightDestName ?? "?");
                }
                return RunStatus.Success;
            }

            // On the ground after having flown → we've landed. Hand back to the grind (it walks the last leg).
            if (_flightTookOff)
            {
                Logging.Write(System.Drawing.Color.SkyBlue,
                    "[VibeGrinder/Flight] Landed at '{0}' — resuming to grind spot.", _flightDestName ?? "destination");
                EndFlightTravel();
                return RunStatus.Failure;
            }

            // If a peel fight stole the Fly POI mid-approach, re-assert it (out of combat) so we resume walking
            // to the start master instead of waiting out the pre-takeoff abort.
            if (!me.Combat && BotPoi.Current.Type != PoiType.Fly
                && FlightPaths.Reason == FlightPathReason.Use && FlightPaths.TakingPathFrom != null)
                FlightPaths.SetPoi(FlightPaths.TakingPathFrom);

            // Pre-takeoff: still walking to the start master / opening the taxi (vendor-run servicing does this).
            // Bail to ground travel if it stalls (unreachable master, taxi won't open).
            if (now > _flightPreTakeoffAbortAt)
            {
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder/Flight] Never took off toward '{0}' in {1}s — falling back to ground travel.",
                    _flightDestName ?? "destination", S.FlightPreTakeoffSeconds);
                EndFlightTravel();
                return RunStatus.Failure;
            }
            return RunStatus.Failure;   // let the vendor-run branch service the walk-to-master + takeoff
        }

        private void EndFlightTravel()
        {
            _flightActive = false;
            _flightTookOff = false;
            _flightDestName = null;
            FlightPaths.Reset();   // clears TakingPathTo/From + the Fly POI so the vendor-run latch releases
        }

        // Decide whether to taxi to a just-installed destination, and if so set it up. Non-fatal: any failure
        // leaves us to ground-travel exactly as before. Returns true only when a taxi hop was actually started.
        private bool MaybeFlyTo(WoWPoint dest, string reason)
        {
            if (!CharacterSettings.Instance.UseFlightPaths || _flightActive) return false;
            var me = StyxWoW.Me;
            if (me == null || me.OnTaxi) return false;

            float speed = me.MovementInfo.CurrentSpeed;
            if (speed < 3f) speed = 7f;   // stationary/at-rest → assume base run speed so the estimate is sane

            // Ground-unreachable (partial/empty path) → fly if a route exists; else compare run-vs-fly time.
            WoWPoint[] path = Navigator.GeneratePath(me.Location, dest);
            bool groundUnreachable = path == null || path.Length == 0 || path[path.Length - 1].Distance(dest) > 15f;
            bool wantFly = groundUnreachable || FlightPaths.ShouldTakeFlightpath(me.Location, dest, speed);
            if (!wantFly) return false;

            // SetFlightPathUsage sets the Fly POI + route. Reason==Use means the START node's master is LEARNED
            // and we're ready to fly; Reason==Update means it wants to learn first — cancel and ground-travel
            // (our learning branch will pick that master up opportunistically instead).
            if (FlightPaths.SetFlightPathUsage(me.Location, dest, out WoWPoint startFp, out WoWPoint endFp)
                && FlightPaths.Reason == FlightPathReason.Use)
            {
                // Defense in depth behind the SetFlightPathUsage null-route fix: never commit a hop whose
                // route came back without both nodes or with a garbage endpoint (the NaN-taxi wedge class).
                float lastLeg = endFp.Distance(dest);
                if (FlightPaths.TakingPathFrom == null || FlightPaths.TakingPathTo == null || !float.IsFinite(lastLeg))
                {
                    Logging.Write(System.Drawing.Color.Orange,
                        "[VibeGrinder/Flight] rejected garbage taxi route to {0} ('{1}' → '{2}', last leg {3}) — ground travel.",
                        reason, FlightPaths.TakingPathFrom?.Name ?? "null", FlightPaths.TakingPathTo?.Name ?? "null", lastLeg);
                    FlightPaths.Reset();
                    return false;
                }

                _flightActive = true;
                _flightTookOff = false;
                _flightLogNext = DateTime.MinValue;
                _flightPreTakeoffAbortAt = DateTime.UtcNow.AddSeconds(S.FlightPreTakeoffSeconds);
                _flightDestName = FlightPaths.TakingPathTo?.Name;
                Logging.Write(System.Drawing.Color.SkyBlue,
                    "[VibeGrinder/Flight] Taxiing to {0}: '{1}' → '{2}' ({3}), then {4:F0}yd on foot to the spot.",
                    reason, FlightPaths.TakingPathFrom?.Name ?? "?", _flightDestName ?? "?",
                    groundUnreachable ? "ground-unreachable" : "faster than running", lastLeg);
                return true;
            }

            if (FlightPaths.Reason != FlightPathReason.None)
                Logging.WriteDebug("[VibeGrinder/Flight] no usable taxi route to {0} (reason {1}) — ground travel.", reason, FlightPaths.Reason);
            FlightPaths.Reset();
            return false;
        }

        // Local range exhausted → try to find (and fly to) an in-band spot anywhere on the continent.
        private bool TryFlyToFarSpot(WoWUnit me, float r, string reason, bool survival = false)
        {
            var exclude = ActiveBlacklist();
            exclude.Add((_current.Centroid, r));
            GrindSpot far = _selector.SelectBest(StyxWoW.Me.MapId, me.Location, me.Level, exclude, _crowdCaution, S.FlightMaxTravelDistance);
            if (far == null || far.Centroid.DistanceSqr(_current.Centroid) <= r * r)
                return false;

            // Commit only if a taxi route actually gets set up — else we'd strand walking to an unreachable spot.
            if (!MaybeFlyTo(far.Centroid, "far spot / " + reason))
            {
                Logging.WriteDebug("[VibeGrinder/Flight] far spot {0} found but no taxi route — staying put.", far.Centroid);
                return false;
            }

            _blacklist[_current.Centroid] = (DateTime.UtcNow.AddMinutes(S.BlacklistMinutes), r);
            _synth.Install(far, me.Level);
            OnInstalled(far);
            if (!survival)
                EaseCaution(S.CrowdCautionEase, "switched areas (flight)");
            Logging.Write(System.Drawing.Color.SkyBlue, "[VibeGrinder] Far relocate via flight ({0}) → {1}", reason, far);
            return true;
        }

        // survival: a FLEE (pack death / death loop / camp wall), not progress — it must not ease the fear
        // it just escalated (audit 2026-07-05: Escalate +0.75 then "switched areas" −0.5 in the same call
        // meant a single pack death never registered on the fear meter). Discretionary relocates keep easing.
        private bool Relocate(string reason, float blacklistRadius = 0f, bool survival = false)
        {
            var me = StyxWoW.Me;
            // Default: blacklist/exclude one GrindRadius. Callers wanting a wider push-off (the water trap)
            // pass a bigger radius so the next pick can't land back on the same strip.
            float r = blacklistRadius > 0f ? blacklistRadius : S.GrindRadius;

            // Instant path: a fresh pre-selected spot (computed during the calm almost-depleted window) skips
            // the 1-2.4s SelectBest freeze entirely. Falls through to the live scan when absent/stale.
            GrindSpot next = TakePreSelected(r);
            if (next != null)
            {
                Logging.Write("[VibeGrinder] Using pre-selected spot for '{0}' — instant relocate.", reason);
            }
            else
            {
                // Exclude the current centroid plus the active blacklist from the search.
                var exclude = ActiveBlacklist();
                exclude.Add((_current.Centroid, r));
                next = _selector.SelectBest(me.MapId, me.Location, me.Level, exclude, _crowdCaution);
            }
            if (next == null || next.Centroid.DistanceSqr(_current.Centroid) <= r * r)
            {
                // Local range is tapped out. If taxis are on, look across the whole continent for an in-band
                // spot we can FLY to (the "Dustwallow exhausted → fly to Tanaris" case). Only commits when a
                // taxi route actually exists, so we never install a far spot we can't reach.
                if (CharacterSettings.Instance.UseFlightPaths && TryFlyToFarSpot(me, r, reason, survival))
                    return true;
                Logging.Write("[VibeGrinder] {0}: no better spot available — staying put.", reason);
                return false;
            }

            _blacklist[_current.Centroid] = (DateTime.UtcNow.AddMinutes(S.BlacklistMinutes), r);
            Logging.Write("[VibeGrinder] Relocating ({0}) → {1}", reason, next);

            _synth.Install(next, me.Level);   // Install() calls EnsureProfile() itself (idempotent)
            OnInstalled(next);
            // A new area is a fresh layout — ease the fear earned at the abandoned spot. NOT for a flee:
            // that would cancel the escalation the triggering death just applied (see Relocate's doc).
            if (!survival)
                EaseCaution(S.CrowdCautionEase, "switched areas");
            // Prefer a taxi to the new spot when it beats running (or it's only reachable by air).
            MaybeFlyTo(next.Centroid, "relocate " + reason);
            return true;
        }
    }
}
