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
using Styx.Helpers;
using GreenMagic;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic
{
    public class Targeting
    {
        private static readonly List<string> _blacklistedMobNames;
        private static Targeting _instance;
        private TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate_0;
        private IncludeTargetsFilterDelegate includeTargetsFilterDelegate_0;
        private RemoveTargetsFilterDelegate removeTargetsFilterDelegate_0;
        private WeighTargetsDelegate weighTargetsDelegate_0;
        private static readonly HashSet<uint> _blacklistedMobIds;
        // from original HB: special quest mobs used in include filter
        private static readonly HashSet<uint> _specialQuestMobIds = new HashSet<uint>
        {
            28577U, 28557U, 28576U, 28560U, 28559U, 28941U, 28942U
        };
        private bool _includeWorldPlayers;
        private bool _includeElites;
        
        // HB uses a 2s timer to throttle include cycle while moving
        private readonly WaitTimer _includeTimer = new WaitTimer(TimeSpan.FromSeconds(2));
        private bool _displayTargetingExceptions;
        private List<WoWObject> _objectList;
        private int _maxTargets;
        private static Converter<WoWObject, WoWUnit> converter_0;
        private static Func<WoWObject, bool> func_0;
        private static Func<WoWObject, bool> func_1;
        private static Func<WoWObject, bool> func_2;
        private static Func<WoWObject, TargetPriority> func_3;
        private static Func<TargetPriority, double> func_4;
        private static Func<TargetPriority, WoWObject> func_5;

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
                if (Targeting.converter_0 == null)
                {
                    Targeting.converter_0 = new Converter<WoWObject, WoWUnit>(ToWoWUnit);
                }
                return objectList.ConvertAll<WoWUnit>(Targeting.converter_0);
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
                TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate = this.targetListUpdateFinishedDelegate_0;
                TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate2;
                do
                {
                    targetListUpdateFinishedDelegate2 = targetListUpdateFinishedDelegate;
                    TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate3 = (TargetListUpdateFinishedDelegate)Delegate.Combine(targetListUpdateFinishedDelegate2, value);
                    targetListUpdateFinishedDelegate = Interlocked.CompareExchange<TargetListUpdateFinishedDelegate>(ref this.targetListUpdateFinishedDelegate_0, targetListUpdateFinishedDelegate3, targetListUpdateFinishedDelegate2);
                }
                while (targetListUpdateFinishedDelegate != targetListUpdateFinishedDelegate2);
            }
            remove
            {
                TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate = this.targetListUpdateFinishedDelegate_0;
                TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate2;
                do
                {
                    targetListUpdateFinishedDelegate2 = targetListUpdateFinishedDelegate;
                    TargetListUpdateFinishedDelegate targetListUpdateFinishedDelegate3 = (TargetListUpdateFinishedDelegate)Delegate.Remove(targetListUpdateFinishedDelegate2, value);
                    targetListUpdateFinishedDelegate = Interlocked.CompareExchange<TargetListUpdateFinishedDelegate>(ref this.targetListUpdateFinishedDelegate_0, targetListUpdateFinishedDelegate3, targetListUpdateFinishedDelegate2);
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
                IncludeTargetsFilterDelegate includeTargetsFilterDelegate = this.includeTargetsFilterDelegate_0;
                IncludeTargetsFilterDelegate includeTargetsFilterDelegate2;
                do
                {
                    includeTargetsFilterDelegate2 = includeTargetsFilterDelegate;
                    IncludeTargetsFilterDelegate includeTargetsFilterDelegate3 = (IncludeTargetsFilterDelegate)Delegate.Combine(includeTargetsFilterDelegate2, value);
                    includeTargetsFilterDelegate = Interlocked.CompareExchange<IncludeTargetsFilterDelegate>(ref this.includeTargetsFilterDelegate_0, includeTargetsFilterDelegate3, includeTargetsFilterDelegate2);
                }
                while (includeTargetsFilterDelegate != includeTargetsFilterDelegate2);
            }
            remove
            {
                IncludeTargetsFilterDelegate includeTargetsFilterDelegate = this.includeTargetsFilterDelegate_0;
                IncludeTargetsFilterDelegate includeTargetsFilterDelegate2;
                do
                {
                    includeTargetsFilterDelegate2 = includeTargetsFilterDelegate;
                    IncludeTargetsFilterDelegate includeTargetsFilterDelegate3 = (IncludeTargetsFilterDelegate)Delegate.Remove(includeTargetsFilterDelegate2, value);
                    includeTargetsFilterDelegate = Interlocked.CompareExchange<IncludeTargetsFilterDelegate>(ref this.includeTargetsFilterDelegate_0, includeTargetsFilterDelegate3, includeTargetsFilterDelegate2);
                }
                while (includeTargetsFilterDelegate != includeTargetsFilterDelegate2);
            }
        }

        public event RemoveTargetsFilterDelegate RemoveTargetsFilter
        {
            add
            {
                RemoveTargetsFilterDelegate removeTargetsFilterDelegate = this.removeTargetsFilterDelegate_0;
                RemoveTargetsFilterDelegate removeTargetsFilterDelegate2;
                do
                {
                    removeTargetsFilterDelegate2 = removeTargetsFilterDelegate;
                    RemoveTargetsFilterDelegate removeTargetsFilterDelegate3 = (RemoveTargetsFilterDelegate)Delegate.Combine(removeTargetsFilterDelegate2, value);
                    removeTargetsFilterDelegate = Interlocked.CompareExchange<RemoveTargetsFilterDelegate>(ref this.removeTargetsFilterDelegate_0, removeTargetsFilterDelegate3, removeTargetsFilterDelegate2);
                }
                while (removeTargetsFilterDelegate != removeTargetsFilterDelegate2);
            }
            remove
            {
                RemoveTargetsFilterDelegate removeTargetsFilterDelegate = this.removeTargetsFilterDelegate_0;
                RemoveTargetsFilterDelegate removeTargetsFilterDelegate2;
                do
                {
                    removeTargetsFilterDelegate2 = removeTargetsFilterDelegate;
                    RemoveTargetsFilterDelegate removeTargetsFilterDelegate3 = (RemoveTargetsFilterDelegate)Delegate.Remove(removeTargetsFilterDelegate2, value);
                    removeTargetsFilterDelegate = Interlocked.CompareExchange<RemoveTargetsFilterDelegate>(ref this.removeTargetsFilterDelegate_0, removeTargetsFilterDelegate3, removeTargetsFilterDelegate2);
                }
                while (removeTargetsFilterDelegate != removeTargetsFilterDelegate2);
            }
        }

        public event WeighTargetsDelegate WeighTargetsFilter
        {
            add
            {
                WeighTargetsDelegate weighTargetsDelegate = this.weighTargetsDelegate_0;
                WeighTargetsDelegate weighTargetsDelegate2;
                do
                {
                    weighTargetsDelegate2 = weighTargetsDelegate;
                    WeighTargetsDelegate weighTargetsDelegate3 = (WeighTargetsDelegate)Delegate.Combine(weighTargetsDelegate2, value);
                    weighTargetsDelegate = Interlocked.CompareExchange<WeighTargetsDelegate>(ref this.weighTargetsDelegate_0, weighTargetsDelegate3, weighTargetsDelegate2);
                }
                while (weighTargetsDelegate != weighTargetsDelegate2);
            }
            remove
            {
                WeighTargetsDelegate weighTargetsDelegate = this.weighTargetsDelegate_0;
                WeighTargetsDelegate weighTargetsDelegate2;
                do
                {
                    weighTargetsDelegate2 = weighTargetsDelegate;
                    WeighTargetsDelegate weighTargetsDelegate3 = (WeighTargetsDelegate)Delegate.Remove(weighTargetsDelegate2, value);
                    weighTargetsDelegate = Interlocked.CompareExchange<WeighTargetsDelegate>(ref this.weighTargetsDelegate_0, weighTargetsDelegate3, weighTargetsDelegate2);
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
                if (Targeting.func_0 == null)
                {
                    Targeting.func_0 = new Func<WoWObject, bool>(IsUnitOrPlayer);
                }
                return objectList.Where(Targeting.func_0).ToList<WoWObject>();
            }
            else if (!this.IncludeWorldPlayers)
            {
                IEnumerable<WoWObject> objectList2 = ObjectManager.ObjectList;
                if (Targeting.func_2 == null)
                {
                    Targeting.func_2 = new Func<WoWObject, bool>(IsUnitNotPlayer);
                }
                return objectList2.Where(Targeting.func_2).ToList<WoWObject>();
            }
            else
            {
                IEnumerable<WoWObject> objectList3 = ObjectManager.ObjectList;
                if (Targeting.func_1 == null)
                {
                    Targeting.func_1 = new Func<WoWObject, bool>(IsUnit);
                }
                return objectList3.Where(Targeting.func_1).ToList<WoWObject>();
            }
        }

        internal virtual void Update()
        {
            try
            {
                if (!(StyxWoW.Me == null))
                {
                    if (StyxWoW.IsInGame)
                    {
                        List<WoWObject> initialObjectList = this.GetInitialObjectList();
                        Delegate e = this.removeTargetsFilterDelegate_0;
                        object[] array = new object[] { initialObjectList };
                        this.InvokeFilterDelegate(e, array);
                        HashSet<WoWObject> hashSet = new HashSet<WoWObject>();
                        Delegate e2 = this.includeTargetsFilterDelegate_0;
                        object[] array2 = new object[] { initialObjectList, hashSet };
                        this.InvokeFilterDelegate(e2, array2);
                        IEnumerable<WoWObject> source = hashSet;
                        if (Targeting.func_3 == null)
                        {
                            Targeting.func_3 = new Func<WoWObject, TargetPriority>(CreateTargetPriority);
                        }
                        List<TargetPriority> list = source.Select(Targeting.func_3).ToList<TargetPriority>();
                        Delegate e3 = this.weighTargetsDelegate_0;
                        object[] array3 = new object[] { list };
                        this.InvokeFilterDelegate(e3, array3);
                        IEnumerable<TargetPriority> source2 = list;
                        if (Targeting.func_4 == null)
                        {
                            Targeting.func_4 = new Func<TargetPriority, double>(GetScore);
                        }
                        list = source2.OrderByDescending(Targeting.func_4).Take(this.MaxTargets).ToList<TargetPriority>();
                        IEnumerable<TargetPriority> source3 = list;
                        if (Targeting.func_5 == null)
                        {
                            Targeting.func_5 = new Func<TargetPriority, WoWObject>(GetObject);
                        }
                        this.ObjectList = source3.Select(Targeting.func_5).ToList<WoWObject>();
                        Targeting._blacklistedMobNames.Clear();
                        foreach (TargetPriority targetPriority in list)
                        {
                            Targeting._blacklistedMobNames.Add(string.Format("{0} Dist: {1}", targetPriority.Object.Name, Math.Ceiling(targetPriority.Object.Distance)));
                        }
                        if (this.targetListUpdateFinishedDelegate_0 != null)
                        {
                            this.targetListUpdateFinishedDelegate_0(Targeting._blacklistedMobNames);
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

        public virtual void Pulse()
        {
            // mirror HB behaviour: perform the update inside a memory frame
            using (StyxWoW.Memory.AcquireFrame())
            {
                Update();
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
            bool flag = StyxWoW.Me.IsInParty || StyxWoW.Me.IsInRaid; // party/raid flag
            double collectionRangeSq = Targeting.CollectionRange * Targeting.CollectionRange;
            int minLevel = 0;
            int maxLevel = 1000;
            bool combat = StyxWoW.Me.Combat;
            var currentGrindArea = StyxWoW.AreaManager.CurrentGrindArea;
            Profile currentProfile = ProfileManager.CurrentProfile;
            if (currentGrindArea != null && !combat)
            {
                minLevel = currentGrindArea.TargetMinLevel;
                maxLevel = currentGrindArea.TargetMaxLevel;
            }

            // gather guid lists for group/raid protection
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

            for (int i = units.Count - 1; i >= 0; i--)
            {
                WoWObject obj = units[i];
                if (!(obj is WoWUnit))
                {
                    units.RemoveAt(i);
                    continue;
                }
                WoWUnit unit = (WoWUnit)obj;
                bool isPlayer = unit is WoWPlayer;

                // replicate original nested logic
                bool allowedByGrindArea = (currentProfile != null && currentProfile.Factions != null && currentProfile.Factions.Contains((uint)unit.FactionId))
                    || (currentGrindArea != null && (currentGrindArea.Factions.Contains((int)unit.FactionId) || currentGrindArea.MobIDs.Contains((int)unit.Entry)));

                if (allowedByGrindArea || !combat || !flag || (!playerGuids.Contains(unit.CurrentTargetGuid) && !minionGuids.Contains(unit.Guid)))
                {
                    bool isAlive = unit.IsAlive;
                    bool petAggro = unit.PetAggro;
                    if (allowedByGrindArea || !combat || !isAlive || (!petAggro && !unit.Aggro && !unit.IsTargetingMeOrPet && !unit.IsTargetingAnyMinion))
                    {
                        double distSq = unit.DistanceSqr;
                        bool isFriendly = unit.IsFriendly;
                        if (!allowedByGrindArea || !combat || !isAlive || distSq >= collectionRangeSq || isFriendly || (!unit.IsTargetingMeOrPet && !unit.IsTargetingAnyMinion))
                        {
                            // perform removal checks
                            bool shouldRemove = _blacklistedMobIds.Contains(unit.Entry)
                                || unit.Level < minLevel
                                || unit.Level > maxLevel
                                || Blacklist.Contains(unit.Guid, false)
                                || unit.Dead
                                || unit.DistanceSqr > collectionRangeSq
                                || unit.OnTaxi
                                || unit.CreatureType == WoWCreatureType.Critter
                                || unit.IsNonCombatPet
                                || unit.IsFlightMaster
                                || unit.IsFlying
                                || !unit.Attackable
                                || isFriendly
                                || (unit.TaggedByOther && !flag)
                                || (currentProfile != null && (currentProfile.AvoidMobs.Contains(unit.Entry) || currentProfile.AvoidMobs.Contains(unit.Name)))
                                || (currentProfile != null && (Targeting.IsTooNearBlackspot(currentProfile.Blackspots, unit.Location) || this.IsNotWithinHotspotRange(unit.Location, false)));

                            if (shouldRemove && !allowedByGrindArea)
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
            List<WoWUnit> minions = StyxWoW.Me.Minions;
            bool isBeingAttacked = StyxWoW.Me.IsBeingAttacked;
            bool mounted = StyxWoW.Me.Mounted;
            bool isFlying = StyxWoW.Me.IsFlying;
            WoWUnit activeMover = WoWMovement.ActiveMover;
            bool flag;
            if (activeMover != null && activeMover.IsMoving)
            {
                _includeTimer.Reset();
                flag = true;
            }
            else
            {
                flag = !_includeTimer.IsFinished;
            }
            Profile currentProfile = ProfileManager.CurrentProfile;
            HashSet<uint> factionSet = new HashSet<uint>();
            List<int> areaMobIds = new List<int>();
            if (StyxWoW.AreaManager.CurrentGrindArea != null)
            {
                areaMobIds = StyxWoW.AreaManager.CurrentGrindArea.MobIDs;
                if (StyxWoW.AreaManager.CurrentGrindArea.Factions.Count > 0)
                {
                    foreach (int fac in StyxWoW.AreaManager.CurrentGrindArea.Factions)
                        factionSet.Add((uint)fac);
                }
            }
            else if (currentProfile != null)
            {
                foreach (uint fac in currentProfile.Factions)
                    factionSet.Add(fac);
            }
            using (StyxWoW.Memory.AcquireFrame())
            {
                foreach (WoWObject o in incomingUnits)
                {
                    WoWUnit unit = o.ToUnit();
                    bool isInsideBattleground = Battlegrounds.IsInsideBattleground;
                    if (BotPoi.Current.Type == PoiType.Kill && unit.Guid == BotPoi.Current.Guid && !unit.TaggedByOther)
                    {
                        outgoingUnits.Add(unit);
                    }
                    else if (isBeingAttacked && (!isFlying || areaMobIds.Contains((int)unit.Entry) || factionSet.Contains(unit.FactionId)))
                    {
                        WoWPlayer pl = o as WoWPlayer;
                        if (pl != null && !isInsideBattleground && pl.Combat &&
                            (pl.Aggro || pl.PetAggro || pl.IsTargetingMeOrPet || (pl.GotTarget && minions.Any(m => m.Guid == pl.CurrentTargetGuid))))
                        {
                            outgoingUnits.Add(pl);
                        }
                        else if (pl == null && (areaMobIds.Contains((int)unit.Entry) || factionSet.Contains(unit.FactionId) || (!mounted || !flag) ||
                                  (StyxSettings.Instance.KillBetweenHotspots && unit.Difficulty != DifficultyColor.Gray)) && !unit.IsCritter)
                        {
                            if (!unit.Aggro && !unit.PetAggro && (!unit.Fleeing || !unit.TaggedByMe) &&
                                (!unit.GotTarget || !minions.Any(m => m.Guid == unit.CurrentTargetGuid)))
                            {
                                if (IsSpecialQuestMob(unit))
                                {
                                    outgoingUnits.Add(unit);
                                }
                            }
                            else
                            {
                                WoWUnit owner = unit.OwnedByRoot;
                                if (owner != null && owner.IsPlayer && !owner.IsFriendly)
                                {
                                    outgoingUnits.Add(owner);
                                }
                                outgoingUnits.Add(unit);
                            }
                        }
                    }
                }
            }
        }

        protected virtual void DefaultTargetWeight(List<TargetPriority> units)
        {
            LocalPlayer me = StyxWoW.Me;
            HashSet<ulong> playerAndPets = new HashSet<ulong> { me.Guid };
            foreach (WoWUnit pet in me.Minions)
                playerAndPets.Add(pet.Guid);
            ulong poiGuid = BotPoi.Current.Guid;
            WoWPoint myLoc = me.Location;
            Profile currentProfile = ProfileManager.CurrentProfile;
            // LOS computation is now handled by ObjectManager.ScanCaches(),
            // which keeps a cache updated on each AcquireFrame call.

            using (StyxWoW.Memory.AcquireFrame())
            {
                for (int j = units.Count - 1; j >= 0; j--)
                {
                    WoWUnit unit = units[j].Object.ToUnit();
                    ulong guid = unit.Guid;
                    double dist = ObjectManager.GetDistance(guid);
                    double score = 200.0 - 2.0 * dist;
                    if (me.Combat)
                    {
                        if (unit.MaxMana > 1 && unit.ManaPercent > 5.0)
                            score += 60.0;
                        score -= unit.HealthPercent;
                        if (poiGuid == guid)
                            score += 50.0;
                        if (playerAndPets.Contains(unit.CurrentTargetGuid))
                            score += 110.0;
                        if (unit.Fleeing)
                            score -= 1000.0;
                    }
                    else
                    {
                        if (currentProfile != null && !currentProfile.TargetElites && unit.Elite)
                            score -= 1000.0;
                        if (unit.MyReaction <= WoWUnitReaction.Neutral)
                            score += (30.0 - dist) * (double)(WoWUnitReaction.Friendly - unit.MyReaction);
                        if (poiGuid == guid)
                            score += 1000.0;
                        if (unit.IsPet)
                            score -= 50.0;
                        float aggroRange = unit.MyAggroRange;
                        if (aggroRange != 0f && dist < (double)(aggroRange + 5f))
                            score += 100.0;
                        if (!ObjectManager.InLineOfSight(guid))
                            score += 25.0;
                        else
                            score -= 25.0;
                        if (unit.Entry == 61245U)
                            score += 50000.0;
                    }
                    WoWUnit owner = unit.OwnedByRoot;
                    if (owner != null && owner.IsPlayer && !owner.IsFriendly)
                        score -= 6000.0;
                    units[j].Score += score;
                }
            }
        }

        private bool IsSpecialQuestMob(WoWUnit u)
        {
            return _specialQuestMobIds.Contains(u.Entry) && ((u.TaggedByMe && u.HealthPercent < 99.0) ||
                   ((StyxWoW.Me.QuestLog.ContainsQuest(12678U) || StyxWoW.Me.QuestLog.ContainsQuest(12722U)) && u.DistanceSqr < 400.0));
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

        /// <summary>
        /// Counts units near a location.
        /// </summary>
        private static int CountUnitsNearLocation(WoWPoint location, float radius)
        {
            int count = 0;
            foreach (WoWObject obj in ObjectManager.ObjectList)
            {
                if (obj is WoWUnit unit && unit.Location.Distance(location) <= radius)
                {
                    count++;
                }
            }
            return count;
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
