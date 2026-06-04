#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
qa_gathernodes.py - Quality control on extracted gather node data.

Checks:
  1. Coord integrity - all (x,y) fall inside the assigned zone bbox.
  2. Coord plausibility - z values are not absurd (e.g. +100000 or NaN).
  3. Duplicate detection - same (x,y) and same name.
  4. Coverage - zones with very few nodes are flagged.
  5. Edge detection - nodes within X yards of bbox edge are flagged
     (these are often 'leaked' from adjacent zones, or unreachable).
  6. Outlier detection - z value deviates strongly from neighbors.
  7. Sub-area leakage - sample nodes whose position falls in another
     known zone bbox (this means our bbox is wrong).
"""

import json
import math
import os
import sys
from collections import defaultdict, Counter

# Use the same bbox list as the extractor
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from extract_gathernodes import ZONE_BBOXES, find_zone

DATA_DIR = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "bin", "Debug", "net10.0-windows7.0", "Plugins", "randomBuddy", "Data")

# Edge threshold: a node within N yards of bbox edge is suspicious
EDGE_THRESHOLD = 50.0

# Bbox for "outside any known zone" detection
def is_outside(x, y, m):
    for (zid, name, mm, xmin, xmax, ymin, ymax, *_) in ZONE_BBOXES:
        if mm != m: continue
        if xmin <= x <= xmax and ymin <= y <= ymax:
            return False
    return True


def load_zones(data_dir):
    zones = {}
    for fn in os.listdir(data_dir):
        if not fn.endswith(".json") or fn == "_manifest.json":
            continue
        fp = os.path.join(data_dir, fn)
        try:
            with open(fp, "r", encoding="utf-8") as f:
                z = json.load(f)
            zones[z["zoneId"]] = z
        except Exception as e:
            print(f"[!] Failed to parse {fn}: {e}")
    return zones


def check_zone(zid, z):
    issues = []
    bbox = next(((xmin, xmax, ymin, ymax) for (zid2, n, m, xmin, xmax, ymin, ymax, *_) in ZONE_BBOXES
                 if zid2 == zid), None)
    if not bbox:
        issues.append(("CRITICAL", "No bbox defined for this zoneId"))
        return issues
    xmin, xmax, ymin, ymax = bbox
    nodes = z.get("herbs", []) + z.get("mines", [])

    # 1. Coord integrity
    bad_coords = 0
    for n in nodes:
        x, y = n["x"], n["y"]
        if not (xmin <= x <= xmax and ymin <= y <= ymax):
            bad_coords += 1
    if bad_coords:
        issues.append(("HIGH", f"{bad_coords}/{len(nodes)} nodes OUTSIDE the zone bbox"))

    # 2. Coord plausibility
    bad_z = 0
    for n in nodes:
        if abs(n["z"]) > 10000 or math.isnan(n["z"]):
            bad_z += 1
    if bad_z:
        issues.append(("HIGH", f"{bad_z} nodes have absurd z values"))

    # 3. Duplicates
    coords = Counter((n["x"], n["y"]) for n in nodes)
    dupes = sum(1 for c, k in coords.items() if k > 1)
    if dupes:
        issues.append(("LOW", f"{dupes} unique positions have multiple nodes (likely 2 spawns sharing a position - OK)"))

    # 4. Coverage
    if len(nodes) < 30:
        issues.append(("MEDIUM", f"Low node count ({len(nodes)}) - zone may be under-populated or bbox too small"))

    # 5. Edge detection
    edge_count = 0
    for n in nodes:
        x, y = n["x"], n["y"]
        if (x - xmin < EDGE_THRESHOLD or xmax - x < EDGE_THRESHOLD or
            y - ymin < EDGE_THRESHOLD or ymax - y < EDGE_THRESHOLD):
            edge_count += 1
    if edge_count > 0.5 * len(nodes):
        issues.append(("MEDIUM", f"{edge_count}/{len(nodes)} nodes ({100*edge_count//len(nodes)}%) within {EDGE_THRESHOLD}yd of bbox edge - bbox probably too generous"))

    # 6. Outlier detection on Z
    zs = [n["z"] for n in nodes]
    if zs:
        mean_z = sum(zs) / len(zs)
        std_z = (sum((z - mean_z) ** 2 for z in zs) / len(zs)) ** 0.5
        outliers = sum(1 for z in zs if abs(z - mean_z) > 3 * std_z and std_z > 0)
        if outliers > 0.1 * len(nodes):
            issues.append(("LOW", f"{outliers} z-value outliers (may be underground mines, etc.)"))

    # 7. Sub-area leakage - check if a node in zone X is actually inside zone Y's bbox
    other_leaks = 0
    mapid = z.get("mapId", 0)
    for n in nodes:
        x, y = n["x"], n["y"]
        # Check all other zones on same map
        for (zid2, name, m, xmin2, xmax2, ymin2, ymax2, *_) in ZONE_BBOXES:
            if zid2 == zid: continue
            if m != mapid: continue
            if xmin2 <= x <= xmax2 and ymin2 <= y <= ymax2:
                # The node falls into a different zone's bbox
                other_leaks += 1
                break
    if other_leaks > 0.05 * len(nodes):
        issues.append(("MEDIUM", f"{other_leaks}/{len(nodes)} nodes fall in ANOTHER zone's bbox (bbox overlap)"))

    return issues


def main():
    print(f"[+] QA scan of: {DATA_DIR}")
    zones = load_zones(DATA_DIR)
    print(f"[+] Loaded {len(zones)} zone files\n")

    total_nodes = 0
    issue_count = defaultdict(int)
    flagged_zones = []
    for zid in sorted(zones):
        z = zones[zid]
        n = len(z.get("herbs", [])) + len(z.get("mines", []))
        total_nodes += n
        issues = check_zone(zid, z)
        if issues:
            flagged_zones.append((zid, z.get("name", "?"), n, issues))
        for sev, _ in issues:
            issue_count[sev] += 1

    print(f"{'ZONEID':>6}  {'NODES':>5}  ISSUES")
    print("-" * 90)
    for zid, name, n, issues in flagged_zones:
        sevs = "/".join(s for s, _ in issues)
        msgs = " | ".join(m for _, m in issues)
        print(f"{zid:>6}  {n:>5}  [{sevs}] {name}: {msgs}")
    print()
    print(f"[+] Total: {total_nodes} nodes across {len(zones)} zones")
    print(f"[+] {len(flagged_zones)} zones flagged")
    print(f"[+] Severity breakdown: {dict(issue_count)}")
    return 1 if issue_count.get("CRITICAL") or issue_count.get("HIGH") else 0


if __name__ == "__main__":
    sys.exit(main())
