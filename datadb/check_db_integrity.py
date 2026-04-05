import sqlite3

db = r'C:\Users\Texy6\Desktop\newhcb\CopilotBuddy\bin\Debug\net10.0-windows7.0\CreatureSpawns.db'
conn = sqlite3.connect(db)
cur = conn.cursor()

# Total spawns
cur.execute("SELECT COUNT(*) FROM spawns")
print(f"Total spawns: {cur.fetchone()[0]}")

# Distinct entries
cur.execute("SELECT COUNT(DISTINCT entry) FROM spawns")
print(f"Distinct creature entries: {cur.fetchone()[0]}")

# Map distribution
cur.execute("SELECT map_id, COUNT(*) as cnt FROM spawns GROUP BY map_id ORDER BY cnt DESC LIMIT 10")
print("\nTop 10 maps by spawn count:")
for row in cur.fetchall():
    print(f"  map {row[0]}: {row[1]} spawns")

# Check some well-known WotLK entry IDs
# Defias Thug = 38 (Elwynn/Northshire)
# Kobold Vermin = 6 (Elwynn)  
# Diseased Young Wolf = 299 (Elwynn)
# Hogger = 448 (Elwynn)
# Defias Pillager = 589 (Westfall)
test_entries = {6: "Kobold Vermin", 38: "Defias Thug", 80: "Kobold Laborer", 257: "Kobold Worker", 
                299: "Diseased Young Wolf", 448: "Hogger", 589: "Defias Pillager",
                822: "Young Forest Bear", 118: "Prowler"}
print("\nWotLK creature entry check:")
for entry, name in test_entries.items():
    cur.execute("SELECT COUNT(*) FROM spawns WHERE entry = ?", (entry,))
    count = cur.fetchone()[0]
    print(f"  entry {entry} ({name}): {count} spawns {'OK' if count > 0 else 'MISSING!'}")

# Check for Cata-only entries (entry > 40000 is suspicious for creature table)
cur.execute("SELECT COUNT(*) FROM spawns WHERE entry > 50000")
print(f"\nEntries > 50000 (Cata territory): {cur.fetchone()[0]}")

cur.execute("SELECT MAX(entry) FROM spawns")
print(f"Max entry ID: {cur.fetchone()[0]}")

# Check item_loot.db too
loot_db = r'C:\Users\Texy6\Desktop\newhcb\CopilotBuddy\bin\Debug\net10.0-windows7.0\item_loot.db'
conn2 = sqlite3.connect(loot_db)
cur2 = conn2.cursor()
cur2.execute("SELECT COUNT(*) FROM creature_loot")
print(f"\nitem_loot.db creature_loot rows: {cur2.fetchone()[0]}")
cur2.execute("SELECT COUNT(DISTINCT creature_entry) FROM creature_loot")
print(f"item_loot.db distinct creatures: {cur2.fetchone()[0]}")
cur2.execute("SELECT MAX(creature_entry) FROM creature_loot")
print(f"item_loot.db max creature entry: {cur2.fetchone()[0]}")
cur2.execute("SELECT COUNT(DISTINCT item_id) FROM creature_loot")
print(f"item_loot.db distinct items: {cur2.fetchone()[0]}")
conn2.close()
conn.close()
