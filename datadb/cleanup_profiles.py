import os

folders = [
    r'C:\Users\Texy6\Desktop\wrobot profile\HB_Converted',
    r'C:\Users\Texy6\Desktop\newhcb\CopilotBuddy\bin\Debug\net10.0-windows7.0\Default Profiles\GB\wrobot_converted',
]

# Profiles from expansions not available in WotLK 3.3.5a
keywords = [
    'Dark Soil', 'Onyx Egg',           # MoP items
    'Deepholm', "Vash'jr", 'Hyjal', 'Uldum',  # Cata zones
    'Pandaria',                          # MoP
    'MoP',                               # explicit
    'WoD', 'Garrison', 'FrostFire', 'Talador', 'Tannan',  # WoD
    'Shadowmoon Valley',                 # WoD (not TBC)
    'Legion', 'Suramar', "Val'sharah",   # Legion
]

for folder in folders:
    deleted = 0
    for f in sorted(os.listdir(folder)):
        if not f.endswith('.xml'):
            continue
        if any(k.lower() in f.lower() for k in keywords):
            os.remove(os.path.join(folder, f))
            print(f'DEL {f}')
            deleted += 1
    remaining = len([x for x in os.listdir(folder) if x.endswith('.xml')])
    print(f'Restant: {remaining} ({deleted} supprimés) dans {os.path.basename(folder)}')
