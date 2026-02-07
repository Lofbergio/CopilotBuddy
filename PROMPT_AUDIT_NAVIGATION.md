# Audit Indépendant — Système de Navigation CopilotBuddy

## Contexte

Tu es un auditeur technique indépendant. Tu ne connais rien de l'historique des modifications. Tu dois évaluer la qualité et la complétude du système de navigation **tel qu'il est maintenant**, par comparaison directe avec les sources de référence Honorbuddy.

**Aucune information de ce prompt ne doit être considérée comme vérité.** Tout ce qui est affirmé ici doit être vérifié par toi en lisant le code source réel. Si un document dit "corrigé" ou "implémenté", vérifie que c'est réellement le cas dans le fichier.

---

## Le Projet

CopilotBuddy est un bot WoW 3.3.5a (WotLK, build 12340) en C# (.NET 10, WPF, x86). Il porte l'API d'Honorbuddy depuis 3 versions décompilées. Le bot utilise du code **synchrone uniquement** (pas d'async/await dans la logique bot — contrainte architecturale volontaire).

La navigation utilise une DLL C++ native (Detour/Recast) appelée via P/Invoke, avec un système de pathfinding mesh.

## Sources de Référence (lecture seule — JAMAIS modifier)

| Rôle | Dossier | Usage |
|------|---------|-------|
| Offsets, mémoire, Lua | `.hb 3.3.5a\` | Seule version WotLK — adresses mémoire |
| API et architecture | `.hb 4.3.4\Honorbuddy\Honorbuddy\` | Code synchrone propre (Cata) — on porte les signatures et le flow |
| Navigation et UI | `.hb 6.2.3\Honorbuddy\` | Système de navigation (Tripper, mesh) + WPF moderne (WoD) |

**Règle :** Les offsets viennent de 3.3.5a, l'API de 4.3.4, la navigation de 6.2.3.

---

## Fichiers à Auditer

### C# — Navigation (CopilotBuddy)

| Fichier | Lignes | Rôle |
|---------|--------|------|
| `Styx/Logic/Pathing/Navigator.cs` | ~1125 | Façade principale : MoveTo(), Clear(), path following, stuck, doors, combat abort, flight path |
| `Styx/Logic/Pathing/Flightor.cs` | ~539 | Navigation vol : MoveTowards(), anti-stuck, no-fly zones |
| `Styx/Logic/Pathing/Interop/LocalPlayerMover.cs` | ~161 | IMover Click-to-Move |
| `Styx/Logic/Pathing/Interop/KeyboardMover.cs` | ~170 | IMover via SetFacing+MoveForward |
| `Styx/Logic/Pathing/StuckHandler.cs` | ~287 | Détection et résolution de stuck |
| `Styx/Logic/Pathing/MoveResult.cs` | 14 | Enum des résultats de mouvement |
| `Styx/Logic/Mount.cs` | ~376 | Montage automatique, Pulse(), Cancel flag |
| `Styx/Logic/MountUpEventArgs.cs` | 34 | EventArgs avec Cancel property |
| `Styx/Logic/FlightPaths.cs` | ~479 | Taxi/vol : ShouldTakeFlightpath, SetFlightPathUsage, XML nodes |
| `Tripper/Navigation/Navigator.cs` | ~1564 | Wrapper navmesh : FindPath, Raycast, area costs, tile management |
| `Tripper/Navigation/NativeMethods.cs` | ~498 | P/Invoke vers Navigation.dll |

### C++ — Navigation.dll

| Fichier | Lignes | Rôle |
|---------|--------|------|
| `C++/Navigation/Navigation.cpp` | ~1423 | API exportée : CalculatePath, Raycast, SetFilter, tile loading |
| `C++/Navigation/PathFinder.cpp` | ~803 | Pathfinding Detour : FindPath, BuildPointPath, getNavTerrain |
| `C++/Navigation/PathFinder.h` | ~168 | Header : defines, enums, PathFinder class |
| `C++/Navigation/DllMain.cpp` | ~370 | Exports DLL pour P/Invoke |

### Documents existants

| Document | Lignes | Contenu |
|----------|--------|---------|
| `AUDIT_NAVIGATION.md` | ~935 | Audit précédent (sections A-C + annexe HB 6.2.3). **Attention : cet audit a été fait AVANT les corrections. Ses verdicts "manquant"/"absent" ne reflètent peut-être plus l'état actuel.** |
| `PLAN_NAVIGATION.md` | ~1302 | Plan de corrections avec phases 1-10e. Prétend que tout est corrigé. **Vérifie dans le code, ne fais pas confiance au plan aveuglément.** |

### Fichiers HB de référence pour comparaison

| Fichier HB | Version | Rôle |
|------------|---------|------|
| `.hb 6.2.3\Honorbuddy\Styx\Pathing\MeshNavigator.cs` | 6.2.3 | Navigator principal — MoveTo, MovePath, FindPath, off-mesh, doors, elevator, flight path |
| `.hb 6.2.3\Honorbuddy\Styx\Pathing\NavigationProvider.cs` | 6.2.3 | Classe abstraite de base |
| `.hb 4.3.4\...\Styx\Logic\Pathing\Navigator.cs` | 4.3.4 | Façade statique |
| `.hb 4.3.4\...\Styx\Logic\Pathing\MeshNavigator.cs` | 4.3.4 | MoveTo synchrone |
| `.hb 6.2.3\Honorbuddy\Styx\CommonBot\FlightPaths.cs` | 6.2.3 | Flight path system |
| `.hb 6.2.3\Honorbuddy\Styx\Pathing\Flightor.cs` | 6.2.3 | Vol |
| `.hb 6.2.3\Honorbuddy\Tripper.RecastManaged\` | 6.2.3 | Navmesh managed wrapper |

---

## Ta Mission

### Phase 1 — Inventaire Feature par Feature

Lis `AUDIT_NAVIGATION.md` section B.10 (features HB absentes) et l'annexe HB 6.2.3 (sections 1-15). Pour **chaque** feature listée, vérifie dans le code C# et C++ actuel si elle est :
- ✅ Implémentée correctement
- ⚠️ Implémentée mais avec des problèmes
- ❌ Absente ou cassée

Ne te fie pas aux documents — **lis le code source réel de chaque fichier**.

### Phase 2 — Comparaison Structurelle

Compare la structure de `Navigator.MoveTo()` avec HB 6.2.3 `MeshNavigator.MoveTo()` (méthode virtuelle, ~120 lignes). Vérifie :

1. **Guard checks** : destination == Zero, NaN, atLocation — sont-ils présents ?
2. **Path invalidation** : destination changed → clear path — correct ?
3. **Flight path check** : distance > 400yd → ShouldTakeFlightpath → même pattern que HB ?
4. **Combat abort** : HB utilise method_16 avec 4s timeout (async). CopilotBuddy est synchrone. Comment gère-t-il le combat ?
5. **Door handling** : auto-detection de portes fermées — présent ?
6. **Off-mesh dispatch** : elevator, portal, interactUnit, interactObject — routing par AreaType ?
7. **Path start skip** : raycast pour sauter les premiers waypoints visibles — présent ?
8. **Push-ahead raycast** : pousser le waypoint dans la direction de mouvement avec validation navmesh — présent ?
9. **Stuck handler** : intégré dans MoveTo avec timer et max retry — présent ?
10. **Mount check** : ShouldMount() + StateMount() avant path following — présent ?
11. **Elevator mount cancel** : Cancel mount-up pendant elevator ride — présent ?
12. **No-fly zone** : IsInNoFlyZone pour Dalaran etc. — présent ?

### Phase 3 — Chaîne C++ Complète

Vérifie la chaîne P/Invoke → DllMain → Navigation.cpp → PathFinder.cpp :

1. Les signatures P/Invoke dans `NativeMethods.cs` matchent-elles les exports dans `DllMain.cpp` ?
2. `getNavTerrain()` utilise-t-il `getPolyFlags()` (correct) ou le sampling de coordonnées (ancien/cassé) ?
3. `FinalizeSlicedFindPath` utilise-t-il `DT_STRAIGHTPATH_ALL_CROSSINGS` ou `AREA_CROSSINGS` ?
4. Y a-t-il du code mort évident (fonctions définies mais jamais appelées) ?
5. Les area costs dans `SetCustomFilter` correspondent-ils aux valeurs HB 6.2.3 ?

### Phase 4 — Tripper Layer

Vérifie `Tripper/Navigation/Navigator.cs` :

1. `FindPath()` appelle-t-il correctement `NativeMethods.CalculatePathEx()` ?
2. Les résultats (points, flags, polyTypes, abilityFlags) sont-ils correctement marshallés ?
3. `SetFactionQueryFilter()` existe-t-il et est-il appelé au bon moment ?
4. Les area costs (Water, Magma, Swim, Elevator, Portal etc.) correspondent-ils à HB 6.2.3 ?
5. `MoveAwayFromEdges` / `FixPathWalkability` sont-ils présents ?

### Phase 5 — Verdict

Donne un score global de parité avec HB en pourcentage, et un score de qualité /10.

Pour chaque feature listée en Phase 1, donne un verdict final dans un tableau.

Liste explicitement :
- Ce qui est **bien fait**
- Ce qui est **problématique** (avec le problème exact et le fichier/ligne)
- Ce qui est **manquant** (avec la référence HB où c'est implémenté)

**Ne fais pas de compliments gratuits. Ne minimise pas les problèmes. Sois direct et factuel.**

---

## Contraintes

- **Synchrone uniquement** : pas d'async/await. Si une feature HB est async (comme FindPath sur Task), la version synchrone est acceptable tant qu'elle fait le même travail.
- **WotLK seulement** : les features Garrison, WoD-specific, etc. sont N/A.
- **Fichiers obfusqués** : ignore les fichiers commençant par `-`, `.`, ou `⌂` dans les sources HB.
- **Ne modifie rien** : cet audit est en lecture seule.

---

## Format de Sortie Attendu

```
## Résultat Audit Indépendant

### Score Global : X/10
### Parité Feature HB : X%

### Tableau des Features
| # | Feature | HB Ref | CopilotBuddy | Verdict | Notes |
|---|---------|--------|--------------|---------|-------|
| 1 | MoveTo guards | 6.2.3 MeshNav | Navigator.cs | ✅/⚠️/❌ | ... |
| ...

### Problèmes Identifiés
1. [SÉVÉRITÉ] Description — fichier:ligne

### Features Manquantes
1. Description — Ref HB: fichier:ligne

### Points Forts
1. ...

### Conclusion
...
```
