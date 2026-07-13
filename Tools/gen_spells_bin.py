#!/usr/bin/env python3
"""Regenerate Spells.bin as SPDB v3: id/name/rank + Spell.dbc school/dispel/mechanic/attributesEx.

The client's in-memory Spell row is NOT the flat Spell.dbc record (see WoWSpell.cs "BROKEN"
note), so SchoolMask/Dispel/Mechanic/AttributesEx must come from static data. Names/ranks are
copied byte-for-byte from the existing v1/v2 file (they match the live client's locale) — the
DBC ints are joined on, from the 3.3.5a Spell.dbc field layout (AzerothCore DBCStructure.h
SpellEntry): [0]=Id, [2]=Dispel, [3]=Mechanic, [5]=AttributesEx, [225]=SchoolMask.

v3 adds AttributesEx so the combo-point remap in SpellManager.Cast can key off the real
ReqTargetComboPoints|ReqComboPoints bits (0x500000) instead of the garbage in-memory value
that hijacked every friendly party buff onto the caster's current target.

Usage:
    python gen_spells_bin.py <Spells.bin v1|v2> <Spell.dbc> <out Spells.bin v3>

Validates known values before writing (Flame Shock=fire 0x4, Lightning Bolt=nature 0x8,
Frost Shock=frost 0x10 — confirmed live from combat-log payloads, log 2026-07-02_1601 —
Deadly Poison dispel=4/poison, and the combo bits: Arcane Intellect attrEx=0x0 vs
Slice and Dice=0x400420 / Savage Roar=0x400400).
"""
import struct
import sys

FIELD_DISPEL = 2
FIELD_MECHANIC = 3
FIELD_ATTRIBUTES_EX = 5
FIELD_SCHOOLMASK = 225


def read_7bit_string_raw(f):
    """C# BinaryReader.ReadString: 7-bit varint byte length + bytes. Returns raw bytes."""
    length = 0
    shift = 0
    while True:
        b = f.read(1)[0]
        length |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    return f.read(length)


def write_7bit_string_raw(f, raw):
    length = len(raw)
    while True:
        b = length & 0x7F
        length >>= 7
        f.write(bytes([b | (0x80 if length else 0)]))
        if not length:
            break
    f.write(raw)


def read_spells(path):
    """Read id/name/rank from an SPDB v1 or v2 file (the extra v2 ints are re-derived here)."""
    spells = []
    with open(path, "rb") as f:
        if f.read(4) != b"SPDB":
            raise SystemExit(f"{path}: not an SPDB file")
        (version,) = struct.unpack("<f", f.read(4))
        if version not in (1.0, 2.0, 3.0):
            raise SystemExit(f"{path}: expected v1/v2/v3, got {version}")
        (count,) = struct.unpack("<i", f.read(4))
        f.read(4)  # reserved
        # Trailing DBC ints per spell that this script re-joins from Spell.dbc: v2 = 3 (school/
        # dispel/mechanic), v3 = 4 (+ attributesEx). Skip them on read — only id/name/rank are kept.
        skip = {1.0: 0, 2.0: 12, 3.0: 16}[version]
        for _ in range(count):
            (sid,) = struct.unpack("<i", f.read(4))
            name = read_7bit_string_raw(f)
            rank = read_7bit_string_raw(f)
            if skip:
                f.read(skip)
            spells.append((sid, name, rank))
    return spells


def read_dbc(path):
    """Spell.dbc → {id: (school, dispel, mechanic, attributesEx)}."""
    with open(path, "rb") as f:
        if f.read(4) != b"WDBC":
            raise SystemExit(f"{path}: not a WDBC file")
        records, fields, record_size, _ = struct.unpack("<4i", f.read(16))
        if fields <= FIELD_SCHOOLMASK:
            raise SystemExit(f"{path}: only {fields} fields — not a 3.3.5a Spell.dbc")
        out = {}
        for _ in range(records):
            rec = f.read(record_size)
            sid = struct.unpack_from("<i", rec, 0)[0]
            dispel = struct.unpack_from("<i", rec, FIELD_DISPEL * 4)[0]
            mechanic = struct.unpack_from("<i", rec, FIELD_MECHANIC * 4)[0]
            attr_ex = struct.unpack_from("<I", rec, FIELD_ATTRIBUTES_EX * 4)[0]
            school = struct.unpack_from("<i", rec, FIELD_SCHOOLMASK * 4)[0]
            out[sid] = (school, dispel, mechanic, attr_ex)
    return out


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    src_path, dbc_path, out_path = sys.argv[1:4]

    spells = read_spells(src_path)
    dbc = read_dbc(dbc_path)
    print(f"src: {len(spells)} spells, dbc: {len(dbc)} rows")

    # Sanity gates — abort rather than ship a mis-parsed file.
    checks = {
        8053: ("Flame Shock", 0x4), 943: ("Lightning Bolt", 0x8), 8056: ("Frost Shock", 0x10),
    }
    for sid, (label, want) in checks.items():
        got = dbc.get(sid, (0, 0, 0, 0))[0]
        status = "OK" if got == want else "FAIL"
        print(f"  check {label} ({sid}): school 0x{got:X} (want 0x{want:X}) {status}")
        if got != want:
            raise SystemExit("school sanity check failed — wrong field index or dbc")
    dp = dbc.get(2818, (0, 0, 0, 0))  # the poison AURA (2823 is the weapon-apply spell, dispel 0)
    print(f"  check Deadly Poison dot (2818): dispel {dp[1]} (want 4) {'OK' if dp[1] == 4 else 'FAIL'}")
    if dp[1] != 4:
        raise SystemExit("dispel sanity check failed")
    # AttributesEx combo bits (0x500000 = ReqTargetComboPoints|ReqComboPoints): finishers set them,
    # buffs must not. This is the exact separation the SpellManager remap relies on.
    attr_checks = {
        1459: ("Arcane Intellect", 0x0), 5171: ("Slice and Dice", 0x400420),
        52610: ("Savage Roar", 0x400400), 2098: ("Eviscerate", 0x100200),
    }
    for sid, (label, want) in attr_checks.items():
        got = dbc.get(sid, (0, 0, 0, 0))[3]
        status = "OK" if got == want else "FAIL"
        print(f"  check {label} ({sid}): attrEx 0x{got:X} (want 0x{want:X}) {status}")
        if got != want:
            raise SystemExit("attributesEx sanity check failed — wrong field index or dbc")

    matched = 0
    with open(out_path, "wb") as f:
        f.write(b"SPDB")
        f.write(struct.pack("<f", 3.0))
        f.write(struct.pack("<i", len(spells)))
        f.write(struct.pack("<i", 0))
        for sid, name, rank in spells:
            school, dispel, mechanic, attr_ex = dbc.get(sid, (0, 0, 0, 0))
            if sid in dbc:
                matched += 1
            f.write(struct.pack("<i", sid))
            write_7bit_string_raw(f, name)
            write_7bit_string_raw(f, rank)
            f.write(struct.pack("<3i", school, dispel, mechanic))
            f.write(struct.pack("<I", attr_ex))
    print(f"wrote {out_path}: {len(spells)} spells, {matched} matched to dbc")


if __name__ == "__main__":
    main()
