from pathlib import Path
import re

d = Path(r'C:\Users\Texy6\Desktop\newhcb\CopilotBuddy\bin\Debug\net10.0-windows7.0\Default Profiles\Grind')
files = sorted(d.rglob('*_HB.xml'))
print(f"Total files: {len(files)}")
for i, f in enumerate(files):
    try:
        text = f.read_text(encoding='utf-8')
        matches = re.findall(r'WRobot EntryIDs: ([\d ]+)', text)
        factions_empty = text.count('<Factions></Factions>')
        factions_filled = len(re.findall(r'<Factions>\d', text))
        total_ids = sum(len(m.split()) for m in matches)
        print(f"  [{i:3d}] {f.name[:60]:60s} | blocks={len(matches):3d} | total_ids={total_ids:4d} | empty={factions_empty:3d} | filled={factions_filled:3d}")
    except Exception as e:
        print(f"  [{i:3d}] ERROR on {f.name}: {e}")
