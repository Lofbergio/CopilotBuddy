#!/usr/bin/env python3
"""Regenerate Spells.bin as SPDB v2: v1 (id/name/rank) + Spell.dbc school/dispel/mechanic.

The client's in-memory Spell row is NOT the flat Spell.dbc record (see WoWSpell.cs "BROKEN"
note), so SchoolMask/Dispel/Mechanic must come from static data. Names/ranks are copied
byte-for-byte from the existing v1 file (they match the live client's locale) — only the
three DBC ints are joined on, from the 3.3.5a Spell.dbc field layout (AzerothCore
DBCStructure.h SpellEntry): [0]=Id, [2]=Dispel, [3]=Mechanic, [225]=SchoolMask.

Usage:
    python gen_spells_bin.py <Spells.bin v1> <Spell.dbc> <out Spells.bin v2>

Validates known values before writing (Flame Shock=fire 0x4, Lightning Bolt=nature 0x8,
Frost Shock=frost 0x10 — confirmed live from combat-log payloads, log 2026-07-02_1601 —
and Deadly Poison dispel=4/poison, the AuraScanDebug reference case).
"""
import struct
import sys

FIELD_DISPEL = 2
FIELD_MECHANIC = 3
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


def read_v1(path):
    spells = []
    with open(path, "rb") as f:
        if f.read(4) != b"SPDB":
            raise SystemExit(f"{path}: not an SPDB file")
        (version,) = struct.unpack("<f", f.read(4))
        if version != 1.0:
            raise SystemExit(f"{path}: expected v1, got {version}")
        (count,) = struct.unpack("<i", f.read(4))
        f.read(4)  # reserved
        for _ in range(count):
            (sid,) = struct.unpack("<i", f.read(4))
            name = read_7bit_string_raw(f)
            rank = read_7bit_string_raw(f)
            spells.append((sid, name, rank))
    return spells


def read_dbc(path):
    """Spell.dbc → {id: (school, dispel, mechanic)}."""
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
            school = struct.unpack_from("<i", rec, FIELD_SCHOOLMASK * 4)[0]
            out[sid] = (school, dispel, mechanic)
    return out


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    v1_path, dbc_path, out_path = sys.argv[1:4]

    spells = read_v1(v1_path)
    dbc = read_dbc(dbc_path)
    print(f"v1: {len(spells)} spells, dbc: {len(dbc)} rows")

    # Sanity gates — abort rather than ship a mis-parsed file.
    checks = {
        8053: ("Flame Shock", 0x4), 943: ("Lightning Bolt", 0x8), 8056: ("Frost Shock", 0x10),
    }
    for sid, (label, want) in checks.items():
        got = dbc.get(sid, (0, 0, 0))[0]
        status = "OK" if got == want else "FAIL"
        print(f"  check {label} ({sid}): school 0x{got:X} (want 0x{want:X}) {status}")
        if got != want:
            raise SystemExit("school sanity check failed — wrong field index or dbc")
    dp = dbc.get(2818, (0, 0, 0))  # the poison AURA (2823 is the weapon-apply spell, dispel 0)
    print(f"  check Deadly Poison dot (2818): dispel {dp[1]} (want 4) {'OK' if dp[1] == 4 else 'FAIL'}")
    if dp[1] != 4:
        raise SystemExit("dispel sanity check failed")

    matched = 0
    with open(out_path, "wb") as f:
        f.write(b"SPDB")
        f.write(struct.pack("<f", 2.0))
        f.write(struct.pack("<i", len(spells)))
        f.write(struct.pack("<i", 0))
        for sid, name, rank in spells:
            school, dispel, mechanic = dbc.get(sid, (0, 0, 0))
            if sid in dbc:
                matched += 1
            f.write(struct.pack("<i", sid))
            write_7bit_string_raw(f, name)
            write_7bit_string_raw(f, rank)
            f.write(struct.pack("<3i", school, dispel, mechanic))
    print(f"wrote {out_path}: {len(spells)} spells, {matched} matched to dbc")


if __name__ == "__main__":
    main()
