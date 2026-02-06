#!/usr/bin/env python3
"""Extract zone IDs for innkeepers from Questie database."""
import re
import json
from pathlib import Path

# Load current NPC mapping
with open('innkeeper_mapping.json', 'r') as f:
    content = f.read()
    # Remove comments for JSON parsing
    lines = [l for l in content.split('\n') if not l.strip().startswith('//')]
    data = json.loads('\n'.join(lines))

# Extract NPC IDs
npc_ids = {}
for name, npc_id in data.items():
    if name.startswith('_'):
        continue
    npc_ids[npc_id] = name

print(f"Loaded {len(npc_ids)} innkeepers")

# Pattern to extract NPC data: [npcId] = {'name',...,{[zoneId]=...},...,zoneId,...}
# Format: [ID] = {'Name',X,X,X,X,X,{[ZONEID]={{coords}}},nil,ZONEID,...}
npc_pattern = re.compile(r'\[(\d+)\]\s*=\s*\{\'([^\']+)\',[^}]+\{\[(\d+)\]=')

# Read Questie NPC DB
questie_path = Path(r"C:\Users\Texy\Desktop\Questie-335\Database\Wotlk\wotlkNpcDB.lua")
npc_zones = {}

with open(questie_path, 'r', encoding='utf-8') as f:
    for line in f:
        match = npc_pattern.search(line)
        if match:
            npc_id = int(match.group(1))
            npc_name = match.group(2)
            zone_id = int(match.group(3))
            if npc_id in npc_ids:
                npc_zones[npc_id] = zone_id
                print(f"Found: {npc_name} (NPC {npc_id}) -> Zone {zone_id}")

# Create enhanced mapping
enhanced = {
    "_comment": "Mapping of inn names to {npc_id, zone_id}",
    "_note": "Zone IDs used for SetHearthstone AreaId parameter"
}

for name, npc_id in data.items():
    if name.startswith('_'):
        continue
    zone_id = npc_zones.get(npc_id, 0)
    enhanced[name] = {"npc_id": npc_id, "zone_id": zone_id}
    
# Save
with open('innkeeper_mapping_v2.json', 'w', encoding='utf-8') as f:
    json.dump(enhanced, f, indent=2)

print(f"\nSaved enhanced mapping with {len(enhanced)-2} entries")
print(f"Found zone IDs for {len(npc_zones)} NPCs")
