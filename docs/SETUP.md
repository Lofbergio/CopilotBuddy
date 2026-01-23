# Documentation Setup Guide

Ce guide explique comment utiliser et modifier la documentation CopilotBuddy.

## Installation

### 1. Installer Python (si pas déjà fait)

Télécharge Python 3.8+ : https://www.python.org/downloads/

Vérifie l'installation:
```bash
python --version
```

### 2. Installer MkDocs Material

Dans le dossier CopilotBuddy:
```bash
pip install -r requirements-docs.txt
```

Ou manuellement:
```bash
pip install mkdocs-material mkdocs-awesome-pages-plugin
```

## Utilisation

### Voir la documentation en local

```bash
mkdocs serve
```

Puis ouvre http://localhost:8000 dans ton navigateur.

**Les changements se mettent à jour automatiquement** quand tu sauvegardes un fichier!

### Éditer la documentation

Tous les fichiers sont dans `docs/` en **Markdown simple**:

```
docs/
├── index.md                          # Page d'accueil
├── api/
│   ├── overview.md                   # Vue d'ensemble API
│   ├── styx/
│   │   ├── objectmanager.md         # ObjectManager docs
│   │   ├── memory.md                # Memory docs
│   │   └── lua.md                   # Lua docs
│   ├── wowobjects/
│   │   ├── localplayer.md           # LocalPlayer (déjà fait)
│   │   ├── wowplayer.md             # À faire
│   │   └── wowunit.md               # À faire
│   └── combat/
│       ├── customclass.md           # À faire
│       └── routinemanager.md        # À faire
├── compatibility/
│   ├── overview.md                   # Compatibilité WotLK (déjà fait)
│   ├── api-differences.md           # À faire
│   └── known-issues.md              # À faire
└── guides/
    ├── creating-routines.md         # À faire
    └── memory-reading.md            # À faire
```

### Exemple d'édition

Ouvre `docs/api/wowobjects/wowplayer.md` et ajoute:

```markdown
# WoWPlayer Class

Represents a player character in the game world.

## Properties

### Name
\`\`\`csharp
public string Name { get; }
\`\`\`

The player's name.

**Example:**
\`\`\`csharp
string name = player.Name;
Logger.Write($"Player: {name}");
\`\`\`
```

Sauvegarde → MkDocs recharge automatiquement!

## Syntaxe Markdown

### Headers
```markdown
# Titre H1
## Titre H2
### Titre H3
```

### Code
````markdown
```csharp
public void Example()
{
    Logger.Write("Hello!");
}
```
````

### Liens
```markdown
[Texte du lien](url-ou-page.md)
```

### Avertissements
```markdown
!!! warning "Titre"
    Contenu de l'avertissement

!!! info "Information"
    Information importante

!!! note "Note"
    Note pour le lecteur
```

### Tables
```markdown
| Colonne 1 | Colonne 2 |
|-----------|-----------|
| Valeur 1  | Valeur 2  |
```

## Publier la Documentation

### Sur GitHub Pages (gratuit)

1. Crée un repo GitHub pour CopilotBuddy
2. Push ton code
3. Active GitHub Pages dans Settings
4. Run:
```bash
mkdocs gh-deploy
```

Ta doc sera en ligne à: `https://username.github.io/CopilotBuddy/`

### Hébergement manuel

Build les fichiers HTML:
```bash
mkdocs build
```

Les fichiers sont dans `site/` - upload où tu veux!

## Configuration

Modifie `mkdocs.yml` pour changer:
- Titre du site
- Couleurs
- Navigation
- Extensions

## Besoin d'aide?

- Documentation MkDocs Material: https://squidfunk.github.io/mkdocs-material/
- Markdown guide: https://www.markdownguide.org/
