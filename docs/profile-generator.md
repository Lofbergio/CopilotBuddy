# Profile Generator

Convertit les guides Zygor en profils CopilotBuddy XML avec coordonnées monde réelles.

## Architecture

```
Tools/ProfileGenerator/
├── zygor_parser.py           # Parser principal Zygor → XML
├── gameobject_exporter.py    # Export spawns depuis MariaDB
└── gameobject_spawns.json    # Cache des coordonnées monde (généré)
```

## Workflow Complet

### 1. Extraction des GameObjects (une seule fois)

Le problème : Zygor utilise des coordonnées en **pourcentage de zone** (ex: 44.03, 67.01) 
qui ne sont pas utilisables par HonorBuddy qui attend des **coordonnées monde** (ex: -489.09, -4301.17, 42.87).

Solution : Extraire les vraies coordonnées depuis la base de données du serveur WoW.

```powershell
# Depuis le dossier CopilotBuddy
.\.venv\Scripts\python.exe Tools\ProfileGenerator\gameobject_exporter.py `
    -q "C:\path\to\Questie-335" `
    --host 127.0.0.1 `
    --port 3310 `
    --user root `
    --password 123456 `
    --database world
```

Ce script :
1. Parse `Questie/Database/Wotlk/wotlkItemDB.lua` pour trouver quels items viennent de quels GameObjects
2. Parse `Questie/Database/Wotlk/wotlkObjectDB.lua` pour les noms des GameObjects
3. Connecte à MariaDB et query la table `gameobject` pour les coordonnées réelles
4. Génère `gameobject_spawns.json` avec toutes les coordonnées monde

**Résultat typique :**
```
Found 2138 items with objectDrops
Found 12085 game objects
Total unique GameObjects to fetch: 909
Retrieved 18969 spawn points for 798 objects
```

### 2. Génération des Profils

```powershell
.\.venv\Scripts\python.exe Tools\ProfileGenerator\zygor_parser.py `
    "C:\path\to\ZygorGuides\ZygorLevelingHordeCLASSIC.lua" `
    -o "Tools\ProfileGenerator\output" `
    -q "C:\path\to\Questie-335"
```

Le parser :
1. Parse les guides Zygor (accept, turnin, kill, collect, etc.)
2. Enrichit avec Questie (NPC IDs, quest objectives)
3. Charge `gameobject_spawns.json` automatiquement s'il existe
4. Génère des `<Quest>` overrides avec `<Hotspots>` pour les CollectItem

### 3. Déploiement

```powershell
Copy-Item "Tools\ProfileGenerator\output\*.xml" `
    "bin\Debug\net10.0-windows7.0\Profiles\Leveling Guides\" -Force
```

## Structure du JSON des Spawns

```json
{
  "_info": "GameObject spawn world coordinates for CopilotBuddy profiles",
  "objects": {
    "171938": {
      "name": "Cactus Apple",
      "spawns": [
        {"x": -489.09, "y": -4301.17, "z": 42.87},
        {"x": -406.27, "y": -4279.2, "z": 46.38}
      ]
    }
  }
}
```

## Format XML Généré

```xml
<Quest Id="4402" Name="Galgar's Cactus Apple Surprise">
  <Objective Type="CollectItem" ItemId="11583" CollectCount="10">
    <CollectFrom>
      <GameObject Name="Cactus Apple" Id="171938" />
    </CollectFrom>
    <Hotspots>
      <Hotspot X="-489.09" Y="-4301.17" Z="42.87" />
      <Hotspot X="-406.27" Y="-4279.2" Z="46.38" />
      <!-- ... jusqu'à 10 spawns -->
    </Hotspots>
  </Objective>
</Quest>
```

## Configuration MariaDB (SPP WotLK)

Les identifiants se trouvent dans `worldserver.conf` :

```properties
WorldDatabaseInfo = "127.0.0.1;3310;root;123456;world"
```

- **Host**: 127.0.0.1
- **Port**: 3310 (attention, pas 3306 par défaut!)
- **User**: root
- **Password**: 123456
- **Database**: world

## Dépendances Python

```powershell
pip install mariadb
```

## Chaîne de Données

```
Questie itemDB.lua          →  item_id → [game_object_ids]
        ↓
Questie objectDB.lua        →  game_object_id → name
        ↓
MariaDB world.gameobject    →  game_object_id → [{x, y, z}, ...]
        ↓
gameobject_spawns.json      →  Cache local
        ↓
zygor_parser.py             →  Génère <Quest><Hotspots>
        ↓
Profils XML                 →  Utilisables par CopilotBuddy
```

## Régénération

Si vous changez de serveur ou de base de données :

1. Supprimer `gameobject_spawns.json`
2. Relancer `gameobject_exporter.py` avec les nouveaux identifiants
3. Relancer `zygor_parser.py` pour régénérer les profils

## Notes

- Maximum 10 hotspots par GameObject pour éviter les profils trop gros
- Les GameObjects sans spawn dans la DB sont ignorés (events, objets inutilisés)
- Le fichier JSON est automatiquement chargé s'il existe à côté du parser
