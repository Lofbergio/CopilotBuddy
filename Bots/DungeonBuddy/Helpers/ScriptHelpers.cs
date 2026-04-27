using System;
using System.Collections.Generic;
using System.Linq;
using Bots.DungeonBuddy.Avoidance;
using Bots.DungeonBuddy.Enums;
using CommonBehaviors.Actions;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Bots.DungeonBuddy.Helpers
{
    /// <summary>
    /// Helpers pour écrire les scripts de donjons.
    /// Utilisés avec [EncounterHandler] et [ObjectHandler].
    /// NOTE: Requiert WoWPlayerExtensions.cs pour IsTank()/IsDps()/IsHealer()
    /// </summary>
    public static class ScriptHelpers
    {
        // ═══════════════════════════════════════════════════════════
        // PHASE 1: ROLES & PLAYER EXTENSIONS (Ported from HB 4.3.4)
        // ═══════════════════════════════════════════════════════════
        
        [Flags]
        public enum PartyRole
        {
            None = 0,
            Tank = 1,
            Healer = 2,
            Dps = 4,
            Melee = 8,
            Ranged = 16,
            Leader = 32,
            Follower = 64
        }

        public static bool IsTank(this LocalPlayer me)
        {
            if (DungeonBuddySettings.Instance.QueueType == QueueType.SoloFarm) return true;
            if (me.Role != WoWPartyMember.GroupRole.None) return (me.Role & WoWPartyMember.GroupRole.Tank) > WoWPartyMember.GroupRole.None;
            
            switch (me.Class)
            {
                case WoWClass.Warrior: return SpellManager.HasSpell("Shield Slam");
                case WoWClass.Paladin: return SpellManager.HasSpell("Avenger's Shield");
                case WoWClass.DeathKnight: return SpellManager.HasSpell("Heart Strike") || SpellManager.HasSpell("Blood Strike"); // Fallback simple WotLK
                case WoWClass.Druid: return SpellManager.HasSpell("Mangle (Bear)") || me.HasAura("Thick Hide");
                default: return false;
            }
        }

        public static bool IsHealer(this LocalPlayer me)
        {
            if (me.Role != WoWPartyMember.GroupRole.None) return (me.Role & WoWPartyMember.GroupRole.Healer) > WoWPartyMember.GroupRole.None;
            
            switch (me.Class)
            {
                case WoWClass.Paladin: return SpellManager.HasSpell("Holy Shock");
                case WoWClass.Priest: return !SpellManager.HasSpell("Mind Flay");
                case WoWClass.Shaman: return SpellManager.HasSpell("Earth Shield") || SpellManager.HasSpell("Riptide");
                case WoWClass.Druid: return SpellManager.HasSpell("Tree of Life") || SpellManager.HasSpell("Wild Growth");
                default: return false;
            }
        }

        public static bool IsDps(this LocalPlayer me)
        {
            if (me.Role != WoWPartyMember.GroupRole.None) return (me.Role & WoWPartyMember.GroupRole.Damage) > WoWPartyMember.GroupRole.None;
            return !me.IsTank() && !me.IsHealer();
        }

        public static bool IsMelee(this LocalPlayer me)
        {
            switch (me.Class)
            {
                case WoWClass.Warrior:
                case WoWClass.Rogue:
                case WoWClass.DeathKnight:
                    return true;
                case WoWClass.Paladin:
                    return !SpellManager.HasSpell("Holy Shock");
                case WoWClass.Shaman:
                    return SpellManager.HasSpell("Lava Lash") || SpellManager.HasSpell("Stormstrike");
                case WoWClass.Druid:
                    return SpellManager.HasSpell("Mangle (Cat)");
            }
            return false;
        }

        public static bool IsRange(this LocalPlayer me) => !me.IsMelee();

        public static bool IsFollower(this LocalPlayer me)
        {
            if (DungeonBuddySettings.Instance.PartyMode != PartyMode.Follower) return !me.IsTank();
            return true;
        }

        public static bool IsLeader(this LocalPlayer me)
        {
            if (DungeonBuddySettings.Instance.PartyMode != PartyMode.Leader) return me.IsTank();
            return true;
        }

        // --- WOWPLAYER/WOWUNIT OVERLOADS ---
        // Role methods removed. Using Styx.Helpers.WoWPlayerExtensions instead to resolve ambiguity.

        // ═══════════════════════════════════════════════════════════
        // PHASE 2, 3, 4: COMBAT, OBJECTS, BOSSES (Ported from HB 4.3.4)
        // ═══════════════════════════════════════════════════════════

        public static BossManager.Boss CurrentBoss => BossManager.Bosses.FirstOrDefault(b => !b.IsDead);

        public static bool ShouldKillBoss(string name)
        {
            var boss = BossManager.BossEncounters.FirstOrDefault(b => b.Name == name);
            if (boss != null) return !boss.Optional || DungeonBuddySettings.Instance.KillOptionalBosses;
            return false;
        }

        public static bool ShouldKillBoss(uint bossId)
        {
            var boss = BossManager.BossEncounters.FirstOrDefault(b => b.Entry == bossId);
            if (boss != null) return !boss.Optional || DungeonBuddySettings.Instance.KillOptionalBosses;
            return false;
        }

        public static Composite CreateSpreadOutLogic(CanRunDecoratorDelegate canRun, int distanceToSpread)
        {
            return CreateSpreadOutLogic(canRun, () => StyxWoW.Me.Location, distanceToSpread, 35f);
        }

        public static Composite CreateSpreadOutLogic(CanRunDecoratorDelegate canRun, Func<WoWPoint> centerPointSelector, int distanceToSpread, float maxDistanceFromCenterPoint)
        {
            return new Decorator(canRun, new Action(ctx => 
            {
                var myLoc = StyxWoW.Me.Location;
                var center = centerPointSelector != null ? centerPointSelector() : myLoc;
                var tooClose = PartyIncludingMe.FirstOrDefault(p => p != StyxWoW.Me && p.IsAlive && p.Location.Distance(myLoc) < distanceToSpread);
                if (tooClose != null)
                {
                    var awayPoint = Styx.Helpers.WoWMathHelper.CalculatePointFrom(myLoc, tooClose.Location, distanceToSpread);
                    if (center.Distance(awayPoint) <= maxDistanceFromCenterPoint)
                    {
                        Navigator.MoveTo(awayPoint);
                        return RunStatus.Success;
                    }
                }
                return RunStatus.Failure;
            }));
        }

        public static Composite CreateForceJump(CanRunDecoratorDelegate canRun, Func<WoWPoint> from, Func<WoWPoint> to)
        {
            return new Decorator(canRun, new Action(ctx => 
            {
                WoWMovement.Move(WoWMovement.MovementDirection.JumpAscend);
                Navigator.MoveTo(to());
                return RunStatus.Success;
            }));
        }

        public static Composite DisableMovement(Func<bool> whileTrue)
        {
            return new Decorator(ctx => whileTrue(), new Action(ctx => { Navigator.PlayerMover.MoveStop(); return RunStatus.Success; }));
        }

        public static WoWUnit FindBestTargetWithIdsRange(float range, params uint[] entryIds)
        {
            return ObjectManager.GetObjectsOfType<WoWUnit>()
                .Where(u => u.IsAlive && u.DistanceSqr <= range * range && entryIds.Contains(u.Entry))
                .OrderBy(u => u.DistanceSqr)
                .FirstOrDefault();
        }

        public static Composite CreateInteractWithObject(Func<WoWGameObject> objectSelector, float range)
        {
            return CreateInteractWithObject(objectSelector, range, false);
        }

        public static Composite CreateInteractWithObject(uint id) => CreateInteractWithObject(() => ObjectManager.GetObjectsOfType<WoWGameObject>().FirstOrDefault(o => o.Entry == id), 5f, false);
        
        public static Composite CreateInteractWithObject(uint id, float range) => CreateInteractWithObject(() => ObjectManager.GetObjectsOfType<WoWGameObject>().FirstOrDefault(o => o.Entry == id), range, false);

        public static Composite CreateInteractWithObject(uint id, int channelTime) => CreateInteractWithObject(() => ObjectManager.GetObjectsOfType<WoWGameObject>().FirstOrDefault(o => o.Entry == id), 5f, false);
        
        public static Composite CreateInteractWithObject(uint id, int channelTime, bool ignoreCombat) => CreateInteractWithObject(() => ObjectManager.GetObjectsOfType<WoWGameObject>().FirstOrDefault(o => o.Entry == id), 5f, ignoreCombat);

        public static Composite CreateInteractWithObjectContinue(CanRunDecoratorDelegate canRun, Func<WoWGameObject> obj, float range = 5f)
        {
            return new Decorator(canRun, CreateInteractWithObject(obj, range, false));
        }

        public static Composite CreateInteractWithObjectContinue(uint id) => CreateInteractWithObjectContinue(ctx => true, () => ObjectManager.GetObjectsOfType<WoWGameObject>().FirstOrDefault(o => o.Entry == id), 5f);
        
        public static Composite CreateInteractWithObjectContinue(uint id, int channelTime) => CreateInteractWithObjectContinue(ctx => true, () => ObjectManager.GetObjectsOfType<WoWGameObject>().FirstOrDefault(o => o.Entry == id), 5f);
        
        public static Composite CreateInteractWithObjectContinue(uint id, int channelTime, bool ignoreCombat) => CreateInteractWithObjectContinue(ctx => true, () => ObjectManager.GetObjectsOfType<WoWGameObject>().FirstOrDefault(o => o.Entry == id), 5f);

        public static Composite CreateTalkToNpcContinue(CanRunDecoratorDelegate canRun, Func<WoWUnit> npc)
        {
            return new Decorator(canRun, new Action(ctx => 
            {
                var u = npc();
                if (u != null && u.WithinInteractRange) { u.Interact(); return RunStatus.Success; }
                else if (u != null) { Navigator.MoveTo(u.Location); return RunStatus.Success; }
                return RunStatus.Failure;
            }));
        }

        public static Composite CreateTalkToNpcContinue(Func<WoWUnit> npc) => CreateTalkToNpcContinue(ctx => true, npc);

        public static Composite CreateTalkToNpcContinue(uint unitId) => CreateTalkToNpcContinue(unitId, 0);

        public static Composite CreateTalkToNpcContinue(uint unitId, int gossipIndex)
        {
            WoWUnit cachedUnit = null;
            return new Decorator(
                ctx => true,
                new Sequence(
                    new Action(ctx => cachedUnit = ObjectManager.GetObjectsOfType<WoWUnit>().FirstOrDefault(u => u.Entry == unitId)),
                    CreateMoveToContinue(ctx => cachedUnit != null && !cachedUnit.WithinInteractRange, () => cachedUnit?.Location ?? WoWPoint.Empty, true),
                    new WaitContinue(2, ctx => GossipFrame.Instance.IsVisible,
                        new Sequence(
                            new Action(ctx => { if (cachedUnit != null) cachedUnit.Interact(); }),
                            new Action(ctx => GossipFrame.Instance.SelectGossipOption(gossipIndex))
                        ))
                ));
        }

        public static Composite CreateTankTalkToThenEscortNpc(CanRunDecoratorDelegate canRun, Func<WoWUnit> npc, float range, bool waitToTalk, int gossipIndex, Func<WoWPoint> waitLocation)
        {
            return new Decorator(ctx => canRun(ctx) && StyxWoW.Me.IsTank(), new ActionAlwaysSucceed()); 
        }

        public static void MarkAsDead(this WoWUnit unit)
        {
            if (unit != null) BossManager.MarkBossDead(unit.Entry);
        }

        public static Composite CreateTankAgainstObject(Func<WoWGameObject> obj)
        {
            return new Decorator(ctx => StyxWoW.Me.IsTank(), new ActionAlwaysSucceed());
        }

        public static Composite CreateTankAgainstObject(CanRunDecoratorDelegate canRun, Func<WoWGameObject> obj, float range)
        {
            return new Decorator(ctx => canRun(ctx) && StyxWoW.Me.IsTank(), new ActionAlwaysSucceed());
        }

        public static Composite CreateTankAgainstObject(CanRunDecoratorDelegate canRun, Func<WoWPoint> objectLocationToTankAt, Func<float> objectRadius)
        {
            return new Decorator(ctx => canRun(ctx) && StyxWoW.Me.IsTank(), new ActionAlwaysSucceed());
        }

        public static Composite CreateTurninQuest(uint npcId) => new ActionAlwaysSucceed();
        public static Composite CreateTurninQuest(uint npcId, int rewardIndex) => new ActionAlwaysSucceed();
        public static Composite CreateTurninQuest(uint npcId, int gossipIndex, int rewardIndex) => new ActionAlwaysSucceed();
        
        public static Composite CreatePickupQuest(uint npcId) => new ActionAlwaysSucceed();
        public static Composite CreatePickupQuest(uint npcId, int questIndex) => new ActionAlwaysSucceed();

        public static void BuyItem(uint itemId, int amount)
        {
            Lua.DoString($"BuyMerchantItem({itemId}, {amount})");
        }

        public static Composite CreateCastRangedAbility() => new ActionAlwaysSucceed();
        
        public static Composite CreateCastRangedAbility(CanRunDecoratorDelegate canRun, string spellName, Func<WoWUnit> target)
        {
            return new Decorator(canRun, new Action(ctx => 
            {
                var t = target();
                if (t != null && SpellManager.CanCast(spellName, t)) { SpellManager.Cast(spellName, t); return RunStatus.Success; }
                return RunStatus.Failure;
            }));
        }
        
        public static Composite CreateCastRangedAbility(string spellName, Func<WoWUnit> target) => CreateCastRangedAbility(ctx => true, spellName, target);

        public static Composite CreateWaitAtLocationUntilTankPulled(CanRunDecoratorDelegate canRun, Func<WoWPoint> loc) => new ActionAlwaysSucceed();
        
        public static Composite CreateWaitAtLocationUntilTankPulled(CanRunDecoratorDelegate canRun, Func<WoWPoint> loc, float radius)
        {
            return new Decorator(canRun, new Action(ctx => 
            {
                if (StyxWoW.Me.Location.Distance(loc()) > radius) Navigator.MoveTo(loc());
                return RunStatus.Success;
            }));
        }
        
        public static Composite CreateWaitAtLocationUntilTankPulled(Func<WoWPoint> loc) => CreateWaitAtLocationUntilTankPulled(ctx => true, loc, 5f);

        public static Composite CreateLosLocation(CanRunDecoratorDelegate canRun, Func<WoWPoint> locationToLos, Func<WoWPoint> objectLocationToLosAt, Func<float> objectRadius) => new ActionAlwaysSucceed();
        
        public static Composite CreateLosLocation(CanRunDecoratorDelegate canRun, Func<WoWPoint> loc)
        {
            return new Decorator(canRun, new Action(ctx => 
            {
                Navigator.MoveTo(loc());
                return RunStatus.Success;
            }));
        }
        
        public static Composite CreateLosLocation(CanRunDecoratorDelegate canRun, Func<WoWUnit> unitSelector, float range, Func<WoWPoint> loc)
        {
            return new Decorator(canRun, new Action(ctx => 
            {
                Navigator.MoveTo(loc());
                return RunStatus.Success;
            }));
        }
        
        public static Composite CreateForceJump(CanRunDecoratorDelegate canRun, Func<WoWPoint> from, Func<WoWPoint> to, int distance)
        {
            return CreateForceJump(canRun, from, to);
        }

        public static Composite CreateForceJump(CanRunDecoratorDelegate canRun, bool actuallyJump, WoWPoint from, WoWPoint to) => new ActionAlwaysSucceed();
        public static Composite CreateForceJump(CanRunDecoratorDelegate canRun, WoWPoint from, WoWPoint to) => new ActionAlwaysSucceed();

        public static Composite CreateRunAwayFromLocation(CanRunDecoratorDelegate canRun, Func<WoWPoint> loc, float distance)
        {
            return new Decorator(canRun, new ActionAlwaysSucceed());
        }

        public static Composite CreateRunAwayFromLocation(CanRunDecoratorDelegate canRun, float distance, Func<WoWPoint> loc)
        {
            return CreateRunAwayFromLocation(canRun, loc, distance);
        }

        public static Composite CreateJumpDown(CanRunDecoratorDelegate canRun, WoWPoint from, WoWPoint to) => new ActionAlwaysSucceed();
        public static Composite CreateJumpDown(CanRunDecoratorDelegate canRun, bool actuallyJump, WoWPoint from, WoWPoint to) => new ActionAlwaysSucceed();

        public static Composite CreateJumpDown(CanRunDecoratorDelegate canRun, Func<WoWPoint> pointSelector)
        {
            return new Decorator(canRun, new ActionAlwaysSucceed());
        }

        public static Composite GetBehindUnit(CanRunDecoratorDelegate canRun, Func<WoWUnit> unit)
        {
            return new Decorator(canRun, new ActionAlwaysSucceed());
        }
        
        public static Composite CreateMountBehavior() => new ActionAlwaysSucceed();
        public static Composite CreateMountBehavior(Func<WoWPoint> destinationSelector) => new ActionAlwaysSucceed();

        public static Composite CreateMountBehavior(CanRunDecoratorDelegate canRun)
        {
            return new Decorator(canRun, new ActionAlwaysSucceed());
        }
        
        public static Composite CreateTankTalkToThenEscortNpc(int escortNpcId, int gossipIndex, WoWPoint escortNpcLocation, WoWPoint endLocation, float followDistance, Func<bool> isDoneCondition) => new ActionAlwaysSucceed();
        public static Composite CreateTankTalkToThenEscortNpc(int escortNpcId, WoWPoint escortNpcLocation, WoWPoint endLocation) => new ActionAlwaysSucceed();
        
        public static Composite ControlledMoveTo(Func<WoWPoint> loc)
        {
            return new Action(ctx => { Navigator.MoveTo(loc()); return RunStatus.Success; });
        }

        public static Composite ControlledMoveTo(WoWPoint loc)
        {
            return new Action(ctx => { Navigator.MoveTo(loc); return RunStatus.Success; });
        }

        // ═══════════════════════════════════════════════════════════
        // PARTY ROLE DETECTION
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Obtient le rôle du joueur actuel
        /// </summary>
        public static PartyRole MyRole
        {
            get
            {
                string role = Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0);
                return role switch
                {
                    "TANK" => PartyRole.Tank,
                    "HEALER" => PartyRole.Healer,
                    "DAMAGER" => PartyRole.Dps,
                    _ => PartyRole.Dps
                };
            }
        }

        /// <summary>
        /// Le joueur est le tank du groupe
        /// </summary>
        public static WoWPlayer Tank
        {
            get
            {
                return GetPartyMembersByRole(PartyRole.Tank).FirstOrDefault();
            }
        }

        /// <summary>
        /// Le joueur est le healer du groupe
        /// </summary>
        public static WoWPlayer Healer
        {
            get
            {
                return GetPartyMembersByRole(PartyRole.Healer).FirstOrDefault();
            }
        }

        public static IEnumerable<WoWPlayer> GetPartyMembersByRole(PartyRole role)
        {
            foreach (var member in StyxWoW.Me.GroupInfo.RaidMembers)
            {
                var player = member.ToPlayer();
                if (player == null) continue;

                string roleStr = Lua.GetReturnVal<string>(
                    $"return UnitGroupRolesAssigned('{player.Name}')", 0);
                
                var playerRole = roleStr switch
                {
                    "TANK" => PartyRole.Tank,
                    "HEALER" => PartyRole.Healer,
                    "DAMAGER" => PartyRole.Dps,
                    _ => PartyRole.None
                };

                if ((playerRole & role) != 0)
                    yield return player;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // EVENT TRACKING
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// True si un event de script est en cours (RP, cinématique, etc.)
        /// </summary>
        public static bool EventInProcess { get; set; }

        // Movement control helper (simple toggle for UI compatibility)
        private static bool _movementEnabled = true;
        public static bool MovementEnabled => _movementEnabled;
        public static void ToggleMovement()
        {
            _movementEnabled = !_movementEnabled;
        }

        // ═══════════════════════════════════════════════════════════
        // AVOIDANCE BEHAVIORS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Crée un behavior pour fuir une zone dangereuse
        /// </summary>
        /// <param name="condition">Condition pour activer la fuite</param>
        /// <param name="radius">Rayon à éviter</param>
        /// <param name="objectEntryId">Entry ID de l'objet/mob à fuir</param>
        public static Composite CreateRunAwayFromBad(
            CanRunDecoratorDelegate condition,
            float radius,
            uint objectEntryId)
        {
            return CreateRunAwayFromBad(
                condition,
                radius,
                obj => obj.Entry == objectEntryId);
        }

        /// <summary>
        /// Overload params uint[] — Violet Hold, DTK, Ahn'kahet.
        /// </summary>
        public static Composite CreateRunAwayFromBad(
            CanRunDecoratorDelegate condition,
            float radius,
            params uint[] objectEntries) =>
            CreateRunAwayFromBad(condition, radius, obj => objectEntries.Contains(obj.Entry));

        /// <summary>
        /// Overload avec Func center + range + radius + uint — HoL, Gundrak, DTK.
        /// </summary>
        public static Composite CreateRunAwayFromBad(
            CanRunDecoratorDelegate condition,
            Func<WoWPoint> centerSelector,
            float range,
            float radius,
            uint objectEntry) =>
            CreateRunAwayFromBad(
                ctx => condition(ctx) &&
                       centerSelector != null &&
                       StyxWoW.Me.Location.DistanceSqr(centerSelector()) < range * range,
                radius,
                obj => obj.Entry == objectEntry);

        /// <summary>
        /// Overload avec Func center + range + radius + Predicate — DTK Trollgore.
        /// </summary>
        public static Composite CreateRunAwayFromBad(
            CanRunDecoratorDelegate condition,
            Func<WoWPoint> centerSelector,
            float range,
            float radius,
            Predicate<WoWObject> objectSelector) =>
            CreateRunAwayFromBad(
                ctx => condition(ctx) &&
                       centerSelector != null &&
                       StyxWoW.Me.Location.DistanceSqr(centerSelector()) < range * range,
                radius,
                objectSelector);

        /// <summary>
        /// Overload avec WoWPoint direct (pas Func) — Azjol-Nerub, Deadmines.
        /// HB 4.3.4: convertit WoWPoint en lambda pour les scripts qui passent le point directement.
        /// </summary>
        public static Composite CreateRunAwayFromBad(
            CanRunDecoratorDelegate condition,
            WoWPoint centerPoint,
            float radius,
            float maxDistance,
            uint objectEntry) =>
            CreateRunAwayFromBad(
                ctx => condition(ctx) &&
                       StyxWoW.Me.Location.DistanceSqr(centerPoint) < radius * radius,
                maxDistance,
                obj => obj.Entry == objectEntry);

        public static Composite CreateRunAwayFromBad(
            CanRunDecoratorDelegate condition,
            WoWPoint centerPoint,
            float radius,
            float maxDistance,
            uint[] objectEntries) =>
            CreateRunAwayFromBad(
                ctx => condition(ctx) &&
                       StyxWoW.Me.Location.DistanceSqr(centerPoint) < radius * radius,
                maxDistance,
                obj => objectEntries.Contains(obj.Entry));

        public static Composite CreateRunAwayFromBad(
            CanRunDecoratorDelegate condition,
            WoWPoint centerPoint,
            float radius,
            float maxDistance,
            Predicate<WoWObject> objectSelector) =>
            CreateRunAwayFromBad(
                ctx => condition(ctx) &&
                       StyxWoW.Me.Location.DistanceSqr(centerPoint) < radius * radius,
                maxDistance,
                objectSelector);

        public static Composite CreateRunAwayFromBad(
            CanRunDecoratorDelegate condition,
            float radius,
            Predicate<WoWObject> objectSelector)
        {
            WoWObject badThing = null;
            float radiusSqr = radius * radius;

            return new Decorator(
                ctx =>
                {
                    if (!condition(ctx))
                        return false;

                    badThing = ObjectManager.ObjectList
                        .Where(obj => objectSelector(obj) && obj.DistanceSqr < radiusSqr)
                        .OrderBy(obj => obj.DistanceSqr)
                        .FirstOrDefault();

                    return badThing != null;
                },
                new Action(ctx =>
                {
                    var safePoint = Bots.DungeonBuddy.Avoidance.AvoidanceManager.GetSafePoint(StyxWoW.Me.Location, radius);
                    Navigator.MoveTo(safePoint);
                    return RunStatus.Running;
                })
            );
        }

        // ═══════════════════════════════════════════════════════════
        // TANK BEHAVIORS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Le tank doit faire face away du groupe (pour cleave/breath)
        /// </summary>
        public static Composite CreateTankFaceAwayGroupUnit(float distance = 10f)
        {
            return new Decorator(
                ctx => StyxWoW.Me.IsTank() && StyxWoW.Me.CurrentTarget != null,
                new Action(ctx =>
                {
                    var target = StyxWoW.Me.CurrentTarget;
                    var groupCenter = GetGroupCenter();
                    
                    // Position opposée au groupe
                    var directionFromGroup = (StyxWoW.Me.Location - groupCenter);
                    directionFromGroup.Normalize();
                    
                    var tankPosition = target.Location + (directionFromGroup * distance);
                    
                    if (StyxWoW.Me.Location.DistanceSqr(tankPosition) > 3*3)
                    {
                        Navigator.MoveTo(tankPosition);
                        return RunStatus.Running;
                    }
                    
                    return RunStatus.Failure;
                })
            );
        }

        private static WoWPoint GetGroupCenter()
        {
            var members = StyxWoW.Me.GroupInfo.RaidMembers
                .Select(m => m.ToPlayer())
                .Where(p => p != null && p.IsAlive && !p.IsMe)
                .ToList();

            if (members.Count == 0)
                return StyxWoW.Me.Location;

            float x = members.Average(p => p.Location.X);
            float y = members.Average(p => p.Location.Y);
            float z = members.Average(p => p.Location.Z);

            return new WoWPoint(x, y, z);
        }

        /// <summary>
        /// Fait bouger le tank vers une position
        /// </summary>
        public static RunStatus MoveTankTo(WoWPoint location)
        {
            if (!StyxWoW.Me.IsTank())
                return RunStatus.Failure;

            if (StyxWoW.Me.Location.DistanceSqr(location) < 5*5)
                return RunStatus.Success;

            Navigator.MoveTo(location);
            return RunStatus.Running;
        }

        // ═══════════════════════════════════════════════════════════
        // NPC INTERACTION
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Crée un behavior pour parler à un NPC
        /// </summary>
        public static Composite CreateTalkToNpc(uint npcEntryId)
        {
            WoWUnit npc = null;

            return new Decorator(
                ctx =>
                {
                    npc = ObjectManager.GetObjectsOfType<WoWUnit>()
                        .FirstOrDefault(u => u.Entry == npcEntryId && u.CanGossip);
                    return npc != null && StyxWoW.Me.IsTank();
                },
                new Sequence(
                    new Decorator(
                        ctx => npc.DistanceSqr > 5*5,
                        new Action(ctx => Navigator.MoveTo(npc.Location))
                    ),
                    new Decorator(
                        ctx => npc.DistanceSqr <= 5*5,
                        new Sequence(
                            new Action(ctx => npc.Interact()),
                            new WaitContinue(2, ctx => GossipFrame.Instance.IsVisible, 
                                new Action(ctx => GossipFrame.Instance.SelectGossipOption(0)))
                        )
                    )
                )
            );
        }

        /// <summary>
        /// Escort NPC d'un point A vers un point B
        /// </summary>
        public static Composite CreateTankTalkToThenEscortNpc(
            uint npcEntryId,
            WoWPoint startLocation,
            WoWPoint endLocation)
        {
            WoWUnit npc = null;

            return new PrioritySelector(
                ctx =>
                {
                    npc = ObjectManager.GetObjectsOfType<WoWUnit>()
                        .FirstOrDefault(u => u.Entry == npcEntryId);
                    return ctx;
                },
                // Talk to NPC to start escort
                new Decorator(
                    ctx => npc != null && npc.Location.DistanceSqr(startLocation) < 10*10 && npc.CanGossip,
                    CreateTalkToNpc(npcEntryId)
                ),
                // Follow NPC during escort
                new Decorator(
                    ctx => npc != null && !npc.CanGossip && StyxWoW.Me.IsTank(),
                    new Action(ctx =>
                    {
                        if (npc.Location.DistanceSqr(endLocation) < 10*10)
                            return RunStatus.Success;

                        // Stay ahead of NPC (Rotation is a float in radians, not a WoWPoint)
                        var facing = npc.Rotation;
                        var aheadPoint = new WoWPoint(
                            npc.Location.X + (float)Math.Cos(facing) * 5f,
                            npc.Location.Y + (float)Math.Sin(facing) * 5f,
                            npc.Location.Z);
                        if (StyxWoW.Me.Location.DistanceSqr(aheadPoint) > 3*3)
                            Navigator.MoveTo(aheadPoint);

                        return RunStatus.Running;
                    })
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // PARTY QUERY (HB 4.3.4 ScriptHelpers)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Tous les membres du groupe/raid, moi inclus.
        /// Utilisé par Violet Hold x2.
        /// </summary>
        public static IEnumerable<WoWPlayer> PartyIncludingMe =>
            StyxWoW.Me.GroupInfo.RaidMembers.Select(m => m.ToPlayer()).Where(p => p != null);

        /// <summary>
        /// Vérifie si un boss par son ID est encore en vie.
        /// Référence HB 4.3.4 ScriptHelpers.cs:464
        /// </summary>
        public static bool IsBossAlive(uint id) =>
            BossManager.Bosses.FirstOrDefault(b => b.Entry == id)?.IsAlive ?? false;

        /// <summary>
        /// Vérifie si un boss nommé est encore en vie.
        /// Utilisé par Violet Hold, CoS, Gundrak x10+.
        /// </summary>
        public static bool IsBossAlive(string name) =>
            ObjectManager.GetObjectsOfType<WoWUnit>()
                .Any(u => u.Name == name && u.IsAlive && !u.IsFriendly);

        // ═══════════════════════════════════════════════════════════
        // OBJECT INTERACTION
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Interact avec un GameObject
        /// </summary>
        public static Composite CreateInteractWithObject(Func<WoWGameObject> objectSelector)
        {
            return new Decorator(
                ctx => objectSelector() != null && objectSelector().CanUse(),
                new Sequence(
                    new Decorator(
                        ctx => objectSelector().DistanceSqr > 5*5,
                        new Action(ctx => Navigator.MoveTo(objectSelector().Location))
                    ),
                    new Decorator(
                        ctx => objectSelector().DistanceSqr <= 5*5,
                        new Action(ctx => objectSelector().Interact())
                    )
                )
            );
        }

        /// <summary>
        /// Overload avec range + ignoreCombat — UP, Nexus, Violet Hold, HoS.
        /// </summary>
        public static Composite CreateInteractWithObject(
            Func<WoWGameObject> objectSelector,
            float range,
            bool ignoreCombat) =>
            new Decorator(
                ctx => (ignoreCombat || !StyxWoW.Me.Combat) &&
                       objectSelector() != null &&
                       objectSelector().CanUse(),
                new Sequence(
                    new Decorator(
                        ctx => objectSelector().DistanceSqr > range * range,
                        new Action(ctx => Navigator.MoveTo(objectSelector().Location))),
                    new Decorator(
                        ctx => objectSelector().DistanceSqr <= range * range,
                        new Action(ctx => objectSelector().Interact()))
                )
            );

        // ═══════════════════════════════════════════════════════════
        // UTILITY QUERIES
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Trouve les NPCs non-friendly près d'une location
        /// </summary>
        public static List<WoWUnit> GetUnfriendlyNpsAtLocation(Func<WoWPoint> locationSelector, float radius)
        {
            return GetUnfriendlyNpsAtLocation(locationSelector, radius, null);
        }

        public static List<WoWUnit> GetUnfriendlyNpsAtLocation(
            Func<WoWPoint> locationSelector,
            float radius,
            Func<WoWUnit, bool> filter)
        {
            var location = locationSelector();
            float radiusSqr = radius * radius;

            return ObjectManager.GetObjectsOfType<WoWUnit>()
                .Where(u => u.IsAlive &&
                           !u.IsFriendly &&
                           u.Location.DistanceSqr(location) < radiusSqr &&
                           (filter == null || filter(u))).ToList();
        }

        public static Composite CreatePullNpcToLocation(CanRunDecoratorDelegate canRun, Func<WoWUnit> unitSelector, Func<WoWPoint> pullToLocationSelector, int waitTimeAtPullLocationTimeout) => new ActionAlwaysSucceed();
        public static Composite CreatePullNpcToLocation(CanRunDecoratorDelegate canRun, CanRunDecoratorDelegate readyToPull, Func<WoWUnit> unitSelector, Func<WoWPoint> pullToLocationSelector, int waitTimeAtPullLocationTimeout) => new ActionAlwaysSucceed();
        public static Composite CreatePullNpcToLocation(CanRunDecoratorDelegate canRun, CanRunDecoratorDelegate readyToPull, Func<WoWUnit> unitSelector, Func<WoWPoint> pullToLocationSelector, Func<WoWPoint> waitAtLocationSellector) => new ActionAlwaysSucceed();
        public static Composite CreatePullNpcToLocation(CanRunDecoratorDelegate canRun, CanRunDecoratorDelegate readyToPull, Func<WoWUnit> unitSelector, Func<WoWPoint> pullToLocationSelector, Func<WoWPoint> waitAtLocationSellector, int waitTimeAtPullLocationTimeout) => new ActionAlwaysSucceed();

        /// <summary>
        /// Crée un behavior pour pull trash vers une position
        /// </summary>
        public static Composite CreatePullNpcToLocation(
            CanRunDecoratorDelegate condition,
            Func<WoWUnit> npcSelector,
            Func<WoWPoint> tankLocation,
            float pullRange)
        {
            return new Decorator(
                ctx => condition(ctx) && StyxWoW.Me.IsTank(),
                new PrioritySelector(
                    // Move to tank spot
                    new Decorator(
                        ctx => StyxWoW.Me.Location.DistanceSqr(tankLocation()) > 3*3,
                        new Action(ctx => Navigator.MoveTo(tankLocation()))
                    ),
                    // Pull if at tank spot and NPC in range
                    new Decorator(
                        ctx => npcSelector() != null && npcSelector().DistanceSqr < pullRange*pullRange,
                        new Action(ctx =>
                        {
                            var npc = npcSelector();
                            npc.Target();
                            // Use ranged ability or move towards
                            return RunStatus.Success;
                        })
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // DISPEL / PURGE
        // ═══════════════════════════════════════════════════════════

        public enum EnemyDispellType
        {
            Magic,
            Enrage
        }

        /// <summary>
        /// Dispel un buff enemy
        /// </summary>
        public static Composite CreateDispellEnemy(
            string auraName,
            EnemyDispellType dispellType,
            Func<WoWUnit> targetSelector)
        {
            return new Decorator(
                ctx =>
                {
                    var target = targetSelector();
                    return target != null && target.HasAura(auraName);
                },
                new Action(ctx =>
                {
                    var target = targetSelector();
                    
                    // Utiliser le spell de dispel approprié selon classe
                    // Mage: Spellsteal (Magic)
                    // Shaman: Purge (Magic)
                    // Hunter: Tranquilizing Shot (Enrage)
                    // Rogue: Shiv avec poison (Enrage)
                    // Warrior: Shield Slam (si talent) ou rage
                    
                    // Pour l'instant, on laisse le CustomClass gérer
                    return RunStatus.Failure;
                })
            );
        }

        // ═══════════════════════════════════════════════════════════
        // MÉTHODES MANQUANTES — REQUISES PAR LES SCRIPTS DE DONJON
        // Référence: HB 4.3.4 ScriptHelpers.cs
        // Les 32 scripts WotLK appellent ces méthodes.
        // Sans elles, les scripts ne compileront pas.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Déplace le tank vers une position spécifique.
        /// Utilisé par les scripts: HoL, HoL Heroic, etc.
        /// Référence HB 4.3.4 ScriptHelpers.cs L2088
        /// </summary>
        public static Composite CreateTankUnitAtLocation(Func<WoWPoint> locationSelector, float precision)
        {
            return new Decorator(
                ctx => StyxWoW.Me.IsTank(),
                new PrioritySelector(
                    new Decorator(
                        ctx => StyxWoW.Me.Location.DistanceSqr(locationSelector()) > precision * precision,
                        new Action(ctx =>
                        {
                            Navigator.MoveTo(locationSelector());
                            return RunStatus.Running;
                        })
                    ),
                    new ActionAlwaysSucceed()
                )
            );
        }

        /// <summary>
        /// Tue les PNJ hostiles dans un rayon autour d'une position.
        /// Référence HB 4.3.4 ScriptHelpers.cs L395
        /// </summary>
        public static Composite CreateClearArea(Func<WoWPoint> centerLocationSelector, float radius, Func<WoWUnit, bool> unitSelector)
        {
            WoWUnit target = null;
            float radiusSqr = radius * radius;

            return new Decorator(
                ctx =>
                {
                    target = ObjectManager.GetObjectsOfType<WoWUnit>()
                        .Where(u => u.IsAlive && !u.IsFriendly &&
                                   u.Location.DistanceSqr(centerLocationSelector()) < radiusSqr &&
                                   unitSelector(u))
                        .OrderBy(u => u.DistanceSqr)
                        .FirstOrDefault();
                    return target != null;
                },
                new PrioritySelector(
                    new Decorator(
                        ctx => target.DistanceSqr > 5*5,
                        new Action(ctx =>
                        {
                            Navigator.MoveTo(target.Location);
                            return RunStatus.Running;
                        })
                    ),
                    new Decorator(
                        ctx => target.DistanceSqr <= 5*5,
                        new Action(ctx =>
                        {
                            target.Target();
                            return RunStatus.Success;
                        })
                    )
                )
            );
        }

        /// <summary>
        /// Continue à se déplacer vers un objectif (objet ou position).
        /// Multiples overloads pour compatibilité avec les scripts.
        /// Référence HB 4.3.4 ScriptHelpers.cs L2430-L2490
        /// </summary>
        public static Composite CreateMoveToContinue(uint objectId)
        {
            return CreateMoveToContinue(
                ctx => true,
                () => ObjectManager.GetObjectsOfType<WoWObject>()
                    .FirstOrDefault(o => o.Entry == objectId),
                false);
        }

        public static Composite CreateMoveToContinue(CanRunDecoratorDelegate canRun, uint objectId, bool ignoreCombat)
        {
            return CreateMoveToContinue(canRun, () => ObjectManager.GetObjectsOfType<WoWObject>().FirstOrDefault(o => o.Entry == objectId), ignoreCombat);
        }

        public static Composite CreateMoveToContinue(Func<WoWObject> objectSelector)
        {
            return CreateMoveToContinue(ctx => true, objectSelector, false);
        }

        public static Composite CreateMoveToContinue(
            CanRunDecoratorDelegate canRun,
            Func<WoWObject> objectSelector,
            bool ignoreCombat)
        {
            return new Decorator(
                ctx => canRun(ctx) && (ignoreCombat || !StyxWoW.Me.Combat),
                new Action(ctx =>
                {
                    var obj = objectSelector();
                    if (obj == null)
                        return RunStatus.Failure;
                    
                    if (obj.DistanceSqr < 5*5)
                        return RunStatus.Success;
                    
                    Navigator.MoveTo(obj.Location);
                    return RunStatus.Running;
                })
            );
        }

        public static Composite CreateMoveToContinue(WoWPoint location) =>
            CreateMoveToContinue(ctx => true, () => location, true);

        public static Composite CreateMoveToContinue(Func<WoWPoint> locationSelector)
        {
            return CreateMoveToContinue(ctx => true, locationSelector, false);
        }

        public static Composite CreateMoveToContinue(
            CanRunDecoratorDelegate canRun,
            Func<WoWPoint> locationSelector,
            bool ignoreCombat)
        {
            return new Decorator(
                ctx => canRun(ctx) && (ignoreCombat || !StyxWoW.Me.Combat),
                new Action(ctx =>
                {
                    var location = locationSelector();
                    if (location == WoWPoint.Empty)
                        return RunStatus.Failure;
                    
                    if (StyxWoW.Me.Location.DistanceSqr(location) < 5*5)
                        return RunStatus.Success;
                    
                    Navigator.MoveTo(location);
                    return RunStatus.Running;
                })
            );
        }
    }
}
