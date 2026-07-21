#!/usr/bin/env python3
"""Query the 3.3.5a client Spell.dbc — the oracle for "how does this ability actually work".

Companion to disasm_wow.py / audit_pointers.py. Written 2026-07-21 after a GoodVibes audit found three
whole defect CLASSES that were invisible to code review because the code was self-consistent:

  * StartRecoveryTime == 0 means the ability is OFF the GCD. Six hunter abilities were routed through a
    GCD-gated, pass-consuming cast wrapper — each COST a shot it should have been free alongside.
  * RecoveryTime == CategoryRecoveryTime == 0 means genuinely spammable, so anything below it in a
    PrioritySelector is unreachable FOREVER. Volley has no cooldown; Multi-Shot below it was dead code.
  * ManaCostPercentage is the real price under the zero-consumable damage-per-mana rule. Explosive Trap
    is 19% of base mana — 4x a Steady Shot — and was firing un-gated.

Ask this before ranking two abilities, before calling anything "the filler", and before adding a
cooldown rung. Reasoning from memory or from a MoP-era reference routine is how all three shipped.

  python Tools/spell_dbc.py "Steady Shot"          # by name (all ranks)
  python Tools/spell_dbc.py 49052                  # by id
  python Tools/spell_dbc.py --off-gcd "Hunter"     # every off-GCD spell whose name matches
  python Tools/spell_dbc.py --no-cooldown "Volley"
  python Tools/spell_dbc.py "Kill Command" --dbc "D:\\other\\dbc"

Field indices below were located EMPIRICALLY against known spells (Frostbolt/Steady Shot read 1500,
Auto Shot/Silencing Shot/Rapid Fire read 0 -> field 206 is the only candidate), not copied from a
struct definition on the internet. Self-tests at the bottom re-derive them, so a wrong DBC or a
changed layout fails loudly instead of printing plausible garbage.
"""
import argparse
import os
import struct
import sys

DEFAULT_DBC = r"E:\!Games\World of Warcraft\azerothcore\dbc"

# Empirically located in the 234-field / 936-byte 3.3.5a Spell.dbc record.
F_ID = 0
F_CAST_TIME_IDX = 28
F_RECOVERY = 29           # cooldown, ms
F_CATEGORY_RECOVERY = 30  # shared-category cooldown, ms
F_POWER_TYPE = 41
F_MANA_COST = 42          # flat cost; 0 when the spell uses a percentage instead
F_RANGE_IDX = 46
F_NAME = 136              # enUS (locale 0)
F_MANA_PCT = 204          # ManaCostPercentage — % of BASE mana
F_START_RECOVERY_CAT = 205
F_START_RECOVERY = 206    # the GCD, ms. 0 == OFF the global cooldown.

POWER = {0: "Mana", 1: "Rage", 2: "Focus", 3: "Energy", 5: "Runes", 6: "Runic Power"}


class Dbc:
    """A parsed WDBC file: fixed-width integer records plus a shared string block."""

    def __init__(self, path):
        with open(path, "rb") as fh:
            self.raw = fh.read()
        if self.raw[:4] != b"WDBC":
            raise SystemExit("%s is not a WDBC file" % path)
        self.records, self.fields, self.rsize, self.ssize = struct.unpack("<4I", self.raw[4:20])
        self.base = 20
        self.sbase = self.base + self.records * self.rsize
        self.by_id = {}
        for i in range(self.records):
            off = self.base + i * self.rsize
            self.by_id[struct.unpack("<I", self.raw[off:off + 4])[0]] = off

    def row(self, spell_id):
        off = self.by_id.get(spell_id)
        if off is None:
            return None
        return struct.unpack("<%dI" % self.fields, self.raw[off:off + self.fields * 4])

    def rows(self):
        for off in self.by_id.values():
            yield struct.unpack("<%dI" % self.fields, self.raw[off:off + self.fields * 4])

    def string(self, offset):
        if offset == 0 or self.sbase + offset >= len(self.raw):
            return ""
        end = self.raw.index(b"\x00", self.sbase + offset)
        return self.raw[self.sbase + offset:end].decode("utf8", "replace")


def lookup_indexed(dbc, index, field):
    """Read one field out of an index-table DBC (SpellCastTimes / SpellRange / SpellRadius)."""
    row = dbc.row(index) if dbc else None
    return row[field] if row else None


def describe(spell, cast_times, ranges):
    name = spell.string_name
    gcd = spell.row_data[F_START_RECOVERY]
    cd = spell.row_data[F_RECOVERY]
    cat_cd = spell.row_data[F_CATEGORY_RECOVERY]
    pct = spell.row_data[F_MANA_PCT]
    flat = spell.row_data[F_MANA_COST]
    power = POWER.get(spell.row_data[F_POWER_TYPE], "type %d" % spell.row_data[F_POWER_TYPE])

    # SpellCastTimes.CastTime is SIGNED: -1 marks a spell whose duration is not a cast bar at all
    # (channels carry theirs in Duration instead). Reading it unsigned printed "4293967296 ms" for Volley.
    cast_ms = lookup_indexed(cast_times, spell.row_data[F_CAST_TIME_IDX], 1) or 0
    if cast_ms >= 1 << 31:
        cast_ms -= 1 << 32
    max_range = None
    if ranges:
        rrow = ranges.row(spell.row_data[F_RANGE_IDX])
        if rrow:
            max_range = struct.unpack("<f", struct.pack("<I", rrow[3]))[0]

    out = ["%s (id %d)" % (name, spell.row_data[F_ID])]
    out.append("  GCD          : %s" % ("OFF THE GCD  <-- free alongside another cast" if gcd == 0
                                        else "%d ms" % gcd))
    if cd == 0 and cat_cd == 0:
        out.append("  cooldown     : NONE  <-- spammable; it SHADOWS every rung below it")
    else:
        out.append("  cooldown     : %d ms%s" % (cd, "  (category %d ms)" % cat_cd if cat_cd else ""))
    if cast_ms < 0:
        out.append("  cast time    : not a cast bar (channel - see Duration)")
    else:
        out.append("  cast time    : %s" % ("instant" if cast_ms == 0 else "%d ms" % cast_ms))
    if pct:
        out.append("  cost         : %d%% of BASE %s" % (pct, power.lower()))
    elif flat:
        out.append("  cost         : %d %s (flat)" % (flat, power.lower()))
    else:
        out.append("  cost         : free")
    if max_range:
        out.append("  range        : %.0f yd" % max_range)
    return "\n".join(out)


class Spell:
    def __init__(self, row_data, string_name):
        self.row_data = row_data
        self.string_name = string_name


def load(dbc_dir):
    spell_path = os.path.join(dbc_dir, "Spell.dbc")
    if not os.path.exists(spell_path):
        raise SystemExit("no Spell.dbc under %s (pass --dbc)" % dbc_dir)
    spells = Dbc(spell_path)
    cast_times = ranges = None
    for attr, fname in (("cast_times", "SpellCastTimes.dbc"), ("ranges", "SpellRange.dbc")):
        p = os.path.join(dbc_dir, fname)
        if os.path.exists(p):
            if attr == "cast_times":
                cast_times = Dbc(p)
            else:
                ranges = Dbc(p)
    return spells, cast_times, ranges


def selftest(spells):
    """Re-derive the two load-bearing field indices, so a layout change fails loudly."""
    def col(field, expect):
        return all(spells.row(sid) and spells.row(sid)[field] == v for sid, v in expect.items())

    if not col(F_START_RECOVERY, {116: 1500, 49052: 1500, 75: 0, 34490: 0, 3045: 0}):
        raise SystemExit("SELF-TEST FAILED: field %d is not StartRecoveryTime in this DBC" % F_START_RECOVERY)
    if not col(F_MANA_PCT, {49052: 5, 49001: 9, 49067: 19, 58434: 17}):
        raise SystemExit("SELF-TEST FAILED: field %d is not ManaCostPercentage in this DBC" % F_MANA_PCT)


def main():
    ap = argparse.ArgumentParser(description="Query the 3.3.5a Spell.dbc.")
    ap.add_argument("query", help="spell name (substring, case-insensitive) or numeric spell id")
    ap.add_argument("--dbc", default=DEFAULT_DBC, help="directory holding Spell.dbc")
    ap.add_argument("--off-gcd", action="store_true", help="only spells with StartRecoveryTime == 0")
    ap.add_argument("--no-cooldown", action="store_true", help="only spells with no cooldown at all")
    ap.add_argument("--limit", type=int, default=25)
    args = ap.parse_args()

    spells, cast_times, ranges = load(args.dbc)
    selftest(spells)

    matches = []
    if args.query.isdigit():
        row = spells.row(int(args.query))
        if row:
            matches.append(Spell(row, spells.string(row[F_NAME])))
    else:
        needle = args.query.lower()
        for row in spells.rows():
            name = spells.string(row[F_NAME])
            if needle in name.lower():
                matches.append(Spell(row, name))

    if args.off_gcd:
        matches = [m for m in matches if m.row_data[F_START_RECOVERY] == 0]
    if args.no_cooldown:
        matches = [m for m in matches
                   if m.row_data[F_RECOVERY] == 0 and m.row_data[F_CATEGORY_RECOVERY] == 0]

    if not matches:
        print("no match for %r" % args.query)
        return 1

    matches.sort(key=lambda m: m.row_data[F_ID])
    for m in matches[:args.limit]:
        print(describe(m, cast_times, ranges))
        print()
    if len(matches) > args.limit:
        print("... %d more (use --limit)" % (len(matches) - args.limit))
    return 0


if __name__ == "__main__":
    sys.exit(main())
