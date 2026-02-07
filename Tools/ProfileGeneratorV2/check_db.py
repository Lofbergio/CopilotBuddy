"""
Reverse-engineer WorldMapArea boundaries from known Questie (percentage) + DB (world) coordinate pairs.
We need: WorldY = left - (pctX/100) * (left - right)
         WorldX = top - (pctY/100) * (top - bottom)
For each zone, solve with 2+ known points.
"""
import json
import re
from pathlib import Path

# Load creature spawns (world coords from DB)
with open(Path(__file__).parent / "creature_spawns.json", 'r') as f:
    creature_data = json.load(f)

# Load Questie NPC DB (percentage coords)
questie_path = Path(r"c:\Users\Texy\Desktop\Questie-335")
npc_file = questie_path / "Database" / "Wotlk" / "wotlkNpcDB.lua"
content = npc_file.read_text(encoding='utf-8', errors='ignore')

# Parse Questie NPCs: [id] = {'name',minLvlHP,maxLvlHP,minLvl,maxLvl,rank,spawns,...}
npc_pattern = re.compile(r"^\[(\d+)\]\s*=\s*\{'([^']+)',")
coord_pattern = re.compile(r'\{\[(\d+)\]\s*=\s*\{\{([\d.]+),([\d.]+)\}')

questie_npcs = {}
for line in content.split('\n'):
    npc_match = npc_pattern.match(line)
    if npc_match:
        npc_id = int(npc_match.group(1))
        coord_match = coord_pattern.search(line)
        if coord_match:
            questie_npcs[npc_id] = {
                'zone': int(coord_match.group(1)),
                'pct_x': float(coord_match.group(2)),
                'pct_y': float(coord_match.group(3)),
            }

# For each zone, collect pairs of (pctX, pctY) -> (worldX, worldY)
zone_pairs = {}  # zone_id -> [(pctX, pctY, worldX, worldY), ...]

for npc_id_str, cdata in creature_data['creatures'].items():
    npc_id = int(npc_id_str)
    if npc_id not in questie_npcs:
        continue
    q = questie_npcs[npc_id]
    zone_id = q['zone']
    spawn = cdata['spawns'][0]
    world_x = spawn['x']
    world_y = spawn['y']
    
    if zone_id not in zone_pairs:
        zone_pairs[zone_id] = []
    zone_pairs[zone_id].append((q['pct_x'], q['pct_y'], world_x, world_y))

# For each zone with enough data, compute boundaries via least squares
import numpy as np

# Zone name mapping (partial)
ZONE_NAMES = {
    14: "Durotar", 215: "Mulgore", 85: "Tirisfal Glades", 17: "The Barrens",
    12: "Elwynn Forest", 1: "Dun Morogh", 40: "Westfall", 130: "Silverpine Forest",
    3430: "Eversong Woods", 3433: "Ghostlands", 331: "Ashenvale",
    3537: "Borean Tundra", 495: "Howling Fjord", 65: "Dragonblight",
    3483: "Hellfire Peninsula", 3521: "Zangarmarsh", 3519: "Terokkar Forest",
    44: "Redridge Mountains", 10: "Duskwood", 33: "Stranglethorn Vale",
    267: "Hillsbrad Foothills", 406: "Stonetalon Mountains", 400: "Thousand Needles",
    363: "Valley of Trials", 221: "Camp Narache", 20: "Deathknell",
    38: "Loch Modan", 11: "Wetlands", 45: "Arathi Highlands",
    47: "The Hinterlands", 139: "Eastern Plaguelands", 28: "Western Plaguelands",
    405: "Desolace", 15: "Dustwallow Marsh", 357: "Feralas",
    440: "Tanaris", 490: "Un'Goro Crater", 1377: "Silithus",
    361: "Felwood", 618: "Winterspring", 16: "Azshara",
    3518: "Nagrand", 3522: "Blade's Edge Mountains", 3523: "Netherstorm",
    3520: "Shadowmoon Valley", 394: "Grizzly Hills", 66: "Zul'Drak",
    3711: "Sholazar Basin", 67: "The Storm Peaks", 210: "Icecrown",
    4: "Blasted Lands", 46: "Burning Steppes", 51: "Searing Gorge", 3: "Badlands",
    8: "Swamp of Sorrows", 36: "Alterac Mountains",
    9: "Northshire", 1637: "Orgrimmar", 1519: "Stormwind City",
    1537: "Ironforge", 1497: "Undercity", 1638: "Thunder Bluff",
    3487: "Silvermoon City", 3703: "Shattrath City", 4395: "Dalaran",
    493: "Moonglade", 2817: "Crystalsong Forest",
}

print("=== Zone Boundaries (computed from NPC coordinate pairs) ===\n")
print("ZONE_BOUNDARIES = {")

computed_zones = {}
for zone_id in sorted(zone_pairs.keys()):
    pairs = zone_pairs[zone_id]
    if len(pairs) < 3:
        continue
    
    # Solve: worldY = left + (pctX/100) * (right - left)
    #        worldX = top + (pctY/100) * (bottom - top)
    # Using least squares: worldY = a + b * pctX  where a=left, b=(right-left)/100
    #                      worldX = c + d * pctY  where c=top, d=(bottom-top)/100
    
    pct_x = np.array([p[0] for p in pairs])
    pct_y = np.array([p[1] for p in pairs])
    world_x = np.array([p[2] for p in pairs])
    world_y = np.array([p[3] for p in pairs])
    
    # Solve for Y axis: worldY = a + b * pctX
    A_y = np.column_stack([np.ones_like(pct_x), pct_x])
    result_y = np.linalg.lstsq(A_y, world_y, rcond=None)
    a_y, b_y = result_y[0]
    left = a_y
    right = a_y + 100 * b_y
    
    # Solve for X axis: worldX = c + d * pctY
    A_x = np.column_stack([np.ones_like(pct_y), pct_y])
    result_x = np.linalg.lstsq(A_x, world_x, rcond=None)
    a_x, b_x = result_x[0]
    top = a_x
    bottom = a_x + 100 * b_x
    
    # Compute residuals
    pred_y = a_y + b_y * pct_x
    pred_x = a_x + b_x * pct_y
    rmse_y = np.sqrt(np.mean((world_y - pred_y)**2))
    rmse_x = np.sqrt(np.mean((world_x - pred_x)**2))
    
    zone_name = ZONE_NAMES.get(zone_id, f"Zone_{zone_id}")
    
    if rmse_x < 50 and rmse_y < 50:  # Good fit
        print(f"    {zone_id}: ({round(top, 2)}, {round(left, 2)}, {round(bottom, 2)}, {round(right, 2)}),  # {zone_name} ({len(pairs)} pts, RMSE: x={rmse_x:.1f} y={rmse_y:.1f})")
        computed_zones[zone_id] = (round(top, 2), round(left, 2), round(bottom, 2), round(right, 2))

print("}")
print(f"\nComputed {len(computed_zones)} zone boundaries")

# Debug missing zones
print("\n=== Debugging missing zones ===")
for zone_id in [14, 215, 85, 17, 12, 1, 130, 3430, 3433, 331, 3537, 495, 65, 3483, 3521, 3519, 400, 440, 490, 357, 618, 44, 10, 38, 11, 45, 139, 8, 363, 221, 9, 1637, 1519, 1537, 1638, 3487, 3703, 4395, 16, 493, 3518, 3522, 3523, 3520, 394, 66, 3711, 67, 210, 3703, 20]:
    pairs = zone_pairs.get(zone_id, [])
    zone_name = ZONE_NAMES.get(zone_id, f"Zone_{zone_id}")
    if len(pairs) < 3:
        print(f"  {zone_name} (ID {zone_id}): only {len(pairs)} pairs - NOT ENOUGH DATA")
    elif zone_id not in computed_zones:
        pct_x = np.array([p[0] for p in pairs])
        pct_y = np.array([p[1] for p in pairs])
        world_x = np.array([p[2] for p in pairs])
        world_y = np.array([p[3] for p in pairs])
        A_y = np.column_stack([np.ones_like(pct_x), pct_x])
        result_y = np.linalg.lstsq(A_y, world_y, rcond=None)
        a_y, b_y = result_y[0]
        A_x = np.column_stack([np.ones_like(pct_y), pct_y])
        result_x = np.linalg.lstsq(A_x, world_x, rcond=None)
        a_x, b_x = result_x[0]
        pred_y = a_y + b_y * pct_x
        pred_x = a_x + b_x * pct_y
        rmse_y = np.sqrt(np.mean((world_y - pred_y)**2))
        rmse_x = np.sqrt(np.mean((world_x - pred_x)**2))
        top = a_x; bottom = a_x + 100 * b_x; left = a_y; right = a_y + 100 * b_y
        print(f"  {zone_name} (ID {zone_id}): {len(pairs)} pairs, RMSE x={rmse_x:.1f} y={rmse_y:.1f} - HIGH ERROR")
        print(f"    Boundaries: top={top:.1f}, left={left:.1f}, bottom={bottom:.1f}, right={right:.1f}")
        # Show worst outliers
        errors = np.sqrt((world_x - pred_x)**2 + (world_y - pred_y)**2)
        worst = np.argsort(errors)[-3:]
        for idx in worst:
            print(f"    Outlier: pct=({pct_x[idx]:.1f},{pct_y[idx]:.1f}) world=({world_x[idx]:.1f},{world_y[idx]:.1f}) pred=({pred_x[idx]:.1f},{pred_y[idx]:.1f}) err={errors[idx]:.1f}")
