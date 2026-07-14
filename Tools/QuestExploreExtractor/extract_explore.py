#!/usr/bin/env python3
"""
Supplementary QuestData.db extractor: areatrigger-based EXPLORE objectives.

The original Tools/QuestDataExtractor (C#) is not in the repo, so rather than regenerate the whole
QuestData.db this ADDS a `quest_explore` table alongside the existing (untouched) tables. VibeQuester2's
DataLoader synthesizes an `Explore` objective per row. Idempotent: drops+rebuilds only quest_explore.

Source: acore_world.areatrigger_involvedrelation (quest -> areatrigger id) JOIN areatrigger (coord+radius).
Run manually against the live AC world DB after a core update. Requires mysql.exe on PATH or at MYSQL_EXE.

  python extract_explore.py "E:/!Games/World of Warcraft/CopilotBuddy/Bots/VibeQuester/QuestData.db"
"""
import subprocess, sqlite3, sys, os

MYSQL_EXE = os.environ.get("MYSQL_EXE", r"D:\MySQL\bin\mysql.exe")
DB_USER, DB_PASS, DB_NAME = "acore", "acore", "acore_world"

QUERY = (
    "SELECT air.quest, a.map, a.x, a.y, a.z, a.radius "
    "FROM areatrigger_involvedrelation air "
    "JOIN areatrigger a ON a.entry = air.id "
    "JOIN quest_template q ON q.ID = air.quest "
    "ORDER BY air.quest;"
)

def dump_rows():
    out = subprocess.run(
        [MYSQL_EXE, "-u", DB_USER, f"-p{DB_PASS}", "-N", "-B", "-e", QUERY, DB_NAME],
        capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(f"mysql failed: {out.stderr.strip()}")
    rows = []
    for line in out.stdout.splitlines():
        if not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) != 6:   # loud, like the original extractor's introspection guard — AC renamed/dropped a column
            sys.exit(f"unexpected column count {len(parts)} (expected 6: quest,map,x,y,z,radius) in row: {line!r} "
                     f"— the areatrigger/areatrigger_involvedrelation schema changed; update QUERY.")
        q, m, x, y, z, r = parts
        rows.append((int(q), int(m), float(x), float(y), float(z), float(r)))
    return rows

def check_schema():
    """Fail LOUD if areatrigger lost/renamed a column we SELECT — the original QuestDataExtractor had this
    introspection guard; this bolt-on must not silently emit a broken/empty table on a core update."""
    out = subprocess.run(
        [MYSQL_EXE, "-u", DB_USER, f"-p{DB_PASS}", "-N", "-B", "-e",
         f"SELECT COLUMN_NAME FROM information_schema.columns "
         f"WHERE table_schema='{DB_NAME}' AND table_name='areatrigger'", DB_NAME],
        capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(f"mysql schema probe failed: {out.stderr.strip()}")
    have = {c.strip().lower() for c in out.stdout.split()}
    missing = {"entry", "map", "x", "y", "z", "radius"} - have
    if missing:
        sys.exit(f"areatrigger is missing expected column(s) {sorted(missing)} (has: {sorted(have)}) "
                 f"— AC schema changed; update the extractor before regenerating.")

def main():
    db_path = sys.argv[1] if len(sys.argv) > 1 else \
        r"E:\!Games\World of Warcraft\CopilotBuddy\Bots\VibeQuester\QuestData.db"
    check_schema()
    rows = dump_rows()
    con = sqlite3.connect(db_path)
    cur = con.cursor()
    cur.execute("DROP TABLE IF EXISTS quest_explore")
    cur.execute("CREATE TABLE quest_explore "
                "(quest_id INT, map INT, x REAL, y REAL, z REAL, radius REAL)")
    cur.executemany("INSERT INTO quest_explore VALUES (?,?,?,?,?,?)", rows)
    cur.execute("CREATE INDEX IF NOT EXISTS ix_quest_explore ON quest_explore(quest_id)")
    con.commit()
    quests = len({r[0] for r in rows})
    print(f"quest_explore: {len(rows)} areatrigger row(s) across {quests} quest(s) -> {db_path}")
    con.close()

if __name__ == "__main__":
    main()
