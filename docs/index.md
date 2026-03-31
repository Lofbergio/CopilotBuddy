# CopilotBuddy Documentation

Bienvenue dans la documentation **CopilotBuddy** (WoW Bot Framework pour World of Warcraft 3.3.5a).

!!! note "Structure actuelle"
    Cette documentation est construite à partir des fichiers de `docs/` et de `mkdocs.yml`. La page d'accueil `index.md` est utilisée par le site et ne doit pas être supprimée sans mise à jour de la navigation.

## Liens rapides

- **[API Reference](api/overview.md)** - Documentation API (contient routes actuelles)
- **[API Public (Auto-generated)](api/auto/README.md)** - Les types publics automatiquement extraits du code
- **[WotLK Compatibility](compatibility/overview.md)** - Infos et limitations WotLK
- **[Création de routines de combat](guides/creating-routines.md)** - Guide de création de routines
- **[Changelog](about/changelog.md)** - Historique des versions

## Fonctionnalités clés

- ✅ Compatible WotLK 3.3.5a (build 12340)
- ✅ Système de combat avec TreeSharp (behavior tree)
- ✅ Lecture directe de mémoire WoW (GreenMagic + Styx.WoWInternals)
- ✅ Intégration Lua (`Lua.DoString`, `GetReturnVal`, etc.)
- ✅ Navigation et pathfinding via Tripper
- ✅ Bot framework (quest, grind, gather, dungeon…)

## Comment utiliser

### Installation

1. Extraire dans un dossier, par exemple `C:\CopilotBuddy`
2. Exécuter `CopilotBuddy.exe`
3. Attacher au processus WoW 3.3.5a
4. Sélectionner un bot et une routine de combat

### Prérequis

- Windows 7+ 64-bit
- .NET Runtime (10.0 conseillé)
- WoW 3.3.5a build 12340

## Structure du projet (2026)

```text
CopilotBuddy/
├── Styx/                      # noyau bot + WoW interactions
│   ├── WoWInternals/          # descripteurs WoW, cache, objet, etc.
│   │   └── WoWObjects/        # WoWItem, WoWUnit, LocalPlayer, etc.
│   ├── Logic/                 # comportement des bots
│   ├── Combat/                # management des sorts, cibles
│   └── ...
├── TreeSharp/                 # moteur behavior tree
├── Tripper/                   # pathfinding + navigation
├── Bots/                      # bots (Grind, Gather, Dungeon, Quest...)
├── Buddy/                     # coroutines, actions, decorators
├── docs/                      # documentation MKDocs
├── mkdocs.yml                 # config site docs (nav)
└── ...
```

## Mise à jour requise par rapport aux fichiers obsolètes

- Si tu supprimes `docs/index.md`, mettre à jour `mkdocs.yml` :
  - remplacer `Home: index.md` par un autre fichier existant (ex. `api/overview.md`)
- Vérifier que les fichiers listés dans `nav` existent bien (ex: `api/combat/wowspell.md`, `compatibility/known-issues.md`).

## Notifications importantes

- Certaines pages de l’ancienne documentation peuvent pointer vers des chemins supprimés (`api/core/memory.md` => `api/greenmagic/memory.md` si besoin).
- Utilise `git status` et `git diff` sur `docs/` pour voir ce qui a été renommé ou déplacé.

## Support & bugs

- Ouvrir un issue GitHub
- Page `[Known Issues](compatibility/known-issues.md)`
- Page `[API Differences](compatibility/api-differences.md)`

---

**Note**: usage de ce bot à vos risques et périls ; projet à but éducatif.
