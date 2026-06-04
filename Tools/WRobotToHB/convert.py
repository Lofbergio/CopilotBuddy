#!/usr/bin/env python3
"""
WRobot EasyQuestProfile  ->  HonorBuddy / CopilotBuddy HBProfile (grind)
==========================================================================
Extrait les zones de grind (EasyQuest + IsGrinderNotQuest=true) depuis un
profil WRobot, reconstruit les tranches de niveau joueur depuis le flux
QuestsSorted, associe les NPCs vendeurs/réparateurs par proximité, et génère
un <HBProfile> compatible CopilotBuddy.

Usage:
    python convert.py <input_wrobot.xml> [output_hb.xml]

Si output_hb.xml est omis, le fichier est créé dans le même dossier avec
le suffixe _HB.xml.

Limitations connues:
  - HB utilise des Faction IDs, WRobot des Entry IDs de créatures.
    Les entrées WRobot sont placées en commentaire XML à côté de <Factions/>.
    Vous devez les remplir manuellement si nécessaire (souvent pas obligatoire
    car le bot attaque tout ce qui est en range de niveau).
  - Les profils de quête purs (sans zone IsGrinderNotQuest) ne se convertissent
    pas — c'est normal, ils n'ont pas d'équivalent HB grind.
"""
import sys
import re
import math
from pathlib import Path
from xml.etree import ElementTree as ET


# ---------------------------------------------------------------------------
# Parsing WRobot
# ---------------------------------------------------------------------------

def _normalize_name(name: str) -> str:
    """Normalise un nom de zone pour la comparaison floue (minuscule, sans _/- /espace)."""
    return re.sub(r'[\s_\-]+', '', name).lower()


def parse_wrobot(path: Path):
    """Parse le fichier WRobot et retourne les données structurées."""
    # L'encodage WRobot est souvent utf-16
    try:
        tree = ET.parse(path)
    except ET.ParseError:
        # Essai en utf-8
        content = path.read_text(encoding='utf-8-sig')
        tree = ET.ElementTree(ET.fromstring(content))

    root = tree.getroot()

    grind_zones = _parse_grind_zones(root)
    npcs = _parse_npcs(root)
    blackspots = _parse_blackspots(root)
    zone_order_raw = _parse_level_flow(root)

    # Construire un index de correspondance floue: normalized_name -> real_name
    norm_to_real = {_normalize_name(k): k for k in grind_zones}

    # Résoudre les noms dans zone_order via correspondance exacte puis floue
    zone_order = []
    for pulse_name, min_lvl, max_lvl in zone_order_raw:
        if pulse_name in grind_zones:
            zone_order.append((pulse_name, min_lvl, max_lvl))
        else:
            # Essai correspondance floue
            norm = _normalize_name(pulse_name)
            real = norm_to_real.get(norm)
            if real:
                zone_order.append((real, min_lvl, max_lvl))
            else:
                # Garder le nom brut (sera filtré si absent de grind_zones)
                zone_order.append((pulse_name, min_lvl, max_lvl))

    return grind_zones, npcs, blackspots, zone_order


def _parse_grind_zones(root):
    """Retourne dict {name: {hotspots, entries, max_mob_level, min_mob_level}}"""
    zones = {}
    easy_quests = root.find('EasyQuests')
    if easy_quests is None:
        return zones

    for eq in easy_quests.findall('EasyQuest'):
        name = (eq.findtext('Name') or '').strip()
        if not name:
            continue

        qclass = eq.find('QuestClass')
        if qclass is None:
            continue

        is_grinder = (qclass.findtext('IsGrinderNotQuest') or 'false').lower() == 'true'
        if not is_grinder:
            continue

        hotspots = []
        hotspots_el = qclass.find('HotSpots')
        if hotspots_el is not None:
            for v in hotspots_el.findall('Vector3'):
                hotspots.append({
                    'X': v.get('X', '0'),
                    'Y': v.get('Y', '0'),
                    'Z': v.get('Z', '0'),
                })

        entries = []
        entry_target = qclass.find('EntryTarget')
        if entry_target is not None:
            for e in entry_target.findall('int'):
                if e.text:
                    entries.append(e.text.strip())

        max_mob = int(eq.findtext('MaxLevel') or '0')
        min_mob = int(eq.findtext('MinLevel') or '0')

        zones[name] = {
            'hotspots': hotspots,
            'entries': entries,
            'max_mob_level': max_mob,
            'min_mob_level': min_mob,
        }

    return zones


def _parse_npcs(root):
    """Retourne liste de dicts NPC {name, entry, type, X, Y, Z}"""
    npcs = []
    seen = set()
    npc_section = root.find('Npc')
    if npc_section is None:
        return npcs

    for npc in npc_section.findall('Npc'):
        pos = npc.find('Position')
        if pos is None:
            continue
        entry = (npc.findtext('Entry') or '').strip()
        ntype = (npc.findtext('Type') or '').strip()
        key = (entry, ntype)
        if key in seen:
            continue
        seen.add(key)
        npcs.append({
            'name': (npc.findtext('Name') or '').strip(),
            'entry': entry,
            'type': ntype,
            'X': pos.get('X', '0'),
            'Y': pos.get('Y', '0'),
            'Z': pos.get('Z', '0'),
        })

    return npcs


def _parse_blackspots(root):
    """Retourne liste de dicts blackspot {X, Y, Z, Radius}"""
    spots = []
    bs_section = root.find('Blackspots')
    if bs_section is None:
        return spots
    for bs in bs_section.findall('Blackspot'):
        spots.append({
            'X': bs.get('X', '0'),
            'Y': bs.get('Y', '0'),
            'Z': bs.get('Z', '0'),
            'Radius': bs.get('Radius', '15'),
        })
    return spots


def _parse_level_flow(root):
    """
    Parse QuestsSorted pour reconstruire l'ordre des zones et leurs plages de
    niveau joueur.

    Algorithme:
      - On marche linéairement dans les actions.
      - On maintient une pile de conditions If (level_lt, class, other).
      - current_min avance quand on sort d'un bloc level_lt.
      - Chaque Pulse reçoit: min = current_min, max = seuil du bloc level_lt
        actif (ou None si pas de bloc level_lt actif).
    
    Retourne liste de (zone_name, min_player_level, max_player_level|None).
    """
    qs_root = root.find('QuestsSorted')
    if qs_root is None:
        return []

    actions = qs_root.findall('QuestsSorted')
    zone_order = []
    nesting = []       # stack de {'type': ..., 'value': ...}
    current_min = 1

    for action in actions:
        act = action.get('Action', '')
        name = (action.get('NameClass') or '').strip()

        if act == 'If':
            m_lt = re.match(r'ObjectManager\.Me\.Level\s*<\s*(\d+)', name)
            if m_lt:
                nesting.append({'type': 'level_lt', 'value': int(m_lt.group(1))})
            elif 'WowClass' in name:
                nesting.append({'type': 'class'})
            else:
                nesting.append({'type': 'other'})

        elif act == 'EndIf':
            if nesting:
                cond = nesting.pop()
                # Sortir d'un bloc level_lt fait avancer le min courant
                if cond['type'] == 'level_lt':
                    if cond['value'] > current_min:
                        current_min = cond['value']

        elif act == 'Pulse':
            # Filtrer: si on est dans un bloc class seulement (pas de level_lt)
            # c'est un voyage vendeur spécifique à une classe — ignorer.
            level_conds = [c for c in nesting if c['type'] == 'level_lt']
            class_only = any(c['type'] == 'class' for c in nesting) and not level_conds
            if class_only:
                continue

            if level_conds:
                max_lvl = min(c['value'] for c in level_conds) - 1
            else:
                max_lvl = None

            zone_order.append((name, current_min, max_lvl))

    return zone_order


# ---------------------------------------------------------------------------
# Utilitaires géométriques
# ---------------------------------------------------------------------------

def centroid(hotspots):
    if not hotspots:
        return None
    n = len(hotspots)
    return (
        sum(float(h['X']) for h in hotspots) / n,
        sum(float(h['Y']) for h in hotspots) / n,
        sum(float(h['Z']) for h in hotspots) / n,
    )


def dist3(a, b):
    return math.sqrt((a[0]-b[0])**2 + (a[1]-b[1])**2 + (a[2]-b[2])**2)


def find_nearby_npcs(hotspots, all_npcs, relevant_types, max_dist=2000):
    """
    Renvoie les NPCs de types voulus les plus proches du centroïde de la zone.
    """
    c = centroid(hotspots)
    if c is None:
        return []
    result = []
    seen_entries = set()
    for npc in all_npcs:
        if npc['type'] not in relevant_types:
            continue
        try:
            pos = (float(npc['X']), float(npc['Y']), float(npc['Z']))
        except ValueError:
            continue
        d = dist3(c, pos)
        if d <= max_dist:
            entry = npc['entry']
            if entry not in seen_entries:
                seen_entries.add(entry)
                result.append((d, npc))
    result.sort(key=lambda x: x[0])
    return [n for _, n in result]


# ---------------------------------------------------------------------------
# Mapping type WRobot → HB
# ---------------------------------------------------------------------------

WROBOT_TO_HB_TYPE = {
    'Vendor':          'Sell',
    'Repair':          'Repair',
    'Food':            'Food',
    'MageTrainer':     'Train',
    'PriestTrainer':   'Train',
    'WarlockTrainer':  'Train',
    'HunterTrainer':   'Train',
    'RogueTrainer':    'Train',
    'ShamanTrainer':   'Train',
    'DruidTrainer':    'Train',
    'WarriorTrainer':  'Train',
    'PaladinTrainer':  'Train',
    'ClassTrainer':    'Train',
}

VENDOR_TYPES = set(WROBOT_TO_HB_TYPE.keys())


# ---------------------------------------------------------------------------
# Construction du profil HB
# ---------------------------------------------------------------------------

def build_hb_profile(grind_zones, npcs, blackspots, zone_order,
                     profile_name, profile_min=1, profile_max=60):
    """
    Construit le XML HBProfile et le retourne sous forme de string.
    """
    # ------------------------------------------------------------------
    # Construire la liste ordonnée de SubProfiles
    # ------------------------------------------------------------------
    subprofiles = []
    used_zones = set()

    # Collecte d'abord les grind_zones référencées dans l'ordre du flux
    for i, (zone_name, min_lvl, max_lvl) in enumerate(zone_order):
        if zone_name not in grind_zones:
            continue
        if zone_name in used_zones:
            continue
        used_zones.add(zone_name)

        zone = grind_zones[zone_name]
        if not zone['hotspots']:
            continue

        p_min = max(min_lvl, profile_min)

        if max_lvl is not None:
            p_max = min(max_lvl, profile_max)
        else:
            # Zone inconditionnelle: on cherche la prochaine gate dans le flux
            p_max = profile_max
            for j in range(i + 1, len(zone_order)):
                _, _, next_max = zone_order[j]
                if next_max is not None and next_max > p_min:
                    p_max = next_max - 1
                    break
            # Plafond par le niveau max des mobs de la zone
            if zone['max_mob_level'] > 0:
                mob_ceiling = zone['max_mob_level'] + 3
                if mob_ceiling < p_max:
                    p_max = mob_ceiling

        # Garde-fou: max ne peut pas être inférieur à min (double gate WRobot)
        if p_max < p_min:
            # Utiliser le niveau max des mobs comme référence
            if zone['max_mob_level'] > 0:
                p_max = min(zone['max_mob_level'] + 3, profile_max)
            else:
                p_max = min(p_min + 5, profile_max)
            # Si toujours incohérent, sauter cette zone
            if p_max < p_min:
                continue

        subprofiles.append({
            'name':      zone_name,
            'min_level': p_min,
            'max_level': p_max,
            'hotspots':  zone['hotspots'],
            'entries':   zone['entries'],
            'mob_max':   zone['max_mob_level'],
            'mob_min':   zone['min_mob_level'],
        })

    # Ajouter les zones présentes dans EasyQuests mais absentes du flux
    for zone_name, zone in grind_zones.items():
        if zone_name in used_zones:
            continue
        if not zone['hotspots']:
            continue
        subprofiles.append({
            'name':      zone_name,
            'min_level': profile_min,
            'max_level': profile_max,
            'hotspots':  zone['hotspots'],
            'entries':   zone['entries'],
            'mob_max':   zone['max_mob_level'],
            'mob_min':   zone['min_mob_level'],
        })

    # ------------------------------------------------------------------
    # Écriture XML
    # ------------------------------------------------------------------
    L = []

    def w(s=''):
        L.append(s)

    w('<?xml version="1.0" encoding="UTF-8"?>')
    w(f'<HBProfile creator="WRobotConverter" version="1.0">')
    w(f'\t<Name>{profile_name}</Name>')
    w(f'\t<MinLevel>{profile_min}</MinLevel>')
    w(f'\t<MaxLevel>{profile_max}</MaxLevel>')
    w(f'\t<MinDurability>0.2</MinDurability>')
    w(f'\t<MinFreeBagSlots>2</MinFreeBagSlots>')
    w(f'\t<SellGrey>True</SellGrey>')
    w(f'\t<SellWhite>True</SellWhite>')
    w(f'\t<SellGreen>False</SellGreen>')
    w(f'\t<SellBlue>False</SellBlue>')
    w(f'\t<SellPurple>False</SellPurple>')
    w(f'\t<TargetElites>False</TargetElites>')
    w()

    for sp in subprofiles:
        nearby_service = find_nearby_npcs(
            sp['hotspots'], npcs,
            relevant_types=VENDOR_TYPES,
            max_dist=2500,
        )

        # Dédupliquer: garder repair + sell le plus proche
        repair_added = set()
        sell_added = set()
        vendors_out = []
        for npc in nearby_service:
            entry = npc['entry']
            if npc['type'] == 'Repair' and entry not in repair_added:
                repair_added.add(entry)
                vendors_out.append(npc)
                if len(repair_added) >= 2:
                    break
        for npc in nearby_service:
            entry = npc['entry']
            if npc['type'] == 'Vendor' and entry not in sell_added and entry not in repair_added:
                sell_added.add(entry)
                vendors_out.append(npc)
                if len(sell_added) >= 1:
                    break

        # Mob level cible
        mob_min = sp['mob_min'] if sp['mob_min'] > 0 else max(1, sp['min_level'] - 2)
        mob_max = sp['mob_max'] if sp['mob_max'] > 0 else sp['max_level'] + 2

        w(f'\t<SubProfile> <!-- {sp["name"]} -->')
        w(f'\t\t<Name>{sp["name"]}</Name>')
        w(f'\t\t<MinLevel>{sp["min_level"]}</MinLevel>')
        w(f'\t\t<MaxLevel>{sp["max_level"]}</MaxLevel>')

        if vendors_out:
            w(f'\t\t<Vendors>')
            for npc in vendors_out:
                hb_type = WROBOT_TO_HB_TYPE.get(npc['type'], 'Sell')
                safe_name = npc['name'].replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;').replace('"', '&quot;')
                w(f'\t\t\t<Vendor Name="{safe_name}" Entry="{npc["entry"]}" Type="{hb_type}" X="{npc["X"]}" Y="{npc["Y"]}" Z="{npc["Z"]}" />')
            w(f'\t\t</Vendors>')

        w(f'\t\t<GrindArea>')
        w(f'\t\t\t<TargetMinLevel>{mob_min}</TargetMinLevel>')
        w(f'\t\t\t<TargetMaxLevel>{mob_max}</TargetMaxLevel>')
        if sp['entries']:
            entries_str = ' '.join(sp['entries'])
            w(f'\t\t\t<!-- WRobot EntryIDs: {entries_str} -->')
        w(f'\t\t\t<Factions></Factions>')
        w(f'\t\t\t<Hotspots>')
        for hs in sp['hotspots']:
            w(f'\t\t\t\t<Hotspot X="{hs["X"]}" Y="{hs["Y"]}" Z="{hs["Z"]}" />')
        w(f'\t\t\t</Hotspots>')
        w(f'\t\t</GrindArea>')
        w(f'\t</SubProfile>')
        w()

    if blackspots:
        w('\t<Blackspots>')
        for bs in blackspots:
            w(f'\t\t<Blackspot X="{bs["X"]}" Y="{bs["Y"]}" Z="{bs["Z"]}" Radius="{bs["Radius"]}" />')
        w('\t</Blackspots>')

    w('</HBProfile>')
    return '\n'.join(L)


# ---------------------------------------------------------------------------
# Point d'entrée
# ---------------------------------------------------------------------------

def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(0)

    input_path = Path(sys.argv[1])
    if not input_path.exists():
        print(f"Erreur: fichier introuvable: {input_path}")
        sys.exit(1)

    if len(sys.argv) >= 3:
        output_path = Path(sys.argv[2])
    else:
        output_path = input_path.parent / (input_path.stem + '_HB.xml')

    # Détecter la plage de niveau depuis le nom du fichier (ex: "12-60")
    m = re.search(r'(\d+)[-_](\d+)', input_path.stem)
    if m:
        profile_min, profile_max = int(m.group(1)), int(m.group(2))
    else:
        profile_min, profile_max = 1, 60

    profile_name = input_path.stem

    print(f"Lecture : {input_path}")
    grind_zones, npcs, blackspots, zone_order = parse_wrobot(input_path)

    if not grind_zones:
        print("Aucune zone de grind trouvée (EasyQuest + IsGrinderNotQuest=true).")
        print("Ce fichier est peut-être un profil de quête pur sans zones de grind.")
        sys.exit(1)

    print(f"{len(grind_zones)} zones de grind trouvées:")
    for name in list(grind_zones.keys())[:10]:
        z = grind_zones[name]
        print(f"  {name}: {len(z['hotspots'])} hotspots, mobs {z['min_mob_level']}-{z['max_mob_level']}")
    if len(grind_zones) > 10:
        print(f"  ... et {len(grind_zones)-10} autres")

    print(f"\n{len(npcs)} NPCs trouvés")
    print(f"{len(blackspots)} blackspots trouvés")

    print(f"\nFlux de zones (zones de grind uniquement):")
    for zone_name, min_lvl, max_lvl in zone_order:
        if zone_name in grind_zones:
            max_str = str(max_lvl) if max_lvl is not None else "?"
            print(f"  {zone_name}: joueur lvl {min_lvl}-{max_str}")

    xml = build_hb_profile(
        grind_zones, npcs, blackspots, zone_order,
        profile_name, profile_min, profile_max,
    )

    output_path.write_text(xml, encoding='utf-8')
    print(f"\nProfil HB écrit: {output_path}")


if __name__ == '__main__':
    main()
