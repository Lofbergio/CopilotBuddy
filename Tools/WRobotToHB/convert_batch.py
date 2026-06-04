#!/usr/bin/env python3
"""
Conversion batch: tous les fichiers WRobot XML d'un dossier -> HBProfile
========================================================================
Parcourt récursivement un dossier, convertit tous les .xml qui contiennent
des zones de grind WRobot (EasyQuestProfile + IsGrinderNotQuest=true).

Usage:
    python convert_batch.py <dossier_wrobot> [dossier_sortie]

Si dossier_sortie est omis, les fichiers _HB.xml sont créés dans le même
dossier que les fichiers source (côte à côte).

Exemples:
    python convert_batch.py "C:\\...\\Eeny's Horde Vanilla+BC V10"
    python convert_batch.py "C:\\...\\Eeny's Horde Vanilla+BC V10" "C:\\...\\HB Output"
"""
import sys
import subprocess
from pathlib import Path

CONVERTER = Path(__file__).parent / 'convert.py'


def is_wrobot_grind_file(path: Path) -> bool:
    """Vérifie rapidement si le fichier contient des zones de grind WRobot."""
    try:
        # Lecture partielle pour détecter le format
        content = path.read_bytes()
        return (b'EasyQuestProfile' in content and
                b'IsGrinderNotQuest' in content)
    except Exception:
        return False


def convert_all(input_dir: Path, output_dir: Path | None):
    xml_files = list(input_dir.rglob('*.xml'))
    print(f"Fichiers XML trouvés: {len(xml_files)}")

    converted = 0
    skipped = 0
    errors = 0

    for xml_file in sorted(xml_files):
        # Ignorer les fichiers déjà convertis (_HB.xml)
        if xml_file.stem.endswith('_HB'):
            continue

        if not is_wrobot_grind_file(xml_file):
            skipped += 1
            continue

        if output_dir:
            # Reproduire la structure de dossiers relative dans output_dir
            rel = xml_file.relative_to(input_dir)
            out_file = output_dir / rel.parent / (xml_file.stem + '_HB.xml')
            out_file.parent.mkdir(parents=True, exist_ok=True)
        else:
            out_file = xml_file.parent / (xml_file.stem + '_HB.xml')

        print(f"\n{'='*60}")
        print(f"Conversion: {xml_file.name}")

        result = subprocess.run(
            ['python', str(CONVERTER), str(xml_file), str(out_file)],
            capture_output=True, text=True
        )

        if result.returncode == 0:
            print(result.stdout.strip())
            converted += 1
        else:
            print(f"ERREUR:")
            print(result.stdout.strip())
            print(result.stderr.strip())
            errors += 1

    print(f"\n{'='*60}")
    print(f"Résumé: {converted} convertis, {skipped} ignorés (pas de grind), {errors} erreurs")


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(0)

    input_dir = Path(sys.argv[1])
    if not input_dir.is_dir():
        print(f"Erreur: dossier introuvable: {input_dir}")
        sys.exit(1)

    output_dir = Path(sys.argv[2]) if len(sys.argv) >= 3 else None

    convert_all(input_dir, output_dir)


if __name__ == '__main__':
    main()
