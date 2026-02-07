#!/usr/bin/env python3
"""
Creature Spawns Exporter - Direct MariaDB Connection
Extracts NPC/creature spawn world coordinates from WoW 3.3.5a server database.

Produces creature_spawns.json containing world coordinates for all NPCs
referenced in Questie's quest database (quest givers, turn-in NPCs, mob objectives).

Usage:
    python creature_exporter.py -q "path/to/Questie-335" [--host 127.0.0.1] [--port 3310] [--user root] [--password 123456]
"""

import re
import sys
import json
import argparse
from pathlib import Path
from typing import Dict, List, Set, Tuple

try:
    import mariadb
except ImportError:
    import subprocess
    subprocess.check_call([sys.executable, "-m", "pip", "install", "mariadb"])
    import mariadb


def parse_questie_npc_ids(questie_path: str) -> Set[int]:
    """Extract all NPC IDs referenced in Questie quest and NPC databases"""
    npc_ids: Set[int] = set()
    
    # Parse NPC database
    npc_file = Path(questie_path) / "Database" / "Wotlk" / "wotlkNpcDB.lua"
    if npc_file.exists():
        content = npc_file.read_text(encoding='utf-8', errors='ignore')
        for match in re.finditer(r'^\[(\d+)\]\s*=', content, re.MULTILINE):
            npc_ids.add(int(match.group(1)))
        print(f"  NPC DB: {len(npc_ids)} NPCs")
    
    # Parse quest database for quest giver / turn-in NPCs
    quest_file = Path(questie_path) / "Database" / "Wotlk" / "wotlkQuestDB.lua"
    if quest_file.exists():
        content = quest_file.read_text(encoding='utf-8', errors='ignore')
        # Extract all numeric IDs from quest entries (givers, turn-in, mob objectives)
        for match in re.finditer(r'^\[(\d+)\]\s*=\s*\{', content, re.MULTILINE):
            line_start = match.start()
            line_end = content.find('\n', line_start)
            line = content[line_start:line_end] if line_end > 0 else content[line_start:]
            # Extract all numeric IDs that could be NPC references
            ids_in_line = re.findall(r'(?<!\w)(\d{2,6})(?!\w)', line)
            for id_str in ids_in_line:
                npc_id = int(id_str)
                if 1 <= npc_id <= 99999:  # Valid NPC ID range
                    npc_ids.add(npc_id)
        print(f"  After quest DB: {len(npc_ids)} unique NPC IDs")
    
    return npc_ids


def fetch_creature_spawns(npc_ids: Set[int], host: str, port: int, 
                          user: str, password: str, database: str) -> Tuple[Dict[int, List[Dict]], Dict[int, str]]:
    """
    Fetch creature spawn coordinates and names from MariaDB.
    Returns (spawns_dict, names_dict)
    """
    spawns: Dict[int, List[Dict]] = {}
    names: Dict[int, str] = {}
    
    if not npc_ids:
        return spawns, names
    
    print(f"\nConnecting to MariaDB {host}:{port}/{database}...")
    
    conn = mariadb.connect(
        host=host, port=port, user=user, password=password, database=database
    )
    cursor = conn.cursor()
    
    # Fetch names and factions from creature_template
    factions: Dict[int, int] = {}
    batch_size = 500
    npc_list = sorted(npc_ids)
    
    for i in range(0, len(npc_list), batch_size):
        batch = npc_list[i:i + batch_size]
        ids_str = ','.join(str(x) for x in batch)
        
        cursor.execute(f"SELECT entry, name, faction FROM creature_template WHERE entry IN ({ids_str})")
        for entry, name, faction in cursor:
            names[entry] = name
            if faction:
                factions[entry] = int(faction)
    
    print(f"  Found {len(names)} creature templates ({len(factions)} with faction)")
    
    # Fetch spawn positions from creature table
    # For each NPC, get up to 10 spawn positions (some NPCs have many spawns)
    for i in range(0, len(npc_list), batch_size):
        batch = npc_list[i:i + batch_size]
        ids_str = ','.join(str(x) for x in batch)
        
        cursor.execute(f"""
            SELECT id, map, position_x, position_y, position_z 
            FROM creature 
            WHERE id IN ({ids_str})
            ORDER BY id
        """)
        
        for npc_id, map_id, x, y, z in cursor:
            if npc_id not in spawns:
                spawns[npc_id] = []
            # Limit spawns per NPC to keep file manageable
            if len(spawns[npc_id]) < 10:
                spawns[npc_id].append({
                    'map': map_id,
                    'x': round(float(x), 4),
                    'y': round(float(y), 4),
                    'z': round(float(z), 4)
                })
    
    total_spawns = sum(len(v) for v in spawns.values())
    print(f"  Retrieved {total_spawns} spawn points for {len(spawns)} creatures")
    
    cursor.close()
    conn.close()
    
    return spawns, names, factions


def generate_json(spawns: Dict[int, List[Dict]], names: Dict[int, str], output_file: str, factions: Dict[int, int] = None):
    """Generate creature_spawns.json
    
    Includes ALL creatures from creature_template (not just those with spawns),
    so that faction data is available for event mobs, dynamically spawned creatures, etc.
    """
    if factions is None:
        factions = {}
    
    output = {
        "_info": "Creature spawn world coordinates for CopilotBuddy profiles (WoW 3.3.5a)",
        "_usage": "Used by zygor_parser_v2.py to convert Zygor/Questie percentage coords to world coords",
        "_source": "Exported from SPP WotLK database (creature + creature_template tables)",
        "creatures": {}
    }
    
    # Include ALL creatures from creature_template (names dict),
    # not just those with spawns, so we have faction data for all mobs
    all_npc_ids = sorted(set(list(spawns.keys()) + list(names.keys())))
    
    for npc_id in all_npc_ids:
        name = names.get(npc_id, f"Unknown {npc_id}")
        creature_data = {
            "name": name,
            "spawns": spawns.get(npc_id, [])
        }
        if npc_id in factions:
            creature_data["faction"] = factions[npc_id]
        output["creatures"][str(npc_id)] = creature_data
    
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(output, f, indent=2, ensure_ascii=False)
    
    with_spawns = sum(1 for npc_id in all_npc_ids if npc_id in spawns)
    file_size_mb = Path(output_file).stat().st_size / (1024 * 1024)
    print(f"\nCreature spawns saved to: {output_file} ({file_size_mb:.1f} MB)")
    print(f"  Total creatures: {len(all_npc_ids)} ({with_spawns} with spawns)")
    print(f"  Total spawn points: {sum(len(v) for v in spawns.values())}")


def main():
    parser = argparse.ArgumentParser(description='Creature Spawns Exporter for CopilotBuddy')
    parser.add_argument('-q', '--questie', required=True, help='Path to Questie-335 addon folder')
    parser.add_argument('--host', default='127.0.0.1', help='MariaDB host')
    parser.add_argument('--port', type=int, default=3310, help='MariaDB port (SPP default: 3310)')
    parser.add_argument('--user', default='root', help='MariaDB user')
    parser.add_argument('--password', default='123456', help='MariaDB password')
    parser.add_argument('--database', default='world', help='Database name')
    parser.add_argument('-o', '--output', default=None, help='Output JSON file')
    
    args = parser.parse_args()
    
    script_dir = Path(__file__).parent
    output_file = args.output or str(script_dir / "creature_spawns.json")
    
    print("=== Creature Spawns Exporter ===\n")
    
    # Get NPC IDs from Questie
    npc_ids = parse_questie_npc_ids(args.questie)
    
    # Fetch from database
    spawns, names, factions = fetch_creature_spawns(
        npc_ids, args.host, args.port, args.user, args.password, args.database
    )
    
    # Generate JSON
    generate_json(spawns, names, output_file, factions)
    
    # Verify with known NPCs
    print("\n=== Verification ===")
    test_npcs = [3143, 3145, 5765, 10556, 11378, 3188, 6928]
    for npc_id in test_npcs:
        if npc_id in spawns:
            s = spawns[npc_id][0]
            print(f"  {names.get(npc_id, '?'):25s} (#{npc_id}): x={s['x']}, y={s['y']}, z={s['z']}")
        else:
            print(f"  NPC #{npc_id}: NOT FOUND")


if __name__ == '__main__':
    main()
