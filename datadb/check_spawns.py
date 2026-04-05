import sqlite3
conn = sqlite3.connect(r'C:\Users\Texy6\Desktop\newhcb\CopilotBuddy\bin\Debug\net10.0-windows7.0\CreatureSpawns.db')
cur = conn.cursor()
cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
print('Tables:', [r[0] for r in cur.fetchall()])
cur.execute("PRAGMA table_info(spawns)")
print('Columns:', [(r[1], r[2]) for r in cur.fetchall()])
cur.execute("SELECT DISTINCT entry FROM spawns WHERE entry BETWEEN 30 AND 50 ORDER BY entry")
print('Entries 30-50:', [r[0] for r in cur.fetchall()])
# Check defias entries
cur.execute("SELECT DISTINCT entry FROM spawns WHERE entry IN (38, 94, 95, 97, 98, 116, 474, 619)")
print('Defias entries present:', [r[0] for r in cur.fetchall()])
conn.close()
