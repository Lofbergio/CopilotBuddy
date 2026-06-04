#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
extract_gathernodes.py
Extracts herb & mining node spawns from a WoW 3.3.5a world DB
and exports one JSON file per real zone for the randomBuddy plugin.

Strategy:
  1. Pull all gather candidate GOs from MySQL.
  2. Strict-filter on real gather names (no fishing pools, decor, etc.).
  3. Resolve the *real* zone via position-bbox lookup on the continent map
     (this repack stores zoneId=0/areaId=0 on most outdoor GOs).
  4. Drop sub-zones: roll their spawns into the parent continent-zone.
  5. Drop instances: any GO whose map is an instance id (>= 30) is skipped.
  6. Group and write one JSON per zone + a _manifest.json index.

Usage:
    python extract_gathernodes.py [--host HOST] [--user USER] [--password PWD] [--out DIR]
"""

import argparse
import datetime
import json
import os
import re
import sys
from collections import defaultdict

try:
    import pymysql
except ImportError:
    sys.exit("pymysql not installed. Run: pip install pymysql")


# ---------------------------------------------------------------------------
# WotLK 3.3.5a known gather node names (authoritative list).
# Anything else matching the broad regex is excluded.
# ---------------------------------------------------------------------------
HERB_NAMES = {
    "Peacebloom", "Silverleaf", "Earthroot", "Mageroyal", "Briarthorn",
    "Stranglekelp", "Bruiseweed", "Wild Steelbloom", "Grave Moss", "Kingsblood",
    "Liferoot", "Fadeleaf", "Goldthorn", "Khadgar's Whisker", "Dragon's Teeth",
    "Sorrowmoss", "Sungrass", "Arthas' Tears", "Blindweed", "Gromsblood",
    "Golden Sansam", "Dreamfoil", "Mountain Silversage", "Plaguebloom",
    "Icecap", "Black Lotus", "Frozen Herb", "Felweed", "Dreaming Glory",
    "Ragveil", "Flame Cap", "Terocone", "Netherbloom", "Nightmare Vine",
    "Mana Thistle", "Goldclover", "Tiger Lily", "Talandra's Rose",
    "Adder's Tongue", "Lichbloom", "Icethorn", "Frost Lotus",
    "Fire Leaf", "Firethorn",
}

MINE_NAMES = {
    "Copper Vein", "Tin Vein", "Silver Vein", "Gold Vein",
    "Iron Deposit", "Mithril Deposit", "Truesilver Deposit",
    "Dark Iron Deposit", "Small Thorium Vein", "Rich Thorium Vein",
    "Hakkari Thorium Vein", "Fel Iron Deposit", "Adamantite Deposit",
    "Khorium Vein", "Cobalt Deposit", "Saronite Deposit", "Titanium Vein",
    "Ancient Gem Vein", "Lesser Bloodstone Deposit", "Indurium Mineral Vein",
    "Incendicite Mineral Vein", "Miners' League Crates",
    "Draenethyst Mine Crystal", "Nethercite Deposit", "Ooze Covered Gold Vein",
    "Ooze Covered Iron Deposit", "Ooze Covered Mithril Deposit",
    "Ooze Covered Rich Thorium Vein", "Ooze Covered Truesilver Deposit",
    "Ooze Covered Thorium Vein", "Ooze Covered Dark Iron Deposit",
    "Ooze Covered Fel Iron Deposit", "Ooze Covered Adamantite Deposit",
    "Ooze Covered Khorium Vein", "Ooze Covered Cobalt Deposit",
    "Ooze Covered Saronite Deposit", "Ooze Covered Titanium Vein",
    "Rich Adamantite Deposit",
}

# All gather-template entries are also flagged as outdoor spawns on map 0/1/530/571.
# Strip out anything that is NOT a herb or mine:
def classify(name: str) -> str:
    if name in HERB_NAMES:
        return "herb"
    if name in MINE_NAMES:
        return "mine"
    return None  # reject


# ---------------------------------------------------------------------------
# WotLK 3.3.5a zone bounding boxes (continent map coords, in yards).
# Format: (zoneId, name, mapId, minX, maxX, minY, maxY, minLvl, maxLvl, factions)
# faction: "A", "H", or "AH" (contested)
# Any spawn whose (x,y) is inside one of these is assigned to the matching zone.
# If multiple zones overlap, the first match wins (smaller bboxes listed first).
# ---------------------------------------------------------------------------
ZONE_BBOXES = [
    # ---- Eastern Kingdoms (map 0) ----
    # Capital cities & starter zones
    (12,  "Elwynn Forest",          0,  -10000.0,  -8400.0,    -200.0,  1500.0,   1, 10,  "A"),
    (1519,"Stormwind City",         0,   -9100.0,  -8900.0,    200.0,   900.0,   1, 60,  "A"),
    (1,   "Dun Morogh",             0,   -6500.0,  -4900.0,    -800.0,  2000.0,   1, 10,  "A"),
    (1537,"Ironforge",              0,   -5000.0,  -4800.0,   -1300.0, -1100.0,   1, 60,  "A"),
    (38,  "Loch Modan",             0,   -5500.0,  -4500.0,   -4500.0, -3500.0,  10, 20,  "A"),
    (40,  "Westfall",               0,  -11000.0,  -9500.0,    1500.0,  2500.0,  10, 20,  "A"),
    (44,  "Redridge Mountains",     0,   -9800.0,  -8800.0,   -3000.0, -2000.0,  15, 25,  "A"),
    (3,   "Duskwood",               0,  -11000.0,  -9500.0,   -1000.0,   500.0,  20, 30,  "A"),
    (47,  "Badlands",               0,   -7000.0,  -5500.0,   -3500.0, -2500.0,  35, 45,  "AH"),
    (46,  "Burning Steppes",        0,   -8500.0,  -7500.0,   -3500.0, -2500.0,  50, 58,  "AH"),
    (51,  "Searing Gorge",          0,   -7500.0,  -6500.0,   -2500.0, -1500.0,  45, 55,  "AH"),
    (41,  "Wetlands",               0,   -4500.0,  -3500.0,   -1500.0,   500.0,  20, 30,  "AH"),
    (132, "Hillsbrad Foothills",    0,   -3500.0,  -1500.0,   -1500.0,  1500.0,  20, 30,  "AH"),
    (45,  "Arathi Highlands",       0,   -2500.0,  -1500.0,   -3500.0, -2500.0,  30, 40,  "AH"),
    (8,   "Swamp of Sorrows",       0,  -10500.0,  -9500.0,   -3500.0, -2500.0,  35, 45,  "AH"),
    (33,  "Stranglethorn Vale",     0,  -13500.0, -11500.0,   -1500.0,   500.0,  30, 45,  "AH"),
    (133, "The Cape of Stranglethorn", 0, -14000.0, -13000.0,  100.0,  1500.0,  30, 45,  "AH"),
    (85,  "Tirisfal Glades",        0,    1500.0,   3500.0,    1500.0,  3500.0,   1, 10,  "H"),
    (1497,"Undercity",              0,    1300.0,   1700.0,    300.0,   800.0,   10, 60,  "H"),
    (130, "Silverpine Forest",      0,     100.0,   1500.0,    1500.0,  3500.0,  10, 20,  "H"),
    (36,  "Alterac Mountains",      0,   -1000.0,   1000.0,    -500.0,  1500.0,  30, 40,  "AH"),
    (141, "Deadwind Pass",          0,  -11000.0, -10000.0,   -2500.0, -1500.0,  55, 60,  "AH"),
    (2257,"Deeprun Tram",           0,   -8500.0,  -8300.0,    400.0,   700.0,   1, 60,  "AH"),

    # ---- Kalimdor (map 1) ----
    (14,  "Darkshore",              1,    4000.0,   7500.0,    1000.0,  4500.0,  10, 20,  "A"),
    (148, "Dustwallow Marsh",       1,   -4500.0,  -2000.0,   -3500.0, -1500.0,  35, 45,  "AH"),
    (188, "Tanaris",                1,   -9500.0,  -6500.0,   -4500.0, -1500.0,  40, 50,  "AH"),
    (220, "Feralas",                1,   -5500.0,  -3500.0,    1500.0,  4000.0,  40, 50,  "AH"),
    (331, "Ashenvale",              1,    2000.0,   4000.0,   -1500.0,   500.0,  20, 30,  "AH"),
    (361, "Felwood",                1,    4000.0,   6500.0,   -1500.0,   500.0,  45, 55,  "AH"),
    (400, "Thousand Needles",       1,   -6500.0,  -4500.0,   -2000.0,    0.0,   25, 35,  "AH"),
    (405, "Desolace",               1,   -1500.0,    500.0,    1000.0,  2500.0,  30, 40,  "AH"),
    (406, "Stonetalon Mountains",   1,      500.0,   2500.0,   -1500.0,  -500.0,  15, 27,  "AH"),
    (490, "Un'Goro Crater",         1,   -7500.0,  -5500.0,   -1500.0,   500.0,  48, 55,  "AH"),
    (493, "Moonglade",              1,    7500.0,   8500.0,   -2500.0, -1500.0,  45, 55,  "AH"),
    (618, "Winterspring",           1,    5500.0,   8000.0,   -2500.0,  -500.0,  50, 60,  "AH"),
    (1377,"Silithus",               1,   -8500.0,  -6500.0,    1000.0,  2500.0,  40, 50,  "AH"),
    (17,  "The Barrens",            1,   -1500.0,   1500.0,   -2500.0,  -500.0,  10, 25,  "H"),
    (215, "Mulgore",                1,   -1500.0,    500.0,    -1500.0, -1000.0,   1, 10,  "H"),
    (1637,"Orgrimmar",              1,    1000.0,   2000.0,   -4500.0, -3500.0,   1, 60,  "H"),
    (1638,"Thunder Bluff",          1,   -1500.0,   -800.0,    200.0,   800.0,   1, 60,  "H"),
    (1657,"Darnassus",              1,    9500.0,  10500.0,    2000.0,  3000.0,   1, 60,  "A"),
    (1769,"The Exodar",             1,  -4000.0,  -3500.0,   -12000.0,-11000.0,   1, 60,  "A"),
    (3522,"Azuremyst Isle",         1,   -4500.0,  -2500.0,  -12500.0,-11000.0,   1, 10,  "A"),
    (3523,"Bloodmyst Isle",         1,   -3000.0,  -1500.0,  -12500.0,-11000.0,  10, 20,  "A"),
    (3526,"Eversong Woods",         1,    6500.0,   8500.0,   -7500.0, -5500.0,   1, 10,  "H"),
    (3527,"Ghostlands",             1,    6500.0,   8500.0,   -6500.0, -4500.0,  10, 20,  "H"),
    (4080,"Isle of Quel'Danas",     1,  11000.0,  13000.0,   -7500.0, -5500.0,  60, 70,  "AH"),

    # ---- Outland (map 530) ----
    (3483,"Hellfire Peninsula",     530,    -200.0,   1500.0,    2000.0,  4000.0,  58, 63,  "AH"),
    (3521,"Zangarmarsh",            530,   -200.0,    500.0,    4500.0,  7000.0,  60, 64,  "AH"),
    (3518,"Nagrand",                530,  -2000.0,   -500.0,    5500.0,  8500.0,  64, 67,  "AH"),
    (3519,"Terokkar Forest",        530,  -3500.0,  -2000.0,    3500.0,  5500.0,  62, 65,  "AH"),
    (3820,"Netherstorm",            530,   2500.0,   4500.0,    1500.0,  4000.0,  67, 70,  "AH"),
    (3537,"Blade's Edge Mountains", 530,   1500.0,   4000.0,    5500.0,  8500.0,  65, 68,  "AH"),
    (3520,"Shadowmoon Valley",      530,  -4500.0,  -2500.0,    -500.0,  2000.0,  67, 70,  "AH"),

    # ---- Northrend (map 571) ----
    (402, "Borean Tundra",          571,   2500.0,   4500.0,    2000.0,  5000.0,  68, 72,  "AH"),
    (67,  "Howling Fjord",          571,     500.0,   2500.0,    5000.0,  7500.0,  68, 72,  "AH"),
    (65,  "Dragonblight",           571,   2500.0,   4500.0,    5000.0,  7500.0,  71, 75,  "AH"),
    (66,  "Grizzly Hills",          571,   2500.0,   4500.0,    7500.0, 10000.0,  73, 75,  "AH"),
    (396, "Zul'Drak",               571,   4500.0,   6500.0,    5000.0,  7500.0,  74, 77,  "AH"),
    (403, "Sholazar Basin",         571,   5000.0,   7500.0,    2000.0,  5000.0,  75, 78,  "AH"),
    (495, "Icecrown",               571,   4500.0,   7500.0,    4500.0,  8000.0,  77, 80,  "AH"),
    (404, "The Storm Peaks",        571,   6500.0,   9000.0,   -2500.0,   500.0,  77, 80,  "AH"),
    (2817,"Crystalsong Forest",     571,   8000.0,   9500.0,    1000.0,  3000.0,  77, 80,  "AH"),
    (4197,"Wintergrasp",            571,   3500.0,   5500.0,    3000.0,  5000.0,  70, 80,  "AH"),
]


# Build a flat lookup list: for each (map, bbox) test.
def find_zone(mapid: int, x: float, y: float):
    """Return (zoneId, name, faction, minLvl, maxLvl) or None."""
    for (zid, name, m, xmin, xmax, ymin, ymax, lmin, lmax, fac) in ZONE_BBOXES:
        if m != mapid:
            continue
        if xmin <= x <= xmax and ymin <= y <= ymax:
            return (zid, name, fac, lmin, lmax)
    return None


# ---------------------------------------------------------------------------
# Database query
# ---------------------------------------------------------------------------
QUERY = """
SELECT
    go.id          AS entry,
    gt.name        AS name,
    go.map         AS map,
    go.zoneId      AS zoneId,
    go.areaId      AS areaId,
    go.position_x  AS x,
    go.position_y  AS y,
    go.position_z  AS z,
    go.spawnMask   AS spawnMask
FROM gameobject go
JOIN gameobject_template gt ON gt.entry = go.id
WHERE gt.type = 3
  AND go.map IN (0, 1, 530, 571)
"""


def fetch_nodes(conn):
    with conn.cursor() as cur:
        cur.execute(QUERY)
        return cur.fetchall()


def slugify(name: str) -> str:
    s = re.sub(r"[^A-Za-z0-9]+", "_", name).strip("_").lower()
    return s


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=3306)
    ap.add_argument("--user", default="root")
    ap.add_argument("--password", default="")
    ap.add_argument("--database", default="world")
    ap.add_argument("--out", default=None,
                    help="Output directory (default: ../Plugins/randomBuddy/Data)")
    args = ap.parse_args()

    if not args.out:
        here = os.path.dirname(os.path.abspath(__file__))
        args.out = os.path.normpath(os.path.join(
            here, "..", "bin", "Debug", "net10.0-windows7.0",
            "Plugins", "randomBuddy", "Data"))

    os.makedirs(args.out, exist_ok=True)

    print(f"[+] Connecting to MySQL {args.host}:{args.port} db={args.database}...")
    conn = pymysql.connect(host=args.host, port=args.port, user=args.user,
                           password=args.password, database=args.database,
                           charset="utf8mb4", autocommit=True)
    print("[+] Querying gather candidates (gt.type=3, outdoor maps only)...")
    rows = fetch_nodes(conn)
    print(f"[+] {len(rows)} candidate gameobjects (type=3)")
    conn.close()

    # Filter + assign to real zone via position bbox
    zones = defaultdict(lambda: {"herbs": [], "mines": [], "rejected": []})
    total_herbs = 0
    total_mines = 0
    total_unknown = 0
    total_outside = 0

    for entry, name, mapid, zoneid, areaid, x, y, pos_z, spawnmask in rows:
        kind = classify(name)
        if not kind:
            total_unknown += 1
            continue

        zone_info = find_zone(mapid, float(x), float(y))
        if not zone_info:
            total_outside += 1
            continue

        zid, zname, fac, lmin, lmax = zone_info
        node = {
            "x": round(float(x), 2),
            "y": round(float(y), 2),
            "z": round(float(pos_z), 2),
            "name": name,
        }
        if kind == "herb":
            zones[zid]["herbs"].append(node)
            total_herbs += 1
        else:
            zones[zid]["mines"].append(node)
            total_mines += 1
        # Stash zone meta once
        zones[zid].setdefault("_meta", (zname, mapid, fac, lmin, lmax))

    print(f"[+] After classification: {total_herbs} herbs + {total_mines} mines")
    print(f"[+] Rejected: {total_unknown} non-gather, {total_outside} outside known bboxes")

    # Write per-zone files
    summary = []
    for zid, data in sorted(zones.items()):
        zname, mapid, fac, lmin, lmax = data["_meta"]
        out = {
            "zoneId":   zid,
            "name":     zname,
            "mapId":    mapid,
            "faction":  fac,
            "minLevel": lmin,
            "maxLevel": lmax,
            "herbs":    data["herbs"],
            "mines":    data["mines"],
            "mailboxes":  [],
            "vendors":    [],
            "blackspots": [],
        }
        fname = f"{mapid}_{zid:04d}_{slugify(zname)}.json"
        fpath = os.path.join(args.out, fname)
        with open(fpath, "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, separators=(",", ":"))
        summary.append({
            "zoneId": zid, "mapId": mapid, "name": zname, "file": fname,
            "faction": fac, "herbs": len(data["herbs"]),
            "mines": len(data["mines"]),
            "total":  len(data["herbs"]) + len(data["mines"]),
        })

    # Manifest
    manifest = {
        "generated":  datetime.datetime.now().isoformat(timespec="seconds"),
        "source":     f"world DB @ {args.host}:{args.port}",
        "zoneCount":  len(summary),
        "totals":     {"herbs": total_herbs, "mines": total_mines,
                       "rejected": total_unknown, "outside": total_outside},
        "zones":      summary,
    }
    with open(os.path.join(args.out, "_manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)

    # Console summary
    print()
    print(f"{'MAP':>4} {'ZONEID':>6}  {'HERBS':>6} {'MINES':>6}  {'TOTAL':>6}  FAC ZONE NAME")
    print("-" * 78)
    for s in summary:
        print(f"{s['mapId']:>4} {s['zoneId']:>6}  {s['herbs']:>6} {s['mines']:>6}  "
              f"{s['total']:>6}  {s['faction']:>3} {s['name']}")
    print()
    print(f"[+] Wrote {len(summary)} JSON files + _manifest.json to: {args.out}")
    print(f"[+] Totals: {total_herbs} herbs + {total_mines} mining across {len(summary)} zones")


if __name__ == "__main__":
    main()
