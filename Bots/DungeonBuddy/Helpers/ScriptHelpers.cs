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

        // ═══════════════════════════════════════════════════════════
        // UTILITY QUERIES
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Trouve les NPCs non-friendly près d'une location
        /// </summary>
        public static IEnumerable<WoWUnit> GetUnfriendlyNpsAtLocation(
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
                           filter(u));
        }

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
