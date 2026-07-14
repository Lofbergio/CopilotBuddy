using System;
using System.Collections.Generic;
using Bots.VibeGrinder;
using Bots.VibeGrinder.Data;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.Vibes.Shared
{
    /// <summary>
    /// The shared pre-combat pull pipeline, extracted verbatim from VibeGrinder (2026-07-06) so
    /// VibeQuester v2 gets the same survival machinery without a second copy. Semantics are FROZEN —
    /// every invariant documented in Bots/Vibes/VibeGrinder/CLAUDE.md ("Engagement commitment",
    /// "Fight-position pack lookahead", "Pull discipline", "Incidental hostiles", "Unengageable
    /// entries") holds here; treat that file as this class's design doc. Wire-up per host:
    ///   Targeting.Instance.WeighTargetsFilter += governor.WeighTargets;
    ///   Targeting.Instance.IncludeTargetsFilter += governor.IncludeTargets;
    /// plus OnClientPullError from a UI_ERROR_MESSAGE handler, NotifyKillEntry from OnMobKilled.
    /// Engagement knobs stay on VibeGrinderSettings (one tuning surface for both bots).
    /// </summary>
    public class EngagementGovernor
    {
        private readonly IEngagementHost _host;

        private ulong _committedGuid;
        private readonly System.Diagnostics.Stopwatch _committedTimer = new System.Diagnostics.Stopwatch();
        private double _committedLastDist = double.MaxValue;
        private bool _committedProgressed;      // made ANY approach progress / opened on the current commit? (experimental drop-ban gate)
        private string _committedName;          // last-committed mob name — held is null at the drop, so stash it for the ban log

        private int _pullErrorCount;
        private string _lastPullError;
        private DateTime _pullErrorAt = DateTime.MinValue;

        // Entry-level ban: distinct same-entry give-ups → ban the NAME (a per-guid blacklist re-learns the
        // same caged prisoner 11 times as its neighbours rotate in). _dbImmuneCache is STATIC — template
        // unit_flags are DB truth and deliberately survive Stop/Start (one SQL per new entry, ever).
        private readonly Dictionary<uint, HashSet<ulong>> _entryGiveUps = new Dictionary<uint, HashSet<ulong>>();
        private readonly Dictionary<uint, DateTime> _entryBanUntil = new Dictionary<uint, DateTime>();
        private static readonly Dictionary<uint, bool> _dbImmuneCache = new Dictionary<uint, bool>();

        // "Same elevation" tolerance: a hostile more than this many yards above/below is on a different
        // deck (a cliff/bridge) and can't reach us — verified real protection (Kenata Dabyrie, log). One
        // const, six aggro/bubble checks; a retune must not drift between them.
        private const float SameLevelZTolerance = 5f;

        // Pathability-REJECT strikes per guid (session-scoped): 3 no-path rejects of the same mob = a real
        // mesh hole, escalate past the 45s cycle. A successful path resets the guid.
        private readonly Dictionary<ulong, int> _pathRejects = new Dictionary<ulong, int>();

        private readonly System.Diagnostics.Stopwatch _entryVetoLogSw = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _eliteVetoLogSw = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _playerVetoLogSw = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _surfaceLogSw = new System.Diagnostics.Stopwatch();

        public EngagementGovernor(IEngagementHost host)
        {
            _host = host;
        }

        /// <summary>The live pre-pull commitment (0 = none). Hosts read this for their ENGAGING latch.</summary>
        public ulong CommittedGuid => _committedGuid;

        /// <summary>Drop the commitment without blacklisting (force-escape / activity teardown).</summary>
        public void DropCommit()
        {
            _committedGuid = 0;
            _committedTimer.Reset();
            _committedLastDist = double.MaxValue;
            _committedProgressed = false;
        }

        /// <summary>
        /// Client-reported pull failure (UI_ERROR_MESSAGE). Only counted pre-combat while committed; two
        /// LoS/invalid-target-class strikes within 6s → GIVE UP FAST in ApplyPullCommitment. Out-of-range
        /// is deliberately NOT counted (movement fixes it). English-client text match.
        /// </summary>
        public void OnClientPullError(string msg)
        {
            if (_committedGuid == 0 || StyxWoW.Me == null || StyxWoW.Me.Combat) return;   // only pre-combat pull attempts
            if (string.IsNullOrEmpty(msg)) return;
            if (msg.IndexOf("line of sight", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("Invalid target", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("can't attack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _pullErrorCount++;
                _lastPullError = msg;
                _pullErrorAt = DateTime.UtcNow;
                Logging.WriteDebug("[VibeGrinder/Commit] pull-error strike {0} on {1:X}: '{2}'",
                    _pullErrorCount, _committedGuid, msg);
            }
        }

        /// <summary>A kill of an entry proves it's engageable — clear its give-up strikes (the SwimTrap
        /// "one kill proves workable" rule), so real grind mobs can never accumulate to a ban.</summary>
        public void NotifyKillEntry(uint entry)
        {
            _entryGiveUps.Remove(entry);
        }

        /// <summary>
        /// The WeighTargetsFilter hook — the full veto → weigh → commit chain. See VibeGrinder CLAUDE.md.
        /// </summary>
        public void WeighTargets(List<Targeting.TargetPriority> units)
        {
            var me = StyxWoW.Me;
            if (me == null) return;

            // In combat, LOCK onto the mob we're actually fighting: pin it to the top so FirstUnit == current
            // target and LevelBot's ShouldClearPoiForBetterTarget can't hop the POI to a "better" mob mid-fight
            // and pull an add. Finish it, then re-acquire next tick.
            if (me.Combat)
            {
                PinCurrentTarget(units, me);
                return;
            }

            // 0. NEVER pre-combat commit/peel an ELITE (single chokepoint — every pull path reads the
            //    post-weigh FirstUnit). In-combat defense unchanged (early-return above).
            VetoElites(units);

            // 0a. NEVER pre-combat commit/peel a PLAYER or player-controlled unit (no initiating PvP).
            VetoPlayerUnits(units);

            // 0b. Nor a mob whose NAME has proven unengageable (DB-immune templates + session entry-bans).
            VetoBannedEntries(units);

            // ONE hostile snapshot per pulse, shared by the crowd weighting and the commit-layer exposure
            // gates below (a second full OM sweep per pulse is the decision-time-cost smell the doctrine bans).
            var hostiles = SnapshotHostiles();

            // 1. Quality weighting decides which mob is the *cleanest* to open on (runs in transit too —
            //    TransitPeel relies on the resulting isolation ordering).
            ApplyCrowdAndNeutralWeighting(units, me, hostiles);

            // 2. Commitment pins our chosen pull so we see it through instead of re-deciding every tick.
            //    The host's transit peel (vendor-run pull) commits via PeelGuid; pin THAT as FirstUnit so
            //    LevelBot's "POI is not the best pull target" can't override it. Gate on the LIVE, in-range
            //    peel (a stale/out-of-range peel falls through to the normal grind commitment).
            ulong peelGuid = _host.PeelGuid;
            WoWUnit peel = peelGuid != 0 ? FindCandidate(units, peelGuid) : null;
            if (peel != null && !peel.Dead && peel.Distance <= Targeting.PullDistance * 1.5)
                PinGuid(units, peelGuid);
            else if (!_host.OnServiceRun)
                ApplyPullCommitment(units, me, hostiles);
        }

        // Live hostiles from the object manager — NOT just the current target list. An un-aggroed camp
        // member isn't a candidate yet, but it WILL proximity-aggro/assist the instant we engage a mob
        // beside it; counting only list members is how we opened on a "lone" giraffe and ate the adds.
        public static List<WoWUnit> SnapshotHostiles()
        {
            var hostiles = new List<WoWUnit>();
            foreach (WoWUnit h in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
                if (h != null && h is not WoWPlayer && !h.Dead && !h.IsTotem && h.MyReaction <= WoWUnitReaction.Hostile)
                    hostiles.Add(h);   // totems aren't pack adds — don't let them inflate crowd/veto counts
            return hostiles;
        }

        /// <summary>
        /// An out-of-band hostile we're standing bubble-deep in — the "inevitable" (over-level gap-band)
        /// and "unavoidable" (below-band green) classes IncludeTargets surfaces ONLY under this condition.
        /// The fight is coming regardless, so these keep tier-0 pick priority and bypass the exposure gates.
        /// In-band mobs deliberately do NOT qualify (the Lost Rigger fix).
        /// </summary>
        public static bool FightIsOurs(WoWUnit u, WoWUnit me, VibeGrinderSettings s)
        {
            int ulevel = (int)u.Level;
            bool outOfBand = ulevel > me.Level + s.PathHostileLevelMargin
                             || (ulevel > 0 && ulevel < me.Level - s.LevelBandBelow);
            return outOfBand
                   && u.Distance <= u.MyAggroRange + s.PreemptAggroBuffer
                   && Math.Abs(u.Location.Z - me.Location.Z) < SameLevelZTolerance;
        }

        /// <summary>
        /// "If I fight x standing at point, how many OTHERS join?" — hostiles (≠x, not already fighting
        /// us) whose server aggro range + ExposurePad covers the point (Z≥5 excluded — cliffs protect),
        /// plus mobs within AssistRadius of x (coarse same-camp assist). A LOWER bound for a long fight.
        /// </summary>
        public static int FightExposure(WoWUnit x, WoWPoint point, List<WoWUnit> hostiles,
                                        VibeGrinderSettings s, out string colliders)
        {
            int n = 0;
            System.Text.StringBuilder sb = null;
            float assistR2 = s.AssistRadius * s.AssistRadius;
            foreach (WoWUnit h in hostiles)
            {
                if (h.Guid == x.Guid || h.IsTargetingMeOrPet) continue;
                double bubble = h.MyAggroRange + s.ExposurePad;
                bool joins = (h.Location.DistanceSqr(point) <= bubble * bubble
                              && Math.Abs(h.Location.Z - point.Z) < SameLevelZTolerance)
                             || h.Location.DistanceSqr(x.Location) <= assistR2;
                if (!joins) continue;
                n++;
                if (n <= 3)
                {
                    sb ??= new System.Text.StringBuilder();
                    if (sb.Length > 0) sb.Append(", ");
                    sb.AppendFormat("{0} d={1:F0}", h.Name, h.Location.Distance(point));
                }
            }
            colliders = sb?.ToString() ?? "";
            return n;
        }

        // Where WE stand when the opener goes out: on the approach line MaxPullDistance short of x (the
        // mob then runs to us, so the fight sits there); already in pull range → right where we are.
        public static WoWPoint OpenPoint(WoWUnit me, WoWUnit x, VibeGrinderSettings s)
        {
            WoWPoint mine = me.Location, theirs = x.Location;
            double d = mine.Distance(theirs);
            if (d <= s.MaxPullDistance || d < 0.01) return mine;
            double t = (d - s.MaxPullDistance) / d;
            return new WoWPoint((float)(mine.X + (theirs.X - mine.X) * t),
                                (float)(mine.Y + (theirs.Y - mine.Y) * t),
                                (float)(mine.Z + (theirs.Z - mine.Z) * t));
        }

        /// <summary>
        /// Distinct hostiles whose aggro bubble the PATH to x crosses — the open-point gate can't see a
        /// bubble web we'd walk THROUGH to a clean mob beyond it. Densifies the already-generated path at
        /// ~8yd steps, bounded, so it costs distance checks, not pathfinds.
        /// </summary>
        private static int CorridorExposure(WoWPoint[] path, WoWUnit x, List<WoWUnit> hostiles,
                                            VibeGrinderSettings s, out string colliders)
        {
            int n = 0;
            System.Text.StringBuilder sb = null;
            foreach (WoWUnit h in hostiles)
            {
                if (h.Guid == x.Guid || h.IsTargetingMeOrPet) continue;
                double bubble = h.MyAggroRange + s.ExposurePad;
                double b2 = bubble * bubble;
                bool crossed = false;
                WoWPoint prev = path[0];
                for (int i = 0; i < path.Length && !crossed; i++)
                {
                    WoWPoint seg = path[i];
                    double segLen = prev.Distance(seg);
                    int steps = Math.Min(16, (int)(segLen / 8.0) + 1);
                    for (int k = 0; k <= steps; k++)
                    {
                        double t = steps == 0 ? 0 : (double)k / steps;
                        var p = new WoWPoint((float)(prev.X + (seg.X - prev.X) * t),
                                             (float)(prev.Y + (seg.Y - prev.Y) * t),
                                             (float)(prev.Z + (seg.Z - prev.Z) * t));
                        if (h.Location.DistanceSqr(p) <= b2 && Math.Abs(h.Location.Z - p.Z) < SameLevelZTolerance)
                        {
                            crossed = true;
                            break;
                        }
                    }
                    prev = seg;
                }
                if (!crossed) continue;
                n++;
                if (n <= 3)
                {
                    sb ??= new System.Text.StringBuilder();
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(h.Name);
                }
            }
            colliders = sb?.ToString() ?? "";
            return n;
        }

        /// <summary>
        /// True for an elite / rare-elite / world-boss — a mob this bot must never proactively pull.
        /// Plain Rare (non-elite, rank 4) is intentionally NOT included — usually soloable / RareKiller's job.
        /// </summary>
        public static bool IsUnkillableElite(WoWUnit u)
        {
            if (u == null) return false;
            WoWUnitClassificationType r = u.CreatureRank;
            return u.Elite
                || r == WoWUnitClassificationType.Elite
                || r == WoWUnitClassificationType.RareElite
                || r == WoWUnitClassificationType.WorldBoss;
        }

        // Drop every elite from the pre-combat candidate list so nothing downstream can commit/peel one.
        private void VetoElites(List<Targeting.TargetPriority> units)
        {
            int vetoed = 0;
            string sample = null;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                WoWUnit u = units[i].Object?.ToUnit();
                if (u == null || !IsUnkillableElite(u)) continue;
                // Never strip a mob that's ON us or the live commit: Me.Combat lags the aggro by 1-3s, and
                // removing an already-charging elite in that window hands FirstUnit to some other mob while
                // it closes — the leash-off class of bug (audit 2026-07-05).
                if (u.IsTargetingMeOrPet || u.Guid == _committedGuid) continue;
                if (sample == null) sample = u.Name;
                units.RemoveAt(i);
                vetoed++;
            }
            if (vetoed > 0 && (!_eliteVetoLogSw.IsRunning || _eliteVetoLogSw.Elapsed.TotalSeconds >= 3))
            {
                Logging.WriteDebug("[VibeGrinder/Hostiles] vetoed {0} elite(s) from pull candidates (e.g. {1}) — won't engage.",
                    vetoed, sample);
                _eliteVetoLogSw.Restart();
            }
        }

        /// <summary>True if this unit is a player or is controlled by a player (pet / minion / charmed mob) —
        /// something we must never PROACTIVELY engage on a PvP server.</summary>
        public static bool IsPlayerOrPlayerControlled(WoWObject o)
        {
            if (o is WoWPlayer) return true;
            WoWUnit u = o?.ToUnit();
            if (u == null) return false;
            try { return u.ControllingPlayer != null || u.OwnedByRoot is WoWPlayer; }
            catch { return false; }   // OM chain lookups can throw on a stale unit — treat as not-a-player
        }

        // Drop every player + player-controlled unit from the pre-combat candidate list (never initiate PvP).
        // DELIBERATELY no attacker/commit exemption (unlike VetoElites/VetoBannedEntries): damaging an attacking
        // player pet still flags PvP with a real player — worse than eating the pet's damage. Initiation: never.
        private void VetoPlayerUnits(List<Targeting.TargetPriority> units)
        {
            int vetoed = 0;
            string sample = null;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                if (units[i].Object == null || !IsPlayerOrPlayerControlled(units[i].Object)) continue;
                if (sample == null) sample = units[i].Object.Name;
                units.RemoveAt(i);
                vetoed++;
            }
            if (vetoed > 0 && (!_playerVetoLogSw.IsRunning || _playerVetoLogSw.Elapsed.TotalSeconds >= 3))
            {
                Logging.WriteDebug("[VibeGrinder/Hostiles] vetoed {0} player/pet candidate(s) (e.g. {1}) — never proactively PvP.",
                    vetoed, sample);
                _playerVetoLogSw.Restart();
            }
        }

        // Drop DB-flagged-immune and session-banned entries from pre-combat candidates. Initiation only —
        // the weigh hook early-returns in combat, so defense against one that somehow attacks is untouched.
        private void VetoBannedEntries(List<Targeting.TargetPriority> units)
        {
            int vetoed = 0;
            string sample = null;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                WoWUnit u = units[i].Object?.ToUnit();
                if (u == null || u.Entry == 0) continue;
                DateTime until;
                bool banned = _entryBanUntil.TryGetValue(u.Entry, out until) && DateTime.UtcNow < until;
                if (!banned && !IsDbImmune(u.Entry)) continue;
                // A banned-entry mob that's actually ON us or the live commit is never stripped mid-fight;
                // same leash-off protection as VetoElites.
                if (u.IsTargetingMeOrPet || u.Guid == _committedGuid) continue;
                if (sample == null) sample = u.Name;
                units.RemoveAt(i);
                vetoed++;
            }
            if (vetoed > 0 && (!_entryVetoLogSw.IsRunning || _entryVetoLogSw.Elapsed.TotalSeconds >= 3))
            {
                Logging.WriteDebug("[VibeGrinder/Hostiles] vetoed {0} banned/immune-entry candidate(s) (e.g. {1}) — won't engage.",
                    vetoed, sample);
                _entryVetoLogSw.Restart();
            }
        }

        // Template unit_flags check (cached; one SQL per new entry). The DB carries the authored intent
        // (IMMUNE_TO_PC on the prisoner template) even when the live spawn drops the flag.
        private static bool IsDbImmune(uint entry)
        {
            bool immune;
            if (_dbImmuneCache.TryGetValue(entry, out immune)) return immune;
            long flags = GrindMobsRepository.GetTemplateUnitFlags(entry);
            immune = flags > 0 && (flags & Bots.VibeGrinder.Selection.SpotSelector.ImmuneUnitFlagMask) != 0;
            _dbImmuneCache[entry] = immune;
            return immune;
        }

        /// <summary>
        /// "Mega sus" escalation: EntryBanGiveUps DISTINCT mobs of the SAME entry all failing in place means
        /// the NAME is unengageable here → ban the entry. The fast path counts unconditionally (the client
        /// explicitly said the engage can't work); the slow clock only counts with the in-place signature.
        /// Kills reset strikes (NotifyKillEntry).
        /// </summary>
        private void RecordEntryGiveUp(WoWUnit held, double d, VibeGrinderSettings s, bool clientReported)
        {
            if (held == null || held.Entry == 0) return;
            if (!clientReported && (d > s.MaxPullDistance + 3 || !held.InLineOfSpellSight)) return;
            HashSet<ulong> guids;
            if (!_entryGiveUps.TryGetValue(held.Entry, out guids))
                _entryGiveUps[held.Entry] = guids = new HashSet<ulong>();
            guids.Add(held.Guid);
            if (guids.Count < s.EntryBanGiveUps) return;
            _entryBanUntil[held.Entry] = DateTime.UtcNow.AddMinutes(s.EntryBanMinutes);
            _entryGiveUps.Remove(held.Entry);
            Logging.Write(System.Drawing.Color.Khaki,
                "[VibeGrinder/Commit] ENTRY BAN {0} (entry {1}) — {2} distinct in-place give-ups; ignoring that name for {3}m.",
                held.Name, held.Entry, s.EntryBanGiveUps, s.EntryBanMinutes);
        }

        // Pin one mob to the top of the score so it stays FirstUnit (PullCommitBoost). No-op if guid==0 or absent.
        private static void PinGuid(List<Targeting.TargetPriority> units, ulong guid)
        {
            if (guid == 0) return;
            for (int i = 0; i < units.Count; i++)
                if (units[i].Object != null && units[i].Object.Guid == guid)
                {
                    units[i].Score += VibeGrinderSettings.Instance.PullCommitBoost;
                    break;
                }
        }

        /// <summary>
        /// In-combat target lock. Pin the mob we're currently fighting to the top of the score so it stays
        /// FirstUnit — stops the mid-combat POI hop that pulls adds.
        /// </summary>
        private static void PinCurrentTarget(List<Targeting.TargetPriority> units, WoWUnit me)
        {
            ulong ct = me.CurrentTargetGuid;
            if (ct == 0) return;
            for (int i = 0; i < units.Count; i++)
                if (units[i].Object != null && units[i].Object.Guid == ct)
                {
                    units[i].Score += VibeGrinderSettings.Instance.PullCommitBoost;
                    break;
                }
        }

        /// <summary>
        /// Pre-combat QUALITY weighting: deprioritise crowded pulls (capped tiebreaker) and bury neutrals
        /// beside hostiles, so the mob we COMMIT to is the cleanest available. Only shapes the initial pick.
        /// </summary>
        private void ApplyCrowdAndNeutralWeighting(List<Targeting.TargetPriority> units,
                                                   WoWUnit me, List<WoWUnit> hostiles)
        {
            var s = VibeGrinderSettings.Instance;
            if (s.PullCrowdRadius <= 0f) return;   // AddAvoidance Off disables the whole layer

            // Squishy-lowbie COMFORT taper — applies only to the soft tiebreaker penalty below. The hard
            // vetoes are SURVIVAL and never taper (a L47 died to an 11-mob pack while the scale sat at 0.09).
            float levelScale = s.CrowdLevelScale(me.Level);
            // Adaptive: scale up the more we've been dying to packs (eased by progress).
            float caution = (float)_host.CrowdCautionFactor;
            float penalty = s.PullCrowdPenalty * levelScale * caution;

            float crowdR2 = s.PullCrowdRadius * s.PullCrowdRadius;
            float neutR2 = s.NeutralHostileAvoidRadius * s.NeutralHostileAvoidRadius;
            // Iterate BACKWARD so the hard-veto can RemoveAt(i) safely.
            for (int i = units.Count - 1; i >= 0; i--)
            {
                WoWUnit a = units[i].Object.ToUnit();
                if (a == null) continue;
                // Enemy totem: last-resort only — bury below the floor but leave it in the list so the
                // acquire fallback can still break a snare/root totem when nothing else is up.
                if (a.IsTotem) { units[i].Score -= s.NeutralNearHostileVeto * 2f; continue; }
                bool neutral = a.MyReaction > WoWUnitReaction.Hostile;

                // Never veto: a mob already ON us, the COMMITTED mob (mid-flight removal reads as "no longer
                // a candidate" → spurious drop + a drop-ban false positive), or an out-of-band bubble mob
                // (FightIsOurs — must stay pickable so acquire takes it 1v1). The old blanket "inside its
                // bubble → skip" is GONE for in-band mobs (the pirate-camp hole).
                if (!neutral && (a.IsTargetingMeOrPet || a.Guid == _committedGuid || FightIsOurs(a, me, s)))
                    continue;

                int addRisk = 0, bubbleRisk = 0;
                bool hostileInNeutralRange = false;
                foreach (WoWUnit b in hostiles)
                {
                    if (b.Guid == a.Guid) continue;
                    double d2 = a.Location.DistanceSqr(b.Location);
                    if (d2 <= crowdR2) addRisk++;
                    if (neutral && d2 <= neutR2) hostileInNeutralRange = true;
                    // Bubble overlap over the candidate's OWN body: a mob 3+ other aggro bubbles already
                    // cover is deep camp no matter how spread the bodies look.
                    if (s.EnableBubbleVeto && !neutral && !b.IsTargetingMeOrPet)
                    {
                        double br = b.MyAggroRange + s.ExposurePad;
                        if (d2 <= br * br && Math.Abs(b.Location.Z - a.Location.Z) < SameLevelZTolerance) bubbleRisk++;
                    }
                }

                // HARD VETO a genuine camp — assist knot OR bubble web. The capped penalty below is only a
                // tiebreaker; it CANNOT refuse a camp. We won't OPEN on a camp; if the area is all camps the
                // empty list drives a relocate.
                if (!neutral && (addRisk >= s.PullPackVetoCount || bubbleRisk >= s.PullPackVetoCount))
                {
                    units.RemoveAt(i);
                    continue;
                }

                // CAP the crowd penalty so it stays a TIEBREAKER among similarly-close mobs and can never
                // outweigh proximity (base score loses only 2/yd).
                if (penalty > 0f && addRisk > 0)
                    units[i].Score -= Math.Min(addRisk * penalty, s.PullCrowdPenaltyCap);

                // A NEUTRAL beside hostiles is pure downside: opening on it means walking INTO the hostile
                // bubble. Bury it so we never OPEN on it while a cleaner target (or resting) exists.
                if (hostileInNeutralRange)
                    units[i].Score -= s.NeutralNearHostileVeto;
            }
        }

        /// <summary>
        /// True the instant we OPEN on the committed mob — the real "we've engaged, stop selecting" edge,
        /// which is NOT Me.Combat (the flag lags a caster's opener by 1-3s). Signals, earliest first:
        /// mid-cast on it; it's now targeting us; Me.Combat. A committed NEUTRAL we haven't provoked reads
        /// false on all three — the neutral-preempt still fires during the walk-onto phase, as intended.
        /// </summary>
        private bool HasOpenedOnCommit(WoWUnit me)
        {
            if (_committedGuid == 0) return false;
            if (me.CurrentTargetGuid == _committedGuid && (me.IsCasting || me.IsChanneling)) return true;
            if (me.Combat) return true;
            WoWUnit cu = ObjectManager.GetObjectByGuid<WoWUnit>(_committedGuid);
            return cu != null && !cu.Dead && cu.IsTargetingMeOrPet;
        }

        /// <summary>
        /// Engagement commitment — once we COMMIT to a mob, pin it to the top of the score so it stays
        /// FirstUnit, and only re-pick when it's dead, gone, or proven unreachable. Quality scoring above
        /// still chooses the initial commit; after that, stability beats re-optimising.
        /// </summary>
        private void ApplyPullCommitment(List<Targeting.TargetPriority> units,
                                         WoWUnit me, List<WoWUnit> hostiles)
        {
            var s = VibeGrinderSettings.Instance;

            // Dead/ghost: the pre-pull selection layer has no business running. Own drop path — never the
            // held==null branch, so the drop-ban can't misread a death as a wedge.
            if (me.IsDead || me.IsGhost)
            {
                if (_committedGuid != 0)
                {
                    _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue;
                }
                return;
            }

            // Fear meter: max TOTAL mobs in a fight we CHOOSE, tightened by pack-death caution.
            int maxCompany = s.MaxFightCompany(_host.CrowdCautionFactor);

            // Don't tread water after a target: DROP the pin (no blacklist) and don't re-commit while
            // swimming, so Roam pulls us back to the on-land hotspot.
            if (me.IsSwimming)
            {
                if (_committedGuid != 0)
                {
                    Logging.WriteDebug("[VibeGrinder/Commit] swimming — dropping pin on {0:X} (won't tread water after it).",
                        _committedGuid);
                    _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue;
                    _host.RecordSwimBlocked();
                }
                return;
            }

            // The engagement boundary for the whole pre-pull selection layer below (opener edge, not Me.Combat).
            bool opened = HasOpenedOnCommit(me);

            // Path defense (never walk a far commitment past a mob that's about to body-pull us). See
            // VibeGrinder CLAUDE.md "Path-defense preempt".
            if (_committedGuid != 0 && !opened)
            {
                WoWUnit held = FindCandidate(units, _committedGuid);
                double heldDist = held != null && !held.Dead ? held.Location.Distance(me.Location) : double.MaxValue;
                bool heldNeutral = held != null && held.MyReaction > WoWUnitReaction.Hostile;
                WoWUnit threat = null;
                double threatDist = double.MaxValue;
                foreach (var c in units)
                {
                    WoWUnit cu = c.Object?.ToUnit();
                    if (cu == null || cu.Dead || cu.Guid == _committedGuid) continue;
                    if (cu.MyReaction > WoWUnitReaction.Hostile) continue;   // a neutral won't come to us — no path threat
                    double d = cu.Distance;
                    double trigger = cu.MyAggroRange + s.PreemptAggroBuffer;
                    if (heldNeutral) trigger = Math.Max(trigger, s.NeutralOpenAvoidRadius);
                    if (d <= trigger && d < threatDist) { threatDist = d; threat = cu; }
                }
                if (threat != null && (heldNeutral || threatDist < heldDist) && threat.Guid != _committedGuid)
                {
                    // Fear-meter gate on the switch: deny → shed the threat, defer the held commit (own
                    // path — no ban), and break the ENGAGING grace so TRAVELING re-arms NOW.
                    if (s.EnableExposureGate && !threat.IsTargetingMeOrPet && !FightIsOurs(threat, me, s))
                    {
                        int exposure = FightExposure(threat, me.Location, hostiles, s, out string who);
                        if (exposure >= maxCompany)
                        {
                            Logging.Write(System.Drawing.Color.Khaki,
                                "[VibeGrinder/Commit] PREEMPT DENIED {0} (d={1:F1}) — fighting it here pulls {2} more ({3}); cap {4} — shedding it and leaving.",
                                threat.Name, threatDist, exposure, who, maxCompany);
                            Blacklist.Add(threat.Guid, TimeSpan.FromSeconds(s.ExposureRejectSeconds));
                            _host.RecordExposureReject(threat);
                            _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue;
                            _host.RequestBreakEngageGrace();
                            threat = null;
                        }
                    }
                    if (threat != null)
                    {
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] PREEMPT to {0} (d={1:F1}, aggro={2:F0}{3}) — path hostile about to pull; deferring {4:X}",
                            threat.Name, threatDist, threat.MyAggroRange, heldNeutral ? ", neutral commit" : "", _committedGuid);
                        // Commit STRAIGHT to the threat — dropping to 0 and letting Acquire re-pick would
                        // re-grab the NEARER neutral, re-fire the preempt, and oscillate 5x/sec.
                        _committedGuid = threat.Guid; _committedTimer.Restart(); _committedLastDist = double.MaxValue;
                        _committedProgressed = false; _committedName = threat.Name;
                    }
                }
            }

            // Validate the standing commitment: still a live candidate? And have we been genuinely unable
            // to engage it — NOT merely slow to walk there? The give-up clock is PROGRESS-based.
            if (_committedGuid != 0)
            {
                WoWUnit held = FindCandidate(units, _committedGuid);
                bool valid = held != null && !held.Dead;
                if (valid)
                {
                    double d = held.Location.Distance(me.Location);
                    // Progress = closing the gap OR actually ENGAGING (opened) — reset on those, NOT on
                    // bare proximity (an in-range unpullable mob used to reset the clock forever).
                    if (opened || d < _committedLastDist - 0.5)
                    {
                        _committedTimer.Restart();
                        // Ignore the acquire-tick MaxValue→d seed — first reading, not progress.
                        if (opened || _committedLastDist != double.MaxValue) _committedProgressed = true;
                    }
                    _committedLastDist = d;

                    // Fight-duration lookahead: exposure re-checked where we STAND, every pulse until the
                    // opener is away. Post-open is irrevocable (switching ADDS a mob) so this never fires then.
                    if (!opened && s.EnableExposureGate
                        && !held.IsTargetingMeOrPet && !FightIsOurs(held, me, s))
                    {
                        int exposure = FightExposure(held, me.Location, hostiles, s, out string who);
                        if (exposure >= maxCompany)
                        {
                            Logging.Write(System.Drawing.Color.Khaki,
                                "[VibeGrinder/Commit] ABORT approach to {0} (d={1:F1}) — standing inside {2} bubble(s) ({3}); cap {4} — backing off 45s.",
                                held.Name, d, exposure, who, maxCompany);
                            Blacklist.Add(_committedGuid, TimeSpan.FromSeconds(s.ExposureRejectSeconds));
                            _host.RequestBreakEngageGrace();
                            valid = false;
                        }
                    }

                    // FAST give-up on client-reported pull failures: two LoS/invalid-target errors within
                    // 6s mean this pull cannot work from here — resolve NOW with the reason verbatim.
                    if (valid && !opened && _pullErrorCount >= 2 && (DateTime.UtcNow - _pullErrorAt).TotalSeconds < 6)
                    {
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] GIVE UP FAST on {0} — client reported '{1}' ×{2} — blacklisting 2m.",
                            held.Name, _lastPullError, _pullErrorCount);
                        Blacklist.Add(_committedGuid, TimeSpan.FromMinutes(2));
                        RecordEntryGiveUp(held, d, s, clientReported: true);
                        _pullErrorCount = 0;
                        valid = false;
                    }

                    if (valid && _committedTimer.Elapsed.TotalSeconds > s.PullCommitMaxSeconds)
                    {
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] GIVE UP on {0} (reaction={1}, d={2:F1}, noProgressFor={3:F0}s, " +
                            "moving={4}, inLoS={5}) — blacklisting 2m. nearestHostile={6} candidates={7}",
                            held.Name, held.MyReaction, d, _committedTimer.Elapsed.TotalSeconds,
                            me.IsMoving, held.InLineOfSpellSight, NearestHostileDesc(hostiles), units.Count);
                        Blacklist.Add(_committedGuid, TimeSpan.FromMinutes(2));
                        RecordEntryGiveUp(held, d, s, clientReported: false);
                        valid = false;
                    }
                }
                else if (_committedGuid != 0)
                {
                    Logging.WriteDebug("[VibeGrinder/Commit] drop {0:X} — {1}", _committedGuid,
                        held == null ? "no longer a candidate" : "dead");
                    // EXPERIMENTAL (Den of Flame timber-fence trap): left the candidate list withOUT ever
                    // closing distance or opening — almost always physically unreachable; ban at the drop
                    // edge instead of waiting for the give-up clock the flap defeats.
                    if (held == null && !_committedProgressed && s.EnableExperimentalDropBan)
                    {
                        Blacklist.Add(_committedGuid, TimeSpan.FromMinutes(s.ExperimentalDropBanMinutes));
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] EXPERIMENTAL BAN {0} ({1:X}) — committed with zero approach progress before it left the list; likely a pathing wedge. Ignoring {2}min.",
                            _committedName ?? "?", _committedGuid, s.ExperimentalDropBanMinutes);
                    }
                }
                if (!valid) { _committedGuid = 0; _committedTimer.Reset(); _committedLastDist = double.MaxValue; }
            }

            // Acquire: commit to the NEAREST acceptable candidate (tiered), not the highest-scored one.
            // Rest BEFORE pulling: don't START a new pull while resting OR while we need to (both required —
            // RestNeeded catches the entry tick, IsResting catches the sticky recovery band).
            if (_committedGuid == 0 && !_host.IsResting && !_host.RestNeeded(me))
            {
                float buryFloor = -s.NeutralNearHostileVeto * 0.5f;   // below this = a buried neutral; skip it
                // Lookahead: validate the pick is PATHABLE before committing (one GeneratePath per acquire,
                // bounded 3/tick; a mob already ON us is never rejected). 20s give-up clock stays as backstop.
                var rejected = new HashSet<ulong>();
                bool exposureRejected = false;
                Targeting.TargetPriority pick = null;
                for (int attempt = 0; attempt < 3 && pick == null; attempt++)
                {
                    double nearest = double.MaxValue;
                    int pickTier = int.MaxValue;
                    for (int i = 0; i < units.Count; i++)
                    {
                        var c = units[i];
                        if (c.Object == null || c.Score < buryFloor || rejected.Contains(c.Object.Guid)) continue;
                        WoWUnit cu = c.Object.ToUnit();
                        if (cu == null || cu.Dead) continue;   // a just-killed corpse lingers a frame in the list
                        // Tiered nearest: already-ours (attacking us, or an OUT-OF-BAND bubble mob) >
                        // visible > around-a-corner. An IN-band bubble mob is no longer tier 0 — the
                        // exposure gate decides engage vs walk away.
                        double d = c.Object.Distance;
                        int tier = cu.IsTargetingMeOrPet || FightIsOurs(cu, me, s) ? 0
                                 : cu.InLineOfSpellSight ? 1 : 2;
                        if (tier < pickTier || (tier == pickTier && d < nearest)) { nearest = d; pick = c; pickTier = tier; }
                    }
                    if (pick == null) break;

                    WoWUnit pu = pick.Object.ToUnit();
                    if (pu != null && !pu.IsTargetingMeOrPet)
                    {
                        WoWPoint[] path = Navigator.GeneratePath(me.Location, pu.Location);
                        if (path == null || path.Length == 0)
                        {
                            // Escalate a REPEAT offender: 3 no-path strikes → 45 min ban (a mesh hole
                            // re-rejected every 45s forever is a permanent pathfind tax).
                            int strikes = _pathRejects.TryGetValue(pu.Guid, out int prev) ? prev + 1 : 1;
                            _pathRejects[pu.Guid] = strikes;
                            bool longBan = strikes >= 3;
                            Logging.Write(System.Drawing.Color.Khaki,
                                "[VibeGrinder/Commit] REJECT {0} (d={1:F1}) — no path to it ({2}); trying next-nearest.",
                                pu.Name, pu.Distance,
                                longBan ? "3rd no-path — banning " + s.EntryBanMinutes + "m" : "strike " + strikes);
                            Blacklist.Add(pu.Guid, longBan
                                ? TimeSpan.FromMinutes(s.EntryBanMinutes)
                                : TimeSpan.FromSeconds(45));
                            if (longBan) _pathRejects.Remove(pu.Guid);
                            rejected.Add(pick.Object.Guid);
                            pick = null;
                        }
                        else
                        {
                            _pathRejects.Remove(pu.Guid);   // pathable again (it wandered) — clean slate
                            // Fear-meter gate (lookahead): who joins if we open on this pick from where
                            // we'd stand — and does the WALK there cross a bubble web? Out-of-band bubble
                            // mobs bypass (FightIsOurs) but still get the pathability REJECT above.
                            if (s.EnableExposureGate && !FightIsOurs(pu, me, s))
                            {
                                int exposure = FightExposure(pu, OpenPoint(me, pu, s), hostiles, s, out string who);
                                int crossed = 0; string cwho = "";
                                if (exposure < maxCompany && s.EnableCorridorCheck)
                                    crossed = CorridorExposure(path, pu, hostiles, s, out cwho);
                                if (exposure >= maxCompany || crossed >= maxCompany)
                                {
                                    bool viaCorridor = exposure < maxCompany;
                                    Logging.Write(System.Drawing.Color.Khaki,
                                        "[VibeGrinder/Commit] REJECT {0} (d={1:F1}) — {2} pulls {3} more ({4}); cap {5} — 45s.",
                                        pu.Name, pu.Distance,
                                        viaCorridor ? "the walk to it" : "opening on it",
                                        viaCorridor ? crossed : exposure,
                                        viaCorridor ? cwho : who, maxCompany);
                                    Blacklist.Add(pu.Guid, TimeSpan.FromSeconds(s.ExposureRejectSeconds));
                                    _host.RecordExposureReject(pu);
                                    rejected.Add(pick.Object.Guid);
                                    exposureRejected = true;
                                    pick = null;
                                }
                            }
                        }
                    }
                }
                // Everything nearby was buried (neutrals) → act on the top score rather than idle. NOT after
                // an exposure-reject: recommitting the highest-score survivor UN-GATED re-opens the exact
                // hole the gate closed — let the emptying list drive Depleted() → relocate instead.
                if (pick == null && !exposureRejected)
                    for (int i = 0; i < units.Count; i++)
                    {
                        WoWUnit cu = units[i].Object?.ToUnit();
                        if (cu == null || cu.Dead || rejected.Contains(cu.Guid)) continue;
                        if (pick == null || units[i].Score > pick.Score) pick = units[i];
                    }

                if (pick != null)
                {
                    _committedGuid = pick.Object.Guid;
                    _committedTimer.Restart();
                    _committedLastDist = double.MaxValue;
                    _committedProgressed = false;   // fresh commit — no approach progress yet
                    _pullErrorCount = 0;   // fresh commit → fresh error strikes
                    WoWUnit bu = pick.Object.ToUnit();
                    _committedName = bu?.Name;      // stash for the drop-ban log (held is null at the drop)
                    if (bu != null)
                        Logging.Write(System.Drawing.Color.Khaki,
                            "[VibeGrinder/Commit] ACQUIRE {0} (reaction={1}, d={2:F1}, score={3:F0}) " +
                            "nearestHostile={4} candidates={5}",
                            bu.Name, bu.MyReaction, bu.Distance, pick.Score, NearestHostileDesc(hostiles), units.Count);
                }
            }

            // Hold: pin the committed mob to the top so it stays FirstUnit and the pull sees it through.
            if (_committedGuid != 0)
                for (int i = 0; i < units.Count; i++)
                    if (units[i].Object != null && units[i].Object.Guid == _committedGuid)
                    {
                        units[i].Score += s.PullCommitBoost;
                        break;
                    }
        }

        private static WoWUnit FindCandidate(List<Targeting.TargetPriority> units, ulong guid)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].Object != null && units[i].Object.Guid == guid)
                    return units[i].Object.ToUnit();
            return null;
        }

        /// <summary>DIAG: nearest live hostile of any distance, from the pulse's shared snapshot.</summary>
        private static string NearestHostileDesc(List<WoWUnit> hostiles)
        {
            WoWUnit n = null;
            double best = double.MaxValue;
            foreach (WoWUnit u in hostiles)
            {
                double d = u.Distance;
                if (d < best) { best = d; n = u; }
            }
            return n == null ? "none" : string.Format("{0} d={1:F0} inLoS={2}", n.Name, best, n.InLineOfSpellSight);
        }

        /// <summary>
        /// The IncludeTargetsFilter hook — surface incidental hostiles (attackers, path-clear in-band,
        /// bubble-deep out-of-band) so the bot SEES the mobs off its grind list instead of body-pulling
        /// them blind. See VibeGrinder CLAUDE.md "Incidental hostiles".
        /// </summary>
        public void IncludeTargets(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
        {
            var me = StyxWoW.Me;
            if (me == null) return;
            // Surface foreign hostiles WIDER than pull range so the commit/pull pipeline has lead time
            // before they aggro on the approach.
            double surfaceR = VibeGrinderSettings.Instance.IncidentalHostileRadius;
            if (surfaceR <= 0) surfaceR = Targeting.PullDistance > 0 ? Targeting.PullDistance : 28;
            int safeLevel = me.Level + VibeGrinderSettings.Instance.PathHostileLevelMargin;
            // Upper bound of the "inevitable fight" band — derived from DangerLevelMargin so the two
            // systems always meet.
            int inevitableLevel = me.Level + VibeGrinderSettings.Instance.DangerLevelMargin;
            ulong petGuid = me.GotAlivePet && me.Pet != null ? me.Pet.Guid : 0;
            int surfaced = 0, defensive = 0, inevitableN = 0, greenN = 0;

            foreach (WoWObject obj in incoming)
            {
                if (obj is not WoWUnit u || obj is WoWPlayer) continue;
                if (IsPlayerOrPlayerControlled(obj)) continue;                     // never surface an enemy player's pet/minion
                if (u.Dead || u.MyReaction > WoWUnitReaction.Hostile) continue;   // hostiles only
                // OUR OWN totem reads faction-hostile (allegiance comes from the OWNER) — never target it.
                // Enemy totems still surface but get buried to last-resort in the weighting.
                if (u.IsTotem && u.CreatedByGuid == me.Guid) continue;
                if (outgoing.Contains(obj) || Blacklist.Contains(u.Guid)) continue;

                bool attackingUs = u.CurrentTargetGuid == me.Guid || (petGuid != 0 && u.CurrentTargetGuid == petGuid);
                // Path-clear is gated to level-safe in-range hostiles, band-bounded below too (greens have
                // no grind value; if one body-pulls, attackingUs surfaces it and we defend).
                int ulevel = (int)u.Level;
                int floorLevel = me.Level - VibeGrinderSettings.Instance.LevelBandBelow;
                bool pathClear = u.Distance <= surfaceR && ulevel >= floorLevel && ulevel <= safeLevel;
                // Inevitable fight: (L+PathHostileLevelMargin, L+DangerLevelMargin] band, bubble-deep —
                // invisible to BOTH safety systems otherwise (Kenata Dabyrie). Z ≥5yd = real protection.
                bool inevitable = ulevel > safeLevel && ulevel <= inevitableLevel
                                  && u.Distance <= u.MyAggroRange + VibeGrinderSettings.Instance.PreemptAggroBuffer
                                  && Math.Abs(u.Location.Z - me.Location.Z) < SameLevelZTolerance;
                // Downward twin: a BELOW-band hostile on foot inside its bubble — the fight has already
                // started, we just can't see it. Mounted stays invisible (outrun it).
                bool unavoidable = !me.Mounted && ulevel > 0 && ulevel < floorLevel
                                   && u.Distance <= u.MyAggroRange + VibeGrinderSettings.Instance.PreemptAggroBuffer
                                   && Math.Abs(u.Location.Z - me.Location.Z) < SameLevelZTolerance;
                // Commitment hysteresis: the COMMITTED mob stays surfaced regardless of the radius (a mob
                // wandering ON the 40yd boundary otherwise flaps candidate↔gone every pulse). Real
                // cancellers stay real: death/blacklist still drop it; the give-up clock still expires it.
                bool committed = _committedGuid != 0 && u.Guid == _committedGuid;
                if (!attackingUs && !pathClear && !inevitable && !unavoidable && !committed) continue;

                outgoing.Add(obj);
                surfaced++;
                if (attackingUs) defensive++;
                else if (inevitable && !pathClear) inevitableN++;
                else if (unavoidable && !pathClear) greenN++;
            }

            if (surfaced > 0 && (!_surfaceLogSw.IsRunning || _surfaceLogSw.Elapsed.TotalSeconds >= 3))
            {
                Logging.WriteDebug("[VibeGrinder/Hostiles] surfaced {0} incidental hostile(s) ({1} attacking us{2}{3}).",
                    surfaced, defensive,
                    inevitableN > 0 ? ", " + inevitableN + " inevitable over-level" : "",
                    greenN > 0 ? ", " + greenN + " bubble-deep below-band" : "");
                _surfaceLogSw.Restart();
            }
        }
    }
}
