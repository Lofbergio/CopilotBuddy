"""
Rebuild CreatureSpawns.db from Trinity MySQL world database.
Finds all creature entries referenced in item_loot.db that are missing from CreatureSpawns.db,
then extracts their spawns from Trinity and inserts them.
Also does a full pass to add ANY missing creature spawns from Trinity.
"""
import sqlite3
import subprocess
import json
import sys
import os

MYSQL_BIN = r"C:\Users\Texy6\Desktop\Naaru Blizzlike Repack 3.3.5a v2025.07\mysql\bin\mysql.exe"
SPAWNS_DB = r"C:\Users\Texy6\Desktop\newhcb\CopilotBuddy\bin\Debug\net10.0-windows7.0\CreatureSpawns.db"
LOOT_DB = r"C:\Users\Texy6\Desktop\newhcb\CopilotBuddy\bin\Debug\net10.0-windows7.0\item_loot.db"

def mysql_query(sql):
    """Run a MySQL query and return rows as list of tuples."""
    result = subprocess.run(
        [MYSQL_BIN, "-u", "root", "-N", "-B", "-e", sql],
        capture_output=True, text=True, timeout=120
    )
    if result.returncode != 0:
        print(f"MySQL error: {result.stderr.strip()}")
        return []
    rows = []
    for line in result.stdout.strip().split('\n'):
        if line:
            rows.append(tuple(line.split('\t')))
    return rows

# Step 1: Get all creature entries from item_loot.db
print("=== Step 1: Reading item_loot.db ===")
loot_conn = sqlite3.connect(LOOT_DB)
loot_cur = loot_conn.cursor()
loot_cur.execute("SELECT DISTINCT creature_entry FROM creature_loot ORDER BY creature_entry")
loot_entries = set(r[0] for r in loot_cur.fetchall())
print(f"  item_loot.db has {len(loot_entries)} distinct creature entries")
loot_conn.close()

# Step 2: Get all entries already in CreatureSpawns.db
print("=== Step 2: Reading CreatureSpawns.db ===")
spawn_conn = sqlite3.connect(SPAWNS_DB)
spawn_cur = spawn_conn.cursor()
spawn_cur.execute("SELECT DISTINCT entry FROM spawns")
existing_entries = set(r[0] for r in spawn_cur.fetchall())
print(f"  CreatureSpawns.db has {len(existing_entries)} distinct creature entries")
spawn_cur.execute("SELECT COUNT(*) FROM spawns")
existing_count = spawn_cur.fetchone()[0]
print(f"  CreatureSpawns.db has {existing_count} total spawns")

# Step 3: Find missing entries from item_loot.db
missing_from_loot = loot_entries - existing_entries
print(f"\n=== Step 3: Missing entries ===")
print(f"  {len(missing_from_loot)} creature entries in item_loot.db but NOT in CreatureSpawns.db")

# Step 4: Get ALL creature entries from Trinity that are missing
print("\n=== Step 4: Query Trinity for all creature entries ===")
rows = mysql_query("SELECT DISTINCT id FROM world.creature ORDER BY id")
trinity_entries = set(int(r[0]) for r in rows)
print(f"  Trinity has {len(trinity_entries)} distinct creature entries")

all_missing = trinity_entries - existing_entries
print(f"  {len(all_missing)} entries in Trinity but NOT in CreatureSpawns.db")

# Step 5: Extract spawns for ALL missing entries from Trinity
# Do it in batches to avoid huge queries
print(f"\n=== Step 5: Extracting spawns for {len(all_missing)} missing entries ===")
missing_list = sorted(all_missing)
batch_size = 500
total_inserted = 0

# Get max id for auto-increment
spawn_cur.execute("SELECT MAX(id) FROM spawns")
max_id_result = spawn_cur.fetchone()[0]
next_id = (max_id_result or 0) + 1

for batch_start in range(0, len(missing_list), batch_size):
    batch = missing_list[batch_start:batch_start + batch_size]
    entry_list = ",".join(str(e) for e in batch)
    
    sql = f"SELECT id, map, position_x, position_y, position_z FROM world.creature WHERE id IN ({entry_list})"
    rows = mysql_query(sql)
    
    if rows:
        insert_rows = []
        for row in rows:
            entry = int(row[0])
            map_id = int(row[1])
            x = float(row[2])
            y = float(row[3])
            z = float(row[4])
            insert_rows.append((next_id, entry, map_id, x, y, z))
            next_id += 1
        
        spawn_cur.executemany("INSERT INTO spawns (id, entry, map_id, x, y, z) VALUES (?, ?, ?, ?, ?, ?)", insert_rows)
        total_inserted += len(insert_rows)
    
    processed = min(batch_start + batch_size, len(missing_list))
    print(f"  Processed {processed}/{len(missing_list)} entries, inserted {total_inserted} spawns so far")

spawn_conn.commit()

# Step 6: Verify
print(f"\n=== Step 6: Verification ===")
spawn_cur.execute("SELECT COUNT(*) FROM spawns")
new_total = spawn_cur.fetchone()[0]
print(f"  CreatureSpawns.db now has {new_total} total spawns (was {existing_count}, added {total_inserted})")

spawn_cur.execute("SELECT COUNT(DISTINCT entry) FROM spawns")
new_entries = spawn_cur.fetchone()[0]
print(f"  CreatureSpawns.db now has {new_entries} distinct entries (was {len(existing_entries)})")

# Verify the specific problematic entries
test_entries = {38: "Defias Thug", 589: "Defias Pillager", 6: "Kobold Vermin"}
print("\n  Spot check:")
for entry, name in test_entries.items():
    spawn_cur.execute("SELECT COUNT(*) FROM spawns WHERE entry = ?", (entry,))
    count = spawn_cur.fetchone()[0]
    print(f"    entry {entry} ({name}): {count} spawns {'OK' if count > 0 else 'STILL MISSING'}")

# Check how many item_loot entries are now covered
spawn_cur.execute("SELECT DISTINCT entry FROM spawns")
final_entries = set(r[0] for r in spawn_cur.fetchall())
still_missing = loot_entries - final_entries
if still_missing:
    print(f"\n  WARNING: {len(still_missing)} creature entries from item_loot.db still have no spawns in Trinity")
    if len(still_missing) <= 20:
        print(f"    Missing: {sorted(still_missing)}")
else:
    print(f"\n  All creature entries from item_loot.db now have spawns!")

spawn_conn.close()
print("\nDone!")
