# ⚔️ PLAN DE DÉVELOPPEMENT - DungeonBuddy pour WotLK 3.3.5a

## 📋 RÉSUMÉ

**Objectif:** Créer un bot BotBase pour les donjons automatiques via le Dungeon Finder (LFG).

**Complexité:** 🔴 ÉLEVÉE - Système complet avec scripts par donjon  
**Temps estimé:** 2-3 mois  
**Scripts disponibles:** 32 scripts WotLK dans `C:\Users\Texy\Desktop\.test\Dungeon Scripts\Wrath of the Lich King\`

---

## ✅ COMPATIBILITÉ WOTLK 3.3.5a

### API Lua LFG — Vérifiées pour WotLK 3.3.5a:

| API Lua | Statut | Notes |
|---------|--------|-------|
| **GetLFGMode()** | ✅ EXISTE | Retourne "queued","proposal","lfgparty","abandoned","suspended","rolecheck" ou nil. **SANS argument** en WotLK (category ajouté en MoP) |
| **SetLFGDungeon(1, id)** | ✅ EXISTE | category 1 = LFD |
| **JoinLFG(1)** | ✅ EXISTE | Lance la queue |
| **ClearAllLFGDungeons(1)** | ✅ EXISTE | Reset sélections |
| **AcceptProposal()** | ✅ EXISTE | ⚠️ RESTRICTED (hw event) — fonctionne via injection EndScene |
| **LFGTeleport(bool)** | ✅ EXISTE | false=in, true=out |
| **SetLFGRoles(leader,tank,healer,dps)** | ✅ EXISTE | 4 booleans |
| **LeaveLFG(1)** | ✅ EXISTE | Quitte la queue |
| **LFDRoleCheckPopupAcceptButton** | ✅ EXISTE | Frame UI LFD |
| **SetLFGBootVote(bool)** | ✅ EXISTE | Vote kick |
| **UnitGroupRolesAssigned('unit')** | ✅ EXISTE | "TANK","HEALER","DAMAGER" |
| **GetDungeonInfo(i)** | ✅ EXISTE | Liste donjons disponibles |
| **GetNumDungeons()** | ✅ EXISTE | Nombre de donjons |
| **GetInstanceDifficulty()** | ✅ EXISTE | 1=Normal, 2=Heroic |

### Events LFG — Vérifiés pour WotLK 3.3.5a:

| Event | Statut | Notes |
|-------|--------|-------|
| **LFG_PROPOSAL_SHOW** | ✅ EXISTE | Popup proposal affiché |
| **LFG_PROPOSAL_SUCCEEDED** | ✅ EXISTE | Tous accepté |
| **LFG_PROPOSAL_FAILED** | ✅ EXISTE | Quelqu'un a refusé |
| **LFG_COMPLETION_REWARD** | ✅ EXISTE | Donjon terminé |
| **LFG_OFFER_CONTINUE** | ✅ EXISTE | Requeue disponible |
| **LFG_ROLE_CHECK_SHOW** | ✅ EXISTE | Role check window |
| **LFG_BOOT_PROPOSAL_UPDATE** | ✅ EXISTE | Vote kick en cours |

### Ce qui EXISTE dans WotLK 3.3:

| Fonctionnalité | Statut | Notes |
|----------------|--------|-------|
| **Dungeon Finder (LFG)** | ✅ EXISTE | Patch 3.3 "Fall of the Lich King" |
| **Random Dungeon Queue** | ✅ EXISTE | Daily Frost Emblems |
| **Random Heroic Queue** | ✅ EXISTE | Daily Frost Emblems |
| **Specific Dungeon Queue** | ✅ EXISTE | |
| **Role Selection** | ✅ EXISTE | Tank/Healer/DPS |
| **LFGTeleport()** | ✅ EXISTE | Lua API téléport donjon |
| **Party Role Detection** | ✅ EXISTE | Via UnitGroupRolesAssigned() |

### Ce qui N'EXISTE PAS dans WotLK:

| Fonctionnalité | Ajouté dans |
|----------------|-------------|
| ❌ LFR (Looking for Raid) | Cataclysm 4.3 |
| ❌ Challenge Mode | MoP |
| ❌ Scenarios | MoP |
| ❌ Proving Grounds | WoD |
| ❌ Mythic+ | Legion |

---

## 📁 SCRIPTS DE DONJONS DISPONIBLES

**Emplacement:** `C:\Users\Texy\Desktop\.test\Dungeon Scripts\Wrath of the Lich King\`

### Donjons Normaux + Héroïques (32 scripts):

| Donjon | DungeonId | Normal | Héroïque |
|--------|-----------|--------|----------|
| Utgarde Keep | 202 | ✅ | ✅ |
| The Nexus | 225 | ✅ | ✅ |
| Azjol-Nerub | 204 | ✅ | ✅ |
| Ahn'kahet - The Old Kingdom | 218 | ✅ | ✅ |
| Drak'Tharon Keep | 214 | ✅ | ✅ |
| Violet Hold | 220 | ✅ | ✅ |
| Gundrak | 216 | ✅ | ✅ |
| Halls of Stone | 208 | ✅ | ✅ |
| Halls of Lightning | 207/212 | ✅ | ✅ |
| The Oculus | 206/211 | ✅ | ✅ |
| The Culling of Stratholme | 209/210 | ✅ | ✅ |
| Utgarde Pinnacle | 203/205 | ✅ | ✅ |
| Trial of the Champion | 245/249 | ✅ | ✅ |
| The Forge of Souls | 251 | ✅ | ✅ |
| Pit of Saron | 253 | ✅ | ✅ |
| Halls of Reflection | 255 | ✅ | ✅ |

**Total: 16 donjons × 2 (Normal/Heroic) = 32 scripts**

---

## 📁 STRUCTURE DE FICHIERS À CRÉER

```
CopilotBuddy/
└── Bots/
    └── DungeonBuddy/
        ├── DungeonBuddy.cs                 # BotBase principal
        ├── DungeonBuddySettings.cs         # Settings
        ├── Dungeon.cs                      # Classe de base des scripts
        ├── DungeonManager.cs               # Chargement/compilation scripts
        ├── BossManager.cs                  # Gestion des boss actifs
        ├── LfgManager.cs                   # Interface avec Dungeon Finder
        │
        ├── Attributes/
        │   ├── EncounterHandlerAttribute.cs    # [EncounterHandler] pour boss
        │   ├── ObjectHandlerAttribute.cs       # [ObjectHandler] pour objets
        │   └── CallBehaviorMode.cs             # Mode d'appel (Proximity, Combat)
        │
        ├── Avoidance/
        │   ├── AvoidanceManager.cs         # Gestionnaire zones à éviter
        │   ├── AvoidInfo.cs                # Info zone à éviter
        │   ├── Avoid.cs                    # Classe de base avoid
        │   ├── AvoidObject.cs              # Avoid basé sur objet
        │   └── AvoidLocation.cs            # Avoid basé sur position
        │
        ├── Enums/
        │   ├── DungeonMode.cs              # LookingForGroup, Farm
        │   ├── LfgState.cs                 # None, InQueue, Proposal, InDungeon, AbandonedInDungeon
        │   ├── QueueType.cs                # RandomDungeon, RandomHeroic, Specific
        │   ├── LootMode.cs                 # BossesOnly, Always
        │   └── PartyRole.cs                # Tank, Healer, Dps
        │
        ├── Helpers/
        │   └── ScriptHelpers.cs            # Helpers pour scripts (RunAway, TankFace, etc.)
        │
        └── Dungeon Scripts/
            └── Wrath of the Lich King/     # COPIER depuis .test
                ├── Utgarde Keep.cs
                ├── Utgarde Keep Heroic.cs
                ├── The Nexus.cs
                ├── The Nexus Heroic.cs
                └── ... (32 fichiers)
```

**Total: ~50 fichiers à créer**

---

## 📝 SPÉCIFICATIONS DÉTAILLÉES

### 1. Bots/DungeonBuddy/Enums/ (5 fichiers)

#### DungeonMode.cs
```csharp
namespace Bots.DungeonBuddy.Enums
{
    /// <summary>
    /// Mode de fonctionnement du bot
    /// </summary>
    public enum DungeonMode
    {
        /// <summary>
        /// Utilise le Dungeon Finder (LFG)
        /// </summary>
        LookingForGroup,
        
        /// <summary>
        /// Farm solo (pas de queue, entrer manuellement)
        /// </summary>
        Farm
    }
}
```

#### LfgState.cs
```csharp
namespace Bots.DungeonBuddy.Enums
{
    /// <summary>
    /// État actuel du Dungeon Finder
    /// </summary>
    public enum LfgState
    {
        None,
        NotInLfg,
        InQueue,
        Proposal,               // Popup accepter/refuser
        InDungeon,
        Suspended,              // Queue pausée (teleport out)
        RoleCheck,              // Sélection de rôle
        AbandonedInDungeon      // Groupe quitté mais toujours dans l'instance
    }
}
```

#### QueueType.cs
```csharp
namespace Bots.DungeonBuddy.Enums
{
    /// <summary>
    /// Type de queue LFG
    /// </summary>
    public enum QueueType
    {
        /// <summary>
        /// Donjon spécifique choisi
        /// </summary>
        Specific,
        
        /// <summary>
        /// Donjon aléatoire normal (bonus emblem quotidien)
        /// </summary>
        RandomDungeon,
        
        /// <summary>
        /// Donjon héroïque aléatoire (bonus Frost Emblems)
        /// </summary>
        RandomHeroic,
        
        /// <summary>
        /// Mode solo farm (pas de LFG)
        /// </summary>
        SoloFarm
    }
}
```

#### LootMode.cs
```csharp
namespace Bots.DungeonBuddy.Enums
{
    public enum LootMode
    {
        /// <summary>
        /// Loot uniquement les boss
        /// </summary>
        BossesOnly,
        
        /// <summary>
        /// Loot tous les mobs
        /// </summary>
        Always,
        
        /// <summary>
        /// Ne jamais loot
        /// </summary>
        Never
    }
}
```

#### PartyRole.cs
```csharp
namespace Bots.DungeonBuddy.Enums
{
    [Flags]
    public enum PartyRole
    {
        None = 0,
        Tank = 1,
        Healer = 2,
        Dps = 4
    }
}
```

---

### 2. Bots/DungeonBuddy/Attributes/ (3 fichiers)

#### CallBehaviorMode.cs
```csharp
namespace Bots.DungeonBuddy.Attributes
{
    public enum CallBehaviorMode
    {
        /// <summary>
        /// Behavior appelé quand le boss est ciblé en combat
        /// </summary>
        Combat,
        
        /// <summary>
        /// Behavior appelé quand le joueur est à proximité du boss
        /// </summary>
        Proximity,
        
        /// <summary>
        /// Behavior appelé quand c'est le boss actuel à tuer
        /// </summary>
        CurrentBoss
    }
}
```

#### EncounterHandlerAttribute.cs
```csharp
using System;

namespace Bots.DungeonBuddy.Attributes
{
    /// <summary>
    /// Marque une méthode comme handler pour un boss spécifique.
    /// La méthode doit retourner un Composite (behavior tree).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public sealed class EncounterHandlerAttribute : Attribute
    {
        public EncounterHandlerAttribute(int bossEntryId)
            : this(bossEntryId, "")
        {
        }

        public EncounterHandlerAttribute(int bossEntryId, string bossDisplayName)
        {
            BossEntry = bossEntryId;
            BossName = bossDisplayName;
            BossRange = 75;
            Mode = CallBehaviorMode.Combat;
        }

        /// <summary>
        /// Entry ID du boss (from creature_template)
        /// </summary>
        public int BossEntry { get; set; }
        
        /// <summary>
        /// Nom d'affichage du boss
        /// </summary>
        public string BossName { get; set; }
        
        /// <summary>
        /// Distance de détection du boss (yards)
        /// </summary>
        public int BossRange { get; set; }
        
        public int BossRangeSqr => BossRange * BossRange;
        
        /// <summary>
        /// Mode d'appel du behavior
        /// </summary>
        public CallBehaviorMode Mode { get; set; }
    }
}
```

#### ObjectHandlerAttribute.cs
```csharp
using System;

namespace Bots.DungeonBuddy.Attributes
{
    /// <summary>
    /// Marque une méthode comme handler pour un GameObject spécifique.
    /// Utilisé pour interagir avec des objets du donjon (leviers, portes, etc.)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public sealed class ObjectHandlerAttribute : Attribute
    {
        public ObjectHandlerAttribute(int objectEntryId)
            : this(objectEntryId, "", 40)
        {
        }

        public ObjectHandlerAttribute(int objectEntryId, string objectDisplayName)
            : this(objectEntryId, objectDisplayName, 40)
        {
        }

        public ObjectHandlerAttribute(int objectEntryId, string objectDisplayName, int range)
        {
            ObjectEntry = objectEntryId;
            ObjectName = objectDisplayName;
            ObjectRange = range;
        }

        public int ObjectEntry { get; set; }
        public string ObjectName { get; set; }
        public int ObjectRange { get; set; }
        public int ObjectRangeSqr => ObjectRange * ObjectRange;
    }
}
```

---

### 3. Bots/DungeonBuddy/Avoidance/ (5 fichiers)

#### AvoidInfo.cs
```csharp
using System;
using Styx.Logic.Pathing;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Bots.DungeonBuddy.Avoidance
{
    /// <summary>
    /// Définit une zone à éviter (ability de boss, feu au sol, etc.)
    /// </summary>
    public class AvoidInfo
    {
        /// <summary>
        /// Créer un avoid basé sur un objet
        /// </summary>
        /// <param name="condition">Condition pour activer l'avoid</param>
        /// <param name="objectSelector">Sélecteur d'objets à éviter</param>
        /// <param name="radiusSelector">Sélecteur de rayon d'évitement</param>
        public AvoidInfo(
            CanRunDecoratorDelegate condition,
            Predicate<WoWObject> objectSelector,
            Func<float> radiusSelector)
            : this(condition, objectSelector, radiusSelector, null, 40f, true)
        {
        }

        public AvoidInfo(
            CanRunDecoratorDelegate condition,
            Predicate<WoWObject> objectSelector,
            Func<float> radiusSelector,
            bool isBlocking)
            : this(condition, objectSelector, radiusSelector, null, 40f, isBlocking)
        {
        }

        public AvoidInfo(
            CanRunDecoratorDelegate condition,
            Predicate<WoWObject> objectSelector,
            Func<float> radiusSelector,
            Func<WoWPoint> leashPointSelector,
            float leashRadius,
            bool isBlocking)
        {
            Condition = condition;
            ObjectSelector = objectSelector;
            LocationSelector = null;
            RadiusSelector = radiusSelector;
            LeashPointSelector = leashPointSelector;
            LeashRadius = leashRadius;
            IsBlocking = isBlocking;
        }

        /// <summary>
        /// Créer un avoid basé sur une position fixe
        /// </summary>
        public AvoidInfo(
            CanRunDecoratorDelegate condition,
            Func<WoWPoint> locationSelector,
            Func<float> radiusSelector)
            : this(condition, locationSelector, radiusSelector, null, 40f, true)
        {
        }

        public AvoidInfo(
            CanRunDecoratorDelegate condition,
            Func<WoWPoint> locationSelector,
            Func<float> radiusSelector,
            Func<WoWPoint> leashPointSelector,
            float leashRadius,
            bool isBlocking)
        {
            Condition = condition;
            ObjectSelector = null;
            LocationSelector = locationSelector;
            RadiusSelector = radiusSelector;
            LeashPointSelector = leashPointSelector;
            LeashRadius = leashRadius;
            IsBlocking = isBlocking;
        }

        /// <summary>
        /// Condition pour activer l'avoid
        /// </summary>
        public CanRunDecoratorDelegate Condition { get; private set; }
        
        /// <summary>
        /// Sélecteur d'objets (pour avoid dynamique)
        /// </summary>
        public Predicate<WoWObject> ObjectSelector { get; private set; }
        
        /// <summary>
        /// Sélecteur de position (pour avoid statique)
        /// </summary>
        public Func<WoWPoint> LocationSelector { get; private set; }
        
        /// <summary>
        /// Sélecteur de rayon
        /// </summary>
        public Func<float> RadiusSelector { get; private set; }
        
        /// <summary>
        /// Point d'ancrage (ne pas fuir plus loin que LeashRadius de ce point)
        /// </summary>
        public Func<WoWPoint> LeashPointSelector { get; private set; }
        
        /// <summary>
        /// Rayon maximum de fuite depuis le point d'ancrage
        /// </summary>
        public float LeashRadius { get; private set; }
        
        /// <summary>
        /// Si true, bloque la navigation à travers cette zone
        /// </summary>
        public bool IsBlocking { get; private set; }

        public bool CanRun(object ctx)
        {
            try
            {
                return Condition == null || Condition(ctx);
            }
            catch
            {
                return false;
            }
        }
    }
}
```

#### Avoid.cs
```csharp
using Styx.Logic.Pathing;

namespace Bots.DungeonBuddy.Avoidance
{
    /// <summary>
    /// Classe de base pour une zone d'évitement active
    /// </summary>
    public abstract class Avoid
    {
        protected Avoid(AvoidInfo info)
        {
            Info = info;
        }

        public AvoidInfo Info { get; }
        
        public abstract WoWPoint Location { get; }
        
        public float Radius => Info.RadiusSelector();
        
        public float RadiusSqr => Radius * Radius;
        
        public abstract bool IsValid { get; }
        
        /// <summary>
        /// Met à jour l'état de l'avoid
        /// </summary>
        public abstract void Update();
    }
}
```

#### AvoidObject.cs
```csharp
using Styx.Logic.Pathing;
using Styx.WoWInternals.WoWObjects;

namespace Bots.DungeonBuddy.Avoidance
{
    /// <summary>
    /// Zone d'évitement attachée à un WoWObject
    /// </summary>
    public class AvoidObject : Avoid
    {
        private readonly WoWObject _object;

        public AvoidObject(AvoidInfo info, WoWObject obj) : base(info)
        {
            _object = obj;
        }

        public override WoWPoint Location => _object?.Location ?? WoWPoint.Empty;
        
        public override bool IsValid => 
            _object != null && 
            _object.IsValid && 
            Info.CanRun(_object);

        public override void Update()
        {
            // La position est mise à jour automatiquement via _object.Location
        }
    }
}
```

#### AvoidLocation.cs
```csharp
using Styx.Logic.Pathing;

namespace Bots.DungeonBuddy.Avoidance
{
    /// <summary>
    /// Zone d'évitement à position fixe
    /// </summary>
    public class AvoidLocation : Avoid
    {
        private WoWPoint _location;

        public AvoidLocation(AvoidInfo info) : base(info)
        {
            _location = info.LocationSelector?.Invoke() ?? WoWPoint.Empty;
        }

        public override WoWPoint Location => _location;
        
        public override bool IsValid => 
            _location != WoWPoint.Empty && 
            Info.CanRun(null);

        public override void Update()
        {
            if (Info.LocationSelector != null)
                _location = Info.LocationSelector();
        }
    }
}
```

#### AvoidanceManager.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Styx;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.DungeonBuddy.Avoidance
{
    /// <summary>
    /// Gestionnaire global des zones à éviter
    /// </summary>
    public static class AvoidanceManager
    {
        public static readonly List<AvoidInfo> AvoidInfos = new();
        public static readonly List<Avoid> Avoids = new();
        
        public static void Add(AvoidInfo avoid)
        {
            AvoidInfos.Add(avoid);
        }

        public static void AddRange(IEnumerable<AvoidInfo> avoids)
        {
            AvoidInfos.AddRange(avoids);
        }

        public static void Remove(AvoidInfo avoid)
        {
            AvoidInfos.Remove(avoid);
        }

        public static void RemoveAll(Predicate<AvoidInfo> match)
        {
            AvoidInfos.RemoveAll(match);
        }

        public static void Clear()
        {
            AvoidInfos.Clear();
            Avoids.Clear();
        }

        /// <summary>
        /// Met à jour les zones d'évitement actives.
        /// Appelé à chaque pulse du bot.
        /// </summary>
        public static void Update()
        {
            // Supprimer les avoids invalides
            Avoids.RemoveAll(a => !a.IsValid);

            // Parcourir les objets du monde pour créer de nouveaux avoids
            foreach (var obj in ObjectManager.ObjectList)
            {
                foreach (var avoidInfo in AvoidInfos.Where(ai => ai.ObjectSelector != null))
                {
                    if (avoidInfo.ObjectSelector(obj) && avoidInfo.CanRun(obj))
                    {
                        // Vérifier si cet objet n'a pas déjà un avoid
                        if (!Avoids.Any(a => a is AvoidObject ao && ao.Info == avoidInfo))
                        {
                            Avoids.Add(new AvoidObject(avoidInfo, obj));
                        }
                    }
                }
            }

            // Ajouter les avoids de position
            foreach (var avoidInfo in AvoidInfos.Where(ai => ai.LocationSelector != null))
            {
                if (avoidInfo.CanRun(null))
                {
                    if (!Avoids.Any(a => a is AvoidLocation al && al.Info == avoidInfo))
                    {
                        Avoids.Add(new AvoidLocation(avoidInfo));
                    }
                }
            }

            // Mettre à jour tous les avoids
            foreach (var avoid in Avoids)
            {
                avoid.Update();
            }
        }

        /// <summary>
        /// Vérifie si une position est dans une zone d'évitement
        /// </summary>
        public static bool IsInAvoidance(WoWPoint location)
        {
            return Avoids.Any(a => a.Location.DistanceSqr(location) < a.RadiusSqr);
        }

        /// <summary>
        /// Trouve un point sûr pour fuir
        /// </summary>
        public static WoWPoint GetSafePoint(WoWPoint from, float minDistance = 10f)
        {
            // Trouver la direction opposée à la menace la plus proche
            var nearestAvoid = Avoids
                .Where(a => a.Location.DistanceSqr(from) < (a.Radius + 20) * (a.Radius + 20))
                .OrderBy(a => a.Location.DistanceSqr(from))
                .FirstOrDefault();

            if (nearestAvoid == null)
                return from;

            // Direction opposée
            var directionAway = (from - nearestAvoid.Location);
            directionAway.Normalize();

            // Point de fuite
            var safePoint = from + (directionAway * (nearestAvoid.Radius + minDistance));

            // Vérifier que le point est navigable
            if (Navigator.CanNavigateFully(from, safePoint))
                return safePoint;

            // Essayer d'autres directions
            for (float angle = 45f; angle <= 315f; angle += 45f)
            {
                var rotated = RotatePoint(directionAway, angle);
                var testPoint = from + (rotated * (nearestAvoid.Radius + minDistance));
                
                if (Navigator.CanNavigateFully(from, testPoint) && !IsInAvoidance(testPoint))
                    return testPoint;
            }

            return from;
        }

        private static WoWPoint RotatePoint(WoWPoint direction, float angleDegrees)
        {
            float angleRadians = angleDegrees * (float)Math.PI / 180f;
            float cos = (float)Math.Cos(angleRadians);
            float sin = (float)Math.Sin(angleRadians);
            
            return new WoWPoint(
                direction.X * cos - direction.Y * sin,
                direction.X * sin + direction.Y * cos,
                direction.Z
            );
        }
    }
}
```

---

### 4. Bots/DungeonBuddy/Dungeon.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Bots.DungeonBuddy.Avoidance;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.WoWInternals.WoWObjects;

namespace Bots.DungeonBuddy.Profiles
{
    /// <summary>
    /// Classe de base pour tous les scripts de donjon.
    /// Chaque donjon hérite de cette classe.
    /// NOTE: Namespace = Bots.DungeonBuddy.Profiles pour compatibilité avec les 32 scripts
    /// existants qui font "using Bots.DungeonBuddy.Profiles;"
    /// </summary>
    public abstract class Dungeon : IDisposable
    {
        private readonly List<AvoidInfo> _avoidInfos = new();
        private bool _disposed;

        /// <summary>
        /// Nom du donjon (affiché depuis LFG_Dungeons.dbc)
        /// </summary>
        public virtual string Name => DungeonId > 0 ? $"Dungeon {DungeonId}" : "Unknown";

        /// <summary>
        /// ID du donjon dans LFG_Dungeons.dbc
        /// </summary>
        public abstract uint DungeonId { get; }

        /// <summary>
        /// Position de l'entrée du donjon (pour corpse run)
        /// </summary>
        public virtual WoWPoint Entrance => WoWPoint.Empty;

        /// <summary>
        /// Position de la sortie du donjon
        /// </summary>
        public virtual WoWPoint ExitLocation => WoWPoint.Empty;

        /// <summary>
        /// True si le corpse run peut se faire en volant
        /// </summary>
        public virtual bool IsFlyingCorpseRun => false;

        /// <summary>
        /// True si le donjon est terminé (tous les boss tués)
        /// </summary>
        public virtual bool IsComplete => BossManager.CurrentBoss == null;

        // ═══════════════════════════════════════════════════════════
        // TARGETING FILTERS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Filtre de ciblage: Ajouter des cibles
        /// </summary>
        public virtual void IncludeTargetsFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
        {
        }

        /// <summary>
        /// Filtre de ciblage: Retirer des cibles
        /// </summary>
        public virtual void RemoveTargetsFilter(List<WoWObject> units)
        {
        }

        /// <summary>
        /// Filtre de ciblage: Modifier les priorités
        /// </summary>
        public virtual void WeighTargetsFilter(List<Targeting.TargetPriority> units)
        {
        }

        // ═══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Appelé quand le joueur entre dans le donjon
        /// </summary>
        internal void Attach()
        {
            Targeting.Instance.IncludeTargetsFilter += IncludeTargetsFilter;
            Targeting.Instance.WeighTargetsFilter += WeighTargetsFilter;
            Targeting.Instance.RemoveTargetsFilter += RemoveTargetsFilter;
            
            // Ajouter les avoids
            AvoidanceManager.AddRange(_avoidInfos.Where(a => !AvoidanceManager.AvoidInfos.Contains(a)));
            
            OnEnter();
        }

        /// <summary>
        /// Appelé quand le joueur quitte le donjon
        /// </summary>
        internal void Detach()
        {
            Targeting.Instance.IncludeTargetsFilter -= IncludeTargetsFilter;
            Targeting.Instance.WeighTargetsFilter -= WeighTargetsFilter;
            Targeting.Instance.RemoveTargetsFilter -= RemoveTargetsFilter;
            
            // Retirer les avoids
            AvoidanceManager.RemoveAll(a => _avoidInfos.Contains(a));
            
            OnExit();
        }

        /// <summary>
        /// Override pour logique d'entrée custom
        /// </summary>
        public virtual void OnEnter()
        {
        }

        /// <summary>
        /// Override pour logique de sortie custom
        /// </summary>
        public virtual void OnExit()
        {
        }

        // ═══════════════════════════════════════════════════════════
        // AVOIDANCE HELPERS
        // ═══════════════════════════════════════════════════════════

        protected void AddAvoid(AvoidInfo avoidInfo)
        {
            AvoidanceManager.Add(avoidInfo);
            _avoidInfos.Add(avoidInfo);
        }

        protected void AddAvoidRange(IEnumerable<AvoidInfo> avoidInfos)
        {
            foreach (var a in avoidInfos)
                AddAvoid(a);
        }

        // ═══════════════════════════════════════════════════════════
        // IDISPOSABLE
        // ═══════════════════════════════════════════════════════════

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        ~Dungeon()
        {
            Dispose();
        }
    }
}
```

---

### 5. Bots/DungeonBuddy/Helpers/ScriptHelpers.cs

```csharp
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
                    var safePoint = AvoidanceManager.GetSafePoint(StyxWoW.Me.Location, radius);
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
```

---

### 6. Bots/DungeonBuddy/LfgManager.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Bots.DungeonBuddy.Enums;
using Styx;
using Styx.Helpers;
using Styx.WoWInternals;

namespace Bots.DungeonBuddy
{
    /// <summary>
    /// Gère l'interface avec le Dungeon Finder (LFG).
    /// Compatible WotLK 3.3 - Dungeon Finder ajouté en patch 3.3.
    /// </summary>
    public static class LfgManager
    {
        // ═══════════════════════════════════════════════════════════
        // LFG STATE
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// État actuel du LFG.
        /// Utilise GetLFGMode() — API canonique confirmée WotLK 3.3.
        /// NOTE: En WotLK 3.3, GetLFGMode() ne prend AUCUN argument
        ///       (le paramètre category a été ajouté en MoP 5.x).
        /// Retourne: "queued", "proposal", "lfgparty", "abandonedInDungeon",
        ///           "suspended", "rolecheck", ou nil
        /// C'est la même méthode utilisée par HB 4.3.4 DungeonBuddy.
        /// </summary>
        public static LfgState CurrentState
        {
            get
            {
                string mode = Lua.GetReturnVal<string>(
                    "return GetLFGMode() or 'none'", 0);
                
                return mode?.ToLowerInvariant() switch
                {
                    "queued"              => LfgState.InQueue,
                    "proposal"            => LfgState.Proposal,
                    "lfgparty"            => LfgState.InDungeon,
                    "abandonedindungeon"  => LfgState.AbandonedInDungeon,
                    "suspended"           => LfgState.Suspended,
                    "rolecheck"           => LfgState.RoleCheck,
                    _                     => LfgState.NotInLfg
                };
            }
        }

        /// <summary>
        /// Temps d'attente estimé (en secondes)
        /// </summary>
        public static int EstimatedWaitTime
        {
            get
            {
                // WotLK 3.3.5a: category 1 = LFD (pas de constante LE_LFG_CATEGORY_LFD)
                // NOTE: La 16e valeur retour (waitTime estimé) n'est pas documentée sur Wowpedia 
                // pour WotLK. La position exacte est déduite de HB 3.3.5a. À valider en jeu.
                return Lua.GetReturnVal<int>(
                    "local _,_,_,_,_,_,_,_,_,_,_,_,_,_,_,w=GetLFGQueueStats(1); return w or 0", 0);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // QUEUE ACTIONS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Queue pour un donjon aléatoire
        /// </summary>
        public static void QueueForRandomDungeon()
        {
            Logging.Write("[DungeonBuddy] Queuing for Random Dungeon...");
            
            // WotLK 3.3.5a: LFG_Dungeons.dbc IDs fixes:
            //   261 = Random Lich King Dungeon (normal)
            //   262 = Random Lich King Heroic Dungeon
            // NOTE: GetLFGDungeonInfo() n'existe PAS en WotLK. Utiliser les IDs fixes.
            // ClearAllLFGDungeons(1) — reset sélections précédentes (pattern HB)
            // SetLFGDungeon(category, dungeonID) — category 1 = LFD
            Lua.DoString(@"
                ClearAllLFGDungeons(1);
                SetLFGDungeon(1, 261);
                JoinLFG(1);
            ");
        }

        /// <summary>
        /// Queue pour un héroïque aléatoire
        /// </summary>
        public static void QueueForRandomHeroic()
        {
            Logging.Write("[DungeonBuddy] Queuing for Random Heroic...");
            
            // WotLK 3.3.5a: LFG_Dungeons.dbc ID 262 = Random Lich King Heroic Dungeon
            // Pas besoin de boucle sur GetDungeonInfo(), l'ID est fixe.
            Lua.DoString(@"
                ClearAllLFGDungeons(1);
                SetLFGDungeon(1, 262);
                JoinLFG(1);
            ");
        }

        /// <summary>
        /// Queue pour un donjon spécifique
        /// </summary>
        public static void QueueForSpecificDungeon(uint dungeonId)
        {
            Logging.Write($"[DungeonBuddy] Queuing for dungeon ID {dungeonId}...");
            
            Lua.DoString($@"
                ClearAllLFGDungeons(1);
                SetLFGDungeon(1, {dungeonId});
                JoinLFG(1);
            ");
        }

        /// <summary>
        /// Accepter la proposition de donjon.
        /// ⚠️ AcceptProposal() est une fonction Lua RESTRICTED (Protected) en WotLK.
        /// Elle nécessite un "hardware event" (clic utilisateur) pour s'exécuter
        /// via l'interface Blizzard standard. MAIS CopilotBuddy injecte via EndScene
        /// (GreenMagic), ce qui bypass cette restriction car le code s'exécute
        /// dans le contexte du thread principal du jeu.
        /// Pattern HB: ajout d'un délai aléatoire (1-3s) avant d'accepter
        /// pour paraître plus humain et éviter la détection.
        /// </summary>
        public static void AcceptProposal()
        {
            Logging.Write("[DungeonBuddy] Accepting dungeon proposal...");
            // Le délai aléatoire est géré par le behavior tree dans DungeonBuddy.cs
            // via WaitContinue avant d'appeler cette méthode.
            Lua.DoString("AcceptProposal()");
        }

        /// <summary>
        /// Refuser la proposition
        /// </summary>
        public static void DeclineProposal()
        {
            Logging.Write("[DungeonBuddy] Declining dungeon proposal...");
            Lua.DoString("RejectProposal()");
        }

        /// <summary>
        /// Quitter la queue
        /// </summary>
        public static void LeaveQueue()
        {
            Logging.Write("[DungeonBuddy] Leaving LFG queue...");
            Lua.DoString("LeaveLFG(1)");
        }

        // ═══════════════════════════════════════════════════════════
        // TELEPORT
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Téléporte dans le donjon
        /// </summary>
        public static void TeleportIn()
        {
            Logging.Write("[DungeonBuddy] Teleporting into dungeon...");
            // LFGTeleport(toSafety) — false = teleport INTO dungeon
            // Wowpedia: toSafety=false to teleport to the dungeon
            Lua.DoString("LFGTeleport(false)");
        }

        /// <summary>
        /// Téléporte hors du donjon
        /// </summary>
        public static void TeleportOut()
        {
            Logging.Write("[DungeonBuddy] Teleporting out of dungeon...");
            Lua.DoString("LFGTeleport(true)");
        }

        // ═══════════════════════════════════════════════════════════
        // ROLE SELECTION
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Définit le rôle pour le LFG
        /// </summary>
        public static void SetRole(PartyRole role)
        {
            bool tank = (role & PartyRole.Tank) != 0;
            bool healer = (role & PartyRole.Healer) != 0;
            bool dps = (role & PartyRole.Dps) != 0;
            
            Lua.DoString($"SetLFGRoles(false, {(tank ? "true" : "false")}, {(healer ? "true" : "false")}, {(dps ? "true" : "false")})");
        }

        /// <summary>
        /// Obtient les rôles disponibles pour la classe actuelle
        /// </summary>
        public static PartyRole GetAvailableRoles()
        {
            var result = PartyRole.None;
            
            // Toutes les classes peuvent DPS
            result |= PartyRole.Dps;
            
            // Tanks: Warrior, Paladin, Death Knight, Druid (Bear)
            var tankClasses = new[] { WoWClass.Warrior, WoWClass.Paladin, WoWClass.DeathKnight, WoWClass.Druid };
            if (tankClasses.Contains(StyxWoW.Me.Class))
                result |= PartyRole.Tank;
            
            // Healers: Priest, Paladin, Shaman, Druid
            var healerClasses = new[] { WoWClass.Priest, WoWClass.Paladin, WoWClass.Shaman, WoWClass.Druid };
            if (healerClasses.Contains(StyxWoW.Me.Class))
                result |= PartyRole.Healer;
            
            return result;
        }

        // ═══════════════════════════════════════════════════════════
        // DUNGEON INFO
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Obtient le MapID du donjon actuel
        /// </summary>
        public static uint CurrentMapId => (uint)StyxWoW.Me.MapId;

        /// <summary>
        /// Obtient la difficulté du donjon actuel (1=Normal, 2=Heroic)
        /// </summary>
        public static int CurrentDifficulty
        {
            get
            {
                return Lua.GetReturnVal<int>("return GetInstanceDifficulty()", 0);
            }
        }

        /// <summary>
        /// Liste des donjons disponibles pour le niveau actuel.
        /// NOTE: GetDungeonInfo(i) existe en WotLK mais n'est PAS GetLFGDungeonInfo().
        /// GetNumDungeons() et GetDungeonInfo() sont des API de LFDFrame.
        /// </summary>
        public static List<(uint Id, string Name, bool IsHeroic)> GetAvailableDungeons()
        {
            var result = new List<(uint, string, bool)>();
            
            var luaResult = Lua.GetReturnVal<string>(@"
                local dungeons = '';
                for i = 1, GetNumDungeons() do
                    local name, typeID, subtypeID, minLevel, maxLevel, _, minRecLevel, maxRecLevel,
                          _, _, _, difficulty = GetDungeonInfo(i);
                    if UnitLevel('player') >= minLevel and UnitLevel('player') <= maxLevel then
                        dungeons = dungeons .. i .. '|' .. name .. '|' .. difficulty .. ';';
                    end
                end
                return dungeons;
            ", 0);

            if (string.IsNullOrEmpty(luaResult))
                return result;

            foreach (var entry in luaResult.Split(';').Where(s => !string.IsNullOrEmpty(s)))
            {
                var parts = entry.Split('|');
                if (parts.Length >= 3)
                {
                    uint id = uint.Parse(parts[0]);
                    string name = parts[1];
                    bool isHeroic = parts[2] == "2";
                    result.Add((id, name, isHeroic));
                }
            }

            return result;
        }

        // ═══════════════════════════════════════════════════════════
        // LFG EVENTS (via Lua.Events — confirmé dans CopilotBuddy)
        // ═══════════════════════════════════════════════════════════

        // Événements LFG WotLK confirmés:
        //   LFG_PROPOSAL_SHOW          — Popup de proposal affiché
        //   LFG_PROPOSAL_SUCCEEDED     — Tous ont accepté, téléport imminent
        //   LFG_PROPOSAL_FAILED        — Quelqu'un a refusé/timeout
        //   LFG_COMPLETION_REWARD      — Donjon terminé, emblem reward
        //   LFG_OFFER_CONTINUE         — Proposer de requeue après completion
        //   LFG_ROLE_CHECK_SHOW        — Role check window
        //   LFG_BOOT_PROPOSAL_UPDATE   — Vote kick en cours

        /// <summary>
        /// Flag: un proposal est en attente (set par événement Lua)
        /// </summary>
        public static bool ProposalPending { get; set; }

        /// <summary>
        /// Flag: le donjon est terminé (reward reçu)
        /// </summary>
        public static bool DungeonCompleted { get; set; }

        /// <summary>
        /// Flag: un vote kick est en cours
        /// </summary>
        public static bool BootProposalActive { get; set; }

        /// <summary>
        /// Attache les événements LFG via Lua.Events.AttachEvent
        /// Appelé dans DungeonBuddy.Start()
        /// </summary>
        public static void AttachLfgEvents()
        {
            ProposalPending = false;
            DungeonCompleted = false;
            BootProposalActive = false;

            Lua.Events.AttachEvent("LFG_PROPOSAL_SHOW", OnProposalShow);
            Lua.Events.AttachEvent("LFG_PROPOSAL_SUCCEEDED", OnProposalSucceeded);
            Lua.Events.AttachEvent("LFG_PROPOSAL_FAILED", OnProposalFailed);
            Lua.Events.AttachEvent("LFG_COMPLETION_REWARD", OnCompletionReward);
            Lua.Events.AttachEvent("LFG_OFFER_CONTINUE", OnOfferContinue);
            Lua.Events.AttachEvent("LFG_ROLE_CHECK_SHOW", OnRoleCheck);
            Lua.Events.AttachEvent("LFG_BOOT_PROPOSAL_UPDATE", OnBootProposal);
        }

        /// <summary>
        /// Détache les événements LFG.
        /// Appelé dans DungeonBuddy.Stop()
        /// </summary>
        public static void DetachLfgEvents()
        {
            Lua.Events.DetachEvent("LFG_PROPOSAL_SHOW", OnProposalShow);
            Lua.Events.DetachEvent("LFG_PROPOSAL_SUCCEEDED", OnProposalSucceeded);
            Lua.Events.DetachEvent("LFG_PROPOSAL_FAILED", OnProposalFailed);
            Lua.Events.DetachEvent("LFG_COMPLETION_REWARD", OnCompletionReward);
            Lua.Events.DetachEvent("LFG_OFFER_CONTINUE", OnOfferContinue);
            Lua.Events.DetachEvent("LFG_ROLE_CHECK_SHOW", OnRoleCheck);
            Lua.Events.DetachEvent("LFG_BOOT_PROPOSAL_UPDATE", OnBootProposal);
        }

        private static void OnProposalShow(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] LFG proposal received!");
            ProposalPending = true;
        }

        private static void OnProposalSucceeded(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] LFG proposal accepted by all! Teleporting...");
            ProposalPending = false;
            // Téléport imminent — pas d'action requise, le serveur gère
        }

        private static void OnProposalFailed(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] LFG proposal failed/declined");
            ProposalPending = false;
        }

        private static void OnCompletionReward(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] Dungeon completed! Reward received.");
            DungeonCompleted = true;
        }

        private static void OnOfferContinue(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] LFG offer to continue (requeue)");
            // Le behavior tree dans DungeonBuddy.cs gère le requeue
        }

        private static void OnRoleCheck(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] Role check requested, accepting...");
            // Accepter automatiquement le role check
            Lua.DoString("LFDRoleCheckPopupAcceptButton:Click()");
        }

        private static void OnBootProposal(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] Vote kick in progress");
            BootProposalActive = true;
            // Voter oui par défaut (ne pas bloquer le groupe)
            Lua.DoString("SetLFGBootVote(true)");
        }
    }
}
```

---

### 7. Bots/DungeonBuddy/BossManager.cs

```csharp
using System.Collections.Generic;
using System.Linq;
using Styx;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.DungeonBuddy
{
    /// <summary>
    /// Gère les boss du donjon actuel.
    /// Track les boss tués et le boss actuel à target.
    /// </summary>
    public static class BossManager
    {
        private static readonly HashSet<uint> _killedBossIds = new();
        private static readonly List<BossInfo> _bosses = new();

        public class BossInfo
        {
            public uint EntryId { get; set; }
            public string Name { get; set; }
            public bool IsOptional { get; set; }
            public bool IsDead { get; set; }
        }

        /// <summary>
        /// Boss actuel à tuer (le plus proche, non-mort)
        /// </summary>
        public static WoWUnit CurrentBoss
        {
            get
            {
                return ObjectManager.GetObjectsOfType<WoWUnit>()
                    .Where(u => u.IsBoss && u.IsAlive && !_killedBossIds.Contains(u.Entry))
                    .OrderBy(u => u.DistanceSqr)
                    .FirstOrDefault();
            }
        }

        /// <summary>
        /// Liste de tous les boss du donjon
        /// </summary>
        public static IReadOnlyList<BossInfo> Bosses => _bosses;

        /// <summary>
        /// Initialise les boss pour le donjon actuel
        /// </summary>
        public static void Initialize(Dungeon dungeon)
        {
            _killedBossIds.Clear();
            _bosses.Clear();
            
            // Les boss sont découverts dynamiquement via les handlers
        }

        /// <summary>
        /// Marque un boss comme tué
        /// </summary>
        public static void MarkBossDead(uint entryId)
        {
            _killedBossIds.Add(entryId);
            
            var boss = _bosses.FirstOrDefault(b => b.EntryId == entryId);
            if (boss != null)
                boss.IsDead = true;
        }

        /// <summary>
        /// Register un boss (appelé par DungeonManager lors du chargement des scripts)
        /// </summary>
        public static void RegisterBoss(uint entryId, string name, bool isOptional = false)
        {
            if (!_bosses.Any(b => b.EntryId == entryId))
            {
                _bosses.Add(new BossInfo
                {
                    EntryId = entryId,
                    Name = name,
                    IsOptional = isOptional,
                    IsDead = false
                });
            }
        }

        /// <summary>
        /// Vérifie si tous les boss obligatoires sont morts
        /// </summary>
        public static bool AreAllRequiredBossesDead()
        {
            return _bosses.Where(b => !b.IsOptional).All(b => b.IsDead);
        }

        /// <summary>
        /// Reset pour nouveau donjon
        /// </summary>
        public static void Reset()
        {
            _killedBossIds.Clear();
            _bosses.Clear();
        }
    }
}
```

---

### 8. Bots/DungeonBuddy/DungeonManager.cs

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bots.DungeonBuddy.Attributes;
using Bots.DungeonBuddy.Profiles;
using Styx;
using Styx.Helpers;
using TreeSharp;

namespace Bots.DungeonBuddy
{
    /// <summary>
    /// Charge et gère les scripts de donjons.
    /// Charge et gère les scripts de donjons.
    /// Les scripts .cs sont inclus dans le .csproj et compilés avec le projet.
    /// </summary>
    public static class DungeonManager
    {
        private static readonly Dictionary<uint, Type> _dungeonTypes = new();
        private static readonly Dictionary<uint, Dictionary<int, MethodInfo>> _encounterHandlers = new();
        private static readonly Dictionary<uint, Dictionary<int, MethodInfo>> _objectHandlers = new();
        
        private static Dungeon _currentDungeon;

        /// <summary>
        /// Donjon actif
        /// </summary>
        public static Dungeon CurrentDungeon => _currentDungeon;

        /// <summary>
        /// Charge tous les scripts de donjon via réflection sur l'assembly compilée.
        /// Les scripts .cs doivent être inclus dans le .csproj.
        /// </summary>
        public static void LoadDungeonScripts()
        {
            _dungeonTypes.Clear();
            _encounterHandlers.Clear();
            _objectHandlers.Clear();

            LoadDungeonTypes();

            Logging.Write($"[DungeonBuddy] Loaded {_dungeonTypes.Count} dungeon scripts");
        }

        /// <summary>
        /// Scanne l'assembly pour trouver tous les types héritant de Dungeon.
        /// NOTE: Les fichiers .cs des scripts doivent être inclus dans le .csproj
        /// pour être compilés. La compilation dynamique (comme HB) peut être ajoutée plus tard.
        /// Cette méthode est appelée une seule fois, pas par fichier.
        /// </summary>
        private static void LoadDungeonTypes()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var dungeonTypes = assembly.GetTypes()
                    .Where(t => t.IsSubclassOf(typeof(Dungeon)) && !t.IsAbstract);

                foreach (var type in dungeonTypes)
                {
                    try
                    {
                        var instance = (Dungeon)Activator.CreateInstance(type);
                        var dungeonId = instance.DungeonId;
                        instance.Dispose();

                        if (!_dungeonTypes.ContainsKey(dungeonId))
                        {
                            _dungeonTypes[dungeonId] = type;
                            IndexHandlers(type, dungeonId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.WriteDiagnostic($"[DungeonBuddy] Error loading {type.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDiagnostic($"[DungeonBuddy] Error scanning dungeon types: {ex.Message}");
            }
        }

        private static void IndexHandlers(Type dungeonType, uint dungeonId)
        {
            _encounterHandlers[dungeonId] = new Dictionary<int, MethodInfo>();
            _objectHandlers[dungeonId] = new Dictionary<int, MethodInfo>();

            foreach (var method in dungeonType.GetMethods())
            {
                // Index encounter handlers
                var encounterAttrs = method.GetCustomAttributes<EncounterHandlerAttribute>();
                foreach (var attr in encounterAttrs)
                {
                    _encounterHandlers[dungeonId][attr.BossEntry] = method;
                    BossManager.RegisterBoss((uint)attr.BossEntry, attr.BossName);
                }

                // Index object handlers
                var objectAttrs = method.GetCustomAttributes<ObjectHandlerAttribute>();
                foreach (var attr in objectAttrs)
                {
                    _objectHandlers[dungeonId][attr.ObjectEntry] = method;
                }
            }
        }

        /// <summary>
        /// Active le script de donjon approprié
        /// </summary>
        public static void SetDungeon(uint mapId)
        {
            // Détacher l'ancien donjon
            _currentDungeon?.Detach();
            _currentDungeon = null;

            // Trouver le type de donjon correspondant au MapId
            Type matchedType = null;
            foreach (var kvp in _dungeonTypes)
            {
                var dungeonId = kvp.Key;
                if (dungeonId == mapId || GetMapIdForDungeon(dungeonId) == mapId)
                {
                    matchedType = kvp.Value;
                    break;
                }
            }

            if (matchedType != null)
            {
                _currentDungeon = (Dungeon)Activator.CreateInstance(matchedType);
                _currentDungeon.Attach();
                BossManager.Initialize(_currentDungeon);
                Logging.Write($"[DungeonBuddy] Activated script: {_currentDungeon.Name}");
            }
            else
            {
                Logging.Write($"[DungeonBuddy] No script found for map {mapId}");
            }
        }

        /// <summary>
        /// Obtient le behavior pour un boss spécifique
        /// </summary>
        public static Composite GetEncounterBehavior(int bossEntryId)
        {
            if (_currentDungeon == null)
                return null;

            var dungeonId = _currentDungeon.DungeonId;
            
            if (_encounterHandlers.TryGetValue(dungeonId, out var handlers) &&
                handlers.TryGetValue(bossEntryId, out var method))
            {
                return method.Invoke(_currentDungeon, null) as Composite;
            }

            return null;
        }

        /// <summary>
        /// Obtient le behavior pour un objet spécifique
        /// </summary>
        public static Composite GetObjectBehavior(int objectEntryId)
        {
            if (_currentDungeon == null)
                return null;

            var dungeonId = _currentDungeon.DungeonId;
            
            if (_objectHandlers.TryGetValue(dungeonId, out var handlers) &&
                handlers.TryGetValue(objectEntryId, out var method))
            {
                return method.Invoke(_currentDungeon, null) as Composite;
            }

            return null;
        }

        private static uint GetMapIdForDungeon(uint lfgDungeonId)
        {
            // Mapping LFG DungeonId → MapId pour WotLK
            // Ces IDs viennent de LFG_Dungeons.dbc
            // Note: Les IDs normaux et héroïques mappent vers le même MapId
            // DungeonIds vérifiés depuis les 32 scripts dans Dungeon Scripts\Wrath of the Lich King\
            return lfgDungeonId switch
            {
                202 => 574,  // Utgarde Keep (Normal)
                242 => 574,  // Utgarde Keep (Heroic)
                203 => 575,  // Utgarde Pinnacle (Normal)
                205 => 575,  // Utgarde Pinnacle (Heroic)
                204 => 601,  // Azjol-Nerub (Normal)
                241 => 601,  // Azjol-Nerub (Heroic)
                206 => 578,  // The Oculus (Normal)
                211 => 578,  // The Oculus (Heroic)
                207 => 602,  // Halls of Lightning (Normal)
                212 => 602,  // Halls of Lightning (Heroic)
                208 => 599,  // Halls of Stone (Normal)
                213 => 599,  // Halls of Stone (Heroic)
                209 => 595,  // Culling of Stratholme (Normal)
                210 => 595,  // Culling of Stratholme (Heroic)
                214 => 600,  // Drak'Tharon Keep (Normal)
                215 => 600,  // Drak'Tharon Keep (Heroic)
                216 => 604,  // Gundrak (Normal)
                217 => 604,  // Gundrak (Heroic)
                218 => 619,  // Ahn'kahet (Normal)
                219 => 619,  // Ahn'kahet (Heroic)
                220 => 608,  // Violet Hold (Normal)
                221 => 608,  // Violet Hold (Heroic)
                225 => 576,  // The Nexus (Normal)
                226 => 576,  // The Nexus (Heroic)
                245 => 650,  // Trial of the Champion (Normal)
                249 => 650,  // Trial of the Champion (Heroic)
                251 => 632,  // Forge of Souls (Normal)
                252 => 632,  // Forge of Souls (Heroic)
                253 => 658,  // Pit of Saron (Normal)
                254 => 658,  // Pit of Saron (Heroic)
                255 => 668,  // Halls of Reflection (Normal)
                256 => 668,  // Halls of Reflection (Heroic)
                _ => 0
            };
        }

        public static void Clear()
        {
            _currentDungeon?.Detach();
            _currentDungeon = null;
        }
    }
}
```

---

### 9. Bots/DungeonBuddy/DungeonBuddySettings.cs

```csharp
using System;
using System.IO;
using Bots.DungeonBuddy.Enums;
using Styx;
using Styx.Helpers;

namespace Bots.DungeonBuddy
{
    public class DungeonBuddySettings : Settings
    {
        private static DungeonBuddySettings _instance;
        
        public static DungeonBuddySettings Instance => 
            _instance ?? (_instance = new DungeonBuddySettings());

        public DungeonBuddySettings()
            : base(Path.Combine(
                Logging.ApplicationPath, 
                $"Settings\\DungeonBuddySettings_{StyxWoW.Me?.Name ?? "Unknown"}.xml"))
        {
            Load();
        }

        // ═══════════════════════════════════════════════════════════
        // MODE
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(DungeonMode.LookingForGroup)]
        public DungeonMode Mode { get; set; }

        [Setting, DefaultValue(QueueType.RandomHeroic)]
        public QueueType QueueType { get; set; }

        /// <summary>
        /// IDs des donjons sélectionnés (pour QueueType.Specific)
        /// </summary>
        [Setting]
        public uint[] SelectedDungeonIds { get; set; } = Array.Empty<uint>();

        // ═══════════════════════════════════════════════════════════
        // ROLE
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(PartyRole.Dps)]
        public PartyRole PreferredRole { get; set; }

        // ═══════════════════════════════════════════════════════════
        // LOOT
        // ═══════════════════════════════════════════════════════════

        [Setting, DefaultValue(LootMode.BossesOnly)]
        public LootMode LootMode { get; set; }

        [Setting, DefaultValue(3)]
        public int MinFreeBagSlots { get; set; }

        // ═══════════════════════════════════════════════════════════
        // BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Tue les boss optionnels
        /// </summary>
        [Setting, DefaultValue(false)]
        public bool KillOptionalBosses { get; set; }

        /// <summary>
        /// Requeue automatiquement après fin du donjon
        /// </summary>
        [Setting, DefaultValue(true)]
        public bool AutoRequeue { get; set; }

        /// <summary>
        /// Distance max pour suivre le tank
        /// </summary>
        [Setting, DefaultValue(30f)]
        public float FollowDistance { get; set; }
    }
}
```

---

### 10. Bots/DungeonBuddy/DungeonBuddy.cs (FICHIER PRINCIPAL)

```csharp
using System;
using System.Diagnostics;
using System.Linq;
using Bots.DungeonBuddy.Avoidance;
using Bots.DungeonBuddy.Enums;
using CommonBehaviors.Actions;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Bots.DungeonBuddy
{
    /// <summary>
    /// DungeonBuddy - BotBase pour Dungeon Finder automatique
    /// WotLK 3.3.5a (patch 3.3 — Dungeon Finder ajouté)
    /// 
    /// State machine:
    ///   NotInLfg → SetRole + Queue → InQueue → Proposal → Accept → InDungeon
    ///   InDungeon → Combat/Follow → DungeonComplete → TeleportOut → Requeue
    ///   
    /// LFG state détecté via GetLFGMode() (API canonique WotLK 3.3).
    /// Events LFG via Lua.Events.AttachEvent (confirmé dans CopilotBuddy).
    /// </summary>
    public class DungeonBuddy : BotBase
    {
        public override string Name => "DungeonBuddy";
        public override bool IsPrimaryType => true;
        public override bool RequiresProfile => false;
        public override bool RequirementsMet => true;
        public override PulseFlags PulseFlags => PulseFlags.All;

        private PrioritySelector _root;
        private static CombatRoutine Routine => RoutineManager.Current;
        
        // Timers
        private readonly Stopwatch _proposalDelay = new();
        private readonly Stopwatch _requeueDelay = new();
        private readonly Random _rng = new();
        private int _proposalWaitMs;  // Délai aléatoire avant AcceptProposal

        // State tracking
        private uint _lastMapId;
        private bool _hasSetRole;

        public override Composite Root => _root ??= CreateRootBehavior();

        // ═══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════

        public override void Start()
        {
            Logging.Write("[DungeonBuddy] Starting...");
            
            // Charger les scripts de donjon (réflection sur l'assembly)
            DungeonManager.LoadDungeonScripts();
            
            // Attacher les événements LFG
            LfgManager.AttachLfgEvents();
            
            _hasSetRole = false;
            _lastMapId = 0;
            
            Logging.Write("[DungeonBuddy] Started successfully!");
        }

        public override void Stop()
        {
            Logging.Write("[DungeonBuddy] Stopping...");
            
            LfgManager.DetachLfgEvents();
            DungeonManager.Clear();
            BossManager.Reset();
            AvoidanceManager.Clear();
        }

        public override void Pulse()
        {
            // NOTE: HB 4.3.4 wrappe Root.Tick() dans un FrameLock (ObjectManager.Update + lock).
            // Si on observe des incohérences d'état (objets désync), envisager:
            //   using (StyxWoW.Memory.AcquireFrame()) { Root.Tick(...); }
            // À valider en jeu — pour l'instant on pulse sans FrameLock comme LevelBot.
            
            // Détecter changement de map (entrée/sortie donjon)
            var currentMap = (uint)StyxWoW.Me.MapId;
            if (currentMap != _lastMapId)
            {
                _lastMapId = currentMap;
                OnMapChanged(currentMap);
            }
            
            // Mettre à jour l'avoidance
            AvoidanceManager.Update();
        }

        private void OnMapChanged(uint newMapId)
        {
            if (StyxWoW.Me.IsInInstance)
            {
                Logging.Write($"[DungeonBuddy] Entered instance (MapId={newMapId})");
                DungeonManager.SetDungeon(newMapId);
                LfgManager.DungeonCompleted = false;
            }
            else
            {
                Logging.Write($"[DungeonBuddy] Left instance");
                DungeonManager.Clear();
                BossManager.Reset();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // BEHAVIOR TREE
        // ═══════════════════════════════════════════════════════════

        private PrioritySelector CreateRootBehavior()
        {
            return new PrioritySelector(
                // 1. Death handling
                CreateDeathBehavior(),
                
                // 2. LFG State Machine (queue, proposal, teleport)
                CreateLfgBehavior(),
                
                // 3. IN DUNGEON: Avoidance (priorité sur combat)
                CreateAvoidanceBehavior(),
                
                // 4. IN DUNGEON: Combat (avec encounter handlers)
                CreateCombatBehavior(),
                
                // 5. IN DUNGEON: Loot
                CreateLootBehavior(),
                
                // 6. IN DUNGEON: Follow tank (si DPS/Healer)
                CreateFollowBehavior(),
                
                // 7. Idle
                new ActionIdle()
            );
        }

        // ═══════════════════════════════════════════════════════════
        // LFG STATE MACHINE
        // ═══════════════════════════════════════════════════════════

        private Composite CreateLfgBehavior()
        {
            return new PrioritySelector(
                // --- PROPOSAL: Accepter avec délai humain ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.Proposal,
                    new Sequence(
                        new Action(ctx =>
                        {
                            if (!_proposalDelay.IsRunning)
                            {
                                // Délai aléatoire 1-3 secondes (pattern HB anti-détection)
                                _proposalWaitMs = _rng.Next(1000, 3000);
                                _proposalDelay.Restart();
                                Logging.Write($"[DungeonBuddy] Proposal! Accepting in {_proposalWaitMs}ms...");
                            }
                            return RunStatus.Success;
                        }),
                        new WaitContinue(
                            TimeSpan.FromSeconds(5),
                            ctx => _proposalDelay.ElapsedMilliseconds >= _proposalWaitMs,
                            new Action(ctx =>
                            {
                                LfgManager.AcceptProposal();
                                LfgManager.ProposalPending = false;
                                _proposalDelay.Reset();
                                return RunStatus.Success;
                            })
                        )
                    )
                ),

                // --- ROLE CHECK: Accepter automatiquement ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.RoleCheck,
                    new Action(ctx =>
                    {
                        Logging.Write("[DungeonBuddy] Role check — accepting...");
                        Lua.DoString("LFDRoleCheckPopupAcceptButton:Click()");
                        return RunStatus.Success;
                    })
                ),

                // --- IN DUNGEON: Dungeon completed → teleport out + requeue ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.InDungeon &&
                           LfgManager.DungeonCompleted &&
                           DungeonBuddySettings.Instance.AutoRequeue,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Logging.Write("[DungeonBuddy] Dungeon complete! Teleporting out...");
                            LfgManager.TeleportOut();
                            LfgManager.DungeonCompleted = false;
                            _requeueDelay.Restart();
                            return RunStatus.Success;
                        }),
                        new WaitContinue(
                            TimeSpan.FromSeconds(10),
                            ctx => !StyxWoW.Me.IsInInstance,
                            new ActionAlwaysSucceed()
                        )
                    )
                ),

                // --- ABANDONED IN DUNGEON: Teleport out ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.AbandonedInDungeon,
                    new Action(ctx =>
                    {
                        Logging.Write("[DungeonBuddy] Abandoned in dungeon, teleporting out...");
                        LfgManager.TeleportOut();
                        return RunStatus.Success;
                    })
                ),

                // --- NOT IN LFG: Set role + Queue ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.NotInLfg &&
                           DungeonBuddySettings.Instance.Mode == DungeonMode.LookingForGroup,
                    new Sequence(
                        // Set role si pas encore fait
                        new DecoratorContinue(
                            ctx => !_hasSetRole,
                            new Action(ctx =>
                            {
                                var role = DungeonBuddySettings.Instance.PreferredRole;
                                LfgManager.SetRole(role);
                                _hasSetRole = true;
                                Logging.Write($"[DungeonBuddy] Role set to {role}");
                                return RunStatus.Success;
                            })
                        ),
                        // Attendre un peu après teleport out avant requeue
                        new Decorator(
                            ctx => !_requeueDelay.IsRunning || _requeueDelay.ElapsedMilliseconds > 3000,
                            new Action(ctx =>
                            {
                                var settings = DungeonBuddySettings.Instance;
                                switch (settings.QueueType)
                                {
                                    case QueueType.RandomDungeon:
                                        LfgManager.QueueForRandomDungeon();
                                        break;
                                    case QueueType.RandomHeroic:
                                        LfgManager.QueueForRandomHeroic();
                                        break;
                                    case QueueType.Specific:
                                        if (settings.SelectedDungeonIds.Length > 0)
                                            LfgManager.QueueForSpecificDungeon(settings.SelectedDungeonIds[0]);
                                        break;
                                }
                                _requeueDelay.Reset();
                                return RunStatus.Success;
                            })
                        )
                    )
                ),

                // --- IN QUEUE: Idle, afficher timer ---
                new Decorator(
                    ctx => LfgManager.CurrentState == LfgState.InQueue,
                    new ActionAlwaysFail() // Laisser tomber vers les autres behaviors (idle)
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // DEATH BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateDeathBehavior()
        {
            return new PrioritySelector(
                new Decorator(
                    ctx => StyxWoW.Me.IsDead,
                    new Sequence(
                        new Action(ctx =>
                        {
                            Logging.Write("[DungeonBuddy] Died! Releasing...");
                            Lua.DoString("RepopMe()");
                        }),
                        new WaitContinue(5, ctx => StyxWoW.Me.IsGhost, new ActionAlwaysSucceed())
                    )
                ),
                new Decorator(
                    ctx => StyxWoW.Me.IsGhost && StyxWoW.Me.IsInInstance,
                    new Sequence(
                        // En donjon: retour à l'entrée de l'instance
                        new Action(ctx =>
                        {
                            var entrance = DungeonManager.CurrentDungeon?.Entrance ?? WoWPoint.Empty;
                            if (entrance != WoWPoint.Empty)
                                Navigator.MoveTo(entrance);
                            return RunStatus.Running;
                        })
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // AVOIDANCE BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateAvoidanceBehavior()
        {
            return new Decorator(
                ctx => StyxWoW.Me.IsInInstance && 
                       AvoidanceManager.IsInAvoidance(StyxWoW.Me.Location),
                new Action(ctx =>
                {
                    var safePoint = AvoidanceManager.GetSafePoint(StyxWoW.Me.Location);
                    Navigator.MoveTo(safePoint);
                    return RunStatus.Running;
                })
            );
        }

        // ═══════════════════════════════════════════════════════════
        // COMBAT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        // Champ pour tracker le behavior d'encounter actif et le boss associé
        // IMPORTANT: Le Composite NE DOIT PAS être re-Start() à chaque pulse.
        // Start() réinitialise l'état interne (Sequences, WaitContinue, etc.)
        // On doit Start() UNE SEULE FOIS quand le boss change, puis Tick() à chaque pulse.
        // Référence: HB 4.3.4 construit les encounter behaviors dans le Root tree
        // via réflection, pas manuellement. Ici on simule ce pattern.
        private Composite _activeEncounterBehavior;
        private uint _activeEncounterBossEntry;

        private Composite CreateCombatBehavior()
        {
            return new PrioritySelector(
                // Rest si hors combat
                new Decorator(
                    ctx => StyxWoW.Me.IsInInstance && !StyxWoW.Me.Combat && 
                           Routine?.RestBehavior != null,
                    Routine.RestBehavior
                ),
                // Encounter handler pour boss
                new Decorator(
                    ctx => StyxWoW.Me.IsInInstance && StyxWoW.Me.Combat &&
                           StyxWoW.Me.CurrentTarget != null,
                    new PrioritySelector(
                        // Vérifier si un encounter handler existe pour la cible
                        new Decorator(
                            ctx => StyxWoW.Me.CurrentTarget.IsBoss,
                            new Action(ctx =>
                            {
                                var boss = StyxWoW.Me.CurrentTarget;
                                
                                // Si le boss a changé, charger le nouveau behavior
                                if (boss.Entry != _activeEncounterBossEntry)
                                {
                                    // Stop l'ancien behavior proprement
                                    if (_activeEncounterBehavior != null)
                                    {
                                        try { _activeEncounterBehavior.Stop(boss); } catch { }
                                        _activeEncounterBehavior = null;
                                    }
                                    
                                    _activeEncounterBossEntry = boss.Entry;
                                    _activeEncounterBehavior = DungeonManager.GetEncounterBehavior(
                                        (int)boss.Entry);
                                    
                                    // Start() UNE SEULE FOIS pour initialiser le Composite
                                    if (_activeEncounterBehavior != null)
                                    {
                                        // Passer le boss comme contexte car les scripts font
                                        // "ctx => boss = ctx as WoWUnit" dans leur PrioritySelector.
                                        _activeEncounterBehavior.Start(boss);
                                    }
                                }
                                
                                if (_activeEncounterBehavior != null)
                                {
                                    // Tick() à chaque pulse avec le boss comme contexte
                                    var result = _activeEncounterBehavior.Tick(boss);
                                    if (result != RunStatus.Running)
                                    {
                                        // Encounter terminé → cleanup
                                        _activeEncounterBehavior.Stop(boss);
                                        _activeEncounterBehavior = null;
                                        _activeEncounterBossEntry = 0;
                                    }
                                    return result;
                                }
                                return RunStatus.Failure;
                            })
                        ),
                        // Combat normal via CombatRoutine
                        Routine?.CombatBehavior ?? new ActionAlwaysFail()
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // LOOT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateLootBehavior()
        {
            return new Decorator(
                ctx => StyxWoW.Me.IsInInstance && !StyxWoW.Me.Combat &&
                       DungeonBuddySettings.Instance.LootMode != LootMode.Never,
                new PrioritySelector(
                    // Loot boss uniquement ou tout
                    new Decorator(
                        ctx =>
                        {
                            var lootable = ObjectManager.GetObjectsOfType<WoWUnit>()
                                .Where(u => u.IsDead && u.CanLoot && u.DistanceSqr < 50 * 50);
                            
                            if (DungeonBuddySettings.Instance.LootMode == LootMode.BossesOnly)
                                lootable = lootable.Where(u => u.IsBoss);
                            
                            return lootable.Any();
                        },
                        new Action(ctx =>
                        {
                            var target = ObjectManager.GetObjectsOfType<WoWUnit>()
                                .Where(u => u.IsDead && u.CanLoot)
                                .OrderBy(u => u.DistanceSqr)
                                .First();
                            
                            if (target.DistanceSqr > 5 * 5)
                            {
                                Navigator.MoveTo(target.Location);
                                return RunStatus.Running;
                            }
                            
                            target.Interact();
                            return RunStatus.Success;
                        })
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // FOLLOW BEHAVIOR (DPS/Healer suit le Tank)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateFollowBehavior()
        {
            return new Decorator(
                ctx => StyxWoW.Me.IsInInstance && !StyxWoW.Me.Combat &&
                       !StyxWoW.Me.IsTank(), // IsTank() = extension method (role-based via UnitGroupRolesAssigned)
                new Action(ctx =>
                {
                    var tank = Helpers.ScriptHelpers.Tank;
                    if (tank == null || !tank.IsAlive)
                        return RunStatus.Failure;
                    
                    float followDist = DungeonBuddySettings.Instance.FollowDistance;
                    if (StyxWoW.Me.Location.DistanceSqr(tank.Location) > followDist * followDist)
                    {
                        Navigator.MoveTo(tank.Location);
                        return RunStatus.Running;
                    }
                    
                    return RunStatus.Failure;
                })
            );
        }
    }
}
```

---

## 📋 COPIE DES SCRIPTS EXISTANTS

**ACTION REQUISE:** Copier le dossier:
```
FROM: C:\Users\Texy\Desktop\.test\Dungeon Scripts\Wrath of the Lich King\
TO:   CopilotBuddy\Bots\DungeonBuddy\Dungeon Scripts\Wrath of the Lich King\
```

Les scripts sont déjà compatibles - ils utilisent les namespaces:
- `Bots.DungeonBuddy.Profiles`
- `Bots.DungeonBuddy.Attributes`
- `Bots.DungeonBuddy.Helpers`

---

## 🧪 TESTS À EFFECTUER

### Phase 0: Prérequis (en jeu, AVANT de coder)
1. Tester `GetLFGMode()` en dehors de queue → doit retourner nil
2. Tester `GetLFGMode()` en queue → doit retourner "queued"
3. Tester `AcceptProposal()` via injection → doit fonctionner (RESTRICTED, bypass EndScene)
4. Tester `ClearAllLFGDungeons(1); SetLFGDungeon(1, 261); JoinLFG(1)` → doit queue
5. Tester extension methods IsTank()/IsDps() compilent et retournent correct

### Phase 1: Infrastructure
1. Compilation sans erreur
2. Chargement des scripts de donjon (réflection)
3. Détection du rôle (Tank/Healer/DPS)
4. LFG Events attachés (Lua.Events.AttachEvent)

### Phase 2: LFG State Machine
1. `GetLFGMode()` → `CurrentState` mapping correct
2. Queue pour random dungeon (ClearAll + SetLFG + JoinLFG)
3. Acceptation de proposal (avec délai aléatoire 1-3s)
4. Téléport in/out
5. Requeue après completion (event LFG_COMPLETION_REWARD)
6. Role check auto-accept (event LFG_ROLE_CHECK_SHOW)
7. Vote kick auto-accept (event LFG_BOOT_PROPOSAL_UPDATE)

### Phase 3: Combat
1. Encounter handlers appelés (Start/Tick/Stop lifecycle)
2. Avoidance fonctionnel
3. Follow tank (si DPS/Healer)

### Phase 4: Scripts individuels
1. Tester Utgarde Keep (le plus simple)
2. Tester The Nexus
3. Tester progressivement les autres donjons

---

## 🚀 ORDRE D'IMPLÉMENTATION

### Semaine 0: Prérequis CopilotBuddy
0. **Créer `Styx/Helpers/WoWPlayerExtensions.cs`** (IsTank()/IsDps()/IsHealer() methods)
0b. **Tester API LFG Lua en jeu** (voir section Prérequis)

### Semaine 1-2: Core + Compilation scripts
1. Enums (5 fichiers)
2. Attributes (3 fichiers)
3. Dungeon.cs (namespace `Bots.DungeonBuddy.Profiles`)
4. ScriptHelpers.cs (les scripts en dépendent)
5. **Copier les 32 scripts → valider compilation** (détecte les APIs manquantes tôt)

### Semaine 3-4: Settings + Avoidance
6. Settings
7. Avoidance system (5 fichiers)

### Semaine 5-6: LFG + Boss + Manager
8. LfgManager.cs (validé par tests Lua semaine 0)
9. BossManager.cs
10. DungeonManager.cs

### Semaine 7-8: Main Bot
9. Dungeon.cs
10. DungeonBuddy.cs (BotBase)

### Semaine 9+: Scripts & Testing
11. Copier scripts WotLK
12. Tests et ajustements

---

## ⚠️ NOTES IMPORTANTES

1. **LFG API WotLK 3.3:**
   - `SetLFGDungeon(1, dungeonId)` - Sélectionne un donjon (category 1 = LFD)
   - `JoinLFG(1)` - Rejoint la queue
   - `LeaveLFG(1)` - Quitte la queue
   - `AcceptProposal()` - Accepte proposition
   - `LFGTeleport()` / `LFGTeleport(true)` - Teleport in/out
   - `GetLFGQueueStats(1)` - État de la queue
   - `SetLFGRoles(leader, tank, healer, damage)` - Définir rôles
   - **IDs fixes:** 261 = Random Normal, 262 = Random Heroic
   - **⚠️ N'EXISTE PAS en WotLK:** `LE_LFG_CATEGORY_LFD`, `GetLFGDungeonInfo()`

2. **Pas de LFR dans WotLK** - Ne pas implémenter

3. **Scripts disponibles** - 32 scripts WotLK prêts à l'emploi dans `.test`

4. **Rôles via Lua:**
   - `UnitGroupRolesAssigned("player")` retourne "TANK", "HEALER", "DAMAGER"
   - `SetLFGRoles(leader, tank, healer, damage)`

5. **MapIds WotLK donjons:**
   - 574: Utgarde Keep
   - 575: Utgarde Pinnacle
   - 576: The Nexus
   - 578: The Oculus
   - 595: Culling of Stratholme
   - 599: Halls of Stone
   - 600: Drak'Tharon Keep
   - 601: Azjol-Nerub
   - 602: Halls of Lightning
   - 604: Gundrak
   - 608: Violet Hold
   - 619: Ahn'kahet
   - 632: Forge of Souls
   - 650: Trial of the Champion
   - 658: Pit of Saron
   - 668: Halls of Reflection

6. **DungeonIds (LFG_Dungeons.dbc) vérifiés depuis les 32 scripts:**
   - Utgarde Keep: Normal=202, Heroic=242
   - Utgarde Pinnacle: Normal=203, Heroic=205
   - Azjol-Nerub: Normal=204, Heroic=241
   - The Oculus: Normal=206, Heroic=211
   - Halls of Lightning: Normal=207, Heroic=212
   - Halls of Stone: Normal=208, Heroic=213
   - Culling of Stratholme: Normal=209, Heroic=210
   - Drak'Tharon Keep: Normal=214, Heroic=215
   - Gundrak: Normal=216, Heroic=217
   - Ahn'kahet: Normal=218, Heroic=219
   - Violet Hold: Normal=220, Heroic=221
   - The Nexus: Normal=225, Heroic=226
   - Trial of the Champion: Normal=245, Heroic=249
   - Forge of Souls: Normal=251, Heroic=252
   - Pit of Saron: Normal=253, Heroic=254
   - Halls of Reflection: Normal=255, Heroic=256
   - Random Normal: 261, Random Heroic: 262

---

## 🔧 PRÉREQUIS — MODIFICATIONS COPILOTBUDDY NÉCESSAIRES

Avant d'implémenter DungeonBuddy, ces modifications sont **OBLIGATOIRES** dans le code existant:

### 1. CRITIQUE: Extension methods IsTank()/IsDps()/IsHealer()

Les 32 scripts de donjons appellent `StyxWoW.Me.IsTank()` et `StyxWoW.Me.IsDps()` comme des **méthodes** (avec parenthèses).
CopilotBuddy a seulement `IsTank` et `IsHealer` comme **propriétés** (sans parenthèses), et `IsDps` n'existe pas du tout.

**Sans cette correction, aucun script de donjon ne compilera.**

**Fichier à créer:** `Styx/Helpers/WoWPlayerExtensions.cs`
```csharp
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Helpers
{
    /// <summary>
    /// Extension methods pour WoWPlayer/LocalPlayer.
    /// Permet d'appeler IsTank(), IsDps(), IsHealer() comme méthodes
    /// (les scripts DungeonBuddy les appellent avec parenthèses).
    /// Compatible HB 4.3.4 qui avait ces méthodes.
    /// 
    /// IMPORTANT: Ces méthodes utilisent UnitGroupRolesAssigned() pour détecter
    /// le rôle LFG ASSIGNÉ, pas la classe du joueur.
    /// Un Paladin Holy ne doit PAS retourner IsTank()=true.
    /// Les properties WoWPlayer.IsTank/IsHealer (class-based) restent disponibles
    /// pour savoir si la classe PEUT jouer ce rôle.
    /// 
    /// Référence HB 4.3.4: ScriptHelpers.cs L1308-L1501
    /// </summary>
    public static class WoWPlayerExtensions
    {
        /// <summary>
        /// True si le joueur a le rôle TANK assigné dans le LFG/groupe.
        /// Utilise UnitGroupRolesAssigned() Lua API.
        /// </summary>
        public static bool IsTank(this LocalPlayer me)
        {
            string role = Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0);
            if (!string.IsNullOrEmpty(role) && role != "NONE")
                return role == "TANK";
            // Fallback: détection par spells de spec (pattern HB 4.3.4)
            return me.Class switch
            {
                WoWClass.Warrior => SpellManager.HasSpell("Shield Slam"),
                WoWClass.Paladin => SpellManager.HasSpell("Avenger's Shield"),
                WoWClass.DeathKnight => SpellManager.HasSpell("Heart Strike"),
                WoWClass.Druid => SpellManager.HasSpell("Mangle") && !SpellManager.HasSpell("Moonkin Form"),
                _ => false
            };
        }

        /// <summary>
        /// True si le joueur a le rôle HEALER assigné dans le LFG/groupe.
        /// </summary>
        public static bool IsHealer(this LocalPlayer me)
        {
            string role = Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0);
            if (!string.IsNullOrEmpty(role) && role != "NONE")
                return role == "HEALER";
            // Fallback par spec
            return me.Class switch
            {
                WoWClass.Priest => !SpellManager.HasSpell("Shadowform"),
                WoWClass.Paladin => SpellManager.HasSpell("Holy Shock"),
                WoWClass.Shaman => SpellManager.HasSpell("Riptide"),
                WoWClass.Druid => SpellManager.HasSpell("Swiftmend"),
                _ => false
            };
        }

        /// <summary>
        /// True si le joueur a le rôle DPS (DAMAGER) assigné.
        /// </summary>
        public static bool IsDps(this LocalPlayer me)
        {
            string role = Lua.GetReturnVal<string>("return UnitGroupRolesAssigned('player')", 0);
            if (!string.IsNullOrEmpty(role) && role != "NONE")
                return role == "DAMAGER";
            // Fallback: ni tank ni healer
            return !me.IsTank() && !me.IsHealer();
        }

        /// <summary>
        /// True si le joueur est un "follower" (suit le tank).
        /// En contexte donjon DungeonBuddy: !IsTank() = follower.
        /// Référence HB 4.3.4 ScriptHelpers.cs L1395
        /// </summary>
        public static bool IsFollower(this LocalPlayer me)
        {
            return !me.IsTank();
        }

        /// <summary>
        /// True si le joueur est un DPS/healer ranged.
        /// Référence HB 4.3.4 ScriptHelpers.cs L1501
        /// </summary>
        public static bool IsRange(this LocalPlayer me)
        {
            return !me.IsMelee();
        }

        /// <summary>
        /// True si le joueur est melee (warrior, rogue, DK, feral druid, ret pally, enh shaman).
        /// Référence HB 4.3.4 ScriptHelpers.cs L1487
        /// </summary>
        public static bool IsMelee(this LocalPlayer me)
        {
            return me.Class switch
            {
                WoWClass.Warrior => true,
                WoWClass.Rogue => true,
                WoWClass.DeathKnight => true,
                WoWClass.Paladin => !SpellManager.HasSpell("Holy Shock"),
                WoWClass.Shaman => SpellManager.HasSpell("Lava Lash"),
                WoWClass.Druid => SpellManager.HasSpell("Mangle"),
                _ => false
            };
        }
    }
}
```

### 2. CRITIQUE: Namespace des scripts = Bots.DungeonBuddy.Profiles

Les 32 scripts existants font `using Bots.DungeonBuddy.Profiles;` pour importer la classe `Dungeon`.
La classe `Dungeon.cs` doit donc être dans le namespace `Bots.DungeonBuddy.Profiles` (déjà corrigé dans ce plan).

### 3. À VÉRIFIER: LFG Lua API in-game

Tester ces commandes Lua **en jeu** avant de coder LfgManager:
```lua
-- Test 1: GetLFGMode() — API PRINCIPALE pour l'état LFG
-- En WotLK 3.3, GetLFGMode() ne prend AUCUN argument (pas de category).
/run print("mode:", GetLFGMode())       -- nil si pas en LFG, "queued"/"proposal"/etc.

-- Test 2: Vérifier que les APIs de queue existent
/run print(type(SetLFGDungeon))        -- "function"
/run print(type(ClearAllLFGDungeons))  -- "function"
/run print(type(JoinLFG))              -- "function"
/run print(type(AcceptProposal))       -- "function"

-- Test 3: Vérifier les IDs random
/run ClearAllLFGDungeons(1); SetLFGDungeon(1, 261); JoinLFG(1)  -- queue random normal
/run ClearAllLFGDungeons(1); SetLFGDungeon(1, 262); JoinLFG(1)  -- queue random heroic

-- Test 4: Vérifier GetLFGMode() en queue
/run print("mode:", GetLFGMode())  -- doit afficher "queued" après queue

-- Test 5: Vérifier AcceptProposal via injection
-- (RESTRICTED: ne fonctionne pas en /run normal, mais doit marcher via Lua.DoString)

-- Test 6: Les events
/run print(type(LFDRoleCheckPopupAcceptButton))  -- "table" si le frame existe
```

### 4. NON-BLOQUANT: WoWGroupInfo.RaidMembers

`ScriptHelpers.cs` utilise `StyxWoW.Me.GroupInfo.RaidMembers` — vérifié, cette API existe dans CopilotBuddy.
`WoWPartyMember.ToPlayer()` existe aussi. ✅

### 5. NON-BLOQUANT: WoWGameObject.CanUse()

Utilisé dans `ScriptHelpers.CreateInteractWithObject()` — vérifié, `CanUse()` existe dans CopilotBuddy:
`public bool CanUse() => !Locked && !InUse;` ✅
