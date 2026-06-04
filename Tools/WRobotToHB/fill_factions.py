#!/usr/bin/env python3
"""
Peuple <Factions> dans tous les _HB.xml depuis la DB TrinityCore 3.3.5a.
==========================================================================
- Lit les commentaires <!-- WRobot EntryIDs: X Y Z --> dans chaque SubProfile
- Interroge creature_template.faction via mysql.exe (pas de librairie externe)
- Remplace <Factions></Factions> par <Factions>ID1 ID2 ...</Factions>

Usage:
    python fill_factions.py [dossier_hb_profiles]

Si dossier omis, utilise le dossier de profils par défaut.
"""
import sys
import re
import subprocess
from pathlib import Path

# ---------------------------------------------------------------------------
# Config DB — ajuste si besoin
# ---------------------------------------------------------------------------
MYSQL_EXE  = r"C:\Users\Texy6\Desktop\Naaru Blizzlike Repack 3.3.5a v2025.07\mysql\bin\mysql.exe"
DB_HOST    = "127.0.0.1"
DB_PORT    = "3306"
DB_USER    = "root"
DB_PASS    = ""
DB_NAME    = "world"

DEFAULT_PROFILES_DIR = (
    r"C:\Users\Texy6\Desktop\newhcb\CopilotBuddy\bin\Debug\net10.0-windows7.0"
    r"\Default Profiles\Grind"
)


def _run_mysql(sql: str, timeout: int = 30) -> list[str]:
    """Exécute un SQL via mysql.exe et retourne les lignes de sortie."""
    args = [
        MYSQL_EXE,
        f"-h{DB_HOST}",
        f"-P{DB_PORT}",
        f"-u{DB_USER}",
        f"--password={DB_PASS}",
        "--skip-column-names",
        "--batch",
        "-e", sql,
    ]
    try:
        result = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
        return result.stdout.strip().splitlines()
    except Exception as e:
        print(f"  [DB ERROR] {e}")
        return []


def query_factions(entry_ids: list[str]) -> list[str]:
    """Retourne les faction IDs distincts pour une liste d'entry IDs."""
    if not entry_ids:
        return []
    entries_csv = ",".join(entry_ids)
    sql = (
        f"SELECT DISTINCT faction FROM world.creature_template "
        f"WHERE entry IN ({entries_csv}) AND faction > 0 AND faction != 35 "
        f"ORDER BY faction;"
    )
    lines = _run_mysql(sql)
    return [l.strip() for l in lines if l.strip().isdigit()]


def query_factions_for_entries(all_entries: list[str]) -> dict[str, str]:
    """
    Batch : retourne un dict {entry_id_str: faction_id_str} pour tous les entries.
    Effectue une seule requête MySQL (chunked par 500 pour éviter les IN() géants).
    Les entries sans faction valide (0 ou 35) sont absents du dict.
    """
    entry_to_faction: dict[str, str] = {}
    unique = list(dict.fromkeys(all_entries))  # déduplique, préserve l'ordre
    chunk_size = 500
    for i in range(0, len(unique), chunk_size):
        chunk = unique[i : i + chunk_size]
        entries_csv = ",".join(chunk)
        sql = (
            f"SELECT entry, faction FROM world.creature_template "
            f"WHERE entry IN ({entries_csv}) AND faction > 0 AND faction != 35;"
        )
        for line in _run_mysql(sql, timeout=60):
            parts = line.split("\t")
            if len(parts) == 2 and parts[0].strip().isdigit() and parts[1].strip().isdigit():
                entry_to_faction[parts[0].strip()] = parts[1].strip()
    return entry_to_faction


# Pattern global réutilisé dans process_file
_FACTION_PATTERN = re.compile(
    r'(<!-- WRobot EntryIDs: ([\d\s]+?)-->)\s*\n(\s*<Factions></Factions>)',
    re.MULTILINE,
)


def process_file(xml_path: Path) -> int:
    """
    Met à jour les <Factions> dans un fichier _HB.xml.
    Une seule requête MySQL batch pour tout le fichier.
    Retourne le nombre de SubProfiles mis à jour.
    """
    content = xml_path.read_text(encoding="utf-8")

    # Étape 1 : collecter tous les EntryIDs uniques du fichier
    all_entries: list[str] = []
    for m in _FACTION_PATTERN.finditer(content):
        all_entries.extend(m.group(2).strip().split())

    if not all_entries:
        return 0

    # Étape 2 : une seule requête batch pour tout le fichier
    entry_to_faction = query_factions_for_entries(all_entries)

    # Étape 3 : remplacer chaque <Factions></Factions> depuis le cache
    updated = 0

    def replacer(m):
        nonlocal updated
        comment_full = m.group(1)
        entries_str  = m.group(2).strip()
        factions_tag = m.group(3)
        indent       = factions_tag[: len(factions_tag) - len(factions_tag.lstrip())]

        entries = entries_str.split()
        # Factions uniques pour ces entries, triées numériquement
        faction_set = sorted(
            {entry_to_faction[e] for e in entries if e in entry_to_faction},
            key=lambda x: int(x),
        )

        if faction_set:
            updated += 1
            return f"{comment_full}\n{indent}<Factions>{' '.join(faction_set)}</Factions>"
        else:
            return f"{comment_full}\n{indent}<Factions></Factions>"

    new_content = _FACTION_PATTERN.sub(replacer, content)

    if new_content != content:
        xml_path.write_text(new_content, encoding="utf-8")

    return updated


def main():
    if len(sys.argv) >= 2:
        search_dir = Path(sys.argv[1])
    else:
        search_dir = Path(DEFAULT_PROFILES_DIR)

    if not search_dir.exists():
        print(f"Dossier introuvable: {search_dir}")
        sys.exit(1)

    # Vérifier que le serveur MySQL est accessible
    test = query_factions(["6"])  # Kobold Vermin entry 6, faction 25
    if not test:
        print("ERREUR: Impossible de contacter MySQL.")
        print(f"  -> Lance d'abord le repack: {MYSQL_EXE}")
        print(f"  -> Ou vérifie les credentials dans ce script.")
        sys.exit(1)
    else:
        print(f"DB OK — test faction pour entry 6: {test}")

    xml_files = list(search_dir.rglob("*_HB.xml"))
    print(f"\n{len(xml_files)} fichiers _HB.xml trouvés\n")

    total_updated = 0
    files_touched = 0

    for f in sorted(xml_files):
        n = process_file(f)
        if n > 0:
            print(f"  {f.name}: {n} SubProfile(s) mis à jour")
            total_updated += n
            files_touched += 1

    print(f"\nTerminé: {total_updated} <Factions> remplis dans {files_touched} fichiers")


if __name__ == "__main__":
    main()
