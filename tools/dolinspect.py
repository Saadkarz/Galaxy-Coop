"""Offline inspection of a GameCube/Wii main.dol against a Syati symbol map.

Used to verify hook addresses (vtable slots, call sites) before they go into
the module, because the module cannot be tested without running the game.

    python tools/dolinspect.py vtable __vt__10MarioActor 40
    python tools/dolinspect.py callers movement__10MarioActorFv
    python tools/dolinspect.py dis 0x80380870 32
    python tools/dolinspect.py sym 0x80380870
    python tools/dolinspect.py find GhostPacketSyncData
"""

from __future__ import annotations

import argparse
import bisect
import os
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DOL = ROOT / "third_party/_downloads/smg2_disc/DATA/sys/main.dol"
DEFAULT_SYMBOLS = ROOT / "third_party/Syati/symbols/SB4E.txt"


class Dol:
    def __init__(self, path: Path):
        data = path.read_bytes()
        self.data = data
        self.sections: list[tuple[int, int, int, bool]] = []  # (addr, size, file_off, is_text)
        offs = struct.unpack(">18I", data[0x00:0x48])
        addrs = struct.unpack(">18I", data[0x48:0x90])
        sizes = struct.unpack(">18I", data[0x90:0xD8])
        for i in range(18):
            if sizes[i]:
                self.sections.append((addrs[i], sizes[i], offs[i], i < 7))
        self.bss_addr, self.bss_size, self.entry = struct.unpack(">3I", data[0xD8:0xE4])

    def _locate(self, addr: int) -> int | None:
        for a, s, o, _ in self.sections:
            if a <= addr < a + s:
                return o + (addr - a)
        return None

    def u32(self, addr: int) -> int | None:
        off = self._locate(addr)
        if off is None:
            return None
        return struct.unpack(">I", self.data[off:off + 4])[0]

    def text_words(self):
        for a, s, o, is_text in self.sections:
            if not is_text:
                continue
            for i in range(0, s, 4):
                yield a + i, struct.unpack(">I", self.data[o + i:o + i + 4])[0]


class Symbols:
    def __init__(self, path: Path):
        self.by_name: dict[str, int] = {}
        pairs = []
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            if "=" not in line:
                continue
            name, _, addr = line.rpartition("=")
            try:
                a = int(addr, 16)
            except ValueError:
                continue
            self.by_name[name] = a
            pairs.append((a, name))
        pairs.sort()
        self.addrs = [p[0] for p in pairs]
        self.names = [p[1] for p in pairs]

    def lookup(self, addr: int) -> str:
        i = bisect.bisect_right(self.addrs, addr) - 1
        if i < 0:
            return "?"
        off = addr - self.addrs[i]
        return self.names[i] if off == 0 else f"{self.names[i]}+0x{off:X}"

    def resolve(self, s: str) -> int:
        if s.lower().startswith("0x"):
            return int(s, 16)
        if s in self.by_name:
            return self.by_name[s]
        raise SystemExit(f"unknown symbol: {s}")


def branch_target(addr: int, word: int) -> tuple[str, int] | None:
    op = word >> 26
    if op == 18:
        li = word & 0x03FFFFFC
        if li & 0x02000000:
            li -= 0x04000000
        target = li if word & 2 else addr + li
        return ("bl" if word & 1 else "b"), target & 0xFFFFFFFF
    return None


def disasm(addr: int, w: int, syms: Symbols) -> str:
    bt = branch_target(addr, w)
    if bt:
        return f"{bt[0]} 0x{bt[1]:08X}  <{syms.lookup(bt[1])}>"
    op = w >> 26
    rd, ra, imm = (w >> 21) & 31, (w >> 16) & 31, w & 0xFFFF
    simm = imm - 0x10000 if imm & 0x8000 else imm
    names = {14: "addi", 15: "addis", 32: "lwz", 36: "stw", 48: "lfs", 52: "stfs",
             34: "lbz", 38: "stb", 40: "lhz", 44: "sth", 37: "stwu", 33: "lwzu", 24: "ori", 11: "cmpwi", 10: "cmplwi"}
    if w == 0x4E800020:
        return "blr"
    if w == 0x4E800421:
        return "bctrl"
    if w == 0x60000000:
        return "nop"
    if op == 31 and ((w >> 1) & 0x3FF) == 444 and rd == (w >> 11) & 31:
        return f"mr r{ra}, r{rd}"
    if op == 31 and ((w >> 1) & 0x3FF) == 339:
        return f"mfspr r{rd}, {((w >> 16) & 31) | (((w >> 11) & 31) << 5)}"
    if op == 31 and ((w >> 1) & 0x3FF) == 467:
        return f"mtspr {((w >> 16) & 31) | (((w >> 11) & 31) << 5)}, r{rd}"
    if op in names:
        if op in (14, 15) and ra == 0:
            return f"{'li' if op == 14 else 'lis'} r{rd}, 0x{imm:X}"
        if op in (24,):
            return f"ori r{ra}, r{rd}, 0x{imm:X}"
        if op in (10, 11):
            return f"{names[op]} cr{rd >> 2}, r{ra}, {simm}"
        return f"{names[op]} r{rd}, {simm}(r{ra})" if op not in (14, 15) else f"{names[op]} r{rd}, r{ra}, {simm}"
    return f".word 0x{w:08X}"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dol", default=str(DEFAULT_DOL))
    ap.add_argument("--symbols", default=str(DEFAULT_SYMBOLS))
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("vtable"); p.add_argument("name"); p.add_argument("count", type=int, nargs="?", default=32)
    p = sub.add_parser("callers"); p.add_argument("name")
    p = sub.add_parser("dis"); p.add_argument("addr"); p.add_argument("count", type=int, nargs="?", default=24)
    p = sub.add_parser("sym"); p.add_argument("addr")
    p = sub.add_parser("find"); p.add_argument("text")
    p = sub.add_parser("word"); p.add_argument("addr")
    args = ap.parse_args()

    dol = Dol(Path(args.dol))
    syms = Symbols(Path(args.symbols))

    if args.cmd == "vtable":
        base = syms.resolve(args.name)
        for i in range(args.count):
            w = dol.u32(base + i * 4)
            if w is None:
                break
            print(f"+0x{i * 4:03X}  0x{w:08X}  {syms.lookup(w) if w else '-'}")
    elif args.cmd == "callers":
        target = syms.resolve(args.name)
        for addr, w in dol.text_words():
            bt = branch_target(addr, w)
            if bt and bt[1] == target:
                print(f"0x{addr:08X}  {bt[0]:2}  in {syms.lookup(addr)}")
    elif args.cmd == "dis":
        a = syms.resolve(args.addr)
        for i in range(args.count):
            w = dol.u32(a + i * 4)
            if w is None:
                break
            print(f"0x{a + i * 4:08X}  {w:08X}  {disasm(a + i * 4, w, syms)}")
    elif args.cmd == "sym":
        print(syms.lookup(syms.resolve(args.addr)))
    elif args.cmd == "word":
        a = syms.resolve(args.addr)
        print(f"0x{dol.u32(a):08X}")
    elif args.cmd == "find":
        for name, a in syms.by_name.items():
            if args.text in name:
                print(f"0x{a:08X}  {name}")


if __name__ == "__main__":
    main()
