#!/usr/bin/env python
"""
Disassemble a virtual address in the 3.3.5a client, to VERIFY a hardcoded pointer.

Written 2026-07-21 after Spell_C__HandleTerrainClick was found to be wrong in two ways at once
(bad address + reversed argument struct) having shipped unnoticed because nothing ever called it.
See the root CLAUDE.md, "audit the hardcoded 3.3.5a pointers".

Requires: pip install capstone   (already present on this machine)

USAGE
    python disasm_wow.py 0x80C340                 # disassemble a VA
    python disasm_wow.py 0x80C340 --count 40      # more instructions
    python disasm_wow.py 0x80C340 --exe "E:/.../Wow.exe"
    python disasm_wow.py --find-func 0x80B740     # which function does this land inside?

WHAT TO LOOK FOR
  * A VALID function address starts with a prologue: `push ebp; mov ebp, esp`, or a `sub esp, N`
    frame. If the first instructions are nonsense (`inc esi`, `and al, 0`, ...) the address is
    MID-FUNCTION and calling it will access-violate.
  * The `ret` gives the calling convention: plain `ret` = cdecl (caller cleans, e.g. `add esp, 4`);
    `ret N` = stdcall (callee cleans N bytes).
  * Arguments are `[ebp+8]`, `[ebp+0xC]`, ... Follow what the function READS out of an argument
    pointer to recover the true struct layout instead of guessing field order. Contiguous
    destination offsets imply contiguous source fields (that is how the GUID-first layout of
    Spell_C__HandleTerrainClick was established).

WHICH BINARY
  The Wow.exe copies on this machine differ by MD5. Disassemble the one the BOT attaches to
  (Clean 3.3.5a by default here) -- offsets are per-binary.
"""
import argparse
import struct
import sys

DEFAULT_EXE = r"E:\!Games\World of Warcraft\Clean 3.3.5a\Wow.exe"


class PE:
    def __init__(self, path):
        self.data = open(path, "rb").read()
        pe = struct.unpack_from("<I", self.data, 0x3C)[0]
        if self.data[pe:pe + 4] != b"PE\0\0":
            raise SystemExit("not a PE file: %s" % path)
        nsec = struct.unpack_from("<H", self.data, pe + 6)[0]
        optsz = struct.unpack_from("<H", self.data, pe + 20)[0]
        self.imagebase = struct.unpack_from("<I", self.data, pe + 24 + 28)[0]
        self.sections = []
        for i in range(nsec):
            o = pe + 24 + optsz + i * 40
            name = self.data[o:o + 8].rstrip(b"\0").decode("latin1")
            vsz, va, rsz, raw = struct.unpack_from("<IIII", self.data, o + 8)
            self.sections.append((name, va, vsz, raw, rsz))

    def va2off(self, va):
        rva = va - self.imagebase
        for _, sva, vsz, raw, rsz in self.sections:
            if sva <= rva < sva + max(vsz, rsz):
                return raw + (rva - sva)
        return None

    def off2va(self, off):
        for _, sva, vsz, raw, rsz in self.sections:
            if raw <= off < raw + rsz:
                return self.imagebase + sva + (off - raw)
        return None


def disasm(pe, va, count):
    import capstone
    off = pe.va2off(va)
    if off is None:
        raise SystemExit("VA 0x%X is not mapped in this binary" % va)
    print("VA 0x%X -> file offset 0x%X  (imagebase 0x%X)" % (va, off, pe.imagebase))
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    n = 0
    for ins in md.disasm(pe.data[off:off + count * 16], va):
        print("  0x%08X  %-20s %s %s" % (ins.address, ins.bytes.hex(), ins.mnemonic, ins.op_str))
        n += 1
        if n >= count:
            break
    if n and pe.data[off] == 0x55 and pe.data[off + 1:off + 3] == b"\x8b\xec":
        print("\n  -> starts with push ebp / mov ebp, esp: looks like a REAL function entry.")
    else:
        print("\n  -> NO standard prologue here. Suspect the address is mid-function (see --find-func).")


def find_func(pe, va):
    """Walk back to the nearest plausible prologue and show it."""
    import capstone
    off = pe.va2off(va)
    if off is None:
        raise SystemExit("VA 0x%X is not mapped" % va)
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    print("looking backwards from 0x%X for a function entry..." % va)
    for back in range(4, 8192):
        o = off - back
        if pe.data[o:o + 3] != b"\x55\x8b\xec":
            continue
        start = pe.off2va(o)
        # does linear disassembly from here land exactly on the target boundary?
        for ins in md.disasm(pe.data[o:o + back + 32], start):
            if ins.address == va:
                print("  0x%08X starts a function whose instruction stream covers 0x%X" % (start, va))
                return
            if ins.address > va:
                break
        print("  0x%08X is a nearby entry but does not linearly cover it" % start)
    print("  no covering prologue found within 8KB -- the address may not be code at all.")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("va", nargs="?", help="virtual address, e.g. 0x80C340")
    ap.add_argument("--exe", default=DEFAULT_EXE)
    ap.add_argument("--count", type=int, default=25)
    ap.add_argument("--find-func", dest="findfunc", help="which function contains this VA?")
    a = ap.parse_args()

    pe = PE(a.exe)
    print("binary: %s\n" % a.exe)
    if a.findfunc:
        find_func(pe, int(a.findfunc, 0))
    elif a.va:
        disasm(pe, int(a.va, 0), a.count)
    else:
        ap.print_help()
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
