#!/usr/bin/env python
"""
Audit EVERY hardcoded 3.3.5a pointer in the source against the client binary.

Companion to disasm_wow.py (which inspects ONE address). This sweeps the whole tree and
reports the three failure modes that have actually shipped bugs here:

  1. MISMATCH  - a line pairs a decimal and a hex that are not the same number. Both live bugs
                 found on 2026-07-21 (Questing.GetQuestIDByIndex, LootTargeting.LootFrameIsOpen)
                 were created by someone copying the hex out of a comment whose hex was rotten.
  2. CODE      - a .text address that has no function prologue AND no `call` site in the binary
                 targeting it. That combination means the address lands mid-function; calling it
                 access-violates. (Spell_C__HandleTerrainClick, 2026-07-21.)
  3. DATA      - a .data/.rdata address that no instruction in .text references, and whose whole
                 +/-0x60 neighbourhood is equally unreferenced. Not proof of an error (statics
                 reached via base+register never show a direct xref) but it is the triage signal:
                 a correct static usually sits in a densely-referenced region.

A `call` count is the strongest evidence a code address is a real entry point -- N call sites
targeting it exactly cannot be coincidence, even when the function has no push-ebp prologue.

USAGE
    python audit_pointers.py                    # full report
    python audit_pointers.py --only mismatch    # one section
    python audit_pointers.py --exe "E:/.../Wow.exe" --src C:/git/CopilotBuddy

Offsets are per-binary: point --exe at the client the bot attaches to (see disasm_wow.py).
"""
import argparse
import collections
import io
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from disasm_wow import PE, DEFAULT_EXE

DEFAULT_SRC = r"C:\git\CopilotBuddy"
SKIP_DIRS = (os.sep + ".git", os.sep + "bin", os.sep + "obj", os.sep + "output", os.sep + "Tools")
VA_LO, VA_HI = 0x400000, 0xF00000


def source_files(src):
    for root, _dirs, files in os.walk(src):
        if any(s in root for s in SKIP_DIRS):
            continue
        for fn in files:
            if fn.endswith(".cs"):
                full = os.path.join(root, fn)
                yield os.path.relpath(full, src), io.open(full, encoding="utf-8-sig", errors="replace").read()


class Binary:
    def __init__(self, exe):
        self.pe = PE(exe)
        text = next(s for s in self.pe.sections if s[0] == ".text")
        _, self.tsva, _, traw, trsz = text
        self.text = self.pe.data[traw:traw + trsz]
        self.imm = collections.Counter()
        self.call = collections.Counter()
        base = self.pe.imagebase
        for i in range(len(self.text) - 4):
            v = struct.unpack_from("<I", self.text, i)[0]
            if base <= v < base + 0x1000000:
                self.imm[v] += 1
        i = 0
        while i < len(self.text) - 5:
            if self.text[i] in (0xE8, 0xE9):
                rel = struct.unpack_from("<i", self.text, i + 1)[0]
                tgt = (base + self.tsva + i + 5 + rel) & 0xFFFFFFFF
                if base <= tgt < base + 0x1000000:
                    self.call[tgt] += 1
            i += 1

    def section_of(self, va):
        rva = va - self.pe.imagebase
        for name, sva, vsz, raw, rsz in self.pe.sections:
            if sva <= rva < sva + max(vsz, rsz):
                return name
        return None

    def has_prologue(self, va):
        off = self.pe.va2off(va)
        if off is None:
            return False
        d = self.pe.data
        return (d[off] == 0x55 and d[off + 1:off + 3] == b"\x8b\xec") or \
               (d[off] in (0x83, 0x81) and d[off + 1] == 0xEC)

    def neighbourhood(self, va):
        return sum(1 for a in range(va - 0x60, va + 0x64, 4) if self.imm.get(a))


def scan_mismatches(src):
    # The [uU]? suffix is load-bearing: `12559336U` has no word boundary before the U, so a plain
    # \b-terminated pattern silently skips every C# unsigned literal -- which hid the whole
    # MerchantFrame block from the first sweep.
    dec = re.compile(r"\b(\d{6,9})[uU]?\b")
    hexp = re.compile(r"0x([0-9A-Fa-f]{5,8})\b")
    out = []
    for rel, txt in source_files(src):
        for n, line in enumerate(txt.splitlines(), 1):
            ds = [d for d in (int(x) for x in dec.findall(line)) if VA_LO <= d < VA_HI]
            hs = [h for h in (int(x, 16) for x in hexp.findall(line)) if VA_LO <= h < VA_HI]
            if ds and hs and not (set(ds) & set(hs)):
                out.append((rel, n, line.strip()[:130], ds, hs))
    return out


# A literal in client range is only an ADDRESS if its line says so. Without this the report
# drowns in bitmask enums (NpcFlags 0x800000), hash primes and struct sizes.
ADDRESSY = re.compile(r"call|address|offset|\bPtr\b|Ptr\s*=|Base\b|GlobalOffsets|Read<|__", re.I)
FLAGGY = re.compile(r"\bflags?\b|\benum\b", re.I)


def is_address_context(line, va):
    if FLAGGY.search(line) and bin(va).count("1") <= 2:
        return False
    if bin(va).count("1") <= 2:  # 0x400000, 0x800000, 0x500000 ... bitmasks, never real VAs here
        return False
    return bool(ADDRESSY.search(line))


def scan_addresses(src):
    """Every client-range literal used in an address-like context, with its site."""
    pat = re.compile(r"0x0*([4-9A-Fa-f][0-9A-Fa-f]{5})\b|\b(\d{7,8})[uU]?\b")
    sites = collections.defaultdict(list)
    for rel, txt in source_files(src):
        for n, line in enumerate(txt.splitlines(), 1):
            for m in pat.finditer(line):
                va = int(m.group(1), 16) if m.group(1) else int(m.group(2))
                if VA_LO <= va < VA_HI and is_address_context(line, va):
                    sites[va].append((rel, n, line.strip()[:110]))
    return sites


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default=DEFAULT_EXE)
    ap.add_argument("--src", default=DEFAULT_SRC)
    ap.add_argument("--only", choices=["mismatch", "code", "data"])
    a = ap.parse_args()

    print("binary: %s\nsource: %s\n" % (a.exe, a.src))
    b = Binary(a.exe)
    sites = scan_addresses(a.src)
    bad = 0

    if a.only in (None, "mismatch"):
        print("=" * 78)
        print("1. DECIMAL/HEX MISMATCHES  (a wrong comment is how both 2026-07-21 bugs were born)")
        print("=" * 78)
        ms = scan_mismatches(a.src)
        for rel, n, line, ds, hs in ms:
            print("  %s:%d\n      %s\n      decimal %s = %s   vs   hex %s" %
                  (rel, n, line, ds, [hex(d) for d in ds], [hex(h) for h in hs]))
        print("  -> %d line(s). Verify each: which side has xrefs/a prologue?\n" % len(ms))
        bad += len(ms)

    if a.only in (None, "code"):
        print("=" * 78)
        print("2. CODE ADDRESSES WITH NO PROLOGUE AND NO CALL SITE  (lands mid-function)")
        print("=" * 78)
        n_bad = 0
        for va, where in sorted(sites.items()):
            if b.section_of(va) != ".text":
                continue
            if b.call.get(va) or b.has_prologue(va):
                continue
            n_bad += 1
            print("  0x%X  calls=0  no prologue" % va)
            for rel, ln, src in where[:3]:
                print("      %s:%d  %s" % (rel, ln, src))
        print("  -> %d suspect code address(es).\n" % n_bad)
        bad += n_bad

    if a.only in (None, "data"):
        print("=" * 78)
        print("3. DATA ADDRESSES IN AN UNREFERENCED REGION  (triage signal, not proof)")
        print("=" * 78)
        n_bad = 0
        for va, where in sorted(sites.items()):
            if b.section_of(va) not in (".data", ".rdata"):
                continue
            if b.imm.get(va) or b.neighbourhood(va) >= 4:
                continue
            n_bad += 1
            print("  0x%X  direct=0  neighbours=%d" % (va, b.neighbourhood(va)))
            for rel, ln, src in where[:2]:
                print("      %s:%d  %s" % (rel, ln, src))
        print("  -> %d address(es) to confirm live in-game.\n" % n_bad)

    print("MISMATCH + CODE findings: %d" % bad)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
