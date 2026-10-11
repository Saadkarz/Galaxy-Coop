"""Check every annotated address in the module's Addresses.h against main.dol.

    python tools/verify_addresses.py [path/to/Addresses.h ...]

Exits non-zero if any annotation does not hold, so a wrong hook address is
caught before the module is ever loaded in the game.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from dolinspect import DEFAULT_DOL, DEFAULT_SYMBOLS, Dol, Symbols, branch_target  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_HEADERS = sorted((ROOT / "game" / "smg2" / "modules").glob("*/include/**/Addresses*.h"))
LINE = re.compile(r"#define\s+(\w+)\s+(0x[0-9A-Fa-f]+)\s*/\*\s*@(sym|call|branch)\s+(\S+)\s*\*/")


def main() -> int:
    if not DEFAULT_DOL.exists():
        print(f"main.dol not found at {DEFAULT_DOL}; run tools/setup.ps1 (extracts the disc)")
        return 2
    dol = Dol(DEFAULT_DOL)
    syms = Symbols(DEFAULT_SYMBOLS)
    headers = [Path(p) for p in sys.argv[1:]] or DEFAULT_HEADERS
    failures = checked = 0
    for header in headers:
        for line in header.read_text(encoding="utf-8").splitlines():
            m = LINE.search(line)
            if not m:
                continue
            name, value, kind, target = m.group(1), int(m.group(2), 16), m.group(3), m.group(4)
            checked += 1
            expected = syms.by_name.get(target)
            if expected is None:
                print(f"FAIL {name}: symbol {target} not in symbol map")
                failures += 1
                continue
            if kind == "sym":
                ok = value == expected
                detail = f"symbol is at 0x{expected:08X}"
            else:
                word = dol.u32(value)
                bt = branch_target(value, word) if word is not None else None
                want = "bl" if kind == "call" else "b"
                ok = bt is not None and bt[0] == want and bt[1] == expected
                detail = f"found {bt[0]} 0x{bt[1]:08X}" if bt else f"found 0x{word or 0:08X}"
            print(f"{'ok  ' if ok else 'FAIL'} {name} = 0x{value:08X} ({kind} {target}){'' if ok else ': ' + detail}")
            failures += 0 if ok else 1
    print(f"{checked - failures}/{checked} addresses verified")
    return 1 if failures or not checked else 0


if __name__ == "__main__":
    sys.exit(main())
