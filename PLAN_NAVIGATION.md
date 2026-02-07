# Plan Navigation Avancée — CopilotBuddy

> Ce document sert de **mémoire persistante** pour le travail de navigation avec LLM.
> À chaque session, copier la section "État actuel" dans le prompt.

---

## Méthode de travail avec LLM (contexte limité)

### Principe : Diviser en phases, documenter chaque résultat

Le contexte LLM n'est pas infini. Pour ne rien perdre :

1. **Phase par phase** — 1 session = 1 problème précis
2. **Après chaque phase** — noter ici les décisions prises et les fichiers modifiés
3. **Au début de chaque session** — coller ce fichier + les fichiers concernés par la phase
4. **Ne jamais faire 2 phases en même temps**

### Phases proposées (ordre recommandé)

| Phase | Sujet | Fichiers concernés | Statut |
|-------|-------|--------------------|--------|
| 1 | Audit C++ : comprendre PathFinder et ses paramètres | `Navigation\PathFinder.cpp/.h` | ✅ |
| 2 | Audit C++ : comprendre Navigation.cpp (API exportée) | `Navigation\Navigation.cpp/.h` | ✅ |
| 3 | Audit C# : comprendre NativeMethods.cs (P/Invoke) | `Tripper\Navigation\NativeMethods.cs` | ✅ |
| 4 | Audit C# : comprendre Navigator.cs (path smoothing, waypoint skip) | `Styx\Logic\Pathing\Navigator.cs` | ✅ |
| 5 | Audit C# : comprendre le mouvement (CTM, PlayerMover, Flightor) | `WoWMovement.cs`, `LocalPlayerMover.cs`, `Flightor.cs` | ✅ |
| 6 | Comparer avec HB 6.2.3 : MeshNavigator, Pathing complet | `.hb 6.2.3\Honorbuddy\Styx\Pathing\` | ✅ |
| 7 | Identifier les corrections à appliquer | Ce fichier | ✅ |
| 7b | Contre-audit sceptique indépendant (QC2) | Ce fichier | ✅ |
| 8 | Implémenter les corrections C# (Navigator.MoveTo) | `Navigator.cs`, `PathPostProcessor.cs` | ✅ |
| 9 | Corrections C++ + off-mesh dispatch + forgotten items | `Navigation.cpp`, `PathFinder.cpp`, `Navigator.cs` | ✅ |
| 10 | Corrections audit QC indépendant (6 fixes C#) | `Navigator.cs` | ✅ |
| 10b | Corrections audit suite (FixPathWalkability, Water Z, path skip) | `Navigator.cs`, `PathPostProcessor.cs`, `TripperNavigator` | ✅ |
| 10c | Corrections audit (area costs, faction filter, off-mesh default) | `TripperNavigator`, `Navigator.cs` | ✅ |
| 11 | Recompilation C++ DLL + test en jeu | Build & test | ⬜ |

---

## Problèmes connus (observés en jeu)

1. **Le bot coupe les virages** — il passe au waypoint suivant avant d'avoir atteint le waypoint courant, ce qui décale la trajectoire et fait traverser les murs/obstacles
2. **Detour path rasant les murs** — le path généré par Detour passe trop près des bords du navmesh (murs, falaises)
3. **Terrain non plat (zone Orc starter)** — le bot se bloque sur les pentes, les rochers, les escaliers

---

## Architecture actuelle

### C++ (Navigation.dll)

```
c:\Users\Texy\Desktop\.test\C++\Navigation\
├── Navigation.cpp/.h        ← API exportée (FindPath, FindHeight, LoadMap, etc.)
├── PathFinder.cpp/.h        ← Wrapper Detour : dtNavMeshQuery, findStraightPath
├── MoveMap.cpp/.h            ← Chargement des mmaps (tiles navmesh)
├── NavBridge.cpp/.h          ← Bridge entre API et PathFinder
├── OffMeshManager.cpp/.h     ← Liens off-mesh (téléporteurs, escaliers, portes)
├── Detour/                   ← Libraire Detour (pathfinding sur navmesh)
│   ├── DetourNavMesh.*       ← Structure du navmesh
│   ├── DetourNavMeshQuery.*  ← Algorithme A* sur le navmesh
│   ├── DetourCommon.*        ← Utilitaires math
│   └── DetourNode.*          ← Noeuds A*
└── DetourCrowd/              ← Gestion de foule (probablement pas utilisé)
    ├── DetourPathCorridor.*  ← Suivi de corridor de path
    └── DetourObstacleAvoidance.* ← Évitement d'obstacles
```

### C# (CopilotBuddy)

```
Tripper/Navigation/
├── NativeMethods.cs         ← P/Invoke vers Navigation.dll
├── Navigator.cs             ← Wrapper C# haut niveau
├── PathPostProcessing.cs    ← Post-traitement du path (smoothing?)
├── PathPostProcessor.cs     ← Processeur de post-traitement
└── PathFindResult.cs        ← Résultats de pathfinding

Styx/Logic/Pathing/
├── Navigator.cs             ← Navigator principal du bot (MoveTo, waypoint logic)
├── Flightor.cs              ← Navigation vol
└── Interop/
    └── LocalPlayerMover.cs  ← Mouvement du joueur (CTM, clés)
```

### HB 6.2.3 Référence (navigation avancée)

```
.hb 6.2.3/Honorbuddy/Tripper/Navigation/
├── WowNavigator.cs          ← Navigator avancé WoW-specific
├── WowQueryFilter.cs        ← Filtre de query Detour customisé
├── PathPostProcessing.cs    ← Lissage de path avancé
├── WorldMeshManager.cs      ← Gestion des tiles navmesh
├── NavHelper.cs             ← Helpers navigation
└── IMeshManager.cs          ← Interface mesh manager

.hb 6.2.3/Tripper.RecastManaged/
├── dtNavMeshQuery.cs        ← Binding managed de DetourNavMeshQuery
├── dtNavMesh.cs             ← Binding managed de DetourNavMesh
├── dtQueryFilter.cs         ← Filtre query managed
└── dtPathCorridor.cs        ← Path corridor managed
```

---

## État actuel

- **Navigation.dll** : Compilée, fonctionne. **DLL est SOLIDE** — tous les exports P/Invoke matchent, conversions coordonnées correctes, CalculatePathEx retourne les metadata complètes.
- **TripperNavigator (C#)** : ✅ SOLIDE — FindPath utilise CalculatePathEx, MoveAwayFromEdges est ACTIF et appelle des exports C++ valides. Les area costs sont appliqués.
- **Problème réel** : C'est le **Styx.Logic.Pathing.Navigator.MoveTo()** qui cause les bugs — il jette la metadata du path (Flags, PolyTypes, AbilityFlags), pas de gestion off-mesh. Le StuckHandler fonctionne dans le flow ActionMoveToPoi (GrindBot/QuestBot) mais les appels directs à MoveTo() (corpse run, loot, hotspot) n'ont PAS de stuck detection.
- **MoveAwayFromEdges** : ACTIF mais wrappé dans un try/catch silencieux — un crash du post-processing passe inaperçu et les paths rasent les murs.
- **Phases complétées** : Phase 1-7 (audit complet + double QC)

### ⚠️ CONTRÔLE QUALITÉ (QC) — Erreurs corrigées

**Phase 3 était CATASTROPHIQUEMENT FAUSSE.** Le LLM a comparé les P/Invoke C# contre les exports NavBridge.cpp (`_C` suffixés) au lieu des exports DllMain.cpp (sans suffixe). En réalité, le C# appelle les exports DllMain.cpp et TOUS matchent parfaitement.

| Finding original | Verdict QC | Explication |
|---|---|---|
| P3.1 "15 naming mismatches `_C`" | ❌ **FAUX** | C# appelle DllMain.cpp (58 exports, pas de `_C`), PAS NavBridge.cpp. Tous les noms matchent. |
| P3.2 "SetAreaCost signature mismatch" | ❌ **FAUX** | C# `SetAreaCost(uint areaId, float cost)` = DllMain `SetAreaCost(unsigned int, float)` ✅ |
| P3.3 "NavStats 44 vs 24 bytes = corruption" | ❌ **FAUX** | C# NavStats (11 champs) = DllMain NavStats (11 champs). `NavStats_C` (6 champs) est le NavBridge version que C# N'APPELLE PAS. |
| P3.4 "Raycast signature incompatible" | ❌ **FAUX** | C# Raycast = DllMain Raycast (même 9 paramètres). `Raycast_C` est la version NavBridge simplifiée. |
| P3.5 "20 exports manquants, MoveAwayFromEdges MORT" | ❌ **FAUX** | TOUS les exports existent dans DllMain.cpp. MoveAwayFromEdges IS ACTIVE et fonctionne. |
| P3.7 "Area costs non appliqués" | ❌ **FAUX** | SetAreaCost matche parfaitement, les coûts SONT appliqués. |

**Conséquence : MoveAwayFromEdges N'EST PAS mort.** Il est actif par défaut (`PathPostProcessing = MoveAwayFromEdges`) et appelle des exports DllMain réels (`FindDistanceToWall`, `FindDistanceToWallFromPoly`, `HasLineOfSight`). Si les paths rasent encore les murs, la cause est AILLEURS (voir diagnostic ci-dessous).

**Findings Phase 4 : PARTIELLEMENT vrais** — Navigator.MoveTo jette la metadata ✅, pas d'off-mesh handling ✅. **MAIS P4.6 "StuckHandler jamais appelé" est PARTIELLEMENT FAUX** : `ActionMoveToPoi.Run()` (ligne 138) appelle `StuckHandler.IsStuck()` + `Unstick()` après chaque `MoveResult.Moved` avec timer 2s. Le StuckHandler fonctionne dans le flow principal GrindBot/QuestBot. Le trou ne concerne que les appels directs à `Navigator.MoveTo()` (corpse run L306, loot L331, move-to-hotspot L1099).

### Réponse : Faut-il réécrire la DLL ?

**NON.** La Navigation.dll est une bonne base solide :
- 58 exports DllMain fonctionnels, conversions de coordonnées correctes
- `CalculatePathEx` retourne un `PathResult` complet (Points, Flags, PolyTypes, AbilityFlags, PolyRefs)
- `SetAreaCost` fonctionne, les coûts sont appliqués au pathfinding
- `FindDistanceToWall`, `Raycast`, `HasLineOfSight` — tous opérationnels
- MoveAwayFromEdges C# appelle ces exports correctement

**Le vrai problème est dans `Styx.Logic.Pathing.Navigator.MoveTo()`** (le layer de mouvement C#, pas la DLL). C'est là qu'il faut concentrer les corrections.

### Priorités RÉELLES (après QC)

| # | Problème | Localisation | Effort | Priorité |
|---|----------|-------------|--------|----------|
| 1 | **Path metadata jetée** — MoveTo ne garde que Points[], jette Flags/PolyTypes/AbilityFlags | `Styx/Logic/Pathing/Navigator.cs` L230 | Moyen | **CRITIQUE** |
| 2 | **StuckHandler non intégré dans MoveTo lui-même** — fonctionne via ActionMoveToPoi mais 3 appels directs MoveTo() (corpse run, loot, hotspot) n'ont PAS de stuck detection | `Styx/Logic/Pathing/Navigator.cs` | Facile | **HAUTE** (pas CRITIQUE — flow principal couvert) |
| 3 | **Off-mesh connection handling** — elevators, portals, interact objects | `Styx/Logic/Pathing/Navigator.cs` | Élevé | **HAUTE** |
| 4 | **Path validity check** — vérifier que le joueur est encore sur le path | `Styx/Logic/Pathing/Navigator.cs` | Moyen | **HAUTE** |
| 5 | **IsPartialPath jamais détecté** — `PathFindResult.IsPartialPath` toujours false | `Tripper/Navigation/` | Facile | **MOYEN** |
| 6 | **Height correction +0.5f** dans BuildPointPath | `PathFinder.cpp` | Facile | **MOYEN** |
| 7 | **FindPath async** avec combat abort | Architecture | Élevé | **HAUTE** (mais complexe) |
| 8 | **NavBridge.cpp** — 4 fonctions `_C` avec bugs de coordonnées | `NavBridge.cpp` | Facile | **BAS** (C# ne les utilise pas) |
| 9 | **Local filter vs global** dans Navigation.cpp query functions | `Navigation.cpp` | Facile | **BAS** |
| 10 | **Debug file write** dans HaveTile() | `PathFinder.cpp` | Trivial | **BAS** |

---

## Prompt pour chaque phase

### Template de prompt à utiliser

```
Tu es un expert en navigation 3D, pathfinding (Detour/Recast), et programmation de bots pour jeux vidéo.

CONTEXTE :
- CopilotBuddy : bot WoW 3.3.5a (WotLK) en C# .NET 10 + Navigation.dll en C++ (Detour)
- On utilise des navmesh (mmaps) de WoW pour le pathfinding
- La Navigation.dll exporte FindPath, FindHeight, LoadMap via P/Invoke
- Le C# fait le post-traitement du path et le mouvement du personnage

PROBLÈMES OBSERVÉS :
1. Le bot coupe les virages — il skip au waypoint suivant trop tôt, la trajectoire dévie et traverse les murs
2. Les paths générés rasent les murs/bords du navmesh
3. Le bot se bloque sur terrain non plat (pentes, rochers, escaliers dans zone orc starter)

RÉFÉRENCE :
- HB 6.2.3 a la navigation la plus avancée (WowNavigator, PathPostProcessing, WowQueryFilter)
- Les développeurs de HB ont pensé à tous les edge cases

PHASE ACTUELLE : [N] — [Description]

FICHIERS À ANALYSER :
[coller le contenu des fichiers ici]

RÈGLES :
- Ne modifie aucun fichier sans mon accord explicite
- Analyse d'abord, propose ensuite
- Documente tes trouvailles pour que je puisse les conserver entre sessions
- Compare toujours avec HB 6.2.3 quand pertinent
- Explique les paramètres Detour importants (maxPathLength, polyPickExtent, stepSize, etc.)

OBJECTIF DE CETTE PHASE :
[objectif spécifique]

LIVRABLE :
- Liste des problèmes identifiés avec explication technique
- Solutions proposées avec code
- Impact sur les autres composants
```

---

## Notes par phase (à remplir au fur et à mesure)

### Phase 1 — PathFinder.cpp

**Fichiers analysés :** `PathFinder.cpp` (1153 lignes), `PathFinder.h` (238 lignes), `MoveMapSharedDefines.h`, `StraightPathFlags.h`

**Origine du code :** MaNGOS server-side pathfinding, adapté pour un bot client-side. Ce code a été conçu pour des créatures NPC sur serveur, PAS pour un joueur navigant en client.

#### Architecture interne

1. **Constructeur** — Charge navmesh/query via `MMapManager`, crée un `dtQueryFilter` global (`m_filter`), initialise le stuck detection
2. **`calculate()`** — Point d'entrée : vérifie navmesh/tiles, appelle `BuildPolyPath()` → `BuildPointPath()`
3. **`BuildPolyPath()`** — Algorithme A* via `dtNavMeshQuery::findPath()`, avec optimisation de réutilisation de prefix quand le chemin courant est encore valide
4. **`BuildPointPath()`** — Convertit le poly-path en waypoints via `findStraightPath()`, résout les area types et ability flags, initialise le corridor
5. **`findSmoothPath()`** — Smooth path alternatif (pas utilisé par `BuildPointPath` actuellement), avance pas à pas avec `moveAlongSurface()`

#### Paramètres Detour identifiés

| Paramètre | Valeur | Impact |
|-----------|--------|--------|
| `MAX_PATH_LENGTH` | 740 | Max polygones dans le poly-path A* |
| `MAX_POINT_PATH_LENGTH` | 740 | Max waypoints dans le point-path |
| `SMOOTH_PATH_STEP_SIZE` | 4.0f | Pas d'avancement dans `findSmoothPath()` (4 yards) |
| `SMOOTH_PATH_SLOP` | 0.3f | Tolérance de proximité au steer target (0.3 yards, très serré) |
| `POLY_SEARCH_EXTENTS` | {3.0, 20.0, 3.0} | Rayon de recherche FindNearestPoly (XZ=3, Y=20 pour grottes) |
| `farFromPoly threshold` | 7.0f | Au-delà de 7 yards de distance au poly, path marqué INCOMPLETE |
| `findStraightPath options` | `DT_STRAIGHTPATH_ALL_CROSSINGS` | Génère un waypoint à CHAQUE changement de polygon |
| `dtQueryFilter includeFlags` | 0xFFFF (tout) | Aucun filtrage par type de terrain |
| `dtQueryFilter excludeFlags` | 0 | Rien d'exclu |
| `dtQueryFilter areaCosts` | 1.0f partout | Pas de préférence route vs terrain vs eau |

#### Coordonnées : Conversion WoW ↔ Detour

Le code fait une permutation de coordonnées critique :
- **WoW** : `{X, Y, Z}` (X=est, Y=nord, Z=haut)
- **Detour** : `{Y, Z, X}` (Detour utilise Y-up)
- Conversion : `float startPoint[3] = { startPos.y, startPos.z, startPos.x }`
- Reconversion : `Vector3(result[2], result[0], result[1])`

#### Problèmes identifiés

##### PROBLÈME 1 — `findStraightPath` avec `DT_STRAIGHTPATH_ALL_CROSSINGS`
**Sévérité : BASSE (atténué par MoveAwayFromEdges C#)**

`BuildPointPath()` utilise `DT_STRAIGHTPATH_ALL_CROSSINGS` ce qui place les waypoints sur les BORDS des polygones.

**ATTÉNUATION EXISTANTE :** CopilotBuddy possède `PathPostProcessor.MoveAwayFromEdges` (339 lignes) qui repousse chaque waypoint vers l'intérieur. Il est actif par défaut (`TripperNavigator.PathPostProcessing = MoveAwayFromEdges`). Les exports C++ nécessaires (`FindDistanceToWall`, `FindDistanceToWallFromPoly`, `HasLineOfSight`) existent dans DllMain.cpp. Si les paths rasent encore les murs, vérifier `EdgeDistance` (défaut 2.0f) ou si des exceptions sont silencieusement catchées.

##### PROBLÈME 2 — `findSmoothPath()` existe mais n'est PAS utilisée
**Sévérité : MOYENNE**

`findSmoothPath()` (ligne ~725) implémente le smooth path avec `moveAlongSurface()` qui génère un path qui suit la surface du navmesh sans couper les obstacles. Cependant, `BuildPointPath()` utilise `findStraightPath()` directement, ignorant complètement `findSmoothPath()`.

Le `findSmoothPath()` a sa propre logique de step-by-step (4.0f step size), gestion d'off-mesh links, et height correction (`result[1] += 0.5f`).

##### PROBLÈME 3 — Area costs C++ par défaut = 1.0f (atténué par SetAreaCost C#)
**Sévérité : BASSE (atténué)**

Le `dtQueryFilter` dans PathFinder.cpp initialise tous les area costs à 1.0f.

**ATTÉNUATION EXISTANTE :** `TripperNavigator.SetDefaultAreaCosts()` appelle `NativeMethods.SetAreaCost()` (DllMain export) pour 14 types : Ground=1.66, Road=0.5, Water=3.33, Lava=55, Blocked=100, etc. PathFinder copie `_defaultFilter` (modifié par SetAreaCost) dans son constructeur. Les costs SONT donc appliqués pour le pathfinding.

**Problème résiduel (P2.5) :** Certaines fonctions Navigation.cpp (`FindDistanceToWall`, `IsPointOnNavMesh`) créent un filtre LOCAL (stack) au lieu d'utiliser `_defaultFilter` → les costs ne s'appliquent pas là. Impact minimal pour MoveAwayFromEdges (FindDistanceToWall doit détecter TOUS les bords).

##### PROBLÈME 4 — `getNavTerrain()` retourne toujours `NAV_GROUND`
**Sévérité : BASSE (pour l'instant)**

La fonction est stubée avec `return NAV_GROUND;` car elle nécessitait l'accès au terrain serveur MaNGOS. Pour un bot client, il faudrait lire le liquid type depuis le navmesh poly flags ou depuis la mémoire du client WoW.

##### PROBLÈME 5 — Height correction `+0.5f` dans `findSmoothPath()` seulement
**Sévérité : MOYENNE**

`findSmoothPath()` ajoute `result[1] += 0.5f` après `getPolyHeight()` pour surélever le point de 0.5 yard au-dessus du navmesh. `BuildPointPath()` n'applique AUCUNE correction de hauteur. Le path généré est exactement sur la surface du navmesh, ce qui peut causer des collisions avec le sol sur terrain non plat.

##### PROBLÈME 6 — `UpdateFollowing()` raycast shortcut peut être dangereux
**Sévérité : MOYENNE**

Le raycast shortcut (Quick Win #1) saute le waypoint+1 si le raycast vers waypoint+2 est clair. Mais :
- Il ne vérifie pas la hauteur entre les points (le raycast Detour est 2D sur le navmesh)
- Il ne vérifie pas si le waypoint+1 est un off-mesh connection (escalier, porte)
- Il `erase()` le waypoint du vector, ce qui modifie les indices (bugs subtils si appelé en boucle)

##### PROBLÈME 7 — `HaveTile()` écrit dans un fichier hardcodé
**Sévérité : BASSE (debug leftover)**

`HaveTile()` ouvre `C:\\Users\\Drew\\Repos\\bloog-bot-v2\\Bot\\navigationDebug.txt` — c'est un résidu de debug du développeur original. À supprimer.

#### Points forts existants

1. ✅ `POLY_SEARCH_EXTENTS = {3, 20, 3}` — identique à HB 6.2.3, correct pour gérer les grottes
2. ✅ Réutilisation de prefix de path (`startPolyFound && !endPolyFound`) — évite de recalculer tout le path quand la destination bouge
3. ✅ `InitializeCorridor()` / `PatchCorridor()` — dtPathCorridor est initialisé, base pour amélioration future
4. ✅ `IsStuck()` avec vérification de distance au mur — évite les faux positifs (loot, cast)
5. ✅ Off-mesh connection support (OffMeshManager intégré)
6. ✅ `forceDestination` logic — snap le dernier point au point demandé si assez proche

#### Solutions proposées (à implémenter en Phase 8)

| # | Solution | Complexité | Impact |
|---|----------|-----------|--------|
| S1 | ~~MoveAwayFromEdges~~ — **DÉJÀ IMPLÉMENTÉ** dans `PathPostProcessor.cs`, actif par défaut. Vérifier si `EdgeDistance=2.0f` est optimal et si des exceptions silencieuses empêchent l'exécution. | Diagnostic | **MOYEN** — vérifier que ça marche EN PRATIQUE |
| S2 | Utiliser `findSmoothPath()` au lieu de `findStraightPath()` dans `BuildPointPath()`, ou offrir un choix via paramètre | Moyenne | **BAS** — MoveAwayFromEdges fait déjà le travail |
| S3 | ~~Configurer area costs~~ — **DÉJÀ FAIT** via `SetDefaultAreaCosts()` dans TripperNavigator.cs (14 types configurés) | N/A | **RÉSOLU** |
| S4 | Ajouter correction de hauteur `+0.5f` dans `BuildPointPath()` pour tous les waypoints | Facile | **MOYEN** — réduit les blocages sur terrain non plat |
| S5 | Protéger `UpdateFollowing()` contre le skip d'off-mesh waypoints | Facile | **BAS** — prévient des bugs edge case |
| S6 | Supprimer le debug `HaveTile()` file write | Trivial | **BAS** — cleanup |

### Phase 2 — Navigation.cpp (API exportée)

**Fichiers analysés :** `Navigation.cpp` (1572 lignes), `Navigation.h` (169 lignes), `NavBridge.cpp` (558 lignes), `NavBridge.h`, `PathResult.h`, `NavTypes.h`, `NavStatus.h`, `MoveMap.h`

#### Architecture : 3 couches — DEUX surfaces d'export

```
DllMain.cpp      ← C API exportée #1 (NAV_API, PAS de suffixe _C) — UTILISÉE par P/Invoke C#
    ↓ appelle
Navigation.cpp   ← Classe singleton Navigation — logique métier, conversions WoW↔Detour
    ↓ utilise
PathFinder.cpp   ← Wrapper Detour A* + findStraightPath
MoveMap.cpp      ← Chargement mmaps/tiles navmesh
OffMeshManager.cpp ← Off-mesh connections (escaliers, portes, téléporteurs)

NavBridge.cpp    ← C API exportée #2 (extern "C", suffixe _C) — NON utilisée par C#
    ↓ wrapper (avec bugs de conversion P2.1)
Navigation.cpp   ← Même singleton, mais appelé sans conversion coords dans 4 fonctions
```

**Note QC :** Le C# appelle exclusivement les exports **DllMain.cpp** (58 fonctions, sans suffixe `_C`). Les exports NavBridge.cpp (32 fonctions, avec suffixe `_C`) ne sont PAS appelés par le C# et contiennent des bugs de conversion de coordonnées dans 4 fonctions (P2.1).

#### Fonctions API exportées (NavBridge.cpp → P/Invoke)

| Fonction C | Navigation.cpp | Usage |
|-----------|---------------|-------|
| `CalculatePath_C` | `CalculatePath()` | Pathfinding basique, retourne XYZ[] |
| `CalculatePathEx` | `CalculatePathEx()` | **Pathfinding enrichi** (status, flags, polyTypes, polyRefs) |
| `FreePathArr_C` / `FreePathResult` | mémoire | Libération mémoire native |
| `Raycast_C` | via query | Raycast navmesh (LOS check basique) |
| `FindNearestPoint_C` | `FindNearestPoly()` | Point navmesh le plus proche |
| `FindNearestPointEx_C` | idem + custom extents | Multi-couche (grottes) |
| `FindRandomPoint_C` | `FindRandomPointAroundCircle()` | Point aléatoire navigable |
| `SetAreaCost_C` | `SetAreaCost()` | Coût par area type |
| `SetIncludeFlags_C` / `SetExcludeFlags_C` | `SetIncludeFlags/ExcludeFlags()` | Filtrage par flags |
| `IsOffMeshConnection_C` | via PathFinder | Détection off-mesh |
| `AddOffMeshConnection_C` | OffMeshManager | Ajout runtime off-mesh |
| `FindNearestPolyRef_C` | `FindNearestPolyRef()` | Retourne polyRef (pour API avancées) |
| `GetPolyHeight_C` | `GetPolyHeight()` | Hauteur sur un poly spécifique |
| `ClosestPointOnPoly_C` | `ClosestPointOnPoly()` | Point le plus proche sur poly |
| `QueryPolygons_C` | `QueryPolygons()` | Query tous les polys dans un volume |
| `GetPolyWallSegments_C` | `GetPolyWallSegments()` | Segments de murs d'un poly |
| `EnsureTiles_C` | `EnsureTiles()` | Tile streaming |
| `UpdatePathFollowing_C` | `UpdatePathFollowing()` | Raycast shortcut during following |
| `GetNavStats_C` / `ResetNavStats_C` | stats | Télémétrie |

#### Conversion de coordonnées WoW ↔ Detour

Documentée proprement en haut de Navigation.cpp avec des helpers inline :

```cpp
// WoW(X,Y,Z) → Detour[Y,Z,X]     (WoW Y=north → Detour X, WoW Z=up → Detour Y)
// Detour[0,1,2] → WoW(Z[2],X[0],Y[1])  (inverse)
inline void WoWToDetour(const XYZ& wow, float detour[3]);
inline void DetourToWoW(const float detour[3], XYZ& wow);
```

**Constantes de conversion :** `NavConstants::kExtX=3.0, kExtY=20.0, kExtZ=3.0` — identiques à HB 6.2.3.

#### Problèmes identifiés

##### PROBLÈME P2.1 — NavBridge.cpp INCONSISTANCE de conversions de coordonnées
**Sévérité : BASSE (C# n'utilise PAS NavBridge — appelle DllMain à la place)**

`Navigation.cpp` utilise correctement `WoWToDetour()` / `DetourToWoW()` dans ses méthodes (depuis les FIXED récents). **MAIS** `NavBridge.cpp` ne fait PAS de conversion dans plusieurs fonctions :

- `Raycast_C()` : passe `start.x, start.y, start.z` directement → **PAS de WoW→Detour** (lignes 130-135)
- `FindNearestPointEx_C()` : passe `position.x, position.y, position.z` directement → **PAS de WoW→Detour** (lignes 180-185)
- `FindRandomPoint_C()` : passe `center.x, center.y, center.z` directement → **PAS de WoW→Detour** (lignes 200-205)
- `CreateCorridorForAgent_C()` : passe les positions brutes → **PAS de WoW→Detour**
- `UpdateCorridorAgentPosition_C()` : idem

Le problème : `Navigation.cpp::FindNearestPoly()` fait la conversion WoW→Detour, mais `NavBridge.cpp::FindNearestPoint_C()` passe les coordonnées directement au `dtNavMeshQuery`, court-circuitant `Navigation.cpp`. C'est un **double standard dangereux** — les deux chemins d'appel ne donnent pas les mêmes résultats.

##### PROBLÈME P2.2 — `CalculatePath` crée un PathFinder à chaque appel
**Sévérité : MOYENNE (performance)**

Chaque appel à `CalculatePath()` / `CalculatePathEx()` crée un `PathFinder(mapId, 1)` sur la pile avec :
- Lookup navmesh via MMapManager
- Création d'un `new dtQueryFilter()`
- Sync de tous les area costs (boucle DT_MAX_AREAS)
- Initialisation du corridor

HB 6.2.3 maintient un `NavMeshQuery` persistent par instance via `WorldMeshManager`. Un PathFinder devrait être réutilisé entre appels.

##### PROBLÈME P2.3 — Continent loading strategy non scalable
**Sévérité : MOYENNE**

`InitializeMapsForContinent()` charge TOUTES les tiles d'un continent au premier appel (boucle `directory_iterator`). Pour le continent Est (mapId=0), c'est ~2000+ fichiers .mmtile. Cela prend plusieurs secondes et utilise beaucoup de mémoire.

Le tile streaming (`EnsureTiles()`) existe en alternative (`_useStreaming=true`), mais n'est pas activé par défaut. HB 6.2.3 utilise un streaming par défaut avec `WorldMeshManager` + `GarbageCollectTime`.

##### PROBLÈME P2.4 — `UpdatePathFollowing` vs `PathFinder::UpdateFollowing` — duplication
**Sévérité : BASSE**

Deux implémentations de raycast shortcut existent :
1. `PathFinder::UpdateFollowing()` — opère sur `m_pathPoints` interne, `erase()` le waypoint
2. `Navigation::UpdatePathFollowing()` — opère sur un array XYZ externe, retourne le meilleur index

La version Navigation.cpp est **meilleure** : elle essaye jusqu'à 5 waypoints ahead (vs 2 dans PathFinder), ne modifie pas le path original, et accumule les stats. PathFinder::UpdateFollowing devrait être supprimée.

##### PROBLÈME P2.5 — Filter locale vs globale inconsistant
**Sévérité : MOYENNE**

Plusieurs fonctions dans Navigation.cpp créent un `dtQueryFilter filter;` local (stack) au lieu d'utiliser `_defaultFilter` :
- `FindNearestPoly()`
- `FindPolysAroundCircle()`
- `FindDistanceToWall()`
- `FindDistanceToWallEx()`
- `IsPointOnNavMesh()`
- `FindRandomPointAroundCircle()`
- `HasLineOfSight()`

Ces filtres locaux ont des area costs par défaut (1.0f partout) et des include/exclude flags par défaut. Ils ignorent les `SetAreaCost()` / `SetIncludeFlags()` configurés globalement. Seuls `Raycast()`, `CalculatePath()` et les sliced functions utilisent `_defaultFilter`.

##### PROBLÈME P2.6 — `FinalizeSlicedFindPath` utilise `DT_STRAIGHTPATH_AREA_CROSSINGS` (pas `ALL_CROSSINGS`)
**Sévérité : BASSE (intéressant)**

`FinalizeSlicedFindPath()` utilise `DT_STRAIGHTPATH_AREA_CROSSINGS` tandis que `PathFinder::BuildPointPath()` utilise `DT_STRAIGHTPATH_ALL_CROSSINGS`. `AREA_CROSSINGS` génère moins de waypoints (seulement quand le type d'area change), ce qui donne un path plus lisse mais avec moins de points de contrôle. C'est la même option utilisée dans le fallback de cette fonction.

#### Points forts identifiés

1. ✅ **API complète type HB RecastManaged** — `FindNearestPoly`, `Raycast`, `FindDistanceToWall`, `QueryPolygons`, `GetPolyWallSegments`, `SetPolyArea/Flags` sont tous implémentés. C'est la base pour `MoveAwayFromEdges` côté C#.
2. ✅ **`NavStats`** — Télémétrie intégrée (timing, shortcuts, raycast hits). Utile pour debug.
3. ✅ **Sliced pathfinding** — `InitSlicedFindPath()` + `UpdateSlicedFindPath()` + `FinalizeSlicedFindPath()` avec calibration adaptive (ms budget). Pattern HB MoP.
4. ✅ **Tile streaming** — `EnsureTiles()` + `EnsureTilesDirectional()` + LRU eviction. Bon pattern pour mémoire.
5. ✅ **Poly area/flags manipulation** — `SetPolyArea()` / `SetPolyFlags()` pour support blackspot (marquer des zones comme coûteuses au runtime).
6. ✅ **`CalculatePathEx`** retourne `PathResult*` avec `status`, `failStep`, `straightPathFlags`, `polyTypes`, `abilityFlags`, `polyRefs` — très riche, identique au pattern HB WoD.
7. ✅ **Off-mesh connections** — support complet via `OffMeshManager` (Add, Load, IsOffMesh).

#### Solutions proposées

| # | Solution | Complexité | Impact |
|---|----------|-----------|--------|
| S2.1 | **Corriger les conversions dans NavBridge.cpp** — toutes les fonctions `_C` doivent passer par `Navigation::` methods (qui font la conversion) au lieu d'appeler `dtNavMeshQuery` directement | Moyenne | **BAS** — C# n'utilise pas NavBridge, mais à corriger pour usage futur |
| S2.2 | Utiliser `_defaultFilter` au lieu de `dtQueryFilter filter;` local dans toutes les fonctions de Navigation.cpp | Facile | **MOYEN** — cohérence des area costs |
| S2.3 | Supprimer `PathFinder::UpdateFollowing()` en faveur de `Navigation::UpdatePathFollowing()` | Facile | **BAS** — nettoyage de duplication |
| S2.4 | Activer tile streaming par défaut (`_useStreaming = true`) ou au moins documenter le choix | Facile | **MOYEN** — temps de chargement initial |
| S2.5 | Pool de PathFinder ou rendre `PathFinder` réutilisable (avoid alloc/dealloc par pathfind) | Moyenne | **BAS** — optimisation performance |

### Phase 3 — NativeMethods.cs + TripperNavigator.cs (P/Invoke layer) ✅ — CORRIGÉ par QC

**Fichiers lus :**
- `Tripper/Navigation/NativeMethods.cs` (587 lignes) — P/Invoke declarations
- `Tripper/Navigation/PathFindResult.cs` (146 lignes) — Rich path result type
- `Tripper/Navigation/PathPostProcessing.cs` — Enum (None=0, MoveAwayFromEdges=1, Randomize=2)
- `Tripper/Navigation/PathPostProcessor.cs` (339 lignes) — C# MoveAwayFromEdges implementation
- `Tripper/Navigation/Navigator.cs` (1697 lignes) — High-level navigation wrapper (TripperNavigator)

#### ⚠️ CORRECTION QC — Phase 3 originale était fausse

L'analyse originale comparait les P/Invoke C# contre les exports **NavBridge.cpp** (`_C` suffixés).
En réalité, le C# appelle les exports **DllMain.cpp** (58 exports, SANS suffixe `_C`).
Les deux API coexistent dans Navigation.dll :
- **DllMain.cpp** : 58 exports avec struct `XYZ { float X,Y,Z; }` — utilisés par C#
- **NavBridge.cpp** : 32 exports avec struct `XYZ_C { float x,y,z; }` et suffixe `_C` — NON utilisés par C#

#### Constat réel : le P/Invoke layer est CORRECT ✅

**Tous les ~45 P/Invoke declarations matchent des exports DllMain.cpp existants.**

Vérification exhaustive des signatures :
| C# P/Invoke | C++ DllMain.cpp export | Match |
|---|---|---|
| `CalculatePath(uint, XYZ, XYZ, bool, out int)` | `CalculatePath(uint, XYZ, XYZ, bool, int*)` | ✅ |
| `CalculatePathEx(uint, XYZ, XYZ, bool)` → `IntPtr` | `CalculatePathEx(uint, XYZ, XYZ, bool)` → `PathResult*` | ✅ |
| `FreePathArr(IntPtr)` / `FreePathResult(IntPtr)` | `FreePathArr(XYZ*)` / `FreePathResult(PathResult*)` | ✅ |
| `FindNearestPoly(uint, XYZ, float, out XYZ)` | `FindNearestPoly(uint, XYZ, float, XYZ*)` | ✅ |
| `FindDistanceToWall(uint, XYZ, float, out XYZ)` | `FindDistanceToWall(uint, XYZ, float, XYZ*)` | ✅ |
| `FindDistanceToWallEx(uint, XYZ, float, out XYZ, out XYZ)` | `FindDistanceToWallEx(uint, XYZ, float, XYZ*, XYZ*)` | ✅ |
| `FindDistanceToWallFromPoly(uint, ulong, XYZ, float, out XYZ, out XYZ)` | `FindDistanceToWallFromPoly(uint, ull, XYZ, float, XYZ*, XYZ*)` | ✅ |
| `Raycast(uint, ulong, XYZ, XYZ, out float, out XYZ, ulong[], out int, int)` | `Raycast(uint, ull, XYZ, XYZ, float*, XYZ*, ull*, int*, int)` | ✅ |
| `HasLineOfSight(uint, XYZ, XYZ)` | `HasLineOfSight(uint, XYZ, XYZ)` | ✅ |
| `SetAreaCost(uint, float)` | `SetAreaCost(uint, float)` | ✅ |
| `GetNavStats(out NavStats)` — 11 champs, 44 bytes | `GetNavStats(NavStats*)` — 11 champs, 44 bytes | ✅ |
| Sliced pathfinding (4 fonctions) | Sliced pathfinding (4 exports) | ✅ |
| Poly area/flags (4 fonctions) | Poly area/flags (4 exports) | ✅ |
| NavStatus helpers (8 fonctions avec EntryPoint) | NavStatus helpers (8 exports) | ✅ |

**Structs :** `XYZ` C# (12 bytes, StructLayout.Sequential) = `XYZ` DllMain (12 bytes). `NavStats` C# (11 champs, 44 bytes) = `NavStats` Navigation.h (11 champs, 44 bytes). `PathResult` C# (5 IntPtr + 3 int = 32 bytes x86) = `PathResult` PathResult.h (5 pointers + 3 values = 32 bytes x86).

#### Ce qui fonctionne réellement (confirmé) :

1. ✅ **MoveAwayFromEdges est ACTIF** — `TripperNavigator.PathPostProcessing` par défaut = `MoveAwayFromEdges`. `FindPath()` appelle `PathPostProcessor.MoveAwayFromEdges()` qui appelle `NativeMethods.FindDistanceToWall/FromPoly/HasLineOfSight` → tous des exports DllMain réels.
2. ✅ **Area costs sont appliqués** — `SetDefaultAreaCosts()` appelle `NativeMethods.SetAreaCost()` → DllMain `SetAreaCost()` → met à jour `_defaultFilter`. PathFinder copie `_defaultFilter` à la construction.
3. ✅ **CalculatePathEx retourne les metadata complètes** — Status, Points, StraightPathFlags, PolyTypes, AbilityFlags, PolyRefs. `FindPath()` les marshalle toutes dans `PathFindResult`.
4. ✅ **Raycast HB-style fonctionne** — retourne le chemin de polygons traversés, pas juste un bool.

#### Problèmes réels restants (Phase 3)

##### P3.1R — IsPartialPath jamais détecté (MOYEN)

```csharp
IsPartialPath = false, // TODO: detect from status flags
```

`PathFindResult.IsPartialPath` est toujours `false`. Il devrait vérifier `Status.HasFlag(PartialResult)`.
Le status flags du C++ contient `DT_PARTIAL_RESULT` quand le path n'atteint pas la destination.

##### P3.2R — Conversions WoW↔Detour dans DllMain : TOUTES correctes ✅

Toutes les fonctions DllMain.cpp passent par `Navigation.cpp` qui utilise `WoWToDetour()`/`DetourToWoW()`.
Pas d'inconsistance de coordonnées sur le chemin d'appel C# → DllMain → Navigation.cpp.

**Note :** Les exports NavBridge.cpp (`_C` suffixés) ont 4 fonctions buggées (pas de conversion de coordonnées) : `Raycast_C`, `FindNearestPoint_C`, `FindRandomPoint_C`, `CreateCorridorForAgent_C`. Mais le C# ne les appelle PAS, donc aucun impact.

##### P3.3R — Local filter vs global filter dans Navigation.cpp (MOYEN)

Phase 2 finding P2.5 toujours valide : certaines fonctions de Navigation.cpp (`FindDistanceToWall`, `FindNearestPoly`, `IsPointOnNavMesh`, etc.) créent un `dtQueryFilter` local (stack) au lieu d'utiliser `_defaultFilter`. Les area costs configurés via `SetAreaCost()` ne sont pas utilisés par ces fonctions.

**Impact pour MoveAwayFromEdges :** `FindDistanceToWall()` utilise un filtre local → include flags = 0xFFFF, exclude = 0, costs = 1.0. Pour la détection de murs, c'est acceptable (on veut détecter les bords de TOUS les polygons). Pas de bug fonctionnel, mais inconsistant.

##### P3.4R — dtPolyRef comments trompeurs (INFO)

Commentaires disent "DT_POLYREF64" mais c'est 32-bit interne, widened à `uint64_t` par `ToExternalRef()`.
Le C# `ulong` est correct pour l'interface. Juste les commentaires à corriger.

#### Solutions Phase 3 (révisées)

| ID | Solution | Effort | Priorité |
|----|----------|--------|----------|
| S3.1R | **Détecter IsPartialPath** via `Status.HasFlag(PartialResult)` | Facile | **MOYEN** — visibilité paths incomplets |
| S3.2R | **Corriger filtres locaux dans Navigation.cpp** — utiliser `_defaultFilter` partout | Facile | **BAS** — cohérence, pas de bug fonctionnel |
| S3.3R | **Corriger bugs NavBridge.cpp** — ajouter WoW↔Detour conversion dans `Raycast_C`, `FindNearestPoint_C`, `FindRandomPoint_C`, `CreateCorridorForAgent_C` | Facile | **BAS** — fonctions non utilisées par C# |
| S3.4R | **Corriger commentaires dtPolyRef** | Trivial | **BAS** |

### Phase 4 — Navigator.cs (Styx-level movement, waypoint logic, CTM) ✅

**Fichiers lus :**
- `Styx/Logic/Pathing/Navigator.cs` (631 lignes) — Bot-level navigator (MoveTo, path generation, waypoint management)
- `Styx/Logic/Pathing/Interop/LocalPlayerMover.cs` (177 lignes) — IMover implementation (CTM wrapper)
- `Styx/Logic/Pathing/StuckHandler.cs` (329 lignes) — Stuck detection + recovery (HB 4.3.4 pattern)
- `Styx/Logic/Pathing/StuckDetector.cs` (26 lignes) — Simple static stuck check
- `Styx/Logic/Pathing/MoveResult.cs` (17 lignes) — Enum
- `Styx/Logic/Pathing/IStuckHandler.cs` (30 lignes) — Interface
- `Styx/Logic/Pathing/BlackspotManager.cs` (595 lignes, first 200) — Blackspot management
- `Styx/Logic/Pathing/MeshHeightHelper.cs` (111 lignes) — Mesh height search
- `Styx/Logic/Pathing/AvoidanceManager.cs` (274 lignes, first 80) — Dynamic mob avoidance
- `Styx/WoWInternals/WoWMovement.cs` (467 lignes) — Click-to-Move, movement control
- **Références HB 4.3.4** : `Navigator.cs` (371 lines), `MeshNavigator.cs` (669 lines)

#### Architecture

```
Behavior Trees → Navigator.MoveTo(destination)
                    ├── TripperNavigator.FindPath() → PathFindResult
                    ├── waypoint advancement (2D distance + Z < 4.5f)
                    ├── stair handling (Z threshold)
                    └── WoWMovement.ClickToMove(clickPoint)
                         └── ASM injection → CGPlayer_C__ClickToMove
```

**Comparaison avec HB 4.3.4 :**
- HB utilise un pattern `INavigationProvider` / `MeshNavigator` + `IPlayerMover` / `ClickToMoveMover` — séparation propre
- CopilotBuddy fusionne tout dans `Navigator.cs` (static) — plus simple mais moins modulaire
- HB utilise `MeshMovePath` avec index tracking et gestion d'off-mesh connections (elevator, portal, interact unit/object)
- CopilotBuddy utilise `List<WoWPoint>` brut sans metadata — perte des flags, polyTypes, abilityFlags

#### P4.1 — Path perd toute metadata (CRITIQUE)

`Navigator.MoveTo()` appelle `TripperNavigator.FindPath()` qui retourne un `PathFindResult` riche (Points, Flags, Polygons, AbilityFlags, PolyTypes), mais **seuls les Points sont conservés** :

```csharp
foreach (var point in result.Points)
    _currentPath.Add(new WoWPoint(point.X, point.Y, point.Z));
```

Les `StraightPathFlags`, `AreaType`, `AbilityFlags` et `PolygonReference` sont **jetés**.
Conséquence : impossible de détecter les off-mesh connections (flag `DT_STRAIGHTPATH_OFFMESH_CONNECTION = 4`),
les changements de terrain (Lava, Water), ou les portals/elevators pendant le suivi de path.

**HB 4.3.4** : `MeshMovePath` conserve tout et `method_4()` gère elevator, portal, interact unit/object basés sur `PolyTypes[index]`.

#### P4.2 — Pas de gestion off-mesh connection pendant le suivi (HAUTE)

HB 4.3.4 `MeshNavigator.method_4()` détecte `(Flags[index-1] & 4) != 0` (off-mesh connection) et
branche vers un handler spécifique selon le `AreaType` : Elevator (wait/ride), Portal (interact),
InteractUnit (move + interact), InteractObject (move + interact).

CopilotBuddy n'a **aucune** de ces gestions. Si le path traverse un elevator ou un portal,
le bot va simplement ClickToMove au prochain waypoint et se bloquer.

#### P4.3 — Waypoint advance ne vérifie pas les off-mesh boundaries (HAUTE)

```csharp
if (distance2DSqr < waypointPrecision * waypointPrecision && zDiff < 4.5f)
{
    _currentPathIndex++;
    // ... advance
}
```

**Identique à HB 4.3.4** (`method_9` : 2D distance < PathPrecisionSqr + |Z| < 4.5).
Mais HB vérifie AVANT d'avancer si `Flags[index-1] & 4` (off-mesh), et si oui, NE skip PAS
et appelle `method_4()` pour le handler d'off-mesh.

CopilotBuddy skip aveuglément tous les waypoints y compris ceux marqués off-mesh.

#### P4.4 — "Push waypoint ahead" potentiellement dangereux (MOYEN)

```csharp
// HB: Push waypoint ahead by PathPrecision in movement direction
clickPoint = nextPoint + direction * PathPrecision;
```

HB 4.3.4 fait la même chose (`smethod_0` : `vector3_0 += vector2 * PathPrecision`).
**Problème potentiel :** la poussée est en 3D (inclut Z) pas seulement 2D. Sur du terrain en pente,
ça pousse le click point sous ou au-dessus du terrain. HB fait pareil donc c'est acceptable,
mais ça peut contribuer aux blocages sur terrain non plat.

De plus, pour la gestion d'escaliers, le code désactive correctement le push ahead (sort en MoveResult.Moved tôt) ✅.

#### P4.5 — LocalPlayerMover.MoveTowards limite la distance de click à 8 yards (MOYEN)

```csharp
if (distance > 10f)
{
    // Click on a point 8 yards away in the direction of target
    direction.Normalize();
    targetPoint = new WoWPoint(
        ObjectManager.Me.Location.X + direction.X * 8f, ...);
}
```

Le ClickToMove de WoW 3.3.5a n'a pas de limite de distance de click (contrairement aux versions plus récentes).
Limiter à 8 yards **force le bot à re-cliquer fréquemment**, ce qui ralentit le mouvement et rend la trajectoire saccadée.

**HB 4.3.4** n'a PAS cette limitation — il clique directement au waypoint pushé.

**Mais attention** : Navigator.MoveTo() appelle directement `WoWMovement.ClickToMove()`, PAS `PlayerMover.MoveTowards()`.
Donc cette limitation de LocalPlayerMover n'est PAS utilisée par le flow principal !
Elle n'affecte que si quelqu'un appelle `PlayerMover.MoveTowards()` directement (ex: un plugin custom).

#### P4.6 — StuckHandler bien implémenté ✅ — intégré via ActionMoveToPoi ⚠️ partiel

Le StuckHandler suit le pattern HB 4.3.4 (Class472/Class485) :
1. Detect stuck via expected distance vs actual path distance ✅
2. Escalation : Dismount → Jump (x3) → StrafeForwardLeft → StrafeForwardRight → StrafeLeft → StrafeRight → Blackspot + Reverse ✅
3. Reset unstick attempts quand le joueur s'éloigne de 10 yards ✅
4. `RefreshClickToMove()` pendant les strafes pour maintenir la direction ✅

**INTÉGRATION :** `IsStuck()` n'est **pas appelé** dans `Navigator.MoveTo()` lui-même,
**MAIS** `ActionMoveToPoi.Run()` (CommonBehaviors/Actions/ActionMoveToPoi.cs, ligne 138) appelle :
```csharp
if (moveResult == MoveResult.Moved && _stuckCheckTimer.IsFinished)
{
    _stuckCheckTimer.Reset();
    if (Navigator.StuckHandler.IsStuck())
        Navigator.StuckHandler.Unstick();
}
```
Reset quand la destination change (ligne 90). Timer 2 secondes entre checks.

**ActionMoveToPoi est utilisé dans le flow principal :** LevelBot.cs lignes 437, 604, 778.
→ Le StuckHandler **fonctionne** pour GrindBot/QuestBot.

**TROU RESTANT :** 3 appels directs à `Navigator.MoveTo()` dans LevelBot.cs ne passent PAS par ActionMoveToPoi :
- L306 : corpse run (`Navigator.MoveTo(StyxWoW.Me.CorpsePoint)`)
- L331 : loot nearby (`Navigator.MoveTo(((WoWObject)ctx).Location)`)
- L1099 : move to hotspot (`Navigator.MoveTo(hotspot)`)
→ Ces 3 cas n'ont **aucune** stuck detection.

HB 4.3.4/6.2.3 : `StuckHandler.IsStuck()` est intégré directement dans le NavigationProvider, pas dans l'action appelante. C'est plus robuste car TOUS les appelants en bénéficient.

#### P4.7 — Pas de gestion des paths partiels (MOYEN)

Quand `TripperNavigator.FindPath()` retourne un path partiel (il n'atteint pas la destination complète),
`PathFindResult.IsPartialPath` est fixé à `false` avec un `// TODO: detect from status flags`.

HB 4.3.4 : affiche un warning orange et continue avec le path partiel vers le point le plus proche atteignable.
CopilotBuddy continue aussi, mais sans savoir que le path est partiel, il n'affiche aucun warning
et ne peut pas décider de régénérer le path plus tard.

#### P4.8 — Pas d'ajout de +2f Z pour Water/Lava (BAS)

HB 4.3.4 `method_6()` :
```csharp
if (areaType == AreaType.Lava || areaType == AreaType.Water)
    vector.Z += 2f;
```

CopilotBuddy ne fait pas cette correction car il ne conserve pas les PolyTypes (voir P4.1).
Résultat : dans l'eau/lave, le bot clique sous la surface au lieu de nager à la surface.

#### P4.9 — WoWMovement.ClickToMove fonctionne ✅

L'implémentation CTM est correcte :
- Appelle `CGPlayer_C__ClickToMove` via injection ASM en EndScene thread ✅
- Utilise AllocatedMemory pour les paramètres (struct sérialisée) ✅
- NaN validation avant CTM ✅
- ClickToMoveStop via `CGPlayer_C__ClickToMoveStop` ✅
- Full MovementDirection API via Lua (MoveForwardStart/Stop, etc.) ✅

#### P4.10 — Navigator.MoveTo appelé potentiellement chaque frame (INFO)

Les behavior trees appellent `MoveTo()` à chaque tick. Si la destination n'a pas changé,
seulement le waypoint advancement + ClickToMove s'exécutent (pas de recalcul du path). ✅

**Cependant** : sans stuck detection intégrée (P4.6), si le bot est bloqué,
il continue de ClickToMove au même waypoint indéfiniment sans tenter de se débloquer.

#### Solutions Phase 4

| ID | Solution | Effort | Priorité |
|----|----------|--------|----------|
| S4.1 | **Conserver PathFindResult metadata** : stocker Flags[], PolyTypes[], AbilityFlags[] en parallèle de _currentPath | Moyenne | **CRITIQUE** — nécessaire pour P4.2, P4.3, P4.8 |
| S4.2 | **Implémenter off-mesh connection handling** pendant le suivi de path (elevator, portal, interact) — pattern HB 4.3.4 method_4 | Élevé | **HAUTE** — bot se bloque sur elevators sinon |
| S4.3 | **Intégrer StuckHandler.IsStuck() dans MoveTo()** — vérifier à chaque call et brancher vers Unstick() | Facile | **CRITIQUE** — stuck detection morte sinon |
| S4.4 | **Ajouter +2f Z pour Water/Lava** quand PolyType est Water ou Lava (après S4.1) | Facile | **BASSE** — amélioration pour la nage |
| S4.5 | **Détecter les paths partiels** via status flags et logger un warning | Facile | **MOYEN** — visibilité |
| S4.6 | Vérifier Flags[index] pour off-mesh avant d'avancer le waypoint index | Facile (après S4.1) | **HAUTE** — évite skip d'off-mesh |

### Phase 5 — Mouvement (CTM, Flightor, PlayerMover)

**Fichiers analysés :**
- `Styx\WoWInternals\WoWMovement.cs` (467 lignes) — ClickToMove + mouvement Lua
- `Styx\Logic\Pathing\Interop\LocalPlayerMover.cs` (177 lignes) — IMover wrapper
- `Styx\Logic\Pathing\Flightor.cs` (604 lignes) — Vol (montures volantes)
- `Styx\Logic\Pathing\WoWPoint.cs` (226 lignes) — Structure 3D point

**Résumé :** La couche de mouvement bas-niveau (CTM, Lua) est correctement implémentée.
Flightor est fonctionnel pour le vol basique mais simplifié par rapport à HB 6.2.3.
Le principal problème est l'absence de lien entre le mouvement et la navigation (StuckHandler non intégré, voir P4.6).

#### P5.1 — WoWMovement.ClickToMove ✅ CORRECT

L'implémentation CTM est correcte et complète :
- `CallClickToMove()` via injection ASM en EndScene thread (`CGPlayer_C__ClickToMove` = 0x00727400) ✅
- `AllocatedMemory` pour la struct de paramètres (24 octets sérialisés) ✅
- Validation NaN avant envoi (`float.IsNaN(X) || float.IsNaN(Y) || float.IsNaN(Z)`) ✅
- `ClickToMoveStop()` via `CGPlayer_C__ClickToMoveStop` (0x0072B3A0) ✅
- Lecture statut CTM depuis `ClickToMoveInfoStruct` (base 0xCA11D8) ✅
- Full `MovementDirection` API via Lua (MoveForwardStart/Stop, StrafeLeft/Right, etc.) ✅

#### P5.2 — LocalPlayerMover.MoveTowards 8yd cap inutilisé (INFO)

```csharp
if (distance > 10f)
{
    direction.Normalize();
    targetPoint = new WoWPoint(
        ObjectManager.Me.Location.X + direction.X * 8f, ...);
}
```

LocalPlayerMover limite les clicks à 8 yards si > 10yd de distance.
**Mais** : `Navigator.MoveTo()` n'utilise PAS `PlayerMover.MoveTowards()` — il appelle
directement `WoWMovement.ClickToMove()`. Ce cap de 8yd n'affecte que les appels directs
à `Navigator.PlayerMover.MoveTowards()` (plugins, Flightor).

**Impact réel :** Flightor utilise `Navigator.PlayerMover.MoveTowards()` pour le CTM
pendant le vol, donc le cap de 8yd S'APPLIQUE au vol. Cela force des re-clicks fréquents
pendant le vol, rendant le mouvement aérien saccadé. WoW 3.3.5a n'a pas de limite native.

#### P5.3 — Flightor: obstacle avoidance par TraceLine (MOYEN)

```csharp
if (GameWorld.TraceLine(myLocation, flightPoint, HitFlags.Collision))
{
    // Try progressive pitch/heading angles: 15°, 30°, 45°, ... 150°
    for (float pitchIncrement = 15f; pitchIncrement <= 150f; pitchIncrement += 15f)
    {
        // Test up, down, left, right at each angle
    }
}
```

Flightor utilise `GameWorld.TraceLine()` (raycast monde réel, pas navmesh) pour détecter
les obstacles pendant le vol. C'est correct car le navmesh ne couvre pas l'espace aérien.
La stratégie d'angles progressifs (15°→150° en pitch/heading) est fonctionnelle.

**Limitation :** Un seul rayon est testé par direction candidate. Si l'obstacle est large
(flanc de montagne), le rayon peut passer au-dessus mais la trajectoire réelle heurte
le côté. HB 6.2.3 utilise des annotations de vol (FlightorAnnotation) pour définir
des zones interdites et des corridors aériens pré-définis — bien plus robuste.

#### P5.4 — Flightor: anti-stuck basique (MOYEN)

```csharp
WoWMovement.Move(MovementDirection.Backwards);
Thread.Sleep(1000);
WoWMovement.MoveStop(MovementDirection.Backwards);
WoWMovement.Move(MovementDirection.StrafeRight);
Thread.Sleep(1000);
WoWMovement.MoveStop(MovementDirection.StrafeRight);
```

Le stuck handler de Flightor est un simple recul arrière + strafe droite + avance gauche.
C'est fonctionnel mais **bloquant** (Thread.Sleep dans le pulse thread).
HB 6.2.3 Flightor utilise des coroutines async pour les manœuvres de dégagement.

Pour WotLK (synchrone), c'est acceptable mais peut causer des blocages de 3+ secondes
où le bot ne répond pas aux événements (combat, PvP).

#### P5.5 — Flightor: MountHelper complet ✅

```csharp
public static bool CanMount => !ObjectManager.Me.Mounted && !ObjectManager.Me.IsIndoors;
public static bool Mounted => ObjectManager.Me.Mounted;
public static void MountUp() { /* Aura flight form (Druid) or random flying mount */ }
```

MountHelper gère correctement :
- Détection de monture volante équipée ✅
- Support Forme de vol druide ✅
- Check Cold Weather Flying pour Northrend (mapId 571) ✅
- Vérification intérieur/extérieur ✅
- Dismount simple via CancelAura/Dismount Lua ✅

#### P5.6 — Flightor: pas d'annotations de vol (MOYEN)

HB 6.2.3 utilise `FlightorAnnotation` + `FlightorNavigation` — des fichiers de données par map
qui définissent des zones indoor intérieures, des entrées, des points de dismount obligatoire.
Quand un trajet passe par une zone indoor, HB détecte automatiquement et :
1. Se pose avant l'entrée
2. Navigue au sol à travers la zone indoor
3. Redécolle de l'autre côté

CopilotBuddy n'a **aucune** de ces annotations. Le bot volant va soit :
- Tenter de voler à travers un bâtiment (heurte les murs)
- Rester au sol quand un fallback ground nav est possible (60yd check)

Pour WotLK, ce n'est pas critique car les zones indoor avec passage aérien sont rares,
mais ça affecte Dalaran (interdit de voler à l'intérieur), Icecrown Citadel entrance, etc.

#### P5.7 — WoWPoint struct correcte ✅

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct WoWPoint
{
    public float X, Y, Z; // 12 bytes, WoW coordinate system
}
```

Structure 12 octets correcte, `StructLayout.Sequential` pour P/Invoke.
Méthodes Distance/Distance2D/DistanceSqr implémentées ✅.
Opérateurs +, -, *, == implémentés ✅.

#### Solutions Phase 5

| ID | Solution | Effort | Priorité |
|----|----------|--------|----------|
| S5.1 | **Supprimer le cap 8yd de LocalPlayerMover** ou le remplacer par une distance plus grande (30yd) pour le vol | Facile | **MOYEN** — améliore le vol |
| S5.2 | **Ajouter annotations de vol Dalaran** — au minimum interdire le vol dans Dalaran city (zoneId 4395) et forcer ground nav | Facile | **MOYEN** — évite les blocages sur la ville principale WotLK |
| S5.3 | **Rendre l'anti-stuck Flightor non-bloquant** — utiliser WaitTimer au lieu de Thread.Sleep | Facile | **BAS** — améliore la réactivité |

### Phase 6 — Comparaison détaillée HB 6.2.3 (WoD)

**Fichiers HB 6.2.3 analysés :**
- `Styx\Pathing\MeshNavigator.cs` (1318 lignes) — NavigationProvider principal
- `Styx\Pathing\Navigator.cs` (358 lignes) — Static wrapper (PlayerMover, NavigationProvider)
- `Styx\Pathing\MeshMovePath.cs` (42 lignes) — Conteneur path + index + metadata
- `Styx\Pathing\StuckHandler.cs` (33 lignes) — Abstract base
- `Styx\Pathing\NavigationProvider.cs` (102 lignes) — Abstract base avec StuckHandler property
- `Styx\Pathing\Flightor.cs` (1690 lignes) — Vol avec annotations + coroutines
- `Styx\Pathing\KeyboardMover.cs` (85 lignes) — Alternative IPlayerMover
- `Tripper\Navigation\WowNavigator.cs` (809 lignes, sessions antérieures)
- `Tripper\Navigation\PathPostProcessing.cs` (sessions antérieures)

**Résumé :** HB 6.2.3 a une architecture de mouvement significativement plus mature.
Les différences les plus critiques sont le maintien des métadonnées de path, l'intégration
du StuckHandler, la gestion des off-mesh connections, et le système de raycast de validation
pendant le suivi de path. CopilotBuddy a les bonnes fondations (CTM, path finding) mais
manque l'étage supérieur (path following intelligent).

---

#### P6.1 — Architecture de path following : MeshMovePath vs raw WoWPoint[] (CRITIQUE)

**HB 6.2.3 :**
```csharp
public class MeshMovePath
{
    public PathFindResult Path { get; set; }  // Points[], Flags[], PolyTypes[], AbilityFlags[], Manager
    public int Index { get; set; }
    public bool IsExitingGarrison { get; set; }
}
```
`MeshNavigator` conserve un `CurrentMovePath` qui encapsule le `PathFindResult` COMPLET.
À chaque appel de `MovePath()`, il utilise `Path.Flags[index]`, `Path.PolyTypes[index]`,
`Path.AbilityFlags[index]` pour décider du comportement : off-mesh, elevator, water Z offset, etc.

**CopilotBuddy :**
`Navigator.MoveTo()` extrait `pathResult.Points` et jette tout le reste (Flags, PolyTypes, AbilityFlags).
Le path following n'a accès qu'à un `WoWPoint[]` et un index. Aucune décision contextuelle possible.

**Gap : CRITIQUE** — c'est le problème structurel #1. Sans métadonnées, rien de ce qui suit ne peut fonctionner.

---

#### P6.2 — Off-mesh connection dispatch (CRITIQUE)

**HB 6.2.3** `MeshNavigator.MovePath()` :
```csharp
if (path.Index > 0 && (path.Path.Flags[path.Index - 1] & 4) != 0)
{
    return this.method_18(path); // Off-mesh dispatch
}
```
`method_18()` route vers le handler correct selon `AreaType` :
- `AreaType.Elevator` → `method_20()` : wait for elevator, ride, detect arrive
- `AreaType.Portal` / `HordePortal` / `AlliancePortal` → `method_23()` : find + interact portal
- `AreaType.InteractUnit` → `method_22()` : move to + interact NPC
- `AreaType.InteractObject` → `method_21()` : move to + interact game object
- Default (Run+Jump) → `method_19()` : continue movement normally

**CopilotBuddy :** Aucune gestion. Le bot ClickToMove au waypoint suivant et se bloque.

**Gap : CRITIQUE** — elevator handling est nécessaire pour Thunder Bluff, Undercity, certains donjons.

---

#### P6.3 — StuckHandler intégré dans le movement loop (CRITIQUE)

**HB 6.2.3** `MeshNavigator.method_24()` (ground movement) :
```csharp
if (Navigator.NavigationProvider.StuckHandler.IsStuck())
{
    Navigator.NavigationProvider.StuckHandler.Unstick();
    return MoveResult.UnstickAttempt;
}
```
Appelé **à chaque frame** de mouvement au sol. Le StuckHandler est aussi `Reset()` :
- Quand un waypoint est atteint (advance)
- Quand on entre dans un handler off-mesh (elevator, portal)
- Quand le path change

**CopilotBuddy :** `StuckHandler.IsStuck()` existe et est bien implémenté (Phase 4, P4.6).
Il n'est pas appelé dans `Navigator.MoveTo()` lui-même, **MAIS** `ActionMoveToPoi.Run()` l'appelle
après chaque `MoveResult.Moved` (timer 2s). Le flow principal GrindBot/QuestBot EST couvert.

**Gap : HAUTE** (pas CRITIQUE) — stuck detection fonctionne pour le flow ActionMoveToPoi.
**Trou :** Les 3 appels directs `Navigator.MoveTo()` dans LevelBot (corpse run L306, loot L331, hotspot L1099)
n'ont PAS de stuck detection. Solution : intégrer IsStuck() dans MoveTo() directement (comme HB).

---

#### P6.4 — Water/Lava Z offset (BAS)

**HB 6.2.3** `MeshNavigator.method_25()` :
```csharp
if (areaType == AreaType.Lava || areaType == AreaType.Water)
{
    vector.Z += 2f;
}
```

**CopilotBuddy :** Impossible car PolyTypes jetés (voir P6.1).

**Gap : BAS** — le bot nage juste sous la surface au lieu de dessus.

---

#### P6.5 — Waypoint push-ahead avec validation raycast (MOYEN)

**HB 6.2.3** `MeshNavigator.method_25()` + `method_26()` :
```csharp
// Seulement si CTM mover ET pas premier/dernier waypoint
if (Navigator.PlayerMover is ClickToMoveMover && index > 0 && index < Points.Length - 1)
{
    this.method_26(path.Manager, ref vector);
}
```
`method_26()` pousse le click point de `PathPrecision` yards en avant dans la direction du mouvement,
MAIS vérifie d'abord par **raycast navmesh** que la position poussée est navigable.
Si le raycast échoue (mur dans la direction poussée), le push est annulé.

**CopilotBuddy :** Pas de push-ahead raycast validé. Le push utilise la direction 3D brute
sans vérification de navigabilité.

**Gap : MOYEN** — peut contribuer aux virages coupés mentionnés en symptôme #1.

---

#### P6.6 — Path start optimization (skip nodes visibles) (MOYEN)

**HB 6.2.3** `MeshNavigator.method_14()` :
Quand un path est généré, HB cherche le waypoint le plus éloigné visible depuis la position actuelle
par **raycast navmesh** (`method_13` : Raycast sur le mesh, vérifie poly areas traversées).
Il skip les waypoints intermédiaires inutiles.

Garde-fous :
- Ne skip PAS les waypoints off-mesh (Flags & 4)
- Vérifie que les polys traversés sont Ground/Water/Road/faction-matching
- Ne skip PAS les premiers waypoints elevator/portal/interact

**CopilotBuddy :** Aucune optimisation de début de path. Le bot commence toujours au waypoint 0
et marche tous les points intermédiaires même si la destination est en ligne droite visible.

**Gap : MOYEN** — gâche du temps mais pas de blocage. Amélioration de fluidité de mouvement.

---

#### P6.7 — Path still valid check (raycast-based) (HAUTE)

**HB 6.2.3** `MeshNavigator.method_15()` :
Avant de continuer un path existant, HB vérifie via **raycast navmesh** que le joueur
est toujours sur le path (distance point-segment < PathPrecisionSqr). Si non, régénère.

Aussi : `method_9()` vérifie que la destination du path n'a pas changé et que le joueur
est encore sur le path avant de réutiliser l'ancien.

**CopilotBuddy :** Vérifie uniquement si la destination a changé (via `_currentEndPosition`).
Pas de vérification que le joueur est toujours sur le path. Si le joueur est knocked-back
ou TP par un mob, il continue de suivre l'ancien path qui part d'une position obsolète.

**Gap : HAUTE** — paths obsolètes = bot qui marche dans la mauvaise direction.

---

#### P6.8 — Door handling automatique (BAS pour WotLK)

**HB 6.2.3** `MeshNavigator.method_7()` + `method_8()` :
Détecte automatiquement les portes (`WoWGameObjectType.Door`) sur le chemin,
vérifie si elles sont fermées/interactables/déverrouillées, et les ouvre automatiquement.

**CopilotBuddy :** Aucune gestion des portes.

**Gap : BAS** — peu de portes interactables en open world WotLK, mais utile pour certains donjons.

---

#### P6.9 — Flight path (taxi) detection (BAS pour bot)

**HB 6.2.3** `MeshNavigator.method_10()` :
```csharp
if (distance > 160000f /* 400yd² */ && FlightPaths.ShouldTakeFlightpath(...))
    return true; // Use taxi instead of walking
```

**CopilotBuddy :** Pas de détection automatique de taxi.

**Gap : BAS** — le bot préfère marcher, ce qui est souvent voulu pour le grind/quest.

---

#### P6.10 — Flightor: annotations vs TraceLine (MOYEN)

**HB 6.2.3 Flightor** (1690 lignes) :
- **Annotations de vol** par map (`FlightorAnnotation`, `FlightorNavigation`) : données pré-définies
  avec des `IndoorEntrance` (point + rayon + dismount flag) pour détecter les zones indoor
- **Coroutines async** pour les manœuvres (dismount, ground nav indoor, remount)
- **Blackspot integration** pour les zones interdites de vol
- Détection indoor : si start ou destination dans zone annotée, route via entrées indoor
- Support multi-entrance : navigue séquentiellement à travers les entrées

**CopilotBuddy Flightor** (604 lignes) :
- **TraceLine world** uniquement pour obstacle avoidance ✅
- **Angles progressifs** (15°-150°) pour contourner ✅
- Pas d'annotations, pas de gestion indoor
- Anti-stuck bloquant (Thread.Sleep)

**Gap : MOYEN** — pour WotLK, TraceLine suffit pour le vol en zone ouverte.
Problème principal : Dalaran (zone interdite de vol en ville) et entrées de donjons.

---

#### P6.11 — IPlayerMover architecture (INFO)

**HB 6.2.3 :**
- `ClickToMoveMover` (default) — CTM direct ✅
- `KeyboardMover` — alternative avec SetFacing + MoveForward
- `Navigator.PlayerMover` swappable à runtime

**CopilotBuddy :**
- `LocalPlayerMover` avec cap 8yd (voir P5.2)
- `Navigator.PlayerMover` property existe ✅

**Gap : INFO** — l'architecture est en place. KeyboardMover pourrait être utile comme fallback
si CTM est détecté par le serveur.

---

#### P6.12 — FindPath exécuté sur un thread séparé avec frame release (HAUTE)

**HB 6.2.3** `MeshNavigator.FindPath()` :
```csharp
Task<PathFindResult> task = Task.Factory.StartNew(() => Nav.FindPath(start, end));
if (!task.Wait(10))
{
    using (StyxWoW.Memory.ReleaseFrame(true))
    {
        while (!task.Wait(1000 / TicksPerSecond))
        {
            StyxWoW.Memory.ClearCache();
            ObjectManager.Update();
            WoWMovement.Pulse();
            // Cancel if in combat
            this.bool_1 = StyxWoW.Me.IsActuallyInCombat;
        }
    }
}
```
Le pathfinding tourne sur un thread séparé. Pendant l'attente, HB :
1. Relâche le frame lock (permet au jeu de continuer)
2. Continue à pulser ObjectManager et WoWMovement
3. Annule la recherche si le bot entre en combat (via progress callback)

**CopilotBuddy :** `TripperNavigator.FindPath()` est synchrone, exécuté sur le thread EndScene.
Un path complexe (long trajet, beaucoup de polys) bloque le bot pendant toute la durée.

**Gap : HAUTE** — les longs trajets peuvent causer un freeze visible du bot.
Pour WotLK, les paths sont généralement courts (navmesh dense), mais les paths
cross-continent (ex: Kalimdor) peuvent prendre plusieurs secondes.

---

#### P6.13 — Alive/Dead query filter (BAS)

**HB 6.2.3** `MeshNavigator.method_28()` :
```csharp
if (StyxWoW.Me.IsAlive)
    Nav.QueryFilter.IncludeFlags |= AbilityFlags.OnlyWhileAlive;
else
    Nav.QueryFilter.ExcludeFlags |= AbilityFlags.OnlyWhileAlive;
```

**CopilotBuddy :** Pas de distinction alive/dead dans les ability flags.

**Gap : BAS** — certains off-mesh links WoD ont des restrictions alive/dead, rare en WotLK.

---

#### P6.14 — Combat abort pendant pathfinding (HAUTE)

**HB 6.2.3 :** Le callback `OnPathFindProgress` vérifie régulièrement si le bot est en combat.
Si oui, annule le pathfinding après 4 secondes (`e.Cancel = true`) pour permettre au bot
de réagir au combat.

**CopilotBuddy :** Le pathfinding est synchrone et ne peut pas être interrompu.
Si un mob attaque pendant un pathfinding de 2+ secondes, le bot ne réagit pas.

**Gap : HAUTE** — couplé à P6.12. Le pathfinding synchrone sans interruption combat
est le second problème structurel majeur.

---

#### Tableau récapitulatif Phase 6

| # | Feature HB 6.2.3 | CopilotBuddy | Priorité |
|---|-------------------|-------------|----------|
| P6.1 | MeshMovePath (metadata complète) | Points[] seulement | **CRITIQUE** |
| P6.2 | Off-mesh dispatch (elevator/portal/interact) | Aucun | **CRITIQUE** |
| P6.3 | StuckHandler dans movement loop | Fonctionne via ActionMoveToPoi, trou sur 3 appels directs MoveTo() | **HAUTE** |
| P6.4 | Water/Lava Z+2f | Impossible (metadata jetée) | **BAS** |
| P6.5 | Push-ahead avec raycast validation | Push brut sans validation | **MOYEN** |
| P6.6 | Path start optimization (skip visible) | Aucune | **MOYEN** |
| P6.7 | Path still valid check (raycast) | Destination check seulement | **HAUTE** |
| P6.8 | Door handling automatique | Aucun | **BAS** |
| P6.9 | Flight path (taxi) auto detection | Aucun | **BAS** |
| P6.10 | Flightor annotations indoor/outdoor | TraceLine seulement | **MOYEN** |
| P6.11 | IPlayerMover (CTM + Keyboard) | CTM seulement | **INFO** |
| P6.12 | FindPath async avec frame release | Synchrone bloquant | **HAUTE** |
| P6.13 | Alive/Dead ability filter | Aucun | **BAS** |
| P6.14 | Combat abort pendant pathfinding | Impossible (synchrone) | **HAUTE** |

#### Solutions Phase 6

| ID | Solution | Effort | Priorité |
|----|----------|--------|----------|
| S6.1 | **Conserver PathFindResult complet** dans Navigator (= S4.1, issue structurelle) | Moyenne | **CRITIQUE** |
| S6.2 | **Implémenter off-mesh dispatch** pattern HB 6.2.3 method_18 (= S4.2) | Élevé | **CRITIQUE** |
| S6.3 | **Intégrer StuckHandler.IsStuck() dans MoveTo()** directement pour couvrir TOUS les appelants (= S4.3) | Facile | **HAUTE** |
| S6.4 | **Ajouter path validity check** : vérifier position joueur vs path avant de continuer | Moyenne | **HAUTE** |
| S6.5 | **Path start skip** : raycast pour skip les waypoints initiaux visibles | Moyenne | **MOYEN** |
| S6.6 | **Push-ahead raycast** : valider le push par raycast navmesh avant de cliquer | Moyenne | **MOYEN** — export C++ Raycast fonctionnel ✅ |
| S6.7 | **FindPath async** : exécuter pathfinding sur un thread séparé avec cancellation | Élevé | **HAUTE** — amélioration significative mais complexe dans architecture synchrone |

### Phase 7 — Corrections QC et plan d'action

#### Résumé du contrôle qualité

Le QC a vérifié TOUTES les phases en relisant le code source réel. Résultat :

**Phases correctes :** Phase 1 (avec ajustements de sévérité), Phase 2, Phase 4, Phase 5, Phase 6
**Phase corrigée :** Phase 3 → entièrement réécrite (comparait contre la mauvaise API C++)

#### Ce qui est RÉSOLU (aucune action nécessaire)

| Item | Statut | Explication |
|------|--------|-------------|
| P/Invoke layer | ✅ SOLIDE | Tous les noms matchent DllMain.cpp, structs alignées |
| MoveAwayFromEdges | ✅ ACTIF | PathPostProcessor appelle des exports réels, actif par défaut |
| Area costs | ✅ APPLIQUÉS | SetDefaultAreaCosts → DllMain → _defaultFilter → PathFinder |
| Raycast HB-style | ✅ | Signature 9 params identique entre C# et DllMain |
| NavStats struct | ✅ | 11 champs = 44 bytes, matchent parfaitement |
| Coordinate conversion | ✅ | DllMain→Navigation.cpp→PathFinder : toutes les conversions correctes |
| Blackspot system | ✅ | SetPolyArea/SetPolyFlags fonctionnels via DllMain |
| OffMeshManager | ✅ | Complet et fonctionnel |

#### Ce qui reste à corriger (par priorité)

**CRITIQUE — Styx Navigator.MoveTo() :**
1. Stocker `PathFindResult` complet (Flags, PolyTypes, AbilityFlags, PolyRefs) aux côtés de `_currentPath`

**HAUTE :**
2. Intégrer `StuckHandler.IsStuck()` directement dans `MoveTo()` pour couvrir TOUS les appelants (corpse run, loot, hotspot)
3. Implémenter off-mesh connection dispatch (elevators, portals, interact objects) basé sur PolyTypes
4. Ajouter path validity check (position joueur vs path via raycast)
5. **Push-ahead raycast** : valider le push avant ClickToMove (cause directe des virages coupés)
6. **MoveAwayFromEdges : ajouter logging dans le try/catch** pour détecter les crashes silencieux
7. FindPath async avec combat abort (complexe mais haute valeur)

**MOYEN :**
8. Détecter `IsPartialPath` dans PathFindResult
9. Ajouter correction hauteur +0.5f dans BuildPointPath
10. Vérifier MoveAwayFromEdges EdgeDistance=2.0f (potentiellement trop faible)
11. Path start skip (raycast pour skip waypoints visibles)

**BAS :**
12. Corriger bugs NavBridge.cpp (4 fonctions `_C` sans conversion coords — C# ne les appelle pas)
13. Unifier filtres locaux dans Navigation.cpp → utiliser `_defaultFilter`
14. Supprimer debug file write dans HaveTile()
15. Supprimer `PathFinder::UpdateFollowing()` en faveur de `Navigation::UpdatePathFollowing()`

---

## Paramètres Detour clés à documenter

| Paramètre | Valeur actuelle | Valeur recommandée | Notes |
|-----------|----------------|-------------------|-------|
| polyPickExtent (Y) | 20.0f | 20.0f ✅ | Identique à HB 6.2.3, supporte grottes/sous-sols |
| polyPickExtent (XZ) | 3.0f | 3.0f ✅ | Identique à HB 6.2.3 |
| maxPathPolys | 740 | 740 ✅ | Large (MaNGOS original = 74, augmenté x10) |
| stepSize (SMOOTH_PATH_STEP_SIZE) | 4.0f | 4.0f | Utilisé dans findSmoothPath() uniquement |
| slop (SMOOTH_PATH_SLOP) | 0.3f | 0.3f | Tolérance de navigation vers steer target |
| dtQueryFilter areasCost | SetDefaultAreaCosts() : Road=0.5, Ground=1.66, Water=3.33, Lava=55, Blocked=100 | Road=1.0, Ground=1.66, Water=3.33, Lava=55, Blocked=100 | ✅ **Appliqués** — `SetAreaCost` P/Invoke matche DllMain, PathFinder copie `_defaultFilter` |
| findStraightPath options | DT_STRAIGHTPATH_ALL_CROSSINGS | DT_STRAIGHTPATH_ALL_CROSSINGS + MoveAwayFromEdges | ✅ **Fonctionnel** — PathPostProcessor.MoveAwayFromEdges est actif, exports C++ existent dans DllMain |
| farFromPoly threshold | 7.0f | 7.0f | Seuil raisonnable |
| Height correction | Aucune dans BuildPointPath | +0.5f sur tous les waypoints | **À corriger** — cause des blocages sur terrain non plat |

## Points de comparaison HB 6.2.3

| Feature | CopilotBuddy | HB 6.2.3 | Gap |
|---------|-------------|----------|-----|
| Path smoothing | findStraightPath (ALL_CROSSINGS) + MoveAwayFromEdges ✅ | PathPostProcessing.MoveAwayFromEdges | ✅ **OK** — MoveAwayFromEdges actif, repousse les waypoints des bords |
| Wall avoidance (path inset) | PathPostProcessor.MoveAwayFromEdges ✅ ACTIF, appelle DllMain exports | MoveAwayFromEdges post-process | ✅ **OK** — fonctionnel. Vérifier EdgeDistance=2.0f en pratique |
| Raycast (HB-style) | P/Invoke → DllMain `Raycast()` — signature HB-style poly-aware ✅ | Detour raycast poly-aware | ✅ **OK** — signatures matchent parfaitement |
| P/Invoke naming | ✅ TOUS les noms matchent les exports DllMain.cpp (sans suffixe `_C`) | N/A (in-process) | ✅ **OK** — aucun mismatch |
| NavStats struct | C# 11 champs = DllMain NavStats 11 champs (44 bytes) ✅ | N/A | ✅ **OK** — structs identiques |
| Waypoint skip logic | Simple 2D+Z check, no off-mesh guard | HB method_5 + method_4 off-mesh handling | **HAUTE** — skip aveugle les off-mesh connections (P4.3) |
| Path metadata | Points only, Flags/PolyTypes/AbilityFlags JETÉS dans Navigator.MoveTo() | MeshMovePath conserve tout | **CRITIQUE** — impossible de gérer elevator/portal/water (P4.1) |
| Stuck handler integration | Fonctionne via ActionMoveToPoi (flow principal). Trou : 3 appels directs MoveTo() sans stuck check | IsStuck() checked every frame dans MeshNavigator | **HAUTE** — couvrir TOUS les appelants en intégrant dans MoveTo() (P4.6) |
| Off-mesh handling | None — bot blocks on elevators/portals | Elevator wait/ride, portal interact, unit/object interact | **HAUTE** — bot bloqué devant tout transport (P4.2) |
| Water/Lava Z offset | None (PolyTypes lost in Navigator.MoveTo) | +2f Z for Water/Lava swim | **BASSE** — nage sous la surface (P4.8) |
| Height correction | Aucune dans BuildPointPath | Intégrée dans pathway | **MOYEN** — +0.5f manquant |
| Stuck detection | IsStuck() + wall distance check | Intégrée | OK — implémenté |
| Off-mesh links | OffMeshManager ✅ | Intégrée | OK |
| Query filter | SetDefaultAreaCosts() ✅ SetAreaCost matche DllMain, costs appliqués | WowQueryFilter (Road=1.0, Ground=1.66, etc.) | ✅ **OK** — coûts transmis et appliqués. Note: P2.5 certaines query functions utilisent filtre local |
| Area costs | SetDefaultAreaCosts() correct, appliqué via DllMain ✅ | 18 area types avec coûts différenciés | ✅ **OK** — 14 types configurés |
| Faction filter | Aucun | Horde/Alliance excludeFlags | **BAS** — pas critique pour WotLK |
| Corridor maintenance | dtPathCorridor initialisé | Managed dtPathCorridor | OK — base en place |
| Sliced pathfinding | P/Invoke ✅ + exports DllMain ✅ | Supporté | ✅ **OK** — disponible mais non utilisé |
| CTM implementation | ClickToMove via ASM injection ✅ | In-process API | OK — fonctionnel |
| Blackspot system | BlackspotManager with poly marking ✅ | Intégré dans MeshNavigator | OK — fonctionnel |
| StuckHandler | Implemented (HB 4.3.4 pattern) ✅, intégré via ActionMoveToPoi, trou sur appels directs | Fully integrated dans NavigationProvider | **HAUTE** — intégrer dans MoveTo() pour couvrir tous les appelants (P4.6, P6.3) |
| Flightor annotations | TraceLine seulement (angles progressifs) | FlightorAnnotation data files + indoor routing | **MOYEN** — manque gestion indoor (P6.10) |
| Path validity check | Destination check seulement | Raycast-based position-on-path verification | **HAUTE** — paths obsolètes (P6.7) |
| FindPath threading | Synchrone bloquant | Async avec frame release + combat abort | **HAUTE** — freeze pendant long pathfinding (P6.12, P6.14) |
| Path start optimization | Aucune | Skip visible waypoints via raycast | **MOYEN** — perte de fluidité (P6.6) |
| Push-ahead validation | Push brut 3D | Push + raycast navmesh validation | **MOYEN** — virages coupés (P6.5) |
| LocalPlayerMover cap | 8yd cap (unused by main flow) | ClickToMoveMover direct | **INFO** — affecte Flightor seulement (P5.2) |
| MoveAwayFromEdges error handling | try/catch VIDE — crash invisible | Logging intégré | **MOYEN** — cause potentielle de paths rasant les murs |
| MoveAwayFromEdges midpoint logic | `(wallPos + nearestPoint) / 2` — peut pousser VERS le mur | Logique d'éloignement vectorielle | **MOYEN** — à auditer en debug |

---

### ⚠️ CONTRÔLE QUALITÉ #2 (QC2) — Contre-audit sceptique indépendant

**Méthode :** Relecture complète du code source pour CHAQUE claim des phases 1-7.
Aucune confiance a priori dans les analyses précédentes.

#### Résultats QC2 — Tableau de vérité

| Claim du PLAN | Verdict QC2 | Preuve code |
|---|---|---|
| P1 — `findSmoothPath()` existe mais jamais utilisée | ✅ **CONFIRMÉ VRAI** | `BuildPointPath()` appelle `findStraightPath()`. Aucun appel à `findSmoothPath()` dans tout le C++. |
| P2.5 — 8 fonctions Navigation.cpp utilisent filtre local | ✅ **CONFIRMÉ VRAI** | `FindNearestPoly`, `FindDistanceToWall`, `FindDistanceToWallEx`, `IsPointOnNavMesh`, `FindRandomPointAroundCircle`, `HasLineOfSight`, `FindPolysAroundCircle` + 1 autre — toutes font `dtQueryFilter filter;` stack. |
| P3 — P/Invoke cassé (QC1 : Phase 3 FAUSSE) | ✅ **QC1 CONFIRMÉ** | C# appelle DllMain.cpp (pas NavBridge `_C`). Exports matchent. |
| P4.1 — Path metadata jetée dans `Navigator.MoveTo()` | ✅ **CONFIRMÉ VRAI** | `Navigator.cs` L230-236 : seuls `Points` sont extraits, `Flags`/`PolyTypes`/`AbilityFlags` ignorés. |
| P4.2 — Pas de gestion off-mesh | ✅ **CONFIRMÉ VRAI** | Aucun code dans `MoveTo()` ne vérifie `Flags & 4` ni ne route vers handler elevator/portal. |
| **P4.6/P6.3 — "StuckHandler JAMAIS appelé"** | ⚠️ **PARTIELLEMENT FAUX** | `ActionMoveToPoi.Run()` L138 appelle `IsStuck()`+`Unstick()` après chaque `MoveResult.Moved` (timer 2s). **Fonctionne dans le flow GrindBot/QuestBot.** Trou : 3 appels directs `MoveTo()` dans LevelBot (L306, L331, L1099) sans stuck check. Sévérité abaissée de CRITIQUE → HAUTE. |
| Area costs appliqués | ✅ **CONFIRMÉ VRAI** | `Navigation::SetAreaCost()` L1326 : met à jour `_areaCosts[id]` ET `_defaultFilter.setAreaCost(id, cost)`. Chaîne complète fonctionne. |
| MoveAwayFromEdges actif | ✅ **CONFIRMÉ VRAI** | `TripperNavigator.PathPostProcessing = MoveAwayFromEdges` par défaut. Exports C++ existent dans DllMain. |

#### Findings NOUVEAUX découverts en QC2

| # | Finding | Détail | Impact |
|---|---------|--------|--------|
| QC2.1 | **MoveAwayFromEdges try/catch VIDE** | `TripperNavigator.FindPath()` wrappe le post-processing dans un `try/catch` sans log. Si MoveAwayFromEdges crash, le path brut (rasant les murs) est utilisé silencieusement. | **MOYEN** — cause invisible potentielle des paths rasant les murs |
| QC2.2 | **MoveAwayFromEdges guard `points.Length > 2`** | Post-processing sauté si path ≤ 2 points. | **BAS** — paths courts de 2 points sont généralement en ligne directe |
| QC2.3 | **MoveAwayFromEdges midpoint douteux** | L218: `point = (wallPos + nearestPoint.ToVector3()) / 2f` — fait la moyenne entre position du mur et point le plus proche. Peut pousser VERS le mur dans certaines géométries au lieu d'éloigner. | **MOYEN** — à vérifier empiriquement |
| QC2.4 | **StuckHandler trou de couverture** | `Navigator.MoveTo()` appelé directement (sans ActionMoveToPoi) pour corpse run, loot, hotspot. Ces 3 flows n'ont AUCUNE stuck detection. | **HAUTE** — bot se bloque en corpse run ou loot |

---

### Phase 8 — Implémentation des corrections C# (Navigator.MoveTo)

**Objectif :** Résoudre les problèmes de navigation observés en jeu :
1. Bot coupe les virages (trajectoire décalée près des murs → se bloque)
2. Paths rasent les murs/obstacles
3. Bot se bloque sur terrain non plat

#### Ordre d'implémentation (chaque étape = un commit testable)

| Étape | Correction | Fichiers | Impact direct sur symptômes |
|-------|-----------|----------|----------------------------|
| 8.1 | **StuckHandler dans MoveTo()** — ajouter `IsStuck()`+`Unstick()` directement dans `Navigator.MoveTo()` pour couvrir TOUS les appelants | `Styx/Logic/Pathing/Navigator.cs` | Symptôme 3 (blocage terrain) : le bot se débloque automatiquement |
| 8.2 | **Push-ahead raycast validation** — avant de pousser le clickPoint, vérifier par raycast navmesh que la position poussée est navigable. Si non, cliquer au waypoint exact. | `Styx/Logic/Pathing/Navigator.cs` | Symptôme 1 (virages coupés) : le bot ne coupe plus les coins près des murs |
| 8.3 | **MoveAwayFromEdges logging** — remplacer le try/catch vide par un log d'erreur. Vérifier et corriger la logique midpoint (QC2.3). | `Tripper/Navigation/Navigator.cs` (TripperNavigator) | Symptôme 2 (paths rasent les murs) : diagnostiquer et corriger |
| 8.4 | **Stocker PathFindResult complet** — conserver Flags[], PolyTypes[], AbilityFlags[] dans Navigator aux côtés de `_currentPath`. Ajouter garde off-mesh au waypoint advance. | `Styx/Logic/Pathing/Navigator.cs` | Prérequis pour off-mesh, water+2f, path start skip |
| 8.5 | **Path validity check** — vérifier que le joueur est encore sur/proche du path. Si trop loin (knockback, TP), régénérer. | `Styx/Logic/Pathing/Navigator.cs` | Symptôme 3 (blocage) : paths obsolètes abandonnés |
| 8.6 | **Height correction +0.5f** — surélever les waypoints dans BuildPointPath | `PathFinder.cpp` (C++) | Symptôme 3 (blocage terrain non plat) |
| 8.7 | **Off-mesh dispatch basique** — détecter Flags & 4 et loguer un warning (stub pour elevator/portal) | `Styx/Logic/Pathing/Navigator.cs` | Visibilité : savoir quand le bot rencontre un off-mesh |
| 8.8 | **IsPartialPath detection** — vérifier status flags dans PathFindResult | `Tripper/Navigation/Navigator.cs` | Visibilité : warning quand path incomplet |

#### Étapes 8.1-8.3 = corrections immédiates pour les symptômes observés
#### Étapes 8.4-8.8 = améliorations structurelles pour robustesse

**Règle :** Chaque étape est un changement isolé testable. On implémente 8.1, on teste. Puis 8.2, on teste. Etc.

---

### Phase 9 — Corrections C++ + Off-mesh dispatch + Items oubliés

**Résultat :** Re-audit exhaustif demandé par l'utilisateur. 11 corrections appliquées.

#### 9.1 IsPartialPath detection (C# — oublié en Phase 8)

**Fichiers :** `Status.cs`, `Tripper/Navigation/Navigator.cs`
- Ajouté propriété `IsPartialResult` à `Status` (vérifie `DT_PARTIAL_RESULT = 1 << 6`)
- Remplacé `IsPartialPath = false, // TODO` par `IsPartialPath = status.IsPartialResult`
- Le warning "partial path" dans Navigator.cs est maintenant fonctionnel

#### 9.2 Fix 8 filtres locaux dans Navigation.cpp (C++ — critique)

**Fichier :** `Navigation.cpp`
- **8 fonctions** créaient un `dtQueryFilter filter;` local au lieu d'utiliser `_defaultFilter`
- Conséquence : `SetAreaCost()`, `SetExcludeFlags()`, `SetIncludeFlags()` étaient **ignorés** par ces fonctions
- Impact direct : `MoveAwayFromEdges` utilise `FindDistanceToWall` et `HasLineOfSight` — ignoraient les blackspots
- Fonctions corrigées : `FindNearestPoly`, `FindPolysAroundCircle`, `FindDistanceToWall`, `FindDistanceToWallEx`, `FindDistanceToWallFromPoly`, `IsPointOnNavMesh`, `FindRandomPointAroundCircle`, `HasLineOfSight`

#### 9.3 Fix memory leak dans FinalizeSlicedFindPath (C++)

**Fichier :** `Navigation.cpp`
- La branche fallback avait une double allocation `path = new XYZ[finalPathCount]` — la première était fuie
- Les 3 lignes dupliquées ont été supprimées

#### 9.4 Suppression debug file write (C++)

**Fichier :** `PathFinder.cpp`
- `HaveTile()` écrivait dans `C:\Users\Drew\Repos\bloog-bot-v2\Bot\navigationDebug.txt` à chaque tile miss
- Suppressé — le fichier n'existerait pas sur la machine de l'utilisateur

#### 9.5 Clean PathPostProcessor.cs redondance (C#)

**Fichier :** `Tripper/Navigation/PathPostProcessor.cs`
- Les branches `if (HasLineOfSight)` et `else` dans `TryMoveAwayFromEdge()` étaient identiques
- Simplifié en une seule branche — supprimé l'appel `HasLineOfSight` inutile

#### 9.6 Implémentation off-mesh dispatch complète (C#)

**Fichier :** `Styx/Logic/Pathing/Navigator.cs`
- Porté depuis HB 4.3.4 `MeshNavigator.method_4`
- 5 nouvelles méthodes privées : `HandleOffMeshConnection`, `HandleElevator`, `HandlePortal`, `HandleInteractUnit`, `HandleInteractObject`
- **Elevator :** détecte transport GO le plus proche, attend arrivée, monte, attend destination, descend
- **Portal :** trouve le Goober/SpellCaster le plus proche, interagit
- **InteractUnit :** trouve l'unité la plus proche, se déplace, interagit
- **InteractObject :** trouve l'objet le plus proche en range, interagit
- Utilise `WoWMovement.ClickToMove()` pour cohérence avec le reste du Navigator
- Ajouté `_elevatorBoarded` field + reset dans `Clear()`

#### Items audités mais non corrigés (justification)

| Item | Statut | Raison |
|------|--------|--------|
| MapId parsing (`std::to_string(mapId)[0]`) | **Non-issue** | Le guard `if (mapId == 0 \|\| mapId == 1)` limite aux continents. Donjons chargent via autre mécanisme. |
| NavBridge coord conversion bugs (5 fonctions) | **Différé** | C# appelle DllMain exports, pas NavBridge. Impact nul. |
| `rand()` thread-safety dans `FindRandomPointAroundCircle` | **Différé** | Queries Detour sont single-threaded. Risque nul. |
| PolyRefs width (uint64_t pour dtPolyRef 32-bit) | **Non-issue** | Widening explicite 32→64 via `static_cast`. C# lit `ulong`. Design intentionnel (forward-compatible si DT_POLYREF64). |
| DLL vs HB RecastManaged | **Comparable** | Notre DLL est équivalente ou supérieure pour le pathfinding. Manque seulement : mesh introspection (visualisation), filtres multiples concurrents. |

#### Build C#
- **0 erreurs, 509 warnings** (préexistants) — build OK
- Navigation.dll **doit encore être recompilée** pour que les corrections C++ prennent effet

---

### Phase 10 — Corrections Audit QC Indépendant

**Résultat :** Audit QC indépendant (AUDIT_NAVIGATION.md, 1210 lignes) exécuté dans un chat séparé. Score initial : 5.5/10. 6 corrections appliquées.

#### 10.1 Fix indexation off-mesh dispatch (CRITIQUE)

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` L348
- **Bug :** `_currentPolyTypes[_currentPathIndex - 1]` prenait le polygon PRÉCÉDENT (toujours Ground/Road)
- **Fix :** Remplacé par `_currentPolyTypes[_currentPathIndex]` — le polygon off-mesh lui-même (Elevator/Portal/etc)
- **Impact :** Tout le off-mesh dispatch (4 handlers) était effectivement DEAD CODE. Fix d'une ligne qui débloque elevators, portals, interact.
- **Guard condition** aussi corrigée : `_currentPathIndex < _currentPolyTypes.Length` au lieu de `_currentPathIndex > 0 && _currentPathIndex - 1 < _currentPolyTypes.Length`

#### 10.2 Sécurisation HandleInteractUnit (HAUTE)

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` — HandleInteractUnit()
- **Bug :** Ciblait N'IMPORTE QUELLE unité la plus proche (y compris hostiles, morts, joueurs)
- **Fix :** Ajouté filtres `.Where(u => !u.IsDead && !u.IsHostile && !u.PlayerControlled && !u.IsPlayer)`
- **Impact :** Empêche le bot de cibler un mob ennemi pendant un off-mesh traverse

#### 10.3 Amélioration HandleElevator — sélection direction-aware (HAUTE)

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` — HandleElevator()
- **Bug :** Sélection par pure proximité 2D — si 2 elevators proches, pouvait monter dans le mauvais
- **Fix :** Limite recherche à 50 yards + quand multiples transports trouvés, sélectionne celui dont le Z est le plus proche du joueur (= celui à notre niveau / qui s'approche)
- **Log debug** ajouté quand choix entre multiples transports

#### 10.4 Ajout compteur max retry unstick (MOYEN)

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` — MoveTo()
- **Bug :** `MoveResult.UnstuckAttempt` retourné indéfiniment sans limite
- **Fix :** Ajouté `_unstickAttempts` compteur (max 5). Après 5 échecs, force path regeneration via `MoveResult.Failed`
- **Reset** du compteur : nouveau path, waypoint advance, `Clear()`

#### 10.5 Debug log dans HandleOffMeshConnection

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` — HandleOffMeshConnection()
- Ajouté `Logging.WriteDebug("[Navigator] Off-mesh dispatch: AreaType={0}, target={1}", areaType, targetPoint)`
- Permet de vérifier en jeu que l'indexation Fix 10.1 fonctionne (devrait loguer Elevator/Portal, pas Ground/Road)

#### 10.6 Sécurisation HandlePortal — limite de range (BAS)

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` — HandlePortal()
- **Bug :** Recherche dans TOUS les Goober/SpellCaster sans limite de distance — pouvait cibler un objet très éloigné
- **Fix :** Limite de recherche à 30 yards (900f distance² max)

#### Findings audités mais différés (justification)

| Finding | Sévérité | Raison du report |
|---------|----------|-----------------|
| Pathfinding asynchrone (P6.12) | CRITIQUE | Gap structurel majeur. Instructions.md : "synchronous code only". Architecture complexe → Phase 11. |
| FixPathWalkability no-op (NEW-2) | MOYEN | Nécessite un sous-pathfinding entre points bloqués. Complexe → Phase 11. |
| getNavTerrain stub NAV_GROUND (NEW-1) | MOYEN | C++ change. Nécessite recompilation DLL → différé à la recompilation. |
| FinalizeSlicedFindPath AREA_CROSSINGS (NEW-3) | BAS | Sliced pathfinding pas utilisé dans le flux principal. Non-bloquant. |
| findSmoothPath code mort (NEW-6) | INFO | 150 lignes de code mort. Suppression lors de la recompilation DLL. |
| Road cost 0.5 vs HB 1.0 (NEW-7) | BAS | Choix de tuning. À tester empiriquement en jeu. |
| Water/Lava Z+2f (P6.4) | BAS | Impact faible en WotLK. Phase future. |

#### Build C# Phase 10
- **0 erreurs, 4 warnings NuGet** — build OK
- Score estimé après corrections : **7/10** (off-mesh fonctionnel, handlers sécurisés, unstick borné)

---

### Phase 10b — Corrections Audit QC (suite C#)

**Résultat :** 3 corrections supplémentaires appliquées (items différés de Phase 10). 1 item ignoré (N/A WotLK).

#### 10b.1 FixPathWalkability — midpoint insertion + récursion (MOYEN)

**Fichier :** `Tripper/Navigation/PathPostProcessor.cs` — FixPathWalkability()
- **Bug :** Quand MoveAwayFromEdges pousse un waypoint à travers un mur, `FixPathWalkability` faisait `continue` (no-op complet)
- **Fix :** Au lieu de skip, insère un midpoint sur le navmesh via `FindNearestPoly` entre les 2 points bloqués, puis récurse (max 5 niveaux)
- **Logique :**
  1. Raycast entre pair de points, détecte blocage
  2. Calcule midpoint = `(start + end) * 0.5f`
  3. `FindNearestPoly(mapId, midpoint, 5.0f)` → snap sur navmesh
  4. Insère dans la liste, récurse
  5. Guard : `MaxRecursionDepth = 5` pour éviter boucle infinie

#### 10b.2 Water/Lava Z+2f waypoint elevation (BAS)

**Fichier :** `Tripper/Navigation/Navigator.cs` (TripperNavigator.FindPath)
- **Bug :** Waypoints en zone Water/Lava sont au fond — le bot nage sous la surface
- **Fix :** Après post-processing, pour chaque point intermédiaire (pas premier/dernier) où `polyTypes[i] == Water || Lava`, ajoute `+2.0f` au Z
- **Impact :** Le bot nage à la surface au lieu de plonger. Ne touche pas start/end.

#### 10b.3 Path start skip — raycast visible waypoints (MOYEN)

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` — MoveTo(), après `_currentPathIndex = 0`
- **Ref HB 6.2.3 :** `method_14` dans MeshNavigator — skip les premiers waypoints visibles en ligne directe
- **Logique :**
  1. Après génération du path, raycast du joueur vers waypoints 1 à 5
  2. Si raycast réussit (hitT >= 1.0f = pas de mur), marque comme visible
  3. Avance `_currentPathIndex` au dernier visible
  4. S'arrête AVANT toute off-mesh connection (ne skip jamais un elevator/portal)
  5. Log debug : `"[Navigator] Skipped {0} visible early waypoints"`
- **Impact :** Mouvement plus fluide, évite zigzag vers le waypoint 0 qui est derrière le joueur

#### 10b.4 Alive/Dead filter toggle — IGNORÉ (N/A WotLK)

- **Ref audit :** P6.13 — HB 6.2.3 a `OnlyWhileAlive` flag pour filtrer les connexions off-mesh
- **Vérifié :** `Tripper/Navigation/AbilityFlags.cs` a `RunSafe = 2`, PAS `OnlyWhileAlive = 2`
- **Conclusion :** Les meshes WotLK n'utilisent pas ce flag → correction inapplicable, ignorée

#### Build C# Phase 10b
- **0 erreurs** — build OK (1 fix scope variable `mapId` → `skipMapId`)
- Score estimé : **~7.5/10** (FixPathWalkability fonctionnel, path start skip fluide, water Z corrigé)

#### Findings restants (non C#, à traiter ultérieurement)

| Finding | Sévérité | Raison du report |
|---------|----------|-----------------|
| Pathfinding asynchrone (P6.12) | CRITIQUE | Instructions.md : "synchronous code only". → Phase 11 |
| getNavTerrain stub NAV_GROUND (NEW-1) | MOYEN | C++ → différé à recompilation DLL |
| FinalizeSlicedFindPath AREA_CROSSINGS (NEW-3) | BAS | C++ → différé |
| findSmoothPath code mort (NEW-6) | INFO | C++ → suppression lors recompilation |
| Door handling (P6.8) | BAS | Feature complexe, priorité basse WotLK |
| Flightor indoor/outdoor (P6.10) | MOYEN | Feature séparée → Phase future |
| **C++ DLL recompilation** | HAUTE | Toutes les corrections C++ (8 local filter→_defaultFilter, memory leak, debug write, +0.5f height) en attente |

---

### Phase 10c — Corrections Audit QC (area costs, faction, off-mesh default)

**Résultat :** 3 corrections supplémentaires alignant le système avec HB 6.2.3. Audit items NEW-7 et comparaisons B.9/B.10 traités.

#### 10c.1 Road cost 0.5 → 1.0 + Blackspot/Faction area costs (NEW-7)

**Fichiers :** `Tripper/Navigation/Navigator.cs` — SetDefaultAreaCosts() + Default QueryFilter
- **Bug :** Road=0.5 suroptimisait vers les routes, causant des détours inutiles. HB 6.2.3 utilise Road=1.0.
- **Fix :**
  - `SetDefaultAreaCosts()` : Road → 1.0f (était 0.5f)
  - Ajouté `Blackspot = 60.0f` (HB 6.2.3)
  - Ajouté `Horde = 1.66f`, `Alliance = 1.66f` (HB 6.2.3)
  - Default QueryFilter : Ground=1.66f, Water=3.33f, Road=1.0f (alignés HB)

#### 10c.2 Faction query filter Horde/Alliance (B.10 P6.x, Audit Annexe 5.3-5.4)

**Fichiers :** `Tripper/Navigation/Navigator.cs` + `Styx/Logic/Pathing/Navigator.cs`
- **Ref HB 6.2.3 :** `WowNavigator.SetFactionQueryFilter(isHorde)` + `MeshNavigator.OnBotStarted → SetFactionQueryFilter`
- **Implémentation :**
  - Nouvelle méthode `TripperNavigator.SetFactionQueryFilter(bool isHorde)`
  - Horde : `ExcludeFlags |= Alliance`, `SetAreaCost(Alliance, 50.0f)`
  - Alliance : `ExcludeFlags |= Horde`, `SetAreaCost(Horde, 50.0f)`
  - Appelé dans `Navigator.OnBotStart()` après chargement des meshes via `StyxWoW.Me.IsHorde`
- **Impact :** Le bot ne pathera plus à travers les zones faction-only ennemies (Dalaran Horde/Alliance zones, BG faction gates)

#### 10c.3 Default off-mesh handler — Run/Jump (HB 6.2.3 method_19)

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` — HandleOffMeshConnection() default case
- **Bug :** Le `default` case retournait `null` (avance silencieusement). Pour les connexions balisées avec des ability flags non-Run/Jump (Teleport, Unwalkable), le bot traverserait aveuglément.
- **Fix :** Nouvelle méthode `HandleStandardOffMesh()` :
  - Vérifie `_currentAbilityFlags[_currentPathIndex]` pour Run/Jump/RunSafe
  - Si Unwalkable flag → `MoveResult.Failed`
  - Si Jump flag → dismount avant de traverser
  - Sinon → ClickToMove vers target, avance normalement
- **Ref HB 6.2.3 :** `method_19` — checks Run|Jump ability flags, fails on Unwalkable

#### Build C# Phase 10c
- **0 erreurs** — build OK
- Score estimé : **~8/10** (area costs alignés HB, faction filter actif, off-mesh dispatch complet)

---

### Phase 10d — Implémentation complète des findings restants (sauf async)

**Objectif :** Fermer TOUS les findings restants identifiés dans AUDIT_NAVIGATION.md, à l'exception de l'async pathfinding (P6.12).

#### 10d.1 MountUpEventArgs.Cancel property

**Fichier :** `Styx/Logic/MountUpEventArgs.cs`
- **Ajout :** `public bool Cancel { get; set; }` — permet aux handlers d'annuler le mount-up
- **Pattern HB 6.2.3 :** MeshNavigator.method_17 annule le mount pendant le transport

#### 10d.2 Mount.Pulse() — Cancel flag check

**Fichier :** `Styx/Logic/Mount.cs`
- **Fix :** `Pulse()` fire OnMountUp avec un MountUpEventArgs. Si `args.Cancel == true`, dismount immédiat via `Dismount("cancelled by event handler")`
- **Impact :** Support du pattern cancel-on-elevator (Navigator.OnMountUpDuringElevator)

#### 10d.3 Navigator — IsRidingElevator + door handling + elevator mount-cancel

**Fichier :** `Styx/Logic/Pathing/Navigator.cs`
- **Champs ajoutés :** `_doorInteractTimer`, `_ridingElevator`, `_noFlyZoneIds`
- **Propriétés ajoutées :** `IsInNoFlyZone`, `IsRidingElevator`
- **Event handler :** `OnMountUpDuringElevator()` — sets `e.Cancel = true` si `_ridingElevator`
- **HandleElevator() :** Met à jour `_ridingElevator` = true quand `me.IsOnTransport`, false à la fin du ride
- **HandleDoors()** (P6.8) : Nouvelle méthode ~50 lignes :
  - Cherche les GameObjects Door fermés (`State == Ready`) et non-verrouillés dans un rayon de 10yd
  - Vérifie que la porte est sur le chemin (entre le joueur et le prochain waypoint)
  - Déplacement vers la porte puis `Interact()` pour l'ouvrir
  - Cooldown de 2 secondes via `_doorInteractTimer`
  - Appelé dans `MoveTo()` avant la détection de stuck
- **Fix collatéral :** Suppression du fichier `Styx/WoWGameObjectState.cs` (doublon avec valeurs inversées — Active=0/Ready=1 au lieu de Ready=0/Active=1). L'enum correcte est dans `WoWGameObject.cs`

#### 10d.4 Flightor — no-fly zones + elevator check + anti-stuck optimisation

**Fichier :** `Styx/Logic/Pathing/Flightor.cs`
- **MoveTo() :** Ajout de deux guards en entrée de la méthode :
  - `Navigator.IsInNoFlyZone` → force ground nav via `Navigator.MoveTo(destination)` (P6.10)
  - `Navigator.IsRidingElevator` → force ground nav
- **MountUpInternal() :** Ajout check `Navigator.IsRidingElevator` → bloque le mount si sur elevator
- **DoAntiStuck() :** Thread.Sleep réduits de 900ms total à ~400ms total (S5.3) :
  - Backward: 200→100ms, pause 100→50ms
  - Strafe+Jump: 200→100ms, pause 100→50ms
  - Forward+Strafe: 300→100ms
- **Zones no-fly WotLK :** IDs 4395 (Dalaran), 4613 (Pit of Saron), 4820 (Halls of Reflection)

#### 10d.5 LocalPlayerMover — CTM cap increase

**Fichier :** `Styx/Logic/Pathing/Interop/LocalPlayerMover.cs`
- **Fix (S5.1) :** Cap CTM click distance augmenté de 8yd à 30yd (via `Math.Min(distance, 30f)`)
- **Raison :** 8yd causait des re-clicks constants et un mouvement saccadé en vol. HB 6.2.3 utilise 27yd ; 30yd offre des arcs CTM fluides pour le sol et le vol.

#### 10d.6 KeyboardMover — fallback IMover

**Fichier :** `Styx/Logic/Pathing/Interop/KeyboardMover.cs` (NOUVEAU — ~190 lignes)
- **Pattern P6.11 :** Alternative IMover utilisant `SetFacing()` + `MoveForward` au lieu de ClickToMove
- **Basé sur :** HB 6.2.3 `Styx.Pathing.KeyboardMover`
- **Usage :** `Navigator.PlayerMover = new KeyboardMover();` — utile quand CTM est unreliable (elevators, pentes raides, passages étroits)
- **Algorithme :** Calcule l'angle vers la cible, ajuste le facing par incréments proportionnels, puis MoveForward
- **Implémente :** Tous les membres IMover (MoveTowards, MoveInDirection, StopMoving, SetFacing, PerformJump, Location, Facing, IsStuck)

#### 10d.7 C++ getNavTerrain fix (NEW-1)

**Fichier :** `C++/Navigation/PathFinder.cpp` — `getNavTerrain()`
- **Bug :** Retournait toujours `NAV_GROUND` (TODO stub depuis l'import Trinity). `updateFilter()` ne pouvait jamais ajouter les flags liquides lors de la nage.
- **Fix :** Utilise `m_navMeshQuery->findNearestPoly()` pour trouver le polygone à la position, puis `m_navMesh->getPolyFlags()` pour lire le terrain. Les mmaps Trinity encodent le type de liquide dans les poly flags (NAV_WATER=0x08, NAV_MAGMA=0x02, NAV_SLIME=0x04).
- **Impact :** Les créatures (et le bot) qui nagent auront les flags d'inclusion corrects → pathfinding aquatique fonctionnel

#### 10d.8 C++ FinalizeSlicedFindPath — ALL_CROSSINGS (NEW-3)

**Fichier :** `C++/Navigation/Navigation.cpp` — `FinalizeSlicedFindPath()`
- **Fix :** `DT_STRAIGHTPATH_AREA_CROSSINGS` → `DT_STRAIGHTPATH_ALL_CROSSINGS`
- **Raison :** Aligne avec `BuildPointPath()` qui utilise déjà `ALL_CROSSINGS`. La variante ALL inclut les crossings aux frontières off-mesh en plus des area crossings.

#### 10d.9 C++ Dead code removal — findSmoothPath (NEW-6)

**Fichier :** `C++/Navigation/PathFinder.cpp` + `PathFinder.h`
- **Supprimé :** ~240 lignes de code mort :
  - `fixupCorridor()` (~55 lignes)
  - `getSteerTarget()` (~45 lignes)
  - `findSmoothPath()` (~130 lignes)
  - `inRangeYZX()` (~6 lignes)
- **Raison :** `BuildPointPath()` utilise `dtNavMeshQuery::findStraightPath()` directement. L'algorithme smooth path itératif (moveAlongSurface loop) n'était jamais appelé.
- **Déclarations supprimées :** `PathFinder.h` — les 4 fonctions + `inRangeYZX`

#### Build C# Phase 10d
- **0 erreurs, 2 warnings** (NuGet package warnings uniquement)
- **C++ :** Source modifiée, DLL non recompilée (nécessite build séparé)

---

### Phase 10e — Nettoyage mineur + P6.14 Combat abort + P6.9 Flight path auto-detection

**Objectif :** Derniers cleanups identifiés en QC + deux features restantes (P6.14, P6.9).

#### 10e.1 C++ Cleanup — Orphaned defines & struct

**Fichier :** `C++/Navigation/PathFinder.h`
- **Supprimé :** `#define SMOOTH_PATH_SLOP 0.3f` — référencé par aucun code (findSmoothPath supprimé en 10d.9)
- **Supprimé :** `GridMapLiquidData` struct (lines 82-87) + tous les `MAP_LIQUID_TYPE_*` defines + `MAP_ALL_LIQUIDS` — utilisés par l'ancienne implémentation `getNavTerrain` remplacée en 10d.7
- **Vérifié :** `Select-String` confirme zéro référence dans *.cpp et *.h

#### 10e.2 C# Cleanup — GetAngleDifference(float) vérifié UTILISÉ

**Fichier :** `Styx/Logic/Pathing/Interop/KeyboardMover.cs`
- **QC disait :** Overload `GetAngleDifference(float neededFacing)` serait inutilisé
- **Vérification :** Ligne 51 appelle `GetAngleDifference(neededFacing)` avec un argument `float` → résout vers cet overload
- **Action :** Pas de suppression — le QC s'était trompé, l'overload est bien utilisé

#### 10e.3 P6.14 — Combat abort synchrone

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` — `MoveTo()`

L'audit identifie P6.14 comme "couplé à P6.12 async" car HB 6.2.3 utilise un callback `method_16` avec timeout 4 secondes pendant le pathfinding asynchrone. En mode synchrone, on ne peut pas annuler mid-pathfind. **Solution :** 3 niveaux de garde combat :

1. **Avant pathfinding** (nouveau path + en combat + pas de path existant) : Skip `FindPath()`, direct click-to-move vers la cible. Évite le freeze de 1-2s du pathfinding synchrone pour kiting/chase.

2. **Nouveau path + en combat** : Utilise mouvement direct (2 points) au lieu du pathfinding mesh. Le bot reste mobile pendant le combat sans coûteux calcul de path.

3. **Pendant le suivi de path** : Si `me.Combat` détecté en début de boucle MoveTo(), abandonne le path courant, clear la destination, retourne `MoveResult.Moved`. Le prochain appel utilisera le shortcut #1. La combat routine du behavior tree reprend le contrôle immédiatement.

**Équivalent HB 6.2.3 :** `method_16(PathFindProgressEventArgs)` avec `e.Cancel = true` après 4s → ici, abort immédiat à chaque pulse car le coût est nul (pas de thread à annuler).

#### 10e.4 P6.9 — Flight path / taxi auto-detection

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` — `MoveTo()`

Intégration du check taxi dans MoveTo(), directement porté de HB 6.2.3 `method_10` :

```csharp
// P6.9 — Flight path auto-detection (HB 6.2.3 method_10)
float distanceSqr = me.Location.DistanceSqr(destination);
if (distanceSqr > 160000f) // 400² yards
{
    if (FlightPaths.ShouldTakeFlightpath(me.Location, destination, me.MovementInfo.RunSpeed))
    {
        if (FlightPaths.SetFlightPathUsage(me.Location, destination, out _, out _))
        {
            Logging.Write("[Navigator] Flight path would be faster — setting taxi POI");
            return MoveResult.PathGenerated;
        }
    }
}
```

**Flow :** Distance > 400yd → `ShouldTakeFlightpath()` compare temps de course vs vol+marche, exige >30s d'économie → `SetFlightPathUsage()` crée `BotPoi(PoiType.Fly)` → le behavior tree marche vers le flight master → `TAXIMAP_OPENED` Lua event → `HandleTaxiMapOpened()` prend le taxi.

**Dépendances :** `FlightPaths.cs` (549 lignes, déjà porté), `TaxiFrame.cs`, `XmlFlightNode`, `CharacterSettings.UseFlightPaths` — tous déjà implémentés.

#### 10e.5 Fix QC — FlightPaths.Initialize() manquant

**Fichier :** `Styx/Logic/Pathing/Navigator.cs` — `OnBotStart()`
- **Trouvaille QC :** `FlightPaths.Initialize()` n'était jamais appelé dans CopilotBuddy. Sans cet appel, `XmlNodes` reste null et `TAXIMAP_OPENED` n'est pas attaché → P6.9 entièrement non-fonctionnel.
- **Référence :** HB 4.3.4 appelle `FlightPaths.Initialize()` depuis `Class448` au démarrage.
- **Fix :** Ajouté `FlightPaths.Initialize()` dans `Navigator.OnBotStart()`, après le faction filter, avec try/catch.

#### 10e.6 C++ Git commit

**Repo :** `C:\Users\Texy\Desktop\.test\C++\` (repo git séparé)
- **Commit :** `88c9d1b` — "Fix navigation: getNavTerrain coords, ALL_CROSSINGS, cleanup dead code"
- **3 fichiers, +58 / -321 lignes**
- Couvre toutes les modifications C++ des phases 9-10e

#### Build C# Phase 10e
- **0 erreurs, 2 warnings** (NuGet package warnings pré-existants)
- **C++ :** Changements commitées, DLL recompilée par l'utilisateur avec 0 erreurs

#### Findings restants après Phase 10e

| Finding | Sévérité | Raison du report |
|---------|----------|-----------------|
| Pathfinding asynchrone (P6.12) | CRITIQUE | Instructions.md : "synchronous code only" |

**Score estimé : ~9/10** — Tous les findings C# et C++ traités, incluant combat abort et taxi. Seul P6.12 (async) reste, bloqué par contrainte architecturale.
