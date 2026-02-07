# 🌿 PLAN DE DÉVELOPPEMENT - GatherBuddy pour WotLK 3.3.5a

## 📋 RÉSUMÉ

**Objectif:** Créer un bot BotBase pour la récolte d'herbes et minerais dans WoW 3.3.5a.

**Complexité:** 🟢 FAIBLE - L'API existe déjà dans CopilotBuddy  
**Temps estimé:** 1-2 semaines  
**Dépendances existantes:** Toutes présentes dans CopilotBuddy

---

## ✅ API DÉJÀ DISPONIBLES DANS COPILOTBUDDY

Ces éléments sont **DÉJÀ IMPLÉMENTÉS** et fonctionnels:

### WoWGameObject.cs (Styx/WoWInternals/WoWObjects/)
```csharp
// Lignes 169-170 - Détection type de node
public bool IsHerb => LockType == WoWLockType.Herbalism;
public bool IsMineral => Entry != 185877U && LockType == WoWLockType.Mining;

// Lignes 249-252 - Disponibilité pour récolte
public bool CanMine => IsMineral && State == WoWGameObjectState.Ready;
public bool CanHarvest => IsHerb && State == WoWGameObjectState.Ready;

// Lignes 196-232 - Vérification skill suffisant
public bool CanLoot { get; } // Vérifie Herbalism/Mining skill vs RequiredSkill
```

### ObjectManager.cs (Styx/WoWInternals/)
```csharp
ObjectManager.GetObjectsOfType<WoWGameObject>() // Liste tous les GameObjects
ObjectManager.Me // Joueur local
```

### Navigator.cs (Tripper/)
```csharp
Navigator.MoveTo(WoWPoint) // Pathfinding
Navigator.CanNavigateFully(from, to) // Vérification chemin
Navigator.Clear() // Reset navigation
```

### WoWPoint (Styx/Logic/Pathing/)
```csharp
WoWPoint.Distance(other) // Distance entre points
WoWPoint.DistanceSqr(other) // Distance au carré (plus rapide)
```

### BotBase.cs (Styx/)
```csharp
public abstract class BotBase
{
    public abstract string Name { get; }
    public abstract Composite Root { get; }
    public abstract PulseFlags PulseFlags { get; }
    public virtual void Start() { }
    public virtual void Stop() { }
    public virtual void Pulse() { }
}
```

### TreeSharp/ (Behavior Tree)
- `PrioritySelector` - Exécute premier enfant qui réussit
- `Sequence` - Exécute tous les enfants en ordre
- `Decorator` - Condition + action
- `Action` - Action simple

---

## 📁 STRUCTURE DE FICHIERS À CRÉER

```
CopilotBuddy/
└── Bots/
    └── GatherBuddy/
        ├── GatherBuddy.cs              # BotBase principal
        ├── GatherBuddySettings.cs      # Settings persistants
        ├── GatherProfile.cs            # Chargement profils XML waypoints
        ├── NodeTracker.cs              # Tracking nodes récoltés/blacklistés
        └── Enums/
            └── PathType.cs             # Circle, Bounce
```

**Total: 5 fichiers à créer**

---

## 📝 SPÉCIFICATIONS DÉTAILLÉES

### 1. Bots/GatherBuddy/Enums/PathType.cs

```csharp
namespace Bots.GatherBuddy.Enums
{
    /// <summary>
    /// Type de parcours des waypoints
    /// </summary>
    public enum PathType
    {
        /// <summary>
        /// Parcours circulaire: 1→2→3→1→2→3→...
        /// </summary>
        Circle,
        
        /// <summary>
        /// Parcours aller-retour: 1→2→3→2→1→2→...
        /// </summary>
        Bounce
    }
}
```

---

### 2. Bots/GatherBuddy/GatherBuddySettings.cs

```csharp
using System;
using System.IO;
using Styx;
using Styx.Helpers;

namespace Bots.GatherBuddy
{
    /// <summary>
    /// Settings persistants pour GatherBuddy.
    /// Sauvegardés dans Settings/GatherBuddySettings_{CharacterName}.xml
    /// </summary>
    public class GatherBuddySettings : Settings
    {
        private static GatherBuddySettings _instance;
        
        public static GatherBuddySettings Instance => 
            _instance ?? (_instance = new GatherBuddySettings());

        public GatherBuddySettings()
            : base(Path.Combine(
                Logging.ApplicationPath, 
                $"Settings\\GatherBuddySettings_{StyxWoW.Me?.Name ?? "Unknown"}.xml"))
        {
            Load();
        }

        // ═══════════════════════════════════════════════════════════
        // RÉCOLTE
        // ═══════════════════════════════════════════════════════════
        
        [Setting, DefaultValue(true)]
        public bool GatherHerbs { get; set; }

        [Setting, DefaultValue(true)]
        public bool GatherMinerals { get; set; }

        // ═══════════════════════════════════════════════════════════
        // NAVIGATION
        // ═══════════════════════════════════════════════════════════
        
        [Setting, DefaultValue(PathType.Circle)]
        public PathType PathingType { get; set; }
        
        /// <summary>
        /// Distance max pour détecter un node (yards)
        /// </summary>
        [Setting, DefaultValue(70f)]
        public float NodeDetectionRange { get; set; }
        
        /// <summary>
        /// Modificateur de hauteur pour vol (yards au-dessus du sol)
        /// </summary>
        [Setting, DefaultValue(0f)]
        public float HeightModifier { get; set; }

        // ═══════════════════════════════════════════════════════════
        // COMBAT
        // ═══════════════════════════════════════════════════════════
        
        /// <summary>
        /// Looter les mobs tués pendant la récolte
        /// </summary>
        [Setting, DefaultValue(false)]
        public bool LootMobs { get; set; }
        
        /// <summary>
        /// Ignorer les mobs Elite (ne pas les pull)
        /// </summary>
        [Setting, DefaultValue(true)]
        public bool IgnoreElites { get; set; }

        /// <summary>
        /// Se tourner face aux nodes avant d'interagir
        /// </summary>
        [Setting, DefaultValue(true)]
        public bool FaceNodes { get; set; }

        // ═══════════════════════════════════════════════════════════
        // ANTI-NINJA
        // ═══════════════════════════════════════════════════════════
        
        /// <summary>
        /// Ne pas voler les nodes que d'autres joueurs récoltent
        /// </summary>
        [Setting, DefaultValue(true)]
        public bool NoNinja { get; set; }
        
        /// <summary>
        /// Temps de blacklist pour un node après échec (secondes)
        /// </summary>
        [Setting, DefaultValue(20)]
        public int BlacklistTimer { get; set; }

        // ═══════════════════════════════════════════════════════════
        // VENDOR/MAIL (Optionnel - Phase 2)
        // ═══════════════════════════════════════════════════════════
        
        [Setting, DefaultValue(false)]
        public bool MailToAlt { get; set; }
        
        /// <summary>
        /// Nom du personnage destinataire du mail
        /// </summary>
        [Setting, DefaultValue("")]
        public string MailRecipient { get; set; }
    }
}
```

---

### 3. Bots/GatherBuddy/NodeTracker.cs

```csharp
using System;
using System.Collections.Generic;
using Styx.Logic.Pathing;
using Styx.WoWInternals.WoWObjects;

namespace Bots.GatherBuddy
{
    /// <summary>
    /// Gère le tracking des nodes récoltés et blacklistés.
    /// Évite de revenir sur un node déjà récolté avant son respawn.
    /// </summary>
    public static class NodeTracker
    {
        // Nodes blacklistés temporairement (guid → expiration)
        private static readonly Dictionary<ulong, DateTime> _blacklistedNodes = new();
        
        // Nodes récemment récoltés (position approximative → expiration)
        // Note: On utilise position car le GUID change après respawn
        private static readonly Dictionary<string, DateTime> _harvestedPositions = new();
        
        // Temps de respawn estimé des nodes (5-10 minutes en WotLK)
        private static readonly TimeSpan RespawnTime = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Blackliste un node pour un temps donné (échec de récolte, ninja, etc.)
        /// </summary>
        public static void Blacklist(WoWGameObject node, TimeSpan duration)
        {
            if (node == null) return;
            _blacklistedNodes[node.Guid] = DateTime.Now + duration;
        }

        /// <summary>
        /// Blackliste un node avec le timer par défaut des settings
        /// </summary>
        public static void Blacklist(WoWGameObject node)
        {
            Blacklist(node, TimeSpan.FromSeconds(GatherBuddySettings.Instance.BlacklistTimer));
        }

        /// <summary>
        /// Marque un node comme récolté (évite de revenir avant respawn)
        /// </summary>
        public static void MarkHarvested(WoWGameObject node)
        {
            if (node == null) return;
            
            // Clé basée sur position arrondie (les nodes respawn au même endroit)
            string posKey = GetPositionKey(node.Location);
            _harvestedPositions[posKey] = DateTime.Now + RespawnTime;
        }

        /// <summary>
        /// Vérifie si un node est valide pour la récolte
        /// </summary>
        public static bool IsNodeValid(WoWGameObject node)
        {
            if (node == null) return false;
            
            // Vérifie blacklist par GUID
            if (_blacklistedNodes.TryGetValue(node.Guid, out var expiry) && DateTime.Now < expiry)
                return false;
            
            // Vérifie si position récemment récoltée
            string posKey = GetPositionKey(node.Location);
            if (_harvestedPositions.TryGetValue(posKey, out var harvestExpiry) && DateTime.Now < harvestExpiry)
                return false;
            
            return true;
        }

        /// <summary>
        /// Nettoie les entrées expirées (appeler périodiquement)
        /// </summary>
        public static void CleanupExpired()
        {
            var now = DateTime.Now;
            
            // Nettoyer blacklist
            var expiredGuids = new List<ulong>();
            foreach (var kvp in _blacklistedNodes)
            {
                if (now >= kvp.Value)
                    expiredGuids.Add(kvp.Key);
            }
            foreach (var guid in expiredGuids)
                _blacklistedNodes.Remove(guid);
            
            // Nettoyer positions récoltées
            var expiredPositions = new List<string>();
            foreach (var kvp in _harvestedPositions)
            {
                if (now >= kvp.Value)
                    expiredPositions.Add(kvp.Key);
            }
            foreach (var pos in expiredPositions)
                _harvestedPositions.Remove(pos);
        }

        private static string GetPositionKey(WoWPoint pos)
        {
            // Arrondi à 5 yards pour grouper les variations de position
            int x = (int)(pos.X / 5) * 5;
            int y = (int)(pos.Y / 5) * 5;
            int z = (int)(pos.Z / 5) * 5;
            return $"{x},{y},{z}";
        }

        public static void Reset()
        {
            _blacklistedNodes.Clear();
            _harvestedPositions.Clear();
        }
    }
}
```

---

### 4. Bots/GatherBuddy/GatherProfile.cs

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Bots.GatherBuddy.Enums;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Bots.GatherBuddy
{
    /// <summary>
    /// Gère le chargement et parcours des profils de waypoints.
    /// Format XML simple compatible avec les anciens profils GB.
    /// </summary>
    public class GatherProfile
    {
        private readonly List<WoWPoint> _waypoints = new();
        private int _currentIndex;
        private bool _movingForward = true; // Pour PathType.Bounce
        
        public string Name { get; private set; }
        public PathType PathType { get; private set; }
        public int WaypointCount => _waypoints.Count;
        public bool HasWaypoints => _waypoints.Count > 0;
        
        /// <summary>
        /// Waypoint actuel vers lequel se diriger
        /// </summary>
        public WoWPoint CurrentWaypoint => 
            _waypoints.Count > 0 ? _waypoints[_currentIndex] : WoWPoint.Empty;

        /// <summary>
        /// Charge un profil depuis un fichier XML
        /// </summary>
        /// <param name="filePath">Chemin du fichier XML</param>
        public bool Load(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Logging.Write($"[GatherBuddy] Profile not found: {filePath}");
                    return false;
                }

                var doc = XDocument.Load(filePath);
                var root = doc.Root;
                
                if (root == null)
                {
                    Logging.Write("[GatherBuddy] Invalid profile XML");
                    return false;
                }

                // Nom du profil
                Name = root.Attribute("Name")?.Value ?? Path.GetFileNameWithoutExtension(filePath);
                
                // Type de parcours
                var pathTypeStr = root.Attribute("PathType")?.Value ?? "Circle";
                PathType = Enum.TryParse<PathType>(pathTypeStr, out var pt) ? pt : PathType.Circle;

                // Charger les waypoints
                _waypoints.Clear();
                var hotspotsElement = root.Element("Hotspots") ?? root.Element("Waypoints");
                
                if (hotspotsElement != null)
                {
                    foreach (var hotspot in hotspotsElement.Elements("Hotspot"))
                    {
                        if (TryParseWaypoint(hotspot, out var point))
                            _waypoints.Add(point);
                    }
                }

                // Support ancien format (waypoints directement sous root)
                foreach (var hotspot in root.Elements("Hotspot"))
                {
                    if (TryParseWaypoint(hotspot, out var point))
                        _waypoints.Add(point);
                }

                _currentIndex = 0;
                Logging.Write($"[GatherBuddy] Loaded profile '{Name}' with {_waypoints.Count} waypoints");
                return _waypoints.Count > 0;
            }
            catch (Exception ex)
            {
                Logging.Write($"[GatherBuddy] Error loading profile: {ex.Message}");
                return false;
            }
        }

        private bool TryParseWaypoint(XElement element, out WoWPoint point)
        {
            point = WoWPoint.Empty;
            
            var xAttr = element.Attribute("X")?.Value;
            var yAttr = element.Attribute("Y")?.Value;
            var zAttr = element.Attribute("Z")?.Value;
            
            if (string.IsNullOrEmpty(xAttr) || string.IsNullOrEmpty(yAttr) || string.IsNullOrEmpty(zAttr))
                return false;

            // Support locale-agnostic (virgule ou point)
            if (float.TryParse(xAttr.Replace(',', '.'), 
                    System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(yAttr.Replace(',', '.'), 
                    System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out var y) &&
                float.TryParse(zAttr.Replace(',', '.'), 
                    System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out var z))
            {
                point = new WoWPoint(x, y, z);
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Avance au prochain waypoint
        /// </summary>
        public void MoveToNextWaypoint()
        {
            if (_waypoints.Count == 0) return;
            
            switch (PathType)
            {
                case PathType.Circle:
                    _currentIndex = (_currentIndex + 1) % _waypoints.Count;
                    break;
                    
                case PathType.Bounce:
                    if (_movingForward)
                    {
                        _currentIndex++;
                        if (_currentIndex >= _waypoints.Count - 1)
                        {
                            _currentIndex = _waypoints.Count - 1;
                            _movingForward = false;
                        }
                    }
                    else
                    {
                        _currentIndex--;
                        if (_currentIndex <= 0)
                        {
                            _currentIndex = 0;
                            _movingForward = true;
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Trouve le waypoint le plus proche et s'y positionne
        /// </summary>
        public void CycleToNearest(WoWPoint fromLocation)
        {
            if (_waypoints.Count == 0) return;
            
            int nearestIndex = 0;
            float nearestDistSqr = float.MaxValue;
            
            for (int i = 0; i < _waypoints.Count; i++)
            {
                float distSqr = fromLocation.DistanceSqr(_waypoints[i]);
                if (distSqr < nearestDistSqr)
                {
                    nearestDistSqr = distSqr;
                    nearestIndex = i;
                }
            }
            
            _currentIndex = nearestIndex;
        }

        /// <summary>
        /// Reset au premier waypoint
        /// </summary>
        public void Reset()
        {
            _currentIndex = 0;
            _movingForward = true;
        }
    }
}
```

---

### 5. Bots/GatherBuddy/GatherBuddy.cs (FICHIER PRINCIPAL)

```csharp
using System;
using System.Diagnostics;
using System.Linq;
using CommonBehaviors.Actions;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Common;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Inventory.Frames.LootFrame;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Bots.GatherBuddy
{
    /// <summary>
    /// GatherBuddy - Bot de récolte pour WoW 3.3.5a (WotLK)
    /// Récolte herbes et minerais le long d'un parcours de waypoints.
    /// </summary>
    public class GatherBuddy : BotBase
    {
        // ═══════════════════════════════════════════════════════════
        // BOTBASE IMPLEMENTATION
        // ═══════════════════════════════════════════════════════════
        
        public override string Name => "GatherBuddy";
        public override bool IsPrimaryType => true;
        public override bool RequiresProfile => true;
        public override bool RequirementsMet => true;
        public override PulseFlags PulseFlags => PulseFlags.All;

        private PrioritySelector _root;
        private GatherProfile _profile;
        private WoWGameObject _currentNode;
        private readonly Stopwatch _cleanupTimer = new();
        private static CombatRoutine Routine => RoutineManager.Current;

        public override Composite Root => _root ??= CreateRootBehavior();

        // ═══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════

        public override void Start()
        {
            Logging.Write("[GatherBuddy] Starting...");
            
            // Charger le profil (TODO: GUI pour sélection)
            _profile = new GatherProfile();
            var profilePath = GetDefaultProfilePath();
            
            if (!string.IsNullOrEmpty(profilePath) && _profile.Load(profilePath))
            {
                _profile.CycleToNearest(StyxWoW.Me.Location);
                Logging.Write($"[GatherBuddy] Profile loaded: {_profile.Name}");
            }
            else
            {
                throw new Exception("[GatherBuddy] No valid profile loaded. Please load a gather profile.");
            }

            // Filtres de targeting
            Targeting.Instance.IncludeTargetsFilter += IncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter += IncludeLootFilter;
            
            _cleanupTimer.Start();
            Logging.Write("[GatherBuddy] Started successfully!");
        }

        public override void Stop()
        {
            Logging.Write("[GatherBuddy] Stopping...");
            
            Targeting.Instance.IncludeTargetsFilter -= IncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter -= IncludeLootFilter;
            
            NodeTracker.Reset();
            _cleanupTimer.Stop();
        }

        public override void Pulse()
        {
            // Cleanup périodique des nodes expirés
            if (_cleanupTimer.ElapsedMilliseconds > 30000)
            {
                NodeTracker.CleanupExpired();
                _cleanupTimer.Restart();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // BEHAVIOR TREE
        // ═══════════════════════════════════════════════════════════

        private PrioritySelector CreateRootBehavior()
        {
            return new PrioritySelector(
                // 1. Gestion de la mort (RÉUTILISE LevelBot)
                CreateDeathBehavior(),
                
                // 2. Combat (si aggro)
                CreateCombatBehavior(),
                
                // 3. Loot (si LootMobs activé)
                new Decorator(
                    ctx => GatherBuddySettings.Instance.LootMobs,
                    CreateLootBehavior()
                ),
                
                // 4. Récolte de nodes
                CreateGatherBehavior(),
                
                // 5. Movement vers prochain waypoint
                CreateMovementBehavior(),
                
                // 6. Idle
                new ActionIdle()
            );
        }

        // ═══════════════════════════════════════════════════════════
        // DEATH BEHAVIOR (simplifié de LevelBot)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateDeathBehavior()
        {
            return new PrioritySelector(
                // Mort - release
                new Decorator(
                    ctx => StyxWoW.Me.IsDead,
                    new Sequence(
                        new Action(ctx => 
                        {
                            Logging.Write("[GatherBuddy] Died! Releasing...");
                            Lua.DoString("RepopMe()");
                        }),
                        new WaitContinue(5, ctx => StyxWoW.Me.IsGhost, new ActionAlwaysSucceed())
                    )
                ),
                // Ghost - retour au corps
                new Decorator(
                    ctx => StyxWoW.Me.IsGhost,
                    new Sequence(
                        new Action(ctx => Navigator.MoveTo(StyxWoW.Me.CorpsePoint)),
                        new Decorator(
                            ctx => StyxWoW.Me.Location.Distance(StyxWoW.Me.CorpsePoint) < 40,
                            new Action(ctx => Lua.DoString("RetrieveCorpse()"))
                        )
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // COMBAT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateCombatBehavior()
        {
            return new PrioritySelector(
                // Rest si pas en combat
                new Decorator(
                    ctx => !StyxWoW.Me.Combat && Routine?.RestBehavior != null,
                    Routine.RestBehavior
                ),
                // Combat si en combat
                new Decorator(
                    ctx => StyxWoW.Me.Combat && Targeting.Instance.FirstUnit != null,
                    new PrioritySelector(
                        // Dismount
                        new Decorator(
                            ctx => StyxWoW.Me.Mounted,
                            new Action(ctx => Mount.Dismount("Combat"))
                        ),
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
                ctx => !StyxWoW.Me.Combat && 
                       LootTargeting.Instance.FirstObject != null &&
                       LootTargeting.Instance.FirstObject.DistanceSqr < 30*30,
                new Sequence(
                    new Action(ctx => Navigator.MoveTo(LootTargeting.Instance.FirstObject.Location)),
                    new Decorator(
                        ctx => LootTargeting.Instance.FirstObject.DistanceSqr < 5*5,
                        new Action(ctx =>
                        {
                            LootTargeting.Instance.FirstObject.Interact();
                            // Attendre loot frame
                        })
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // GATHER BEHAVIOR (CŒUR DU BOT)
        // ═══════════════════════════════════════════════════════════

        private Composite CreateGatherBehavior()
        {
            return new PrioritySelector(
                ctx =>
                {
                    // Trouver le meilleur node
                    _currentNode = FindBestNode();
                    return ctx;
                },
                // Pas de node trouvé
                new Decorator(
                    ctx => _currentNode == null,
                    new ActionAlwaysFail()
                ),
                // Node trouvé - se déplacer et récolter
                new Sequence(
                    // Log
                    new Action(ctx =>
                    {
                        Logging.WriteDiagnostic($"[GatherBuddy] Found {_currentNode.Name} at {_currentNode.Distance:F0}y");
                        return RunStatus.Success;
                    }),
                    // Dismount si nécessaire et proche
                    new DecoratorContinue(
                        ctx => StyxWoW.Me.Mounted && _currentNode.DistanceSqr < 10*10,
                        new Action(ctx => Mount.Dismount("Gathering"))
                    ),
                    // Se déplacer vers le node
                    new Decorator(
                        ctx => _currentNode.DistanceSqr > 5*5,
                        new Action(ctx => 
                        {
                            Navigator.MoveTo(_currentNode.Location);
                            return RunStatus.Running;
                        })
                    ),
                    // Interagir avec le node
                    new Decorator(
                        ctx => _currentNode.DistanceSqr <= 5*5 && !StyxWoW.Me.IsCasting,
                        new Sequence(
                            new Action(ctx =>
                            {
                                // Stop movement
                                WoWMovement.MoveStop();
                                
                                // Face node (optionnel)
                                // NOTE: Face() est sur WoWUnit, pas WoWGameObject.
                                // Utiliser WoWMovement.Face(WoWPoint) à la place.
                                if (GatherBuddySettings.Instance.FaceNodes)
                                    WoWMovement.Face(_currentNode.Location);
                                
                                // Interact
                                _currentNode.Interact();
                                Logging.Write($"[GatherBuddy] Gathering {_currentNode.Name}");
                                return RunStatus.Success;
                            }),
                            // Attendre la fin du cast
                            new WaitContinue(
                                TimeSpan.FromSeconds(5),
                                ctx => !StyxWoW.Me.IsCasting,
                                new Sequence(
                                    // Marquer comme récolté
                                    new Action(ctx =>
                                    {
                                        NodeTracker.MarkHarvested(_currentNode);
                                        _currentNode = null;
                                        return RunStatus.Success;
                                    })
                                )
                            )
                        )
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // MOVEMENT BEHAVIOR
        // ═══════════════════════════════════════════════════════════

        private Composite CreateMovementBehavior()
        {
            return new PrioritySelector(
                // Pas de profil chargé
                new Decorator(
                    ctx => _profile == null || !_profile.HasWaypoints,
                    new Action(ctx =>
                    {
                        Logging.Write("[GatherBuddy] No waypoints! Load a profile.");
                        return RunStatus.Failure;
                    })
                ),
                // Se déplacer vers le waypoint actuel
                new Decorator(
                    ctx => _profile.CurrentWaypoint != WoWPoint.Empty,
                    new PrioritySelector(
                        // Arrivé au waypoint - passer au suivant
                        new Decorator(
                            ctx => StyxWoW.Me.Location.DistanceSqr(_profile.CurrentWaypoint) < 15*15,
                            new Action(ctx =>
                            {
                                _profile.MoveToNextWaypoint();
                                return RunStatus.Success;
                            })
                        ),
                        // Se déplacer
                        new Action(ctx =>
                        {
                            // Mount si possible et loin
                            if (!StyxWoW.Me.Mounted && 
                                Mount.CanMount() && 
                                StyxWoW.Me.Location.DistanceSqr(_profile.CurrentWaypoint) > 50*50)
                            {
                                Mount.MountUp();
                                return RunStatus.Running;
                            }
                            
                            Navigator.MoveTo(_profile.CurrentWaypoint);
                            return RunStatus.Running;
                        })
                    )
                )
            );
        }

        // ═══════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Trouve le meilleur node à récolter
        /// </summary>
        private WoWGameObject FindBestNode()
        {
            var settings = GatherBuddySettings.Instance;
            float maxRange = settings.NodeDetectionRange;
            float maxRangeSqr = maxRange * maxRange;

            return ObjectManager.GetObjectsOfType<WoWGameObject>()
                .Where(obj =>
                {
                    // Distance
                    if (obj.DistanceSqr > maxRangeSqr)
                        return false;
                    
                    // Type de node
                    bool isValidType = 
                        (settings.GatherHerbs && obj.IsHerb && obj.CanHarvest) ||
                        (settings.GatherMinerals && obj.IsMineral && obj.CanMine);
                    
                    if (!isValidType)
                        return false;
                    
                    // Pas blacklisté
                    if (!NodeTracker.IsNodeValid(obj))
                        return false;
                    
                    // Anti-ninja: vérifier si un autre joueur est proche et le target
                    if (settings.NoNinja)
                    {
                        var nearbyPlayers = ObjectManager.GetObjectsOfType<WoWPlayer>()
                            .Where(p => !p.IsMe && p.IsAlive && p.Location.DistanceSqr(obj.Location) < 15*15);
                        
                        if (nearbyPlayers.Any())
                            return false;
                    }
                    
                    // Navigation possible
                    // NOTE PERFORMANCE: CanNavigateFully() appelle le pathfinding DLL
                    // pour chaque node candidat. Avec beaucoup de nodes en zone ouverte,
                    // cela peut causer des ralentissements.
                    // Alternative: retirer cette vérification ici et blacklister le node
                    // si la navigation échoue pendant le déplacement (pattern HB 4.3.4).
                    // TODO: mesurer la performance in-game et décider.
                    if (!Navigator.CanNavigateFully(StyxWoW.Me.Location, obj.Location))
                        return false;
                    
                    return true;
                })
                .OrderBy(obj => obj.DistanceSqr)
                .FirstOrDefault();
        }

        /// <summary>
        /// Obtient le chemin du profil par défaut
        /// </summary>
        private string GetDefaultProfilePath()
        {
            // TODO: Implémenter sélection de profil via GUI
            // Pour l'instant, chercher un fichier .xml dans Profiles/GatherBuddy/
            var profileDir = System.IO.Path.Combine(Logging.ApplicationPath, "Profiles", "GatherBuddy");
            
            if (System.IO.Directory.Exists(profileDir))
            {
                var files = System.IO.Directory.GetFiles(profileDir, "*.xml");
                if (files.Length > 0)
                    return files[0];
            }
            
            return null;
        }

        // ═══════════════════════════════════════════════════════════
        // TARGETING FILTERS
        // ═══════════════════════════════════════════════════════════

        private void IncludeTargetsFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
        {
            // Ignorer les elites si configuré
            if (GatherBuddySettings.Instance.IgnoreElites)
            {
                incoming.RemoveAll(obj =>
                {
                    var unit = obj as WoWUnit;
                    return unit != null && unit.Elite;
                });
            }
        }

        private void IncludeLootFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
        {
            // Ajouter les mobs lootables si LootMobs activé
            if (GatherBuddySettings.Instance.LootMobs)
            {
                foreach (var obj in incoming)
                {
                    var unit = obj as WoWUnit;
                    if (unit != null && unit.IsDead && unit.CanLoot)
                        outgoing.Add(obj);
                }
            }
        }
    }
}
```

---

## 📄 FORMAT DE PROFIL XML

Créer dans `Profiles/GatherBuddy/`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<GatherProfile Name="Sholazar Basin Herbs" PathType="Circle">
    <Hotspots>
        <Hotspot X="5784.25" Y="4768.32" Z="-72.45" />
        <Hotspot X="5650.12" Y="4890.67" Z="-68.90" />
        <Hotspot X="5520.89" Y="5012.34" Z="-65.23" />
        <!-- ... plus de waypoints ... -->
    </Hotspots>
</GatherProfile>
```

---

## 🧪 TESTS À EFFECTUER

1. **Test de détection de nodes:**
   - Vérifier `IsHerb` / `IsMineral` sur différents nodes
   - Vérifier `CanHarvest` / `CanMine`

2. **Test de navigation:**
   - Parcours circulaire
   - Parcours bounce
   - Détour vers node puis retour au parcours

3. **Test de combat:**
   - Aggro pendant récolte
   - Combat puis reprise de récolte

4. **Test anti-ninja:**
   - Présence d'autre joueur près du node

5. **Test de blacklist:**
   - Échec de récolte → blacklist temporaire
   - Respawn de node → disponible à nouveau

---

## 🚀 ORDRE D'IMPLÉMENTATION

1. ✅ `Enums/PathType.cs` (5 min)
2. ✅ `GatherBuddySettings.cs` (30 min)
3. ✅ `NodeTracker.cs` (30 min)
4. ✅ `GatherProfile.cs` (1h)
5. ✅ `GatherBuddy.cs` (2-3h)
6. 📁 Créer profils exemple WotLK
7. 🧪 Tests et ajustements

---

## 🔧 PRÉREQUIS — MODIFICATIONS COPILOTBUDDY NÉCESSAIRES

Avant d'implémenter GatherBuddy, vérifier/corriger ces points dans le code existant:

### CRITIQUE: WoWGameObject.IsHerb / IsMineral

⚠️ Ces propriétés dépendent de `LockType` qui dépend de `LockRecord`.
Si `LockRecord` retourne `null` (DBC lookup non implémenté), alors `IsHerb` et `IsMineral`
retournent toujours `false` et GatherBuddy ne détectera aucun node.

**Fichier:** `Styx/WoWInternals/WoWObjects/WoWGameObject.cs`

**Test à faire AVANT de coder GatherBuddy:**
```csharp
// En jeu, cibler une herbe et exécuter:
var obj = StyxWoW.Me.CurrentTarget?.ToGameObject();
Logging.Write($"LockType={obj?.LockType}, IsHerb={obj?.IsHerb}, CanHarvest={obj?.CanHarvest}");
```

**Si LockType retourne None** pour une herbe connue, il faut soit:
1. Implémenter le lookup DBC Lock.dbc (meilleur long terme)
2. Ajouter un fallback par Entry IDs WotLK (plus rapide):

```csharp
// Fallback pragmatique dans WoWGameObject.cs si LockType ne fonctionne pas
private static readonly HashSet<uint> _wotlkHerbEntries = new()
{
    // Vanilla: Peacebloom(1618), Silverleaf(1617), Earthroot(1619), etc.
    // TBC: Felweed(181270), Dreaming Glory(181271), etc.
    // WotLK:
    189973, // Goldclover
    190169, // Tiger Lily
    190170, // Talandra's Rose
    191019, // Adder's Tongue
    190171, // Lichbloom
    190172, // Icethorn
    191303, // Frost Lotus
    190176, // Frozen Herb
};

private static readonly HashSet<uint> _wotlkMineralEntries = new()
{
    189978, // Cobalt Deposit
    189979, // Rich Cobalt Deposit
    189980, // Saronite Deposit
    189981, // Rich Saronite Deposit
    191133, // Titanium Vein
};
```

### NON-BLOQUANT: Pas de IsDps property

`WoWPlayer` a `IsTank` et `IsHealer` comme properties mais pas `IsDps`.
GatherBuddy n'en a pas besoin, mais DungeonBuddy en aura besoin (voir PLAN_DUNGEONBUDDY.md).

---

## ⚠️ NOTES IMPORTANTES POUR L'IMPLÉMENTATION

1. **NE PAS** ajouter de zones/herbes/minerais de Cataclysm+
2. **RÉUTILISER** le code de LevelBot pour Death/Combat quand possible
3. **TESTER** avec les nodes WotLK: Lichbloom, Icethorn, Adder's Tongue, Titanium, Saronite
4. **Le vol existe** en WotLK (Cold Weather Flying niveau 77)
5. **Utilisez** `ObjectManager.GetObjectsOfType<WoWGameObject>()` - DÉJÀ FONCTIONNEL
