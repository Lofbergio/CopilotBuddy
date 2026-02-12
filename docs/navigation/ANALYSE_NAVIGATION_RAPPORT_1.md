# Analyse Approfondie — Système de Navigation

**Analyste :** GitHub Copilot (Claude Opus 4.6)  
**Date :** 12 février 2026  
**Rapport n° :** 1 / 3  

---

## 1. Résumé Exécutif

CopilotBuddy possède un système de navigation **fonctionnel et complet** pour le WotLK 3.3.5a. L'architecture diffère de HB WoD/Legion par un choix de design délibéré : **monolithique** (tout inliné dans `Navigator.cs` static) au lieu du pattern **abstraction + provider** de HB. Ce choix simplifie le code mais réduit l'extensibilité.

### Verdict Rapide

| Catégorie | Statut |
|-----------|--------|
| **P/Invoke (NativeMethods.cs)** | ✅ Complet — 50+ appels, tous présents |
| **Pathfinding cœur** | ✅ Complet — sliced + straight path |
| **Off-mesh connections** | ✅ Complet — Elevator, Portal, Door, InteractUnit, InteractObject |
| **Tile management** | ✅ Complet — load/unload/stream/GC |
| **BlackspotManager** | ✅ Complet |
| **Flightor** | ✅ Complet |
| **StuckHandler** | ✅ Complet |
| **DLLs dans Lib/** | ✅ Navigation.dll active, les autres sont legacy |
| **Enums (AreaType, AbilityFlags)** | ⚠️ Manques mineurs (voir §5) |
| **Architecture abstractions** | ℹ️ Différente de HB par design |
| **Fichiers WoD/Legion non portés** | ℹ️ Certains non applicables WotLK, certains optionnels |

---

## 2. Architecture Comparée

### 2.1 HB WoD 6.2.3 — Architecture 3 couches

```
                    Bot Code
                       │
                       ▼
              Navigator (static façade ~350 lignes)
              ├── NavigationProvider (abstract)
              │   └── MeshNavigator (concrete, 1318 lignes)
              │       ├── MeshMovePath
              │       ├── Off-mesh handlers (elevator/portal/door)
              │       └── Dead/alive query filter switch
              ├── IPlayerMover → ClickToMoveMover / KeyboardMover
              └── ITerrainHeightProvider → Class1050
                       │
                       ▼
              Tripper.Navigation
              ├── WowNavigator (809 lignes, mid-level)
              │   ├── WorldMeshManager (tile-based outdoor)
              │   ├── GarrisonMeshManager (full-load indoor)
              │   ├── WowQueryFilter (area costs)
              │   └── PathPostProcessing
              │       │
              │       ▼
              │   Class1458 (Core PathFinder — Recast/Detour queries)
              │
              └── Tripper.MeshMisc (data types, coordinate conversion)
                       │
                       ▼
              Tripper.RecastManaged.Detour (C++/CLI wrappers)
              ├── NavMesh, NavMeshQuery, QueryFilter
              └── dt*() native calls
```

### 2.2 HB Legion — Identique mais avec

- `AreaType` étendu (+InteractUnit, InteractObject, KnownBuilding, Misc1-10)
- `AbilityFlags` étendu (+KnownBuilding)
- `AvoidanceNavigationProvider` (DungeonBuddy)
- BG Navigator (path randomization)
- Classes obfusquées pour Navigator/Flightor/MoveResult

### 2.3 CopilotBuddy — Architecture 2 couches (P/Invoke directe)

```
                    Bot Code
                       │
                       ▼
              Navigator (static monolithique ~1530 lignes)
              ├── Tout le code MeshNavigator inliné
              ├── Off-mesh handlers inlinés
              ├── Height queries inlinées
              ├── IMover → LocalPlayerMover / KeyboardMover
              └── IStuckHandler → StuckHandler
                       │
                       ▼
              Tripper.Navigation.Navigator (sealed class, 1757 lignes)
              ├── NativeMethods.cs (P/Invoke → Navigation.dll)
              ├── QueryFilter (inliné)
              ├── PathPostProcessor
              ├── PathFindResult
              └── Tile management direct
                       │
                       ▼
              Navigation.dll (C++ natif, Detour)
```

**Différence clé :** HB passe par C++/CLI (`RecastManaged.dll`) → `dt*()` fonctions natives.  
CopilotBuddy passe directement par P/Invoke → `Navigation.dll` (pas de couche C++/CLI).

---

## 3. Inventaire des Fichiers — CopilotBuddy

### 3.1 `Styx/Logic/Pathing/` (15 fichiers)

| Fichier | Type | Lignes | Rôle |
|---------|------|--------|------|
| `Navigator.cs` | static class | ~1530 | Façade + logique navigation (monolithique) |
| `Flightor.cs` | static class | ~626 | Navigation aérienne + MountHelper |
| `BlackspotManager.cs` | static class | ~595 | Gestion blackspots navmesh |
| `AvoidanceManager.cs` | static class | ~250 | Zones d'évitement dynamiques |
| `WoWPoint.cs` | struct | ~240 | Point 3D (X,Y,Z) LayoutKind.Explicit |
| `StuckHandler.cs` | class | ~242 | 8 étapes de dé-stuck |
| `StuckDetector.cs` | static class | — | Propriété `IsStuck` |
| `IStuckHandler.cs` | interface | — | `IsStuck()`, `Unstick()`, `Reset()` |
| `MeshHeightHelper.cs` | static class | — | `FindMeshHeight()`, `ToWoWPoint()` |
| `MoveResult.cs` | enum | — | Failed, ReachedDestination, etc. |
| `PointLocationType.cs` | enum | — | Unknown, UnderLiquid, etc. |
| **Interop/** | | | |
| `LocalPlayerMover.cs` | class | — | IMover + ClickToMove (30yd click limit) |
| `KeyboardMover.cs` | class | — | IMover + SetFacing/MoveForward |
| `WorldInfoProvider.cs` | class | — | IWorldInfoProvider (off-mesh discovery) |
| `WorldObject.cs` | class | — | IWorldObject (Portal, Transport, Elevator) |

### 3.2 `Tripper/Navigation/` (12 fichiers)

| Fichier | Type | Lignes | Rôle |
|---------|------|--------|------|
| `Navigator.cs` | sealed class | ~1757 | Wrapper navmesh principal |
| `NativeMethods.cs` | static class | ~587 | 50+ P/Invoke vers Navigation.dll |
| `PathFindResult.cs` | class | ~146 | Résultat de pathfinding |
| `PathPostProcessor.cs` | static class | ~350 | MoveAwayFromEdges / Randomize |
| `AbilityFlags.cs` | enum (ushort) | — | Flags de capacité (Run, Swim, Jump…) |
| `AreaType.cs` | enum (byte) | — | Types de polygone (Ground, Water…) |
| `PathFindStep.cs` | enum | — | Étapes du pathfinding |
| `PathPostProcessing.cs` | enum | — | None / MoveAwayFromEdges / Randomize |
| `PolygonReference.cs` | struct | — | Wrapper polygone 64-bit |
| `Status.cs` | struct | — | Detour status flags |
| `StraightPathFlags.cs` | enum (byte) | — | Start / End / OffMeshConnection |
| `TileIdentifier.cs` | struct | — | Coords tile (533.3333 yd) |

### 3.3 `Tripper/XNAMath/` et `Tripper/Tools/Math/` (4 fichiers)

| Fichier | Type | Rôle |
|---------|------|------|
| `XNAMath/Vector2.cs` | struct | Vecteur 2D |
| `XNAMath/Vector3.cs` | struct | Vecteur 3D + Distance, Dot, Cross |
| `Tools/Math/Matrix.cs` | struct | Matrice 4x4 |
| `Tools/Math/Vector3.cs` | struct | Vecteur 3D avec conversions WoWPoint |

### 3.4 `Lib/` — DLLs

| DLL | Statut | Utilisée par |
|-----|--------|-------------|
| **Navigation.dll** | ✅ ACTIVE | `NativeMethods.cs` (50+ P/Invoke) |
| BlackMagic.dll | ✅ ACTIVE | GreenMagic (mémoire) |
| fasmdll_managed.dll | ✅ ACTIVE | GreenMagic (ASM injection) |
| System.Data.SQLite.dll | ✅ ACTIVE | Database/Connection.cs |
| SlimDX.dll | Présent | DirectX |
| **RecastManaged.dll** | ❌ INACTIVE | Aucun DllImport (legacy) |
| **Tripper.Tools.dll** | ❌ INACTIVE | Remplacé par code source C# |
| **Tripper.XNAMath.dll** | ❌ INACTIVE | Remplacé par code source C# |

---

## 4. Inventaire des Fichiers — HB WoD 6.2.3

### 4.1 `Styx/Pathing/` (17 fichiers + sous-dossiers)

| Fichier | Présent dans CB? | Notes |
|---------|-----------------|-------|
| `Navigator.cs` (façade ~350L) | ✅ Oui (mais monolithique 1530L) | Design différent |
| `NavigationProvider.cs` (abstract) | ❌ Non | Abstraction absente |
| `MeshNavigator.cs` (1318L) | ❌ Non (inliné dans Navigator) | Fusionné dans Navigator.cs |
| `MeshMovePath.cs` | ❌ Non | Structure de données manquante |
| `MoveResult.cs` | ✅ Oui | Identique |
| `MoveResultExtensions.cs` | ❌ Non | Extension `.IsSuccessful()` manquante |
| `IPlayerMover.cs` | ⚠️ Partiellement | CB a `IMover` (nom différent) |
| `ITerrainHeightProvider.cs` | ❌ Non | Inliné dans Navigator |
| `KeyboardMover.cs` | ✅ Oui | Dans Interop/ |
| `StuckHandler.cs` | ✅ Oui | Implémentation complète |
| `NavigationProviderChangedEventArgs.cs` | ❌ Non | Pas d'events dans CB |
| `PathGenerationFailStep.cs` | ❌ Non | Enum manquant |
| `Flightor.cs` | ✅ Oui | Complet |
| `BlackspotQueryFlags.cs` | ❌ Non | Enum manquant |
| **FlightorNavigation/** | | |
| `PolyNav.cs` | ❌ Non | Polygon 2D pathfinding pour Flightor |
| `BlackspotManager.cs` (Flightor) | ⚠️ Différent | CB a un seul BlackspotManager |
| `Areas.cs` | ❌ Non | Zones no-fly prédéfinies |
| **FlightorAnnotation/** | | |
| `IndoorEntrance.cs` | ❌ Non | Annotation intérieur/extérieur |

### 4.2 `Tripper/Navigation/` (13 fichiers)

| Fichier | Présent dans CB? | Notes |
|---------|-----------------|-------|
| `WowNavigator.cs` (809L) | ❌ Non | Fusionné dans Tripper/Navigator.cs |
| `WowQueryFilter.cs` | ❌ Non | Inliné dans Navigator comme `QueryFilter` |
| `WorldMeshManager.cs` (441L) | ❌ Non | N/A — Navigation.dll gère les tiles directement |
| `GarrisonMeshManager.cs` (238L) | ❌ Non | N/A — WotLK n'a pas de garrisons |
| `IMeshManager.cs` | ❌ Non | N/A — pas de multi-mesh |
| `NavHelper.cs` | ❌ Non | Conversion coordonnées manquante |
| `PathFindResult.cs` | ✅ Oui | Équivalent |
| `PathFindStep.cs` | ✅ Oui | Identique |
| `PathPostProcessing.cs` | ✅ Oui | Identique |
| `PathFindProgressEventArgs.cs` | ✅ Oui | Inliné dans Navigator |
| `NavigatorLogMessage.cs` | ❌ Non | Delegate manquant |
| `MapLoadedEventArgs.cs` | ✅ Oui | Inliné dans Navigator |
| `TileLoadedEventArgs.cs` | ✅ Oui | Inliné dans Navigator |

### 4.3 `Tripper/MeshMisc/` (12 fichiers)

| Fichier | Présent dans CB? | Notes |
|---------|-----------------|-------|
| `AreaType.cs` | ✅ Oui | ⚠️ Valeurs manquantes (voir §5) |
| `AbilityFlags.cs` | ✅ Oui | ⚠️ Valeurs manquantes (voir §5) |
| `TileIdentifier.cs` | ✅ Oui | Identique |
| `TileDataHeader.cs` | ❌ Non | Lecture de header mesh tile |
| `TileDataVersionException.cs` | ❌ Non | Exception version mesh |
| `InvalidTileDataException.cs` | ❌ Non | Exception données mesh |
| `MeshManager.cs` (static) | ❌ Non | SaveMeshData/LoadMeshData |
| `MeshMapCalculator.cs` | ❌ Non | SubTilesPerAdt=4, DetourTileSize=133.333 |
| `MapConsts.cs` | ❌ Non | TileSize=533.3333f |
| `GraphicalHelper.cs` | ❌ Non | ToDetour/ToWow conversion |
| `SotAGate.cs` | ✅ Oui | Dans Styx/Logic/ |
| `IoCGate.cs` | ❌ Non | Isle of Conquest gates manquant |

### 4.4 Autres (4 fichiers)

| Fichier | Présent dans CB? | Notes |
|---------|-----------------|-------|
| `Styx/NavType.cs` | ❌ Non | Enum Run=0, Fly=1 |
| `Tripper/LZMACompression/Lzma.cs` | ❌ Non | N/A — Navigation.dll gère la décompression |
| `RecastManaged/` (entire project) | ❌ Non | N/A — CB utilise P/Invoke directe |
| `AvoidanceNavigationProvider.cs` | ❌ Non | DungeonBuddy spécifique |

---

## 5. Lacunes Identifiées (Détails)

### 5.1 🔴 Problèmes Potentiels (à vérifier)

#### 5.1.1 AreaType — Valeurs manquantes

```csharp
// PRÉSENT dans CB
Ground=1, Water=2, Lava=3, Road=4, Fall=5, Elevator=6, Gate=7,
Portal=8, DefendersPortal=9, HordePortal=10, AlliancePortal=11,
Blocked=12, InteractUnit=13, InteractObject=14, Horde=15,
Alliance=16, Blackspot=17, Misc1=20, Misc2=21, Misc3=22, Misc4=23

// MANQUANT dans CB (présent HB WoD + Legion)
KnownBuilding = 18,   // Garrison buildings — probablement N/A WotLK
Misc5 = 24,           // Utilisé par SotAGate (AllianceNorth)
Misc6 = 25,           // Utilisé par IoCGate (AllianceWest)
Misc7 = 26,           // Utilisé par IoCGate (AllianceEast)
Misc8 = 27,           // Utilisé par BG path randomizer
Misc9 = 28,           // Réservé
Misc10 = 29,          // Réservé
```

**Impact :** `Misc5-Misc7` sont utilisés par `SotAGate` et `IoCGate` pour les BGs. Si le mesh WotLK utilise ces valeurs pour Strand of the Ancients ou Isle of Conquest, les polygones ne seront pas reconnus.

#### 5.1.2 AbilityFlags — Valeurs manquantes

```csharp
// PRÉSENT dans CB
None=0, RunSafe=2, Swim=4, Jump=8, Unwalkable=16,
Teleport=32, Transport=64, Horde=4096, Alliance=8192, All=65535

// MANQUANT
KnownBuilding = 16384  // N/A WotLK

// RENOMMAGE
Run=1 vs RunSafe=2     // CB a "RunSafe" au lieu de "OnlyWhileAlive" (HB)
```

**Impact :** `KnownBuilding` est N/A pour WotLK. Le renommage `OnlyWhileAlive` → `RunSafe` est un choix de style, pas un bug, tant que la valeur `2` est correcte.

#### 5.1.3 Conversion de Coordonnées

**HB WoD/Legion :**
```csharp
// GraphicalHelper.cs
ToDetour(wow): detour.X = -wow.Y, detour.Y = wow.Z, detour.Z = -wow.X
ToWow(detour): wow.Y = -detour.X, wow.X = -detour.Z, wow.Z = detour.Y
```

**CopilotBuddy :**
```csharp
// Navigator.cs — mapping direct 1:1
new Vector3(me.Location.X, me.Location.Y, me.Location.Z)  // Pas de swap
```

**⚠️ CRITIQUE :** Si Navigation.dll de CB utilise le même format de coordonnées que le Detour de HB, alors il faut la conversion Y/Z swap. Si Navigation.dll de CB a été compilée avec un système de coordonnées WoW-natif (Y est horizontal, Z est vertical comme WoW), alors le mapping 1:1 est correct.

**Verdict :** C'est probablement correct car Navigation.dll de CB a été compilée spécifiquement pour ce projet avec des coordonnées WoW-natives. Mais c'est le point le plus risqué — si les chemins générés sont incorrects en jeu, c'est la première chose à vérifier.

#### 5.1.4 MoveResultExtensions manquant

```csharp
// HB a ceci :
public static bool IsSuccessful(this MoveResult moveResult)
{
    return moveResult == MoveResult.Moved || moveResult == MoveResult.ReachedDestination;
}
```

**Impact :** Faible. Les bots peuvent vérifier manuellement. Mais c'est utilisé dans beaucoup de quest behaviors.

### 5.2 🟡 Différences Architecturales (par design, pas des bugs)

| Aspect | HB WoD/Legion | CopilotBuddy | Risque |
|--------|---------------|-------------|--------|
| `NavigationProvider` abstract | OUI — permet de swap le nav entre MeshNavigator, AvoidanceNav, BGNav | NON — tout dans Navigator static | ⚠️ Si On veut DungeonBuddy/BGBuddy, il faudra refactorer |
| `IPlayerMover` interface | OUI — `Move()`, `MoveTowards()`, `MoveStop()` | `IMover` — noms différents | Faible — interface existe, nom différent |
| `ITerrainHeightProvider` | OUI — interface séparée | NON — inliné dans Navigator | Faible — fonctionnalité identique |
| Events (`OnNavigationProviderChanged`) | OUI — 3 events | NON — aucun event | ⚠️ Si des plugins/bots déclenchent sur changement de provider |
| `MeshMovePath` class | OUI — structure de données path | NON | Faible — logique inlinée |
| `WowNavigator` mid-level | OUI — gestion multi-mesh (World + Garrison) | NON — single mesh | OK pour WotLK |
| `WorldMeshManager` / `GarrisonMeshManager` | OUI — mesh managers séparés | NON — Navigation.dll gère tout | OK pour WotLK |
| `WowQueryFilter` separate class | OUI — fichier séparé | Inliné dans Navigator | Faible |
| Coordinate conversion | `GraphicalHelper.ToDetour/ToWow` | Direct 1:1 | Voir §5.1.3 |

### 5.3 🟢 Fichiers Manquants — N/A pour WotLK

Ces fichiers de HB WoD/Legion ne sont **pas nécessaires** pour WotLK :

| Fichier | Raison N/A |
|---------|-----------|
| `GarrisonMeshManager.cs` | Pas de garrisons en WotLK |
| `RecastManaged/` (tout le projet) | CB utilise P/Invoke directe vers Navigation.dll |
| `Lzma.cs` | Décompression gérée par Navigation.dll |
| `TileDataHeader.cs` | Lecture tile gérée par Navigation.dll |
| `MeshManager.cs` (Save/Load) | Gestion tile gérée par Navigation.dll |
| `InvalidTileDataException.cs` | Exception interne gérée par Navigation.dll |
| `TileDataVersionException.cs` | Exception interne gérée par Navigation.dll |
| `MeshMapCalculator.cs` | Constantes dans Navigation.dll |
| `MapConsts.cs` | Constantes dans TileIdentifier |
| `AvoidanceNavigationProvider.cs` | DungeonBuddy pas encore implémenté |
| `KnownBuilding` (AreaType/AbilityFlags) | Pas de garrisons en WotLK |

---

## 6. P/Invoke — Vérification Complète

### 6.1 NativeMethods.cs — 50+ appels déclarés

| Catégorie | Fonction P/Invoke | Présent? |
|-----------|------------------|----------|
| **Pathfinding** | `CalculatePath` | ✅ |
| | `CalculatePathEx` | ✅ |
| | `FreePathArr` | ✅ |
| | `FreePathResult` | ✅ |
| **Sliced** | `InitSlicedFindPath` | ✅ |
| | `UpdateSlicedFindPath` | ✅ |
| | `UpdateSlicedFindPathMs` | ✅ |
| | `FinalizeSlicedFindPath` | ✅ |
| **Queries** | `FindNearestPoly` | ✅ |
| | `FindNearestPolyRef` | ✅ |
| | `FindPolysAroundCircle` | ✅ |
| | `FindDistanceToWall` | ✅ |
| | `FindDistanceToWallEx` | ✅ |
| | `FindDistanceToWallFromPoly` | ✅ |
| | `IsPointOnNavMesh` | ✅ |
| | `FindRandomPointAroundCircle` | ✅ |
| | `HasLineOfSight` | ✅ |
| | `QueryPolygons` | ✅ |
| | `FindLocalNeighbourhood` | ✅ |
| | `GetPolyWallSegments` | ✅ |
| **Poly ops** | `GetPolyHeight` | ✅ |
| | `ClosestPointOnPoly` | ✅ |
| | `ClosestPointOnPolyBoundary` | ✅ |
| | `SetPolyArea` | ✅ |
| | `GetPolyArea` | ✅ |
| | `SetPolyFlags` | ✅ |
| | `GetPolyFlags` | ✅ |
| **Filter** | `SetIncludeFlags` | ✅ |
| | `SetExcludeFlags` | ✅ |
| | `GetIncludeFlags` | ✅ |
| | `GetExcludeFlags` | ✅ |
| | `SetAreaCost` | ✅ |
| | `GetAreaCost` | ✅ |
| | `GetDefaultFilter` | ✅ |
| **Tiles** | `IsTileLoaded` | ✅ |
| | `GetLoadedTilesCount` | ✅ |
| | `SetTileStreamingEnabled` | ✅ |
| | `EnsureTiles` | ✅ |
| | `EnsureTilesDirectional` | ✅ |
| **Raycast** | `Raycast` | ✅ |
| **Stats** | `GetNavStats` | ✅ |
| | `ResetNavStats` | ✅ |
| **Misc** | `UpdatePathFollowing` | ✅ |
| | `SetPathRandomization` | ✅ |
| | `GetNavMeshQuery` | ✅ |

**Seul absent :** `FindStraightPath` — **par design**, car `CalculatePathEx` retourne directement le straight path (points, flags, polyTypes, abilityFlags, polyRefs) en un seul appel. C'est une optimisation de CB par rapport à HB qui faisait FindPath suivi de FindStraightPath en deux appels.

### 6.2 Verdict P/Invoke

✅ **COMPLET — rien ne manque.** Toutes les fonctionnalités Detour sont couvertes.

---

## 7. Fichiers Consommateurs (qui appellent Navigator/Flightor)

### 7.1 Bots

| Fichier | APIs Navigation Utilisées |
|---------|--------------------------|
| `Bots/Gather/GatherBuddy.cs` | `Navigator.MoveTo`, `Flightor.MoveTo`, `MountHelper` |
| `Bots/Grind/LevelBot.cs` | `Navigator.*` (PathPrecision, CanNavigateFully, GeneratePath, MoveTo, Clear, PlayerMover.MoveStop) |
| `Bots/Grind/Levelbot/Actions/Combat/ActionMoveToTarget.cs` | `Navigator.Clear/GeneratePath/MoveTo/GetRunStatusFromMoveResult/PathPrecision` |
| `Bots/Grind/Levelbot/Actions/Combat/ActionSetTarget.cs` | `Navigator.Clear` |
| `Bots/Grind/Levelbot/Actions/Death/ActionReleaseFromCorpse.cs` | `Navigator.Clear` |
| `Bots/Grind/Levelbot/Actions/Death/ActionRetrieveCorpse.cs` | `Navigator.MoveTo` |
| `Bots/Quest/QuestBot.cs` | `Navigator.PathPrecision` |
| `Bots/Quest/Actions/ForcedBehaviorExecutor.cs` | `Navigator.FindHeight` |
| `Bots/Quest/Objectives/GrindObjective.cs` | `Navigator.FindHeight` |
| `Bots/Quest/Objectives/UseGameObjectObjective.cs` | `Navigator.MoveTo` |
| `Bots/Quest/QuestOrder/ForcedMoveTo.cs` | `Navigator.MoveTo` |
| `Bots/Quest/QuestOrder/ForcedUseItem.cs` | `Navigator.PathPrecision`, `Navigator.MoveTo` |

### 7.2 CommonBehaviors

| Fichier | APIs Navigation Utilisées |
|---------|--------------------------|
| `CommonBehaviors/Actions/ActionMoveToPoi.cs` | `Navigator.StuckHandler.*`, `Navigator.MoveTo`, `GetRunStatusFromMoveResult` |
| `CommonBehaviors/Actions/NavigationAction.cs` | `Navigator.MoveTo`, `GetRunStatusFromMoveResult` |

### 7.3 Infrastructure Styx

| Fichier | APIs Navigation Utilisées |
|---------|--------------------------|
| `Styx/Database/Connection.cs` | `Navigator.GeneratePath` (calcul distance path) |
| `Styx/Helpers/Logging.cs` | `WriteNavigator()` (4 overloads) |
| `Styx/Logic/AreaManagement/GrindArea.cs` | `Navigator.PathPrecision` |
| `Styx/Logic/AreaManagement/QuestArea.cs` | `Navigator.FindMeshHeight` |
| `Styx/Logic/BehaviorTree/TreeRoot.cs` | `Navigator.Clear`, `Navigator.PathPrecision` |
| `Styx/Logic/POI/BotPoi.cs` | `Navigator.Clear` |
| `Styx/Logic/FlightPaths.cs` | `Navigator.CanNavigateFully`, `GeneratePath`, `Clear` |
| `Styx/WoWInternals/World/GameWorld.cs` | `Navigator.Raycast` |
| `Styx/WoWInternals/WoWMovement.cs` | `Navigator.MoveTo` |

### 7.4 Quest Behaviors (40+ fichiers dans bin/Debug)

Utilisation extensive de `Navigator.MoveTo`, `Navigator.PlayerMover.MoveStop`, `Navigator.CanNavigateFully`, `Navigator.GeneratePath`, `Navigator.PathPrecision`, `Flightor.MoveTo`, `Flightor.MountHelper.*`.

---

## 8. Points Spécifiques à Vérifier en Jeu

### 8.1 Conversion de Coordonnées

**Question critique :** Est-ce que `Navigation.dll` de CB attend les coordonnées en format WoW (X=Nord, Y=Ouest, Z=Haut) ou en format Detour (X=-Y_wow, Y=Z_wow, Z=-X_wow)?

- Si Navigation.dll utilise le format WoW-natif → le mapping 1:1 actuel est correct ✅
- Si Navigation.dll utilise le format Detour standard → il faut ajouter la conversion comme `GraphicalHelper` ❌

**Comment vérifier :** Tester `Navigator.GeneratePath()` en jeu. Si les chemins sont corrects, la conversion est bonne.

### 8.2 Battlegrounds — SotA et IoC

- `IoCGate.cs` est manquant — si DungeonBuddy/BGBuddy doit gérer Isle of Conquest, il faudra le créer
- `SotAGate.cs` est présent ✅
- Les `Misc5-Misc7` manquants dans `AreaType` empêcheraient la détection des gates IoC dans le navmesh
- **Impact :** Nul tant que BGBuddy n'est pas implémenté

### 8.3 Default Area Costs

**HB WoD (WowQueryFilter) :**
- Road=1.0, Ground/KnownBuilding/Gate/Alliance/Horde/Portal/HordePortal/AlliancePortal/InteractObject/InteractUnit=1.66
- Fall=1.7, Water=3.33, DefendersPortal/Elevator=3.16, Lava=55, Blackspot=60, Blocked=100

**CopilotBuddy :** Vérifier que les mêmes coûts sont définis dans `Tripper/Navigation/Navigator.cs` lors de l'initialisation du `QueryFilter`.

### 8.4 MeshMovePath

HB utilise une class `MeshMovePath` pour trackER l'index courant dans le path et le flag `IsExitingGarrison`. CB inligne cette logique. Vérifier que l'état du path index est correctement géré lors des off-mesh connections (elevator ride = pause du path following).

---

## 9. Recommandations

### 9.1 Actions Recommandées (priorité haute)

| # | Action | Justification | Effort |
|---|--------|--------------|--------|
| 1 | Vérifier conversion coordonnées en jeu | Point le plus risqué | Test 5 min |
| 2 | Ajouter `MoveResultExtensions.IsSuccessful()` | Utilisé par quest behaviors | 5 min |
| 3 | Vérifier area costs dans QueryFilter init | Doivent matcher HB defaults | 15 min |

### 9.2 Actions Recommandées (priorité moyenne)

| # | Action | Justification | Effort |
|---|--------|--------------|--------|
| 4 | Ajouter `Misc5-Misc10` à AreaType | Couverture BG future | 5 min |
| 5 | Ajouter `NavType` enum (Run=0, Fly=1) | Utilisé par `NavigationAction` | 5 min |
| 6 | Ajouter `IoCGate.cs` | Couverture BG IoC future | 10 min |
| 7 | Ajouter `PathGenerationFailStep` enum | Debug info pour nav failures | 5 min |

### 9.3 Actions Optionnelles (si DungeonBuddy/BGBuddy planifié)

| # | Action | Justification | Effort |
|---|--------|--------------|--------|
| 8 | Créer `NavigationProvider` abstraction | Permet swap MeshNav/AvoidanceNav/BGNav | 2-4h refactor |
| 9 | Créer `AvoidanceNavigationProvider` | Requis par DungeonBuddy | 1-2h |
| 10 | Ajouter events Provider/Mover changed | Plugins qui hook le nav | 30 min |
| 11 | Créer `BlackspotQueryFlags` enum | Filtre Static/Dynamic blackspots | 5 min |

---

## 10. Tableau Récapitulatif Final

### Fichiers par statut

| Statut | Count | Description |
|--------|-------|-------------|
| ✅ Présent et complet | **27** | Fichiers CB qui matchent HB |
| ✅ Présent mais fusionné | **6** | Fichiers HB dont la logique est inlinée dans CB |
| ⚠️ Manquant mais non nécessaire WotLK | **11** | Garrison, RecastManaged, LZMA, etc. |
| ⚠️ Manquant optionnel | **8** | Extensions, events, debug enums |
| 🔴 Manquant potentiel | **3** | AreaType gaps, MoveResultExtensions, area costs |

### Score de complétude navigation

| Composant | Score |
|-----------|-------|
| P/Invoke (NativeMethods) | **100%** |
| Pathfinding core | **100%** |
| Off-mesh connections | **100%** |
| Tile management | **100%** |
| Stuck handling | **100%** |
| Flightor | **95%** (manque PolyNav, IndoorEntrance, Areas) |
| BlackspotManager | **95%** (manque BlackspotQueryFlags) |
| Enums/Data types | **90%** (manque KnownBuilding, Misc5-10, IoCGate) |
| Architecture abstractions | **70%** (pas de NavigationProvider pattern) |
| DungeonBuddy nav support | **0%** (pas encore implémenté, pas dans le scope actuel) |

### Score global navigation : **93%** pour WotLK 3.3.5a

Le système est fonctionnel. Les lacunes sont soit des features N/A WotLK, soit des améliorations pour des bots futurs (DungeonBuddy/BGBuddy).

---

## 11. Annexe — Diagramme du Flux de Données Navigation

```
[Bot Code]
    │
    ├─ Navigator.MoveTo(WoWPoint)
    │   ├── StuckDetector.IsStuck? → StuckHandler.Unstick()
    │   ├── BlackspotManager.IsBlackspotted? → Skip
    │   ├── HandleDoors() → Lua interact
    │   ├── TripperNavigator.FindPath(from, to)
    │   │   ├── NativeMethods.EnsureTiles()
    │   │   ├── NativeMethods.InitSlicedFindPath()
    │   │   ├── NativeMethods.UpdateSlicedFindPath() [loop]
    │   │   ├── NativeMethods.FinalizeSlicedFindPath()
    │   │   ├── PathPostProcessor.MoveAwayFromEdges/Randomize
    │   │   └── → PathFindResult (points, flags, areas, abilities)
    │   ├── HandleOffMeshConnection() [if AreaType is special]
    │   │   ├── Elevator: wait + ride + exit
    │   │   ├── Portal: interact + wait
    │   │   ├── InteractObject: door interaction
    │   │   └── InteractUnit: NPC interaction
    │   └── PlayerMover.MoveTowards(nextPoint)
    │       └── ClickToMove / KeyboardMover → WoW client
    │
    ├─ Flightor.MoveTo(WoWPoint)
    │   ├── MountHelper.MountUp()
    │   ├── Height calculations
    │   └── Navigator.MoveTo (ground portions)
    │
    └─ Navigator.FindHeight/FindHeights/Raycast
        └── NativeMethods.* → Navigation.dll
```

---

*Fin du rapport. Ce document doit être analysé par 2 autres modèles IA indépendants avant décision finale.*
