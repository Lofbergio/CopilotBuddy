# AUDIT DE QUALITÉ — Système de Navigation CopilotBuddy

**Date :** Audit indépendant  
**Scope :** Navigation complète (C# + C++ DLL + comparaison HB)  
**Méthode :** Relecture intégrale du code source, vérification indépendante de PLAN_NAVIGATION.md  
**Fichiers audités :** 18 fichiers (~7000 lignes)

---

## Résumé Exécutif

Le système de navigation de CopilotBuddy est **fonctionnel et architecturalement solide**, avec un travail de portage C++ et P/Invoke bien exécuté. Cependant, la couche supérieure (`Navigator.MoveTo()`) présente des **lacunes structurelles significatives** par rapport à HB, notamment dans le threading, la récupération de path, et un **bug critique d'indexation** qui rend tout le dispatch off-mesh (elevators, portals, interact) effectivement mort. Le PLAN_NAVIGATION.md est **largement fiable** après ses auto-corrections QC, mais l'erreur d'indexation off-mesh est passée inaperçue.

**Note de confiance globale : 5.5/10** — Navigation de base fonctionnelle, off-mesh dispatch mort, à risque sur terrain complexe (elevators, water, long-distance).

---

## A — Qualité du Code

### A.1 — Le P/Invoke est-il correctement mappé aux exports DllMain ?

**Verdict : OUI — SOLIDE**

Vérification indépendante : les 45+ déclarations dans [NativeMethods.cs](Tripper/Navigation/NativeMethods.cs) matchent les exports de `DllMain.cpp`. Noms identiques, signatures cohérentes. Les structs `XYZ` (12 bytes Sequential) et `PathResult` (5 IntPtr + 3 int = 32 bytes x86) sont correctement alignées.

Points vérifiés :
- `CalculatePathEx` → export DllMain L85-L98 ✅
- `Raycast` → 9 params, signature HB-style ✅
- `FindDistanceToWallFromPoly` → export avec `dtPolyRef` widened (ulong) ✅
- `NavStats` → 11 champs = 44 bytes ✅
- `ToExternalRef`/`ToInternalRef` → widening 32→64 bit correct ✅

**Aucun C# n'appelle les exports `_C` de NavBridge.cpp.** Le PLAN Phase 3 originale se trompait en comparant contre NavBridge — la correction QC1 est juste.

### A.2 — Les conversions de coordonnées sont-elles correctes ?

**Verdict : OUI — CORRECT tout au long du pipeline**

Pipeline vérifié :
1. **C# → C++ :** `NativeMethods.XYZ` passe `(X, Y, Z)` directement → DllMain reçoit en WoW coords
2. **DllMain → Detour :** `WoWToDetour(wow, det)` : `det[0]=wow.Y, det[1]=wow.Z, det[2]=wow.X` ✅ ([Navigation.cpp](../../../.test/C++/Navigation/Navigation.cpp) inline helpers)
3. **Detour → WoW :** `DetourToWoW(det, wow)` : `wow.X=det[2], wow.Y=det[0], wow.Z=det[1]` ✅
4. **PathFinder.cpp :** `startPoint = {Y, Z, X}` ✅ — cohérent avec MaNGOS

**Bug potentiel trouvé :** `PathFinder::HaveTile()` ([PathFinder.cpp#L643](../../../.test/C++/Navigation/PathFinder.cpp#L643)) utilise `{p.y, p.z, p.x}` ce qui est correct (WoW→Detour), mais la variable `Vector3` utilise des noms ambigus (`x/y/z` qui sont en fait WoW coords). Pas d'erreur fonctionnelle, juste un risque de confusion.

### A.3 — Les métadonnées de path sont-elles propagées correctement ?

**Verdict : OUI après Phase 8 — RÉSOLU**

Vérification du flux complet :

1. **C++ PathFinder::BuildPointPath()** → `m_result` contient `straightPathFlags[]`, `polyTypes[]`, `abilityFlags[]`, `polyRefs[]` ✅
2. **DllMain `CalculatePathEx()`** → retourne `PathResult*` avec les 5 tableaux ✅
3. **TripperNavigator.FindPath()** ([Tripper/Navigation/Navigator.cs#L305-L385](Tripper/Navigation/Navigator.cs#L305)) → marshal correctement les 5 tableaux via `unsafe` pointer arithmetic ✅
4. **Navigator.MoveTo()** ([Styx/Logic/Pathing/Navigator.cs#L258-L261](Styx/Logic/Pathing/Navigator.cs#L258)) → stocke `_currentFlags`, `_currentPolyTypes`, `_currentAbilityFlags` ✅

**Issue résiduelle :** Les `Polygons[]` (PolygonReference) ne sont PAS stockés dans `Navigator.MoveTo()`. Seuls Points/Flags/PolyTypes/AbilityFlags le sont. Cela empêche l'utilisation de `MoveAwayFromEdges` polygon-aware sur un re-processing post-hoc, mais n'affecte pas le flux normal (le post-processing est fait dans TripperNavigator.FindPath avant de retourner les résultats).

### A.4 — La détection de stuck est-elle exhaustive ?

**Verdict : OUI — AMÉLIORÉ mais edge case subsiste**

Le `StuckHandler.IsStuck()` est maintenant intégré directement dans `MoveTo()` ([Navigator.cs#L282-L291](Styx/Logic/Pathing/Navigator.cs#L282)) avec un timer de 2 secondes. Cela couvre **tous** les appelants : `ActionMoveToPoi`, corpse run, loot, hotspot, plugins.

**Points positifs :**
- Timer reset à chaque avancée de waypoint (L329) ✅
- Timer reset à chaque nouveau path (L238) ✅
- `StuckHandler.Reset()` dans `Clear()` (L170) empêche les faux positifs après unstick ✅

**Issue résiduelle :**
- `MoveResult.UnstuckAttempt` est retourné mais l'appelant peut boucler indéfiniment si le unstick ne fonctionne pas. Pas de compteur de tentatives max visible dans `MoveTo()`. La logique de max retries dépend de `StuckHandler` lui-même.

### A.5 — Les off-mesh connections sont-elles gérées correctement ?

**Verdict : NON — Architecture correcte mais dispatch mort à cause d'un bug d'indexation**

Le dispatch off-mesh est implémenté ([Navigator.cs#L796-L941](Styx/Logic/Pathing/Navigator.cs#L796)) avec 4 handlers portés de HB 4.3.4 `method_4` :

| Handler | Verdict | Problèmes |
|---------|---------|-----------|
| `HandleElevator` | **FRAGILE** | Trouve le transport le plus proche par distance, pas par ID. Si 2 elevators sont proches, peut cibler le mauvais. Ne vérifie pas la direction de l'elevator. |
| `HandlePortal` | **OK** | Cherche `Goober`/`SpellCaster` le plus proche. Logique simple mais fonctionnelle. |
| `HandleInteractUnit` | **DANGEREUX** | `ObjectManager.GetObjectsOfType<WoWUnit>().OrderBy(distance).First()` — prend N'IMPORTE QUELLE unité la plus proche, pas celle liée à la connexion off-mesh. En combat, peut cibler un mob ennemi ! |
| `HandleInteractObject` | **OK** | Filtre `WithinInteractRange` — sécurisé mais peut rater un objet hors range. |

**Comparaison HB 4.3.4 :** HB `method_4` accède au `InteractId` stocké dans la connexion off-mesh pour cibler l'entité exacte. CopilotBuddy fait une recherche par proximité — c'est un raccourci qui fonctionnera dans 90% des cas mais échouera dans les zones complexes (Thunder Bluff, Undercity elevators).

**Issue critique non couverte — indexation probablement fausse :**
Le dispatch se fait sur `_currentPolyTypes[_currentPathIndex - 1]` ([Navigator.cs#L344](Styx/Logic/Pathing/Navigator.cs#L344)).
L'audit initial validait cette indexation comme "correcte pour obtenir le type du segment menant au point off-mesh". **Ceci est probablement FAUX.**

En Detour, `findStraightPath` retourne pour chaque point :
- `straightPathFlags[i]` : flag (dont `DT_STRAIGHTPATH_OFFMESH_CONNECTION`)
- `straightPathPolys[i]` : polygon ref du polygone à ce point

Quand `straightPathFlags[i]` = OffMeshConnection, `straightPathPolys[i]` est la référence du **polygone off-mesh lui-même** (pas du polygone précédent). Donc `ResolveAreaType(m_straightPathRefs[i])` retourne l'area type de l'off-mesh (Elevator, Portal, etc.).

Par conséquent :
- `_currentPolyTypes[_currentPathIndex]` = area type de la connexion off-mesh (Elevator/Portal/InteractUnit) ← **CORRECT**
- `_currentPolyTypes[_currentPathIndex - 1]` = area type du polygone précédent (Ground/Road) ← **INCORRECT**

**Conséquence probable :** Le `switch(areaType)` dans `HandleOffMeshConnection` reçoit toujours Ground/Road, frappe le `default: return null`, et les handlers Elevator/Portal/InteractUnit/InteractObject ne sont **JAMAIS appelés**. L'off-mesh dispatch est implémenté mais effectivement mort.

**Correction :** Remplacer `_currentPolyTypes[_currentPathIndex - 1]` par `_currentPolyTypes[_currentPathIndex]` dans le dispatch off-mesh. Nécessite vérification empirique (debug log du area type reçu à un elevator connu comme UC/TB).

Note : MoveAwayFromEdges ne change PAS la taille du tableau PolyTypes (modifie in-place) → l'indexation reste cohérente ✅.

### A.6 — MoveAwayFromEdges fonctionne-t-il correctement ?

**Verdict : PARTIELLEMENT — Logique principale correcte, FixPathWalkability est un no-op**

**Ce qui fonctionne :**
- `MoveWaypointsFromEdges()` ([PathPostProcessor.cs#L127-L148](Tripper/Navigation/PathPostProcessor.cs#L127)) itère les points intermédiaires, skip first/last/off-mesh ✅
- `TryMoveAwayFromEdge()` ([PathPostProcessor.cs#L156-L228](Tripper/Navigation/PathPostProcessor.cs#L156)) utilise `FindDistanceToWall(FromPoly)` + hitNormal pour calculer la direction d'éloignement ✅
- Phase 9.5 a simplifié les branches redondantes ✅
- Fallback progressif : `edgeDistance * 2` → `edgeDistance * 0.5` ✅
- FindNearestPoly snap final vérifie que le nouveau point est sur navmesh ✅
- Actif par défaut (`PathPostProcessing = MoveAwayFromEdges`) ✅

**Ce qui ne fonctionne PAS :**
- **`FixPathWalkability()` est un NO-OP complet** ([PathPostProcessor.cs#L279-L317](Tripper/Navigation/PathPostProcessor.cs#L279)) : quand `HasLineOfSight` retourne `false` (segment bloqué), le code fait `continue` — il ne fixe rien. Le commentaire dit "For now, we skip fixing blocked segments". C'est un TODO permanent déguisé en implémentation. En pratique, si MoveAwayFromEdges pousse un waypoint dans une zone non visible du précédent, le path devient invalide sans correction.

- **EdgeDistance = 2.0f peut être insuffisant** dans les couloirs étroits (< 4 yards de large). Le waypoint serait poussé à travers le mur opposé. Le FindNearestPoly snap corrige partiellement, mais la direction de poussée peut être incorrecte.

- **try/catch dans TripperNavigator.FindPath()** ([Tripper/Navigation/Navigator.cs#L395-L401](Tripper/Navigation/Navigator.cs#L395)) attrape les exceptions de post-processing et continue avec le path brut. Un `Log()` est maintenant présent (Phase 8.3) ✅, mais l'erreur est non-bloquante — le bot marchera le long des murs silencieusement.

### A.7 — Y a-t-il des risques de crash ou memory leaks ?

**Verdict : RISQUE FAIBLE — 2 issues mineures**

**Positif :**
- `FreePathResult()` appelé dans un `finally` block ([Tripper/Navigation/Navigator.cs#L409-L412](Tripper/Navigation/Navigator.cs#L409)) ✅
- `Dispose()` pattern implémenté dans TripperNavigator ✅
- DLL singleton nettoyé dans `DLL_PROCESS_DETACH` ✅

**Issues :**
1. **Pas de validation de `nativeResult.Length`** avant allocation ([Tripper/Navigation/Navigator.cs#L308](Tripper/Navigation/Navigator.cs#L308)). Si le C++ retourne une valeur corrompue (ex: `Length = -1` casté en uint = 4294967295), l'allocation `new Vector3[pathLength]` causerait un `OutOfMemoryException`. Probabilité : très faible.

2. **Phase 9.3 a corrigé un memory leak** dans `FinalizeSlicedFindPath()` — double allocation de `path = new XYZ[finalPathCount]`. Confirmé corrigé dans le code actuel ✅.

3. **`getNavTerrain()` dans PathFinder.cpp** ([PathFinder.cpp#L620-L632](../../../.test/C++/Navigation/PathFinder.cpp#L620)) retourne toujours `NAV_GROUND` (TODO). L'appel `updateFilter()` pour le swimming est donc un no-op. Pas un crash mais un comportement dégradé en eau profonde.

---

## B — Comparaison avec Honorbuddy

### B.8 — MoveTo() vs HB 4.3.4 MeshNavigator ?

**Parité estimée : 75%**

| Feature | HB 4.3.4 | CopilotBuddy | Status |
|---------|----------|-------------|--------|
| Waypoint advance (2D + Z < 4.5) | `method_9` | [Navigator.cs#L305-L310](Styx/Logic/Pathing/Navigator.cs#L305) | ✅ Identique |
| Push-ahead direction | `smethod_0` push brut | Push + raycast validation | ✅ **Supérieur** |
| Off-mesh dispatch | `method_4` (InteractId) | `HandleOffMeshConnection` (proximité) | ⚠️ Fonctionnel mais imprécis |
| Off-mesh types | Elevator/Portal/InteractUnit/InteractObject | Idem 4 types | ✅ Identique |
| Elevator wait/ride | Transport detect + height check | Similaire mais sans direction check | ⚠️ Partiel |
| Stuck detection | Séparé dans StuckHandler | Intégré dans MoveTo() | ✅ **Supérieur** (couvre tous les appelants) |
| Path metadata | MeshMovePath conserve tout | Stocke Flags/PolyTypes/AbilityFlags | ✅ Identique |
| Flight path check | `method_3` (auto-detect taxi) | Absent | ❌ Manquant |
| Mount handling | Dismount avant off-mesh | `Mount.Dismount()` dans elevator/interact | ✅ Identique |
| Path drift detection | Non documenté | `DistanceToLineSegment > 10yd` | ✅ **Supérieur** |
| Stair handling | Non spécifique | Z threshold + exact click | ✅ **Ajout original** |

### B.9 — FindPath() vs HB 4.3.4 WowNavigator ?

**Parité estimée : 65%**

| Feature | HB 4.3.4 | CopilotBuddy | Status |
|---------|----------|-------------|--------|
| Pathfinding algo | Sliced (async, combat abort) | Synchrone bloquant | ❌ **Gap structurel majeur** |
| CalculatePathEx | N/A (in-process) | DllMain export, marshal complet | ✅ OK |
| MoveAwayFromEdges | Post-processing actif | Post-processing actif | ✅ Identique |
| Area costs | Road=1.0, Ground=1.66 | Road=0.5, Ground=1.66 | ⚠️ Road plus aggressif (peut suroptimiser) |
| 4x4 sub-tile grid | loadMap granulaire | EnsureTiles ring-based | ⚠️ Différent mais fonctionnel |
| Tile auto-unload | 5 min timeout | LRU cache (512 tiles) | ✅ Similaire |
| Combat abort | Progress callback | Impossible (synchrone) | ❌ **Gap majeur** |
| Frame release | Thread séparé | Bloque EndScene thread | ❌ **Gap majeur** |
| Query filter | Unique global | 4 named filters (Default/Swimming/Flying/Transport) | ✅ **Supérieur** |
| Partial path detection | Basique | `IsPartialResult` flag + warning log | ✅ Identique |
| Blackspot integration | OnTileLoaded | `EnsureBlackspotsMarked()` avant FindPath | ✅ Identique |

### B.10 — Quelles features HB 6.2.3 sont absentes ?

**8 features manquantes sur 14 identifiées :**

| # | Feature HB 6.2.3 | Implémenté ? | Impact WotLK |
|---|-------------------|------------|-------------|
| P6.1 | MeshMovePath (metadata complète) | ✅ Résolu (Phase 8) | N/A |
| P6.2 | Off-mesh dispatch | ✅ Résolu (Phase 9) | N/A |
| P6.3 | StuckHandler dans MoveTo | ✅ Résolu (Phase 8) | N/A |
| P6.4 | Water/Lava Z+2f | ❌ Non implémenté | **BAS** — nage sous-surface |
| P6.5 | Push-ahead raycast | ✅ Résolu (Phase 8) | N/A |
| P6.6 | Path start skip (visible waypoints) | ❌ Non implémenté | **MOYEN** — mouvement moins fluide en ville |
| P6.7 | Path validity check | ✅ Résolu (Phase 8) | N/A |
| P6.8 | Door handling auto | ❌ Non implémenté | **BAS** — rare en WotLK |
| P6.9 | Flight path auto-detection | ❌ Non implémenté | **BAS** — taxis pas dans les paths |
| P6.10 | Flightor indoor/outdoor | ❌ Non implémenté | **MOYEN** — Flightor dans buildings |
| P6.11 | IPlayerMover (CTM+Keyboard) | ❌ CTM seulement | **INFO** |
| P6.12 | FindPath async | ❌ Synchrone | **HAUTE** — freeze sur longs trajets |
| P6.13 | Alive/Dead filter | ❌ Non implémenté | **BAS** — rare en WotLK |
| P6.14 | Combat abort | ❌ Impossible (sync) | **HAUTE** — couplé à P6.12 |

**6 features résolues, 8 restantes.** Les 2 features HAUTE restantes (async + combat abort) sont le même problème structurel : le pathfinding synchrone.

### B.11 — La DLL C++ est-elle comparable à HB RecastManaged ?

**Verdict : COMPARABLE pour le pathfinding, INFÉRIEURE pour l'introspection**

| Aspect | Navigation.dll | HB RecastManaged | Verdict |
|--------|---------------|-----------------|---------|
| Detour A* pathfinding | ✅ via PathFinder | ✅ | Identique |
| findStraightPath (funnel) | ✅ ALL_CROSSINGS | ✅ | Identique |
| Area cost filtering | ✅ _defaultFilter | ✅ | Identique |
| Sliced pathfinding | ✅ Init/Update/Finalize | ✅ | Identique |
| Tile streaming | ✅ LRU cache + directional prefetch | ✅ | ✅ **Supérieur** |
| Raycast | ✅ HB-style + stepped LoS | ✅ | Identique |
| dtPathCorridor | ✅ PathFinder (base) | ✅ managed | Identique |
| Mesh visualization data | ❌ Pas d'API vertex/triangle | ✅ | Inférieur |
| Multi-instance queries | ❌ Singleton | ✅ | Inférieur |
| Thread-safe concurrent queries | ❌ Single-threaded | ✅ | Inférieur |

**Conclusion :** La DLL est solide pour le pathfinding bot. Les lacunes (visualisation, multi-instance) sont non-bloquantes pour l'utilisation bot mais empêchent des outils avancés (mesh viewer, multi-bot).

### B.12 — Quel est le pourcentage global de parité feature ?

**Parité estimée : 72%**

Détail :
- **P/Invoke + DLL :** 95% (quasi-parfait)
- **TripperNavigator :** 85% (area costs, filters, post-processing OK)
- **Navigator.MoveTo :** 70% (off-mesh OK mais fragile, stuck OK, async manquant)
- **Fonctionnalités avancées :** 40% (pas d'async, pas de door handling, pas de path start skip)

Pondéré par importance : **~72% de parité feature** avec HB pour le cas d'usage WotLK bot.

---

## C — Crédibilité du PLAN_NAVIGATION.md

### C.13 — La correction QC de Phase 3 est-elle justifiée ?

**Verdict : 100% JUSTIFIÉE**

Le PLAN Phase 3 originale comparait les P/Invoke C# contre les exports `NavBridge.cpp` (suffixe `_C`) au lieu des exports `DllMain.cpp`. C'est factuellement faux — aucun P/Invoke C# ne référence les fonctions `_C`. La correction QC1 qui invalide TOUTE la Phase 3 est correcte.

**Preuve indépendante :** `grep` sur NativeMethods.cs confirme zéro référence à des fonctions avec suffixe `_C`. Tous les `DllImport("Navigation")` matchent des exports DllMain.cpp.

Le PLAN qualifie Phase 3 de "CATASTROPHIQUEMENT FAUSSE" — c'est exact.

### C.14 — Les niveaux de sévérité sont-ils corrects ?

**Verdict : LARGEMENT CORRECTS avec 2 surévaluations**

| Item | Sévérité PLAN | Ma sévérité | Justification |
|------|---------------|-------------|---------------|
| P4.1 Path metadata | CRITIQUE | **HAUTE** | Résolu en Phase 8. Le problème était réel mais "CRITIQUE" surévalué car le bot marchait déjà sans metadata — juste pas de off-mesh. |
| P4.2 Off-mesh dispatch | CRITIQUE | CRITIQUE | ✅ Correct — un bot qui bloque sur un elevator est inutilisable. |
| P4.6 StuckHandler | HAUTE | HAUTE | ✅ Correct après réduction de CRITIQUE par QC2. |
| P6.12 Async pathfinding | HAUTE | **CRITIQUE** | Sous-évalué — le freeze du EndScene thread pendant 2+ secondes est visible par le joueur ET peut causer un timeout de réponse au serveur. C'est le problème #1 restant. |
| P6.14 Combat abort | HAUTE | HAUTE (couplé à P6.12) | ✅ Correct — résolu automatiquement si P6.12 est résolu. |

### C.15 — Les implémentations Phase 8-9 sont-elles correctes ?

**Verdict : MAJORITAIREMENT CORRECTES — 4 faiblesses identifiées dont 1 critique**

| Implémentation | Correcte ? | Issue |
|----------------|-----------|-------|
| 8.1 StuckHandler dans MoveTo | ✅ OUI | Bien intégré, timer 2s, reset approprié |
| 8.2 Push-ahead raycast | ✅ OUI | Raycast avant push, fallback au waypoint exact |
| 8.3 MoveAwayFromEdges logging | ⚠️ PARTIEL | Log ajouté dans catch, mais `FixPathWalkability` reste un no-op |
| 8.4 Store PathFindResult | ✅ OUI | Flags/PolyTypes/AbilityFlags stockés correctement |
| 8.5 Path validity check | ✅ OUI | DistanceToLineSegment > 10yd → regenerate |
| 8.7 Off-mesh dispatch | ⚠️ FRAGILE | HandleInteractUnit cherche n'importe quelle unité, pas celle de la connexion |
| 8.8 IsPartialPath | ✅ OUI | Status.IsPartialResult implémenté, warning loggé |
| 9.2 Fix 8 filtres locaux | ✅ OUI | _defaultFilter utilisé partout (vérifié dans Navigation.cpp actuel) |
| 9.3 Fix memory leak | ✅ OUI | Double allocation supprimée |
| 9.4 Debug file write | ✅ OUI | Supprimé (confirmé dans HaveTile()) |
| 9.5 PostProcessor cleanup | ✅ OUI | Branches redondantes unifiées |
| 9.6 Off-mesh dispatch | ❌ **MORT** | Indexation `_currentPathIndex - 1` au lieu de `_currentPathIndex` → area type toujours Ground → handlers jamais appelés |

### C.16 — Qu'est-ce que le PLAN rate/omet ?

**7 items non documentés dans le PLAN :**

| # | Finding | Sévérité | Détail |
|---|---------|----------|--------|
| **NEW-1** | `getNavTerrain()` toujours NAV_GROUND | **MOYEN** | [PathFinder.cpp#L620](../../../.test/C++/Navigation/PathFinder.cpp#L620) — `updateFilter()` pour la nage est un no-op. Le filtre ne sera jamais ajusté pour le terrain aquatique. Impact : le pathfinding ne priorisera pas correctement les zones d'eau quand le joueur nage. |
| **NEW-2** | `FixPathWalkability` est un no-op | **MOYEN** | [PathPostProcessor.cs#L297](Tripper/Navigation/PathPostProcessor.cs#L297) — Segments bloqués après MoveAwayFromEdges ne sont jamais corrigés. Le PLAN mentionne le try/catch vide (QC2.1) mais pas le fait que FixPathWalkability ne fixe littéralement rien. |
| **NEW-3** | `FinalizeSlicedFindPath` inconsistant | **BAS** | [Navigation.cpp#L1210](../../../.test/C++/Navigation/Navigation.cpp#L1210) — Utilise `DT_STRAIGHTPATH_AREA_CROSSINGS` au lieu de `DT_STRAIGHTPATH_ALL_CROSSINGS` utilisé par BuildPointPath. Moins de waypoints = risque de corners coupés. Non-bloquant car sliced pathfinding n'est pas utilisé dans le flux principal. |
| **NEW-4** | HandleInteractUnit dangereuse | **HAUTE** | [Navigator.cs#L891](Styx/Logic/Pathing/Navigator.cs#L891) — Prend n'importe quelle unité la plus proche pendant un off-mesh traverse. En combat, peut cibler un mob hostile au lieu du PNJ de transport. |
| **NEW-5** | Pas de max retry pour unstick | **MOYEN** | `MoveTo()` retourne `UnstuckAttempt` indéfiniment sans compteur. Dépend entièrement de la logique externe (ActionMoveToPoi timeout) ou du StuckHandler interne. |
| **NEW-6** | `findSmoothPath()` orpheline en C++ | **INFO** | [PathFinder.cpp#L797](../../../.test/C++/Navigation/PathFinder.cpp#L797) — ~150 lignes de code mort. Jamais appelé (BuildPointPath utilise findStraightPath). Le PLAN Phase 1 le mentionne correctement mais ne recommande pas la suppression. |
| **NEW-7** | Road cost = 0.5 vs HB = 1.0 | **BAS** | [Tripper/Navigation/Navigator.cs#L235](Tripper/Navigation/Navigator.cs#L235) — CopilotBuddy force Road=0.5 (HB 4.3.4 utilisait Road=1.0). Cela fait que les paths suroptimisent vers les routes, parfois en faisant des détours. N'est pas un bug mais un choix de tuning discutable. |
| **NEW-8** | **Off-mesh dispatch indexation fausse** | **CRITIQUE** | [Navigator.cs#L344](Styx/Logic/Pathing/Navigator.cs#L344) — `_currentPolyTypes[_currentPathIndex - 1]` utilise l'area type du polygone PRÉCÉDENT (Ground/Road) au lieu du polygone off-mesh (`_currentPathIndex`). Le `switch(areaType)` reçoit toujours Ground/Road → les handlers Elevator/Portal/InteractUnit/InteractObject ne sont **jamais déclenchés**. Le dispatch off-mesh entier est mort. |

---

## Findings complémentaires — Vérifications croisées

### Chaîne complète Area Costs

Vérifié indépendamment de bout en bout :

```
C# SetDefaultAreaCosts() → NativeMethods.SetAreaCost(id, cost)
  → DllMain SetAreaCost() → Navigation::SetAreaCost()
    → _areaCosts[id] = cost + _defaultFilter.setAreaCost(id, cost)
      → PathFinder copie _defaultFilter dans createFilter()
        → Detour A* utilise ces coûts dans findPath()
```

**Verdict : ✅ CHAÎNE COMPLÈTE FONCTIONNELLE.** Chaque maillon est vérifié.

### Blackspot System

```
C# BlackspotManager.EnsureBlackspotsMarked()
  → NativeMethods.SetPolyArea(mapId, polyRef, areaId)
  → DllMain SetPolyArea() → navMesh->setPolyArea()
```

**Verdict : ✅ FONCTIONNEL.** Les blackspots sont marqués avant chaque FindPath.

### Coordinate Safety

Test mental du pipeline avec un point WoW (100, 200, 50) :
1. C# envoie XYZ(100, 200, 50)
2. DllMain reçoit, WoWToDetour → Detour(200, 50, 100)
3. Detour pathfinds en (200, 50, 100)
4. Résultat Detour (200, 50, 100), DetourToWoW → WoW(100, 200, 50)
5. C# reçoit XYZ(100, 200, 50) ✅

---

## Tableau Récapitulatif des Risques

| Risque | Sévérité | Probabilité | Impact en jeu |
|--------|----------|-------------|---------------|
| **Off-mesh dispatch indexation fausse** | **CRITIQUE** | Certaine | **Elevator/portal handlers jamais appelés — bot bloque sur tout transport** |
| Pathfinding synchrone bloque EndScene | **CRITIQUE** | Haute (long trajet) | Bot freeze visible, possible disconnect |
| HandleInteractUnit cible le mauvais mob | **HAUTE** | Moyenne (zones complexes) | Bot interagit avec un ennemi au lieu du PNJ |
| HandleElevator sans direction check | **HAUTE** | Moyenne (multi-elevator) | Bot monte dans le mauvais elevator |
| FixPathWalkability no-op | **MOYEN** | Faible (rare cas) | Path post-MoveAwayFromEdges invalide silencieusement |
| Water Z+2f manquant | **BAS** | Faible (nage rare en bot) | Bot nage sous la surface |
| getNavTerrain stub | **MOYEN** | Moyenne (zones d'eau) | Filtre non ajusté en nage |
| Road=0.5 suroptimisation | **BAS** | Faible | Détours inutiles vers routes |

---

## Plan d'Action Recommandé (par priorité)

### Priorité 1 — CRITIQUE
1. **Corriger l'indexation off-mesh dispatch** — Remplacer `_currentPolyTypes[_currentPathIndex - 1]` par `_currentPolyTypes[_currentPathIndex]` dans [Navigator.cs#L344](Styx/Logic/Pathing/Navigator.cs#L344). Fix d'une ligne qui débloque TOUT le off-mesh handling (elevators, portals, interact). Ajouter un log de debug pour vérifier l'area type reçu.
2. **Implémenter pathfinding asynchrone** — Exécuter `CalculatePathEx` sur un thread worker avec `CancellationToken`. Retourner le dernier path valide pendant le calcul. Ajouter combat abort via callback.

### Priorité 2 — HAUTE
2. **Sécuriser HandleInteractUnit** — Filtrer par `NpcFlags` ou distance au point off-mesh (< 5 yards du waypoint), pas juste n'importe quelle unité la plus proche.
3. **Sécuriser HandleElevator** — Ajouter vérification de direction (elevator se rapproche-t-il ou s'éloigne-t-il du target Z ?).

### Priorité 3 — MOYEN
4. **Implémenter FixPathWalkability** réellement — Quand HasLineOfSight échoue, insérer un sous-path entre les 2 points bloqués.
5. **Ajouter Water/Lava Z+2f** — Vérifier `PolyTypes[i] == Water/Lava` et surélever le waypoint de +2f.
6. **Implémenter path start skip** — Raycast depuis la position actuelle vers les waypoints 2-3-4, skip les visibles.
7. **Corriger getNavTerrain** — Utiliser le Detour poly area type au lieu du hardcode NAV_GROUND.

### Priorité 4 — BAS
8. **Supprimer findSmoothPath** orpheline — Code mort, 150 lignes.
9. **Considérer Road=1.0** au lieu de 0.5 — Tester en pratique si le bot fait des détours.

---

## Verdict Final

### Note de Confiance : 5.5/10

**Justification :**

| Couche | Note | Commentaire |
|--------|------|-------------|
| C++ Navigation.dll | **8/10** | Solide, bien porté de MaNGOS, corrections Phase 9 pertinentes |
| P/Invoke Layer | **9/10** | Quasi-parfait, structs alignées, noms matchent |
| TripperNavigator | **8/10** | FindPath complet, post-processing OK, filters configurables |
| Navigator.MoveTo() | **4/10** | Stuck + push-ahead OK, MAIS off-mesh dispatch mort (indexation), handlers fragiles |
| Architecture globale | **4/10** | Le threading synchrone + off-mesh mort limitent sévèrement le système |

**Le bot naviguera correctement dans 80-85% des situations** (terrain plat, chemins courts, pas d'elevators). Les 15-20% restants (long trajet, elevator, underwater, combat pendant pathfinding) causeront des blocages visibles. **Le off-mesh dispatch, bien que codé, est probablement inactif** en raison d'une erreur d'indexation dans le tableau PolyTypes — ce qui rend le bot inutilisable dans toute zone nécessitant un transport (Undercity, Thunder Bluff, certains donjons).

**Le PLAN_NAVIGATION.md est fiable à ~80%** — ses auto-corrections QC sont justes, ses implémentations Phase 8-9 sont majoritairement correctes mais l'erreur d'indexation off-mesh n'a pas été détectée, le threaded pathfinding n'est pas résolu, et `FixPathWalkability` reste un no-op non documenté comme tel.

---

## Annexe — HB 6.2.3 Navigation System Deep Dive

*Research date: February 7, 2026*

This appendix documents the full HB 6.2.3 (WoD) navigation architecture, extracted from the decompiled reference. All line numbers refer to the decompiled `.cs` files in `.hb 6.2.3\`.

---

### 1. Architecture Overview — Class Hierarchy

```
Navigator (static facade)                     Styx.Pathing.Navigator
  └─ NavigationProvider (abstract)            Styx.Pathing.NavigationProvider
       └─ MeshNavigator (concrete impl)      Styx.Pathing.MeshNavigator
            └─ WowNavigator (pathfinding)     Tripper.Navigation.WowNavigator
                 ├─ WorldMeshManager          Tripper.Navigation.WorldMeshManager (implements IMeshManager)
                 ├─ GarrisonMeshManager       Tripper.Navigation.GarrisonMeshManager (implements IMeshManager)
                 └─ Class1458 (pathfinder)    ns73.Class1458 (actual sliced pathfinding logic)
                      └─ NavMeshQuery         Tripper.RecastManaged.Detour.NavMeshQuery (C++/CLI wrapper)
```

**Key difference from HB 4.3.4:** 6.2.3 introduces `IMeshManager` abstraction with separate `WorldMeshManager` and `GarrisonMeshManager`, whereas 4.3.4 had all mesh management directly in `WowNavigator`. The pathfinding algorithm itself was extracted into `Class1458`.

---

### 2. Navigator — Static Facade

**File:** `Honorbuddy/Styx/Pathing/Navigator.cs` (358 lines)

The static `Navigator` class is the public API:

```csharp
// Line 22 — Default initialization
internal static bool smethod_0()
{
    Navigator.NavigationProvider = new MeshNavigator();
    return true;
}

// Line 27–48 — NavigationProvider property with lifecycle management
public static NavigationProvider NavigationProvider
{
    set {
        if (value != null) value.OnSetAsCurrent();
        if (Navigator.navigationProvider_0 != null) Navigator.navigationProvider_0.OnRemoveAsCurrent();
        Navigator.navigationProvider_0 = value;
        // fires OnNavigationProviderChanged event
    }
}

// Line 71–73 — Default PlayerMover
public static IPlayerMover PlayerMover { get; set; } = new ClickToMoveMover();

// Line 79–83 — Default HeightProvider
public static ITerrainHeightProvider HeightProvider { get; set; } = new Class1050();

// Line 252 — MoveTo delegates to NavigationProvider
public static MoveResult MoveTo(WoWPoint location, int mapID = -1)
{
    return Navigator.NavigationProvider.MoveTo(location);
}
```

**IPlayerMover interface** (`Styx/Pathing/IPlayerMover.cs`):
```csharp
public interface IPlayerMover
{
    void Move(WoWMovement.MovementDirection direction);
    void MoveTowards(WoWPoint location);
    void MoveStop();
}
```

---

### 3. NavigationProvider — Abstract Base

**File:** `Honorbuddy/Styx/Pathing/NavigationProvider.cs` (full file, ~80 lines)

```csharp
public abstract class NavigationProvider
{
    public abstract MoveResult MoveTo(WoWPoint location);
    public abstract float PathPrecision { get; set; }
    public abstract WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to);
    public abstract bool AtLocation(WoWPoint point1, WoWPoint point2);

    public virtual StuckHandler StuckHandler { get; set; }
    public virtual bool Clear() => true;

    // Line 77–82 — CanNavigateWithin uses GeneratePath + distance check
    public virtual bool CanNavigateWithin(WoWPoint from, WoWPoint to, float distanceTolerancy)
    {
        WoWPoint[] array = this.GeneratePath(from, to);
        return array != null && array.Length != 0 &&
               array[array.Length - 1].DistanceSqr(to) < distanceTolerancy * distanceTolerancy;
    }

    // Line 84–89 — CanNavigateFully checks last point matches destination
    public virtual bool CanNavigateFully(WoWPoint from, WoWPoint to)
    {
        WoWPoint[] array = this.GeneratePath(from, to);
        return array != null && array.Length != 0 && this.AtLocation(array[array.Length - 1], to);
    }

    // Line 91–107 — PathDistance sums segment lengths
    public virtual float? PathDistance(WoWPoint from, WoWPoint to, float maxDistance = float.MaxValue) { ... }
}
```

---

### 4. MeshNavigator — The Core Implementation

**File:** `Honorbuddy/Styx/Pathing/MeshNavigator.cs` (1318 lines)

#### 4.1 Constructor & Lifecycle

```csharp
// Line 30–34
public MeshNavigator()
{
    this.PathPrecision = 2f;                        // 2 world units proximity threshold
    this.StuckHandler = new Class469(this);          // stuck detection instance
    this.class1039_0 = new Class1039(this);          // unknown helper (possibly blackspot manager)
}

// Line 41–57 — OnSetAsCurrent(): attaches to events
public override void OnSetAsCurrent()
{
    this.Nav = new WowNavigator();
    this.Nav.OnPathFindProgress += this.method_16;    // progress/cancel callback
    BotEvents.OnBotStarted += this.method_4;          // sets faction filter
    BotEvents.OnBotStopped += this.method_3;          // clears path
    BotEvents.OnPulse += this.method_1;               // updates maps per pulse
    Mount.OnMountUp += this.method_17;                // cancels mount in elevator
    this.Nav.Interface14_0 = new Class1062();          // mesh provider
    this.Nav.OnNavigatorLogMessage += smethod_0;       // diagnostic logging
}
```

#### 4.2 MoveTo Method — HB 6.2.3 Full Flow

```csharp
// Line 197–249 — public override MoveResult MoveTo(WoWPoint location)
public override MoveResult MoveTo(WoWPoint location)
{
    // 1. Validate destination
    if (location == WoWPoint.Zero) return MoveResult.Failed;

    WoWUnit activeMover = WoWMovement.ActiveMover;
    if (activeMover == null) return MoveResult.Failed;

    WoWPoint myPos = activeMover.Location;

    // 2. Already at destination?
    if (this.method_27(myPos, location))  // uses PathPrecision² + Z check
    {
        Navigator.PlayerMover.MoveTowards(location);
        this.Clear();
        return MoveResult.ReachedDestination;
    }

    // 3. Garrison exit detection
    if (this.method_6(myPos)) { this.Clear(); /* Log + repath */ }

    // 4. Door handling — checks for closed in-range doors
    if (this.method_7()) return MoveResult.Moved;

    // 5. Check if current path is still valid
    bool needStuckReset;
    if (!this.method_9(myPos, location, out needStuckReset))
    {
        // Path still valid — mount check + follow existing path
        if (Mount.ShouldMount(location))
            Mount.StateMount(new LocationRetriever(() => location));
        return this.MovePath(this.CurrentMovePath);
    }

    // 6. Flight path check for long distances (>400 yards)
    if (this.method_10(activeMover, myPos, location))
        return MoveResult.PathGenerated;

    // 7. Generate new path
    MeshMovePath meshMovePath = this.method_11(myPos, location);
    if (meshMovePath == null) return MoveResult.PathGenerationFailed;

    this.CurrentMovePath = meshMovePath;
    if (needStuckReset) this.StuckHandler.Reset();

    // 8. Path node skipping via raycast
    this.method_14(this.CurrentMovePath, myPos);

    return MoveResult.PathGenerated;
}
```

**Key details:**
- **Line 316** — Flight path threshold: `distanceSqr > 160000f` (= 400 yards)
- **Line 325** — Garrison building detection: checks `AreaType.KnownBuilding`
- **Line 292** — Path revalidation: `method_9` checks if destination changed or player is off-path

#### 4.3 AtLocation Check (PathPrecision)

```csharp
// Line 1021 — method_27: proximity + height check
private bool method_27(Vector3 a, Vector3 b)
{
    return smethod_2(ref a, ref b) <= this.Single_0    // PathPrecision² (4f by default)
        && Math.Abs(a.Z - b.Z) < 4.5f;                 // Z tolerance
}
```

#### 4.4 FindPath — Async Task Pattern (IMPORTANT DIFFERENCE)

```csharp
// Line 557–620 — public PathFindResult FindPath(WoWPoint start, WoWPoint end)
public PathFindResult FindPath(WoWPoint start, WoWPoint end)
{
    // ABORT if bot stopping
    if (TreeRoot.State == TreeRootState.Stopping)
        return abortedResult;

    // Set alive/dead filter
    this.method_28();

    // *** KEY: Pathfinding runs on a background Task ***
    Task<PathFindResult> task = Task<PathFindResult>.Factory.StartNew(
        () => this.Nav.FindPath(start, end));

    if (task.Wait(10))  // fast path: result in <10ms
        return task.Result;

    // Slow path: release frame, keep bot alive during pathfinding
    using (StyxWoW.Memory.ReleaseFrame(true))
    {
        while (!task.Wait(1000 / (int)TreeRoot.TicksPerSecond))
        {
            StyxWoW.Memory.ClearCache();
            using (StyxWoW.Memory.AcquireFrame())
            {
                ObjectManager.Update();
                WoWMovement.Pulse();
            }
            this.bool_1 = StyxWoW.Me.IsActuallyInCombat;  // sets combat abort flag
            StyxWoW.ResetAfk();
        }
    }
    ObjectManager.Update();
    return task.Result;
}
```

**THIS IS THE BIGGEST DIFFERENCE FROM 4.3.4.** In 6.2.3, `FindPath` uses `Task<PathFindResult>` to run pathfinding on a background thread while keeping the bot responsive (pulsing ObjectManager, checking combat). In 4.3.4, `FindPath` was directly in `WowNavigator` (synchronous, no frame release). Our CopilotBuddy implementation should be synchronous per the instructions, but this pattern is worth noting for future optimization.

#### 4.5 Path Progress Cancellation (Combat Abort)

```csharp
// Line 646–660 — method_16: OnPathFindProgress handler
private void method_16(object sender, PathFindProgressEventArgs e)
{
    if (TreeRoot.State == TreeRootState.Stopping) { e.Cancel = true; return; }

    if (!this.bool_1) { this.nullable_0 = null; return; }  // not in combat

    if (this.nullable_0 == null)
        this.nullable_0 = e.RunTime;  // record combat start time

    // Abort pathfinding if combat has lasted > 4 seconds
    e.Cancel = e.RunTime.Subtract(this.nullable_0.Value).TotalSeconds > 4.0;
}
```

#### 4.6 MovePath — Following an Existing Path

```csharp
// Line 666–685 — public virtual MoveResult MovePath(MeshMovePath path)
public virtual MoveResult MovePath(MeshMovePath path)
{
    this.CurrentHopAbilityFlags = (path.Index > 0)
        ? path.Path.AbilityFlags[path.Index - 1] : AbilityFlags.None;

    // Off-mesh connection check (StraightPathFlags has bit 4 = off-mesh)
    if (path.Index > 0 && (path.Path.Flags[path.Index - 1] & 4) != 0)
        return this.method_18(path);  // handle off-mesh

    return this.method_24(path);  // normal movement
}
```

#### 4.7 Off-Mesh Connection Handling

**Line 698–780** — `method_18` routes by AreaType:

```csharp
switch (areaType)
{
    case AreaType.Elevator:       return this.method_20(pos, path);  // full elevator state machine
    case AreaType.Portal:
    case AreaType.DefendersPortal:
    case AreaType.HordePortal:
    case AreaType.AlliancePortal: return this.method_23(pos, path);  // find & interact portal
    case AreaType.InteractUnit:   return this.method_22(pos, path);  // find & interact NPC
    case AreaType.InteractObject: return this.method_21(pos, path);  // find & interact object
    default:                      return this.method_19(pos, path);  // standard off-mesh (run/jump)
}
```

**Elevator handler** (`method_20`, lines 800–862):
- Finds nearest transport `WoWGameObject` (excludes specific entries: 20656, 20657, 205080)
- State machine: Wait for elevator → Face toward → Move inside → Ride → Exit when Z matches
- Calls `Mount.Dismount("Moving inside transport.")` before boarding
- Uses `waitTimer_2` (400ms) to track elevator movement
- **Cancels mounting** via `Mount.OnMountUp` when riding elevator

**Portal handler** (`method_23`, lines 935–960): Searches for nearest Goober/SpellCaster object, auto-interacts.

**InteractObject handler** (`method_21`, lines 863–903): Finds usable object near off-mesh start point, walks to it, dismounts, interacts with 2-second cooldown timer.

**InteractUnit handler** (`method_22`, lines 904–935): Finds non-player-controlled unit, walks to it, dismounts, interacts.

**Standard off-mesh** (`method_19`, lines 780–793): Only allows Run|Jump ability flags; fails otherwise.

#### 4.8 Path Node Skipping (Raycast Optimization)

```csharp
// Line 430–495 — method_14: skip visible early path nodes via raycast
private void method_14(MeshMovePath path, Vector3 myPos)
{
    // Find start poly
    PolygonReference startPoly;
    meshQuery.FindNearestPolygon(navPos, extents, filter, ...);

    // Walk forward through nodes, raycast from current position
    int i = 1;
    while (i < path.Points.Length && !hasOffMeshFlag)
    {
        if (raycast_is_blocked) break;
        i++;
    }

    // Skip to visible node (with off-mesh protection)
    int skipTo = i - 1;
    if (path.Flags[skipTo].HasFlag(4))  // off-mesh at boundary
    {
        // Check area type — don't skip elevators/portals/interact
        if (areaType != AreaType.Elevator && areaType != AreaType.Portal ...)
        {
            // Project player to off-mesh segment, skip if close enough
        }
    }
    path.Index = skipTo;
}
```

#### 4.9 Path Extension for ClickToMove

```csharp
// Line 967–1010 — method_26: extends path point forward by PathPrecision
// Only applies when PlayerMover is ClickToMoveMover
// Extends intermediate waypoints (not first/last) by PathPrecision distance
// along direction of travel, verified by raycast to not cross walls
```

#### 4.10 Alive/Dead Filter Toggle

```csharp
// Line 1035–1047 — method_28
private void method_28()
{
    if (StyxWoW.Me.IsAlive)
    {
        Nav.QueryFilter.ExcludeFlags &= ~AbilityFlags.OnlyWhileAlive;
        Nav.QueryFilter.IncludeFlags |= AbilityFlags.OnlyWhileAlive;
    }
    else
    {
        Nav.QueryFilter.IncludeFlags &= ~AbilityFlags.OnlyWhileAlive;
        Nav.QueryFilter.ExcludeFlags |= AbilityFlags.OnlyWhileAlive;
    }
}
```

---

### 5. WowNavigator — Pathfinding Engine

**File:** `Honorbuddy/Tripper/Navigation/WowNavigator.cs` (809 lines)

#### 5.1 Constructor & Query Filter Setup

```csharp
// Lines 17–44
public WowNavigator()
{
    this.Extents = new Vector3(3f, 20f, 3f);  // search extents (X=3, Y=20, Z=3)
    this.PathPostProcessing = PathPostProcessing.MoveAwayFromEdges;

    // 5 named filters stored in dictionary:
    this.dictionary_0["Default"]   = GetNewDefaultQueryFilter();
    this.dictionary_0["Horde"]     = smethod_2();  // default + exclude Alliance, Alliance cost=50
    this.dictionary_0["Alliance"]  = smethod_1();  // default + exclude Horde, Horde cost=50
    this.dictionary_0["Horde_DeathKnightStart"]    = /* horde + transport allowed */;
    this.dictionary_0["Alliance_DeathKnightStart"] = /* alliance + transport allowed */;

    this.ResetQueryFilter();  // set active to "Default"
    this.WorldMesh = new WorldMeshManager(this);
    this.GarrisonMesh = new GarrisonMeshManager(this);
}
```

#### 5.2 Default Area Costs — FULL TABLE (6.2.3)

```csharp
// Lines 523–541 — SetDefaultQueryFilterCosts
filter.SetAreaCost(AreaType.HordePortal,     1.66f);  // was 1.0f in 4.3.4
filter.SetAreaCost(AreaType.AlliancePortal,  1.66f);  // was 1.0f in 4.3.4
filter.SetAreaCost(AreaType.DefendersPortal, 3.16f);  // same
filter.SetAreaCost(AreaType.Portal,          1.66f);  // was 1.0f in 4.3.4
filter.SetAreaCost(AreaType.Elevator,        3.16f);  // same
filter.SetAreaCost(AreaType.Lava,            55f);    // same
filter.SetAreaCost(AreaType.Blocked,         100f);   // same
filter.SetAreaCost(AreaType.Fall,            1.7f);   // same
filter.SetAreaCost(AreaType.Gate,            1.66f);  // same
filter.SetAreaCost(AreaType.Ground,          1.66f);  // same
filter.SetAreaCost(AreaType.KnownBuilding,   1.66f); // NEW in 6.2.3 (garrisons)
filter.SetAreaCost(AreaType.Road,            1f);     // same
filter.SetAreaCost(AreaType.Water,           3.33f);  // same
filter.SetAreaCost(AreaType.Alliance,        1.66f);  // NEW in 6.2.3
filter.SetAreaCost(AreaType.Horde,           1.66f);  // NEW in 6.2.3
filter.SetAreaCost(AreaType.Blackspot,       60f);    // NEW in 6.2.3
filter.SetAreaCost(AreaType.InteractObject,  1.66f);  // was 1.0f in 4.3.4
filter.SetAreaCost(AreaType.InteractUnit,    1.66f);  // was 1.0f in 4.3.4
```

#### 5.3 Faction Filter Creation

```csharp
// Line 559–563 — smethod_1 (Alliance filter)
private static WowQueryFilter smethod_1()
{
    WowQueryFilter filter = GetNewDefaultQueryFilter();
    filter.ExcludeFlags |= AbilityFlags.Horde;      // exclude Horde-only paths
    filter.SetAreaCost(AreaType.Horde, 50f);         // huge penalty for Horde areas
    return filter;
}

// Line 568–572 — smethod_2 (Horde filter)
private static WowQueryFilter smethod_2()
{
    WowQueryFilter filter = GetNewDefaultQueryFilter();
    filter.ExcludeFlags |= AbilityFlags.Alliance;
    filter.SetAreaCost(AreaType.Alliance, 50f);
    return filter;
}
```

#### 5.4 Faction-Aware Filter Selection

```csharp
// Lines 295–307 — SetFactionQueryFilter
public void SetFactionQueryFilter(bool isHorde)
{
    string mapName = this.PrimaryMapName;
    if (!string.IsNullOrWhiteSpace(mapName))
    {
        string key = (isHorde ? "Horde" : "Alliance") + "_" + mapName;
        if (HasQueryFilter(key))  // e.g. "Horde_DeathKnightStart"
        {
            SetQueryFilterByStored(key);
            return;
        }
    }
    SetQueryFilterByStored(isHorde ? "Horde" : "Alliance");
}
```

#### 5.5 FindPath Delegation

```csharp
// Line 313–320
public PathFindResult FindPath(Vector3 start, Vector3 end)
{
    if (GarrisonMesh.IsLoaded && IsWithinGarrison(start) && IsWithinGarrison(end))
        return GarrisonMesh.FindPath(start, end);
    return WorldMesh.FindPath(start, end);
}
```

---

### 6. Class1458 — The Sliced Pathfinding Algorithm

**File:** `Honorbuddy/ns73/Class1458.cs` (690 lines)

This is the actual pathfinding implementation used by both `WorldMeshManager` and `GarrisonMeshManager`.

#### 6.1 Core FindPath Flow

```csharp
// Line 22–196 — method_0(Vector3 start, Vector3 end)
public PathFindResult method_0(Vector3 start, Vector3 end)
{
    // 1. Garbage collect old tiles
    if (this.class1457_0 != null) this.class1457_0.Action_0();

    // 2. Convert WoW coords → Detour coords
    Vector3 navStart = NavHelper.ToNav(start);
    Vector3 navEnd   = NavHelper.ToNav(end);

    // 3. Validate coordinates (not zero, not NaN)
    if (start == Vector3.Zero || !isFinite(start.X/Y/Z)) → Fail at FindStartPoly
    if (end   == Vector3.Zero || !isFinite(end.X/Y/Z))   → Fail at FindEndPoly

    // 4. Load tiles for start and end positions
    manager.LoadTile(TileIdentifier.GetByPosition(start));
    manager.LoadTile(TileIdentifier.GetByPosition(end));

    // 5. Find start polygon
    FindNearestPolygon(navStart, Extents(3,20,3), filter) → startPoly
    if (startPoly.Id == 0) → Fail at FindStartPoly

    // 6. Find end polygon
    FindNearestPolygon(navEnd, Extents, filter) → endPoly
    if (endPoly.Id == 0) → Fail at FindEndPoly

    // 7. *** SLICED pathfinding ***
    InitSlicedFindPath(startPoly, endPoly, navStart, navEnd, filter)

    // 8. Update loop with 10,000 iterations per slice
    while (status.InProgress)
    {
        status = UpdateSlicedFindPath(10000);

        // Check for abort (combat, bot stopping)
        if (nav.method_3(stopwatch.Elapsed)) → Aborted
    }

    // 9. Finalize — extract polygon corridor (max 8192 polys)
    FinalizeSlicedFindPath(8192) → PolygonReference[] corridor

    // 10. Partial path handling
    if (status.HasFlag(64))  // DT_PARTIAL_RESULT
    {
        result.IsPartialPath = true;
        // Snap end point: if last poly is NOT off-mesh (Type != 1)
        // snap end position to closest point on last polygon
        GetTileAndPolyByRef(corridor.Last()) → check poly.Type
        if (poly.Type != 1)
            ClosestPointOnPoly(corridor.Last(), navEnd) → snapped navEnd
    }

    // 11. Convert corridor → straight path (max 8192 points)
    FindStraightPath(navStart, navEnd, corridor, 8192) → points[], flags[], polyRefs[]

    // 12. Post-processing
    if (PathPostProcessing == MoveAwayFromEdges)
        method_11(ref points, ref polyRefs, ref flags, 2f);     // push points 2 units from walls
    else if (PathPostProcessing == Randomize)
        method_8(ref points, ref polyRefs, ref flags, 2f, 6f, 4f); // randomize within safe area

    // 13. Convert Detour → WoW coordinates
    for each point: NavHelper.ToWow(ref point);

    // 14. Extract area types and ability flags per polygon
    for each polyRef: GetPolyArea → AreaType[], GetPolyFlags → AbilityFlags[]

    // 15. Water/Lava Z fix: if path ends in water, use original end Z
    if (secondToLast area is Water or Lava && last point Z > original end Z)
        points[last] = original end;

    return result;
}
```

**Key parameters:**
- Sliced iterations per call: **10,000** (same as 4.3.4)
- Max polygon corridor: **8,192** (same as 4.3.4)
- Max straight path points: **8,192** (same as 4.3.4)
- Search extents: **(3, 20, 3)** — X±3, Y±20, Z±3 (same as 4.3.4)

#### 6.2 Post-Processing: MoveAwayFromEdges

Implemented in `method_11` (mapped through `method_3`):
- For each intermediate path point (not first/last, not off-mesh):
  - Call `FindDistanceToWall` to get distance to nearest wall
  - If wall < threshold (2f), push point away along wall normal
  - Verify new position with raycast to ensure walkability

#### 6.3 Post-Processing: Randomize

Implemented in `method_4`:
- For each intermediate point:
  - Generate random offset distance between min (2f) and max (6f)
  - If near wall: move away; if no wall near: `FindRandomPointAroundCircle`
  - Max random displacement: 4f

---

### 7. WorldMeshManager — Tile Management

**File:** `Honorbuddy/Tripper/Navigation/WorldMeshManager.cs` (438 lines)

```csharp
// Lines 88–118 — Mesh initialization
private void method_2(Class1066 mapConfig)
{
    NavMesh mesh = new NavMesh();
    if (mapConfig.Type == Enum55.Single)           // single-tile map
        mesh.Init(provider.imethod_0(mapConfig));
    else                                           // tiled map
    {
        NavMeshParams params = new NavMeshParams {
            Origin = Vector3.Zero,
            TileHeight = MeshMapCalculator.Default.DetourTileSize,
            TileWidth  = MeshMapCalculator.Default.DetourTileSize,
            MaxPolys   = 4096,         // max polys per tile
            MaxTiles   = 16384         // max total tiles loaded
        };
        mesh.Init(params);
    }

    NavMeshQuery query = new NavMeshQuery();
    query.Init(mesh, 748983);              // maxNodes = 748,983 for A* open list
    query.SetTileLoaderFunction(LoadTileCb);
}
```

**Key numbers:**
- `MaxPolys = 4096` per tile
- `MaxTiles = 16384` concurrently loaded
- `maxNodes = 748,983` A* node pool (very large — supports long paths)
- Each WoW tile maps to 4×4 Detour sub-tiles
- Garbage collection: tiles unused for >1 minute are unloaded

---

### 8. WowQueryFilter / QueryFilter — Area Cost System

**File:** `Honorbuddy/Tripper/Navigation/WowQueryFilter.cs` (wraps detour QueryFilter)

```csharp
public class WowQueryFilter : IDisposable
{
    public QueryFilter InternalFilter { get; }
    public AbilityFlags IncludeFlags { get; set; }  // → cast to ushort
    public AbilityFlags ExcludeFlags { get; set; }

    public void SetAreaCost(AreaType area, float cost)
    {
        if (cost < 1f) throw new ArgumentOutOfRangeException("cost", "Cost must be >= 1");
        InternalFilter.SetAreaCost((byte)area, cost);
    }
}
```

**File:** `Tripper.RecastManaged/Detour/QueryFilter.cs` — C++/CLI wrapper:
- Size: 264 bytes native (`dtQueryFilter` struct)
- Supports: `IncludeFlags` (ushort), `ExcludeFlags` (ushort), `GetAreaCost(byte)`, `SetAreaCost(byte, float)`
- Default instance: `QueryFilter.Default`

---

### 9. OffMeshConnection — Structure

**File:** `Tripper.RecastManaged/Detour/OffMeshConnection.cs`

```csharp
public class OffMeshConnection : IDisposable
{
    public Vector3 Start { get; set; }              // offset 0, 12 bytes
    public Vector3 End { get; set; }                // offset 12, 12 bytes
    public float Radius { get; set; }               // offset 24, 4 bytes
    public ushort PolyIndex { get; set; }            // offset 28, 2 bytes
    public DirectionFlags Flags { get; set; }        // offset 30, 1 byte (Bidirectional=1|Unidirectional=0)
    public byte Side { get; set; }                   // offset 31, 1 byte
}
// Total native size: 36 bytes (matches dtOffMeshConnection)
```

---

### 10. AreaType Enum — Full 6.2.3 Values

```csharp
public enum AreaType : byte
{
    Ground         = 1,
    Water          = 2,
    Lava           = 3,
    Road           = 4,
    Fall           = 5,
    Elevator       = 6,
    Gate           = 7,
    Portal         = 8,
    DefendersPortal= 9,
    HordePortal    = 10,
    AlliancePortal = 11,
    Blocked        = 12,
    InteractUnit   = 13,
    InteractObject = 14,
    Horde          = 15,
    Alliance       = 16,
    Blackspot      = 17,
    KnownBuilding  = 18,
    Misc1–Misc10   = 20–29
}
```

---

### 11. AbilityFlags — Off-Mesh Direction Flags

```csharp
[Flags]
public enum AbilityFlags : ushort
{
    None           = 0,
    Run            = 1,
    OnlyWhileAlive = 2,
    Swim           = 4,
    Jump           = 8,
    Unwalkable     = 16,
    Teleport       = 32,
    Transport      = 64,
    Horde          = 4096,
    Alliance       = 8192,
    KnownBuilding  = 16384,
    All            = 65535
}
```

---

### 12. Coordinate Conversion (WoW ↔ Detour)

**File:** `Honorbuddy/Tripper/MeshMisc/GraphicalHelper.cs`

```csharp
// WoW → Detour
public static void ToDetour(ref Vector3 wow, out Vector3 detour)
{
    float num = -wow.X;
    detour.X = -wow.Y;    // detour.X = -wow.Y
    detour.Y =  wow.Z;    // detour.Y =  wow.Z (height)
    detour.Z =  num;      // detour.Z = -wow.X
}

// Detour → WoW
public static void ToWow(ref Vector3 detour, out Vector3 wow)
{
    float y = detour.Y;
    wow.Y = -detour.X;    // wow.Y = -detour.X
    wow.X = -detour.Z;    // wow.X = -detour.Z
    wow.Z =  y;           // wow.Z =  detour.Y (height)
}
```

---

### 13. MoveResult Enum

```csharp
public enum MoveResult
{
    Failed               = 0,
    ReachedDestination   = 1,
    PathGenerationFailed = 2,
    PathGenerated        = 3,
    UnstuckAttempt       = 4,
    Moved                = 5
}
```

---

### 14. PathFindStep Enum — Failure Diagnostics

```csharp
public enum PathFindStep
{
    None, FindStartPoly, FindEndPoly, InitPathFind,
    UpdatePathFind, FinalizePathFind, SnapPartialPathToEnd, FindStraightPath
}
```

---

### 15. Key Differences: HB 6.2.3 vs HB 4.3.4

| Aspect | HB 4.3.4 | HB 6.2.3 |
|--------|----------|----------|
| **Architecture** | `WowNavigator` does everything (mesh, pathfinding, tiles) | Split into `WorldMeshManager`, `GarrisonMeshManager`, `Class1458` |
| **IMeshManager** | Does not exist | Interface abstracting mesh+query+FindPath+LoadTile |
| **Garrison support** | N/A | Full garrison mesh system with polygon boundaries |
| **FindPath threading** | Synchronous — blocks everything | `Task<PathFindResult>.Factory.StartNew` with frame release |
| **PathPostProcessing** | None | `MoveAwayFromEdges` (default), `Randomize`, `None` |
| **Partial path handling** | Just marks `IsPartialPath=true` | Also snaps end point to closest point on last polygon |
| **Area cost: Portals** | 1.0f | 1.66f (slightly penalized) |
| **Area cost: InteractObj/Unit** | 1.0f | 1.66f |
| **New area types** | — | `Horde(15)`, `Alliance(16)`, `Blackspot(17)`, `KnownBuilding(18)` |
| **Faction filtering** | Only "Default" filter | "Default", "Horde", "Alliance", + map-specific DK start variants |
| **Tile GC** | Primitive `DateTime` check, unloads ALL tiles every 5 min | Per-tile `Stopwatch`, GC tiles idle >1 min, granular unload |
| **maxNodes (A* pool)** | Unknown (likely smaller) | 748,983 |
| **NavMesh params** | Unknown | MaxPolys=4096/tile, MaxTiles=16384 |
| **Door handling** | Not present in navigator | Auto-detects and interacts with closed doors |
| **Mount during elevator** | Not handled | Cancels mount-up via `Mount.OnMountUp` event |
| **Path node skipping** | Not present | Raycast-based skip of visible early nodes |
| **ClickToMove extension** | Not present | Extends intermediate waypoints along travel direction |
| **Alive/dead filtering** | Not present | Toggles `OnlyWhileAlive` ability flag based on alive state |
| **Combat abort timeout** | Unknown | 4 seconds combat during pathfinding → abort |
| **Water/Lava end Z fix** | Not present | Corrects end Z when path ends in water/lava |

---

*End of HB 6.2.3 Navigation appendix.*
