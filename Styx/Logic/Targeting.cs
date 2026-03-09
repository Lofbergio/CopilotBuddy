#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.World;
using GreenMagic;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic
{
    public class Targeting
    {
        private static readonly List<string> _blacklistedMobNames;
        private static Targeting _instance;
        // backing fields for events (originally obfuscated names in HB)
        private TargetListUpdateFinishedDelegate _targetListUpdateFinishedHandlers;
        private IncludeTargetsFilterDelegate _includeTargetsFilterHandlers;
        private RemoveTargetsFilterDelegate _removeTargetsFilterHandlers;
        private WeighTargetsDelegate _weighTargetsFilterHandlers;
        private static readonly HashSet<uint> _blacklistedMobIds;
        // from original HB: special quest mobs used in include filter
        private static readonly HashSet<uint> _specialQuestMobIds = new HashSet<uint>
        {
            28577U, 28557U, 28576U, 28560U, 28559U, 28941U, 28942U
        };
        private bool _includeWorldPlayers;
        private bool _includeElites;
        private bool _displayTargetingExceptions;
        private List<WoWObject> _objectList;
        private int _maxTargets;
        private static Converter<WoWObject, WoWUnit> _objectToUnitConverter;
        private static Func<WoWObject, bool> _unitOrPlayerPredicate;
        private static Func<WoWObject, bool> _unitPredicate;
        private static Func<WoWObject, bool> _unitNotPlayerPredicate;
        private static Func<WoWObject, TargetPriority> _targetPrioritySelector;
        private static Func<TargetPriority, double> _getScoreFunc;
        private static Func<TargetPriority, WoWObject> _targetToObjectSelector;

        protected Targeting()
        {
            this.DisplayTargetingExceptions = true;
            this.ObjectList = new List<WoWObject>();
            this.MaxTargets = 5;
            this.InitializeFilters();
        }

        static Targeting()
        {
            Targeting._blacklistedMobNames = new List<string>();
            HashSet<uint> hashSet = new HashSet<uint>();
            hashSet.Add(28093U);
            hashSet.Add(22979U);
            hashSet.Add(24196U);
            hashSet.Add(13358U);
            hashSet.Add(23837U);
            hashSet.Add(13358U);
            hashSet.Add(9157U);
            hashSet.Add(17407U);
            hashSet.Add(17378U);
            hashSet.Add(17408U);
            hashSet.Add(24222U);
            hashSet.Add(32544U);
            hashSet.Add(32522U);
            hashSet.Add(24879U);
            hashSet.Add(25040U);
            // missing ids from original
            hashSet.Add(23537U);
            hashSet.Add(18729U);
            Targeting._blacklistedMobIds = hashSet;
        }

        public static double PullDistance
        {
            get
            {
                // BUG-26 fix: Check CR override first, fall back to settings
                double? crOverride = RoutineManager.Current?.PullDistance;
                if (crOverride.HasValue)
                    return crOverride.Value;
                return (double)LevelbotSettings.Instance.PullDistance;
            }
        }

        public static double PullDistanceSqr
        {
            get
            {
                double pullDistance = PullDistance;
                return pullDistance * pullDistance;
            }
        }

        public static int GetAggroOnMeWithin(WoWPoint position, float range)
        {
            float rangeSqr = range * range;
            // use cached units to reduce memory queries (400ms timeout in ObjectManager)
            return ObjectManager.CachedUnits
                .Count(u => u.Location.DistanceSqr(position) < rangeSqr
                            && u.IsAlive && u.Attackable && u.IsHostile
                            && (u.IsTargetingMeOrPet || u.IsTargetingAnyMinion || u.TappedByAllThreatLists));
        }

        public static int GetAggroWithin(WoWPoint position, float range)
        {
            float rangeSqr = range * range;
            return ObjectManager.CachedUnits
                .Where(u => u.IsAlive && u.Attackable && u.IsHostile && u.Location.DistanceSqr(position) <= rangeSqr)
                .Count();
        }

        public static double CollectionRange
        {
            get
            {
                var area = StyxWoW.AreaManager.CurrentGrindArea;
                if (area != null)
                {
                    double? maxDistance = area.MaxDistance;
                    if (maxDistance != null && maxDistance.Value > 10.0)
                    {
                        return maxDistance.Value;
                    }
                }
                return 100.0;
            }
        }

        public bool IncludeWorldPlayers
        {
            get
            {
                if (Battlegrounds.IsInsideBattleground)
                {
                    return true;
                }
                return this._includeWorldPlayers;
            }
            set
            {
                this._includeWorldPlayers = value;
            }
        }

        public bool IncludeElites
        {
            get { return this._includeElites; }
            set { this._includeElites = value; }
        }

        public bool KillBetweenHotspots
        {
            get
            {
                return StyxSettings.Instance.KillBetweenHotspots;
            }
        }

        public bool DisplayTargetingExceptions
        {
            get { return this._displayTargetingExceptions; }
            set { this._displayTargetingExceptions = value; }
        }

        /// <summary>
        /// HB 4.3.4 compatibility: gates event subscription.
        /// Not enforced in CB (thread-safe Interlocked pattern used instead).
        /// </summary>
        protected bool AllowEvents { get; set; } = true;

        public static Targeting Instance
        {
            get
            {
                Targeting targeting;
                if ((targeting = Targeting._instance) == null)
                {
                    targeting = (Targeting._instance = new Targeting());
                }
                return targeting;
            }
            set
            {
                Targeting._instance = value;
            }
        }

        public WoWUnit FirstUnit
        {
            get
            {
                if (this.ObjectList.Count == 0)
                {
                    return null;
                }
                return (this.ObjectList[0] as WoWUnit);
            }
        }

        public List<WoWUnit> TargetList
        {
            get
            {
                List<WoWObject> objectList = this.ObjectList;
                if (Targeting._objectToUnitConverter == null)
                {
                    Targeting._objectToUnitConverter = new Converter<WoWObject, WoWUnit>(ToWoWUnit);
                }
                return objectList.ConvertAll<WoWUnit>(Targeting._objectToUnitConverter);
            }
        }

        protected List<WoWObject> ObjectList
        {
            get { return this._objectList; }
            private set { this._objectList = value; }
        }

        public int MaxTargets
        {
            get { return this._maxTargets; }
            set { this._maxTargets = value; }
        }

        public event TargetListUpdateFinishedDelegate OnTargetListUpdateFinished
        {
            add
            {
                TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate = this._targetListUpdateFinishedHandlers;
                TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate2;
                do
                {
                    targetListUpdateFinishedDelegate2 = targetListUpdateFinishedDelegate;
                    TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate3 = (TargetListUpdateFinishedDelegate)Delegate.Combine(targetListUpdateFinishedDelegate2, value);
                    targetListUpdateFinishedDelegate = Interlocked.CompareExchange<TargetListUpdateFinishedDelegate>(ref this._targetListUpdateFinishedHandlers, targetListUpdateFinishedDelegate3, targetListUpdateFinishedDelegate2);
                }
                while (targetListUpdateFinishedDelegate != targetListUpdateFinishedDelegate2);
            }
            remove
            {
                TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate = this._targetListUpdateFinishedHandlers;
                TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate2;
                do
                {
                    targetListUpdateFinishedDelegate2 = targetListUpdateFinishedDelegate;
                    TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate3 = (TargetListUpdateFinishedDelegate)Delegate.Remove(targetListUpdateFinishedDelegate2, value);
                    targetListUpdateFinishedDelegate = Interlocked.CompareExchange<TargetListUpdateFinishedDelegate>(ref this._targetListUpdateFinishedHandlers, targetListUpdateFinishedDelegate3, targetListUpdateFinishedDelegate2);
                }
                while (targetListUpdateFinishedDelegate != targetListUpdateFinishedDelegate2);
            }
        }

        private void InitializeFilters()
        {
            this.IncludeTargetsFilter += this.DefaultIncludeTargetsFilter;
            this.RemoveTargetsFilter += this.DefaultRemoveTargetsFilter;
            this.WeighTargetsFilter += this.DefaultTargetWeight;
        }

        public event IncludeTargetsFilterDelegate IncludeTargetsFilter
        {
            add
            {
                IncludeTargetsFilterDelegate includeTargetsFilterDelegate = this._includeTargetsFilterHandlers;
                IncludeTargetsFilterDelegate includeTargetsFilterDelegate2;
                do
                {
                    includeTargetsFilterDelegate2 = includeTargetsFilterDelegate;
                    IncludeTargetsFilterDelegate includeTargetsFilterDelegate3 = (IncludeTargetsFilterDelegate)Delegate.Combine(includeTargetsFilterDelegate2, value);
                    includeTargetsFilterDelegate = Interlocked.CompareExchange<IncludeTargetsFilterDelegate>(ref this._includeTargetsFilterHandlers, includeTargetsFilterDelegate3, includeTargetsFilterDelegate2);
                }
                while (includeTargetsFilterDelegate != includeTargetsFilterDelegate2);
            }
            remove
            {
                IncludeTargetsFilterDelegate includeTargetsFilterDelegate = this._includeTargetsFilterHandlers;
                IncludeTargetsFilterDelegate includeTargetsFilterDelegate2;
                do
                {
                    includeTargetsFilterDelegate2 = includeTargetsFilterDelegate;
                    IncludeTargetsFilterDelegate includeTargetsFilterDelegate3 = (IncludeTargetsFilterDelegate)Delegate.Remove(includeTargetsFilterDelegate2, value);
                    includeTargetsFilterDelegate = Interlocked.CompareExchange<IncludeTargetsFilterDelegate>(ref this._includeTargetsFilterHandlers, includeTargetsFilterDelegate3, includeTargetsFilterDelegate2);
                }
                while (includeTargetsFilterDelegate != includeTargetsFilterDelegate2);
            }
        }

        public event RemoveTargetsFilterDelegate RemoveTargetsFilter
        {
            add
            {
                RemoveTargetsFilterDelegate removeTargetsFilterDelegate = this._removeTargetsFilterHandlers;
                RemoveTargetsFilterDelegate removeTargetsFilterDelegate2;
                do
                {
                    removeTargetsFilterDelegate2 = removeTargetsFilterDelegate;
                    RemoveTargetsFilterDelegate removeTargetsFilterDelegate3 = (RemoveTargetsFilterDelegate)Delegate.Combine(removeTargetsFilterDelegate2, value);
                    removeTargetsFilterDelegate = Interlocked.CompareExchange<RemoveTargetsFilterDelegate>(ref this._removeTargetsFilterHandlers, removeTargetsFilterDelegate3, removeTargetsFilterDelegate2);
                }
                while (removeTargetsFilterDelegate != removeTargetsFilterDelegate2);
            }
            remove
            {
                RemoveTargetsFilterDelegate removeTargetsFilterDelegate = this._removeTargetsFilterHandlers;
                RemoveTargetsFilterDelegate removeTargetsFilterDelegate2;
                do
                {
                    removeTargetsFilterDelegate2 = removeTargetsFilterDelegate;
                    RemoveTargetsFilterDelegate removeTargetsFilterDelegate3 = (RemoveTargetsFilterDelegate)Delegate.Remove(removeTargetsFilterDelegate2, value);
                    removeTargetsFilterDelegate = Interlocked.CompareExchange<RemoveTargetsFilterDelegate>(ref this._removeTargetsFilterHandlers, removeTargetsFilterDelegate3, removeTargetsFilterDelegate2);
                }
                while (removeTargetsFilterDelegate != removeTargetsFilterDelegate2);
            }
        }

        public event WeighTargetsDelegate WeighTargetsFilter
        {
            add
            {
                WeighTargetsDelegate weighTargetsDelegate = this._weighTargetsFilterHandlers;
                WeighTargetsDelegate weighTargetsDelegate2;
                do
                {
                    weighTargetsDelegate2 = weighTargetsDelegate;
                    WeighTargetsDelegate weighTargetsDelegate3 = (WeighTargetsDelegate)Delegate.Combine(weighTargetsDelegate2, value);
                    weighTargetsDelegate = Interlocked.CompareExchange<WeighTargetsDelegate>(ref this._weighTargetsFilterHandlers, weighTargetsDelegate3, weighTargetsDelegate2);
                }
                while (weighTargetsDelegate != weighTargetsDelegate2);
            }
            remove
            {
                WeighTargetsDelegate weighTargetsDelegate = this._weighTargetsFilterHandlers;
                WeighTargetsDelegate weighTargetsDelegate2;
                do
                {
                    weighTargetsDelegate2 = weighTargetsDelegate;
                    WeighTargetsDelegate weighTargetsDelegate3 = (WeighTargetsDelegate)Delegate.Remove(weighTargetsDelegate2, value);
                    weighTargetsDelegate = Interlocked.CompareExchange<WeighTargetsDelegate>(ref this._weighTargetsFilterHandlers, weighTargetsDelegate3, weighTargetsDelegate2);
                }
                while (weighTargetsDelegate != weighTargetsDelegate2);
            }
        }

        public void Clear()
        {
            if (this.ObjectList != null)
            {
                this.ObjectList.Clear();
            }
        }

        protected virtual List<WoWObject> GetInitialObjectList()
        {
            if (Battlegrounds.IsInsideBattleground)
            {
                IEnumerable<WoWObject> objectList = ObjectManager.ObjectList;
                if (Targeting._unitOrPlayerPredicate == null)
                {
                    Targeting._unitOrPlayerPredicate = new Func<WoWObject, bool>(IsPlayer);
                }
                return objectList.Where(Targeting._unitOrPlayerPredicate).ToList<WoWObject>();
            }
            else if (!this.IncludeWorldPlayers)
            {
                IEnumerable<WoWObject> objectList2 = ObjectManager.ObjectList;
                if (Targeting._unitNotPlayerPredicate == null)
                {
                    Targeting._unitNotPlayerPredicate = new Func<WoWObject, bool>(IsUnitNotPlayer);
                }
                return objectList2.Where(Targeting._unitNotPlayerPredicate).ToList<WoWObject>();
            }
            else
            {
                IEnumerable<WoWObject> objectList3 = ObjectManager.ObjectList;
                if (Targeting._unitPredicate == null)
                {
                    Targeting._unitPredicate = new Func<WoWObject, bool>(IsUnit);
                }
                return objectList3.Where(Targeting._unitPredicate).ToList<WoWObject>();
            }
        }

        public virtual void Pulse()
        {
            try
            {
                if (!(StyxWoW.Me == null))
                {
                    if (StyxWoW.IsInGame)
                    {
                      // HB 6.2.3: Pulse wraps in AcquireFrame (soft lock).
                      using (StyxWoW.Memory.AcquireFrame())
                      {
                        List<WoWObject> initialObjectList = this.GetInitialObjectList();
                        Delegate e = this._removeTargetsFilterHandlers;
                        object[] array = new object[] { initialObjectList };
                        this.InvokeFilterDelegate(e, array);
                        HashSet<WoWObject> hashSet = new HashSet<WoWObject>();
                        Delegate e2 = this._includeTargetsFilterHandlers;
                        object[] array2 = new object[] { initialObjectList, hashSet };
                        this.InvokeFilterDelegate(e2, array2);
                        IEnumerable<WoWObject> source = hashSet;
                        if (Targeting._targetPrioritySelector == null)
                        {
                            Targeting._targetPrioritySelector = new Func<WoWObject, TargetPriority>(CreateTargetPriority);
                        }
                        List<TargetPriority> list = source.Select(Targeting._targetPrioritySelector).ToList<TargetPriority>();
                        Delegate e3 = this._weighTargetsFilterHandlers;
                        object[] array3 = new object[] { list };
                        this.InvokeFilterDelegate(e3, array3);
                        IEnumerable<TargetPriority> source2 = list;
                        if (Targeting._getScoreFunc == null)
                        {
                            Targeting._getScoreFunc = new Func<TargetPriority, double>(GetScore);
                        }
                        list = source2.OrderByDescending(Targeting._getScoreFunc).Take(this.MaxTargets).ToList<TargetPriority>();
                        IEnumerable<TargetPriority> source3 = list;
                        if (Targeting._targetToObjectSelector == null)
                        {
                            Targeting._targetToObjectSelector = new Func<TargetPriority, WoWObject>(GetObject);
                        }
                        this.ObjectList = source3.Select(Targeting._targetToObjectSelector).ToList<WoWObject>();

                        Targeting._blacklistedMobNames.Clear();
                        foreach (TargetPriority targetPriority in list)
                        {
                            Targeting._blacklistedMobNames.Add(string.Format("{0} Dist: {1}", targetPriority.Object.Name, Math.Ceiling(targetPriority.Object.Distance)));
                        }
                        if (this._targetListUpdateFinishedHandlers != null)
                        {
                            this._targetListUpdateFinishedHandlers(Targeting._blacklistedMobNames);
                        }
                      }
                    }
                }
            }
            catch (Exception ex)
            {
                if (this.DisplayTargetingExceptions)
                {
                    Logging.WriteException(ex);
                }
            }
        }

        private void InvokeFilterDelegate(Delegate e, params object[] args)
        {
            if (e != null)
            {
                foreach (Delegate @delegate in e.GetInvocationList())
                {
                    try
                    {
                        @delegate.DynamicInvoke(args);
                    }
                    catch (Exception ex)
                    {
                        if (this.DisplayTargetingExceptions)
                        {
                            Logging.WriteException(ex);
                        }
                    }
                }
            }
        }

        protected virtual void DefaultRemoveTargetsFilter(List<WoWObject> units)
        {
            Blacklist.Flush();
            bool flag = StyxWoW.Me.IsInParty || StyxWoW.Me.IsInRaid;
            double collectionRangeSq = Targeting.CollectionRange * Targeting.CollectionRange;
            Profile currentProfile = ProfileManager.CurrentProfile;

            // Level bounds (from 3.3.5a — not in 4.3.4 but needed for WotLK)
            int minLevel = 0;
            int maxLevel = 1000;
            var currentGrindArea = StyxWoW.AreaManager.CurrentGrindArea;
            if (currentGrindArea != null && !StyxWoW.Me.Combat)
            {
                minLevel = currentGrindArea.TargetMinLevel;
                maxLevel = currentGrindArea.TargetMaxLevel;
            }

            // Party/raid guid sets for combat protection
            HashSet<ulong> minionGuids = new HashSet<ulong>();
            HashSet<ulong> playerGuids = new HashSet<ulong>();
            if (StyxWoW.Me.IsInRaid)
            {
                foreach (WoWPlayer member in StyxWoW.Me.RaidMembers)
                {
                    if (member == null) continue;
                    playerGuids.Add(member.Guid);
                    foreach (WoWUnit min in member.Minions)
                        minionGuids.Add(min.Guid);
                }
            }
            else if (StyxWoW.Me.IsInParty)
            {
                foreach (WoWPlayer member in StyxWoW.Me.PartyMembers)
                {
                    if (member == null) continue;
                    playerGuids.Add(member.Guid);
                    foreach (WoWUnit min in member.Minions)
                        minionGuids.Add(min.Guid);
                }
            }

            bool mounted = StyxWoW.Me.Mounted;
            bool isInsideBattleground = Battlegrounds.IsInsideBattleground;

            // Hard distance cap - units beyond this are stale pointers or server
            // ghosts; no mob in WoW 3.3.5a can legitimately aggro from this far.
            double hardDistCapSq = 300.0 * 300.0; // 300 yd

            // HB 6.2.3: AcquireFrame around the removal loop.
            using (StyxWoW.Memory.AcquireFrame())
            for (int i = units.Count - 1; i >= 0; i--)
            {
                WoWObject obj = units[i];
                if (obj == null || !(obj is WoWUnit) || !obj.IsValid)
                {
                    units.RemoveAt(i);
                    continue;
                }
                WoWUnit unit = (WoWUnit)obj;

                // Remove stale/impossibly-distant objects before any combat
                // protection can keep them alive (fixes 4347-yard phantom targets).
                if (unit.DistanceSqr > hardDistCapSq)
                {
                    units.RemoveAt(i);
                    continue;
                }

                bool isPlayer = unit is WoWPlayer;
                bool combat = unit.Combat;

                // Layer 1: In party/raid combat, protect units targeting party members or party pets
                if (isPlayer || !combat || !flag || (!playerGuids.Contains(unit.CurrentTargetGuid) && !minionGuids.Contains(unit.Guid)))
                {
                    bool isAlive = unit.IsAlive;
                    bool petAggro = unit.PetAggro;
                    // Layer 2: In combat, protect aggroed/targeting units
                    if (isPlayer || !combat || !isAlive || (!petAggro && !unit.Aggro && !unit.IsTargetingMeOrPet && !unit.IsTargetingAnyMinion))
                    {
                        double distSq = unit.DistanceSqr;
                        bool isFriendly = unit.IsFriendly;
                        // Layer 3: In combat, protect hostile units targeting me within range
                        if (!isPlayer || !combat || !isAlive || distSq >= collectionRangeSq || isFriendly || (!unit.IsTargetingMeOrPet && !unit.IsTargetingAnyMinion))
                        {
                            uint entry = unit.Entry;
                            WoWPoint location = unit.Location;

                            // Big removal OR-chain (matches HB 4.3.4 + 3.3.5a level bounds)
                            if (_blacklistedMobIds.Contains(entry)
                                || unit.Level < minLevel
                                || unit.Level > maxLevel
                                || Blacklist.Contains(unit.Guid, false)
                                || unit.Dead
                                || distSq > collectionRangeSq
                                || unit.OnTaxi
                                || unit.IsFlightMaster
                                || (unit.IsFlying && !isInsideBattleground)
                                || (unit.TaggedByOther && !flag)
                                || (currentProfile != null && (currentProfile.AvoidMobs.Contains(entry) || currentProfile.AvoidMobs.Contains(unit.Name)))
                                || (currentProfile != null && (Targeting.IsTooNearBlackspot(currentProfile.Blackspots, location) || this.IsNotWithinHotspotRange(location, false)))
                                || unit.IsCritter
                                || unit.IsNonCombatPet
                                || !unit.Attackable
                                || (mounted && this.IsNotWithinHotspotRange(location, false))
                                || isFriendly)
                            {
                                units.RemoveAt(i);
                            }
                        }
                    }
                }
            }
        }

        protected virtual void DefaultIncludeTargetsFilter(List<WoWObject> incomingUnits, HashSet<WoWObject> outgoingUnits)
        {
            // HB 4.3.4: flag2 = me.Combat || me.Minions.Any(m => m.Combat)
            bool isInCombat;
            if (!StyxWoW.Me.Combat)
            {
                isInCombat = StyxWoW.Me.Minions.Any(m => m.Combat);
            }
            else
            {
                isInCombat = true;
            }

            // HB 6.2.3: AcquireFrame around the include loop.
            using (StyxWoW.Memory.AcquireFrame())
            {
            foreach (WoWObject woWObject in incomingUnits)
            {
                bool isInsideBattleground = Battlegrounds.IsInsideBattleground;
                WoWUnit unit = woWObject.ToUnit();

                if (isInCombat)
                {
                    // Player branch (HB 4.3.4 lines 503-512)
                    if (!isInsideBattleground && woWObject is WoWPlayer)
                    {
                        WoWPlayer woWPlayer = woWObject.ToPlayer();
                        if (woWPlayer != null
                            && (woWPlayer.Aggro || woWPlayer.PetAggro || woWPlayer.IsTargetingAnyMinion || woWPlayer.IsTargetingMeOrPet)
                            && woWPlayer.Combat
                            && !woWPlayer.IsFriendly)
                        {
                            outgoingUnits.Add(woWPlayer);
                        }
                    }
                    else
                    {
                        // Special quest mob check (HB 4.3.4 lines 518-530, hashSet_2)
                        if (_specialQuestMobIds.Contains(unit.Entry))
                        {
                            if (unit.TaggedByMe && unit.HealthPercent < 99.0)
                            {
                                outgoingUnits.Add(unit);
                                continue;
                            }
                            if ((StyxWoW.Me.QuestLog.ContainsQuest(12678U) || StyxWoW.Me.QuestLog.ContainsQuest(12722U))
                                && unit.DistanceSqr < 400.0
                                && unit.NpcEmoteState == 431U) // EmoteState.STATE_COWER
                            {
                                outgoingUnits.Add(unit);
                                continue;
                            }
                        }

                        // Normal unit combat include (HB 4.3.4 lines 530-535)
                        // + IsTargetingMeOrPet: covers mobs whose threat-table entry hasn't
                        //   populated yet (e.g. adds that aggro while killing another mob).
                        //   The remove filter already protects these units; the include filter
                        //   must also recognise them so FirstUnit is never null mid-combat.
                        if (unit.Aggro || (unit.Fleeing && unit.TaggedByMe) || unit.IsTargetingAnyMinion || unit.IsTargetingMeOrPet)
                        {
                            outgoingUnits.Add(unit);
                        }
                    }
                }
            }

            }

            // BotPoi Kill AFTER the loop (HB 4.3.4 lines 537-544)
            // With dungeon/raid exclusion
            if (!StyxWoW.Me.CurrentMap.IsDungeon && !StyxWoW.Me.CurrentMap.IsRaid)
            {
                WoWObject asObject;
                if (BotPoi.Current.Type == PoiType.Kill
                    && (asObject = BotPoi.Current.AsObject) != null
                    && asObject.IsValid
                    && !outgoingUnits.Contains(asObject))
                {
                    outgoingUnits.Add(asObject);
                }
            }
        }

        /// <summary>
        /// Weights each unit in the target list. Matches HB 4.3.4 structure.
        /// </summary>
        protected virtual void DefaultTargetWeight(List<TargetPriority> units)
        {
            bool inCombat = StyxWoW.Me.PetInCombat || StyxWoW.Me.Combat;
            LocalPlayer me = StyxWoW.Me;
            Profile currentProfile = ProfileManager.CurrentProfile;

            // HB 4.3.4: inline batch MassTraceLine for LOS before scoring.
            bool[] losResults = null;
            if (!Battlegrounds.IsInsideBattleground)
            {
                WorldLine[] traceLines = new WorldLine[units.Count];
                WoWPoint traceLinePos = StyxWoW.Me.GetTraceLinePos();
                for (int i = units.Count - 1; i >= 0; i--)
                {
                    traceLines[i] = new WorldLine(traceLinePos, units[i].Object.ToUnit().GetTraceLinePos());
                }
                GameWorld.MassTraceLine(traceLines, GameWorld.CGWorldFrameHitFlags.HitTestLOS, out losResults);
            }

            // HB 6.2.3: AcquireFrame around the scoring loop (AFTER MassTraceLine).
            using (StyxWoW.Memory.AcquireFrame())
            for (int j = units.Count - 1; j >= 0; j--)
            {
                WoWUnit unit = units[j].Object.ToUnit();
                double num = 200.0 - 2.0 * unit.Distance;
                double distance = unit.Distance;
                ulong guid = unit.Guid;
                ulong currentTargetGuid = unit.CurrentTargetGuid;

                if (!inCombat)
                {
                    // Elite distance-gated removal (HB 4.3.4)
                    if (!currentProfile.TargetElites && unit.Elite)
                    {
                        if (distance > 20.0)
                        {
                            units.RemoveAt(j);
                            continue;
                        }
                        num -= 1000.0;
                    }

                    // Blacklist distance-gated removal (HB 4.3.4)
                    if (Blacklist.Contains(guid))
                    {
                        if (distance > 20.0)
                        {
                            units.RemoveAt(j);
                            continue;
                        }
                        num -= 1000.0;
                    }

                    // Reaction score, gated by distance < 30.0 (HB 4.3.4)
                    if (unit.MyReaction <= WoWUnitReaction.Neutral && distance < 30.0)
                    {
                        num += (30.0 - distance) * (double)(WoWUnitReaction.Friendly - unit.MyReaction);
                    }

                    // CurrentTarget or POI Kill bonus (HB 4.3.4: +150)
                    if (currentTargetGuid == guid || (BotPoi.Current.Type == PoiType.Kill && BotPoi.Current.Guid == unit.Guid))
                    {
                        num += 150.0;
                    }

                    // Pet penalty (HB 4.3.4)
                    if (unit.IsPet)
                    {
                        num -= 50.0;
                    }

                    // Aggro range bonus (HB 4.3.4)
                    float myAggroRange = unit.MyAggroRange;
                    if (myAggroRange != 0f && distance < (double)(myAggroRange + 5f))
                    {
                        num += 100.0;
                    }

                    // LOS bonus/penalty (HB 4.3.4: MassTraceLine returns true=blocked)
                    if (losResults != null && !losResults[j])
                    {
                        num += 25.0;
                    }
                    else
                    {
                        num -= 25.0;
                    }

                    // HB 6.2.3 removed GetAggroWithin from scoring (was O(N²), too
                    // expensive for external-process RPM).  Kept for reference:
                    // num -= (double)(5 * Targeting.GetAggroWithin(unit.Location, 15f));
                }
                else
                {
                    // Combat scoring (HB 4.3.4)
                    if (me.GotTarget && me.CurrentTarget.MaxMana > 1U && me.CurrentTarget.ManaPercent > 0.0)
                    {
                        num += unit.ManaPercent;
                    }
                    num -= unit.HealthPercent;
                    if (!unit.CanSelect)
                    {
                        num -= 1000.0;
                    }
                }

                units[j].Score += num;
            }
        }

        /// <summary>
        /// Indicates that a point lies outside the current hotspot collection range.
        /// </summary>
        public bool IsNotWithinHotspotRange(WoWPoint point, bool force = false)
        {
            if ((KillBetweenHotspots || Battlegrounds.IsInsideBattleground || StyxWoW.Me.IsInInstance) && !force)
                return false;

            try
            {
                var ga = ProfileManager.CurrentProfile?.GrindArea;
                if (ga?.CurrentHotSpot != null)
                {
                    float distance2D = ga.CurrentHotSpot.Position.Distance2D(point);
                    return distance2D >= CollectionRange;
                }
            }
            catch { }
            return false;
        }

        private static WoWUnit ToWoWUnit(WoWObject o)
        {
            return o.ToUnit();
        }

        private static bool IsPlayer(WoWObject o)
        {
            return (o is WoWPlayer);
        }

        private static bool IsUnit(WoWObject o)
        {
            return (o is WoWUnit);
        }

        private static bool IsUnitOrPlayer(WoWObject o)
        {
            return (o is WoWUnit) || (o is WoWPlayer);
        }

        private static bool IsUnitNotPlayer(WoWObject o)
        {
            if (o is WoWUnit)
            {
                return !(o is WoWPlayer);
            }
            return false;
        }

        private static TargetPriority CreateTargetPriority(WoWObject unit)
        {
            TargetPriority targetPriority = new TargetPriority();
            targetPriority.Object = unit;
            targetPriority.Score = 0.0;
            return targetPriority;
        }

        private static double GetScore(TargetPriority p)
        {
            return p.Score;
        }

        private static WoWObject GetObject(TargetPriority p)
        {
            return p.Object;
        }

        #region Blackspot Support

        /// <summary>
        /// Checks if a point is within any blackspot.
        /// HB 4.3.4 compatible - used by Quest Behaviors.
        /// </summary>
        /// <param name="blackspots">Collection of blackspots to check.</param>
        /// <param name="point">The point to check.</param>
        /// <returns>True if the point is within any blackspot.</returns>
        public static bool IsTooNearBlackspot(IEnumerable<Blackspot> blackspots, WoWPoint point)
        {
            if (blackspots == null)
                return false;

            foreach (var spot in blackspots)
            {
                float dx = point.X - spot.Location.X;
                float dy = point.Y - spot.Location.Y;
                float dz = point.Z - spot.Location.Z;

                // Check horizontal distance (within radius)
                if (dx * dx + dy * dy <= spot.Radius * spot.Radius)
                {
                    // Check vertical distance (within height)
                    if (Math.Abs(dz) <= spot.Height)
                        return true;
                }
            }
            return false;
        }

        #endregion

        public sealed class TargetPriority
        {
            public WoWObject Object;
            public double Score;
        }
    }
}
