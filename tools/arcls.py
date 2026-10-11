"""List the contents of a (Yaz0-compressed) RARC archive from the game files.

    python tools/arcls.py ObjectData/Mario.arc [ObjectData/LuigiNPC.arc ...]
"""

from __future__ import annotations

import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GAME_FILES = ROOT / "third_party/_downloads/smg2_disc/DATA/files"


def yaz0_decompress(src: bytes) -> bytes:
    if src[:4] != b"Yaz0":
        return src
    size = struct.unpack(">I", src[4:8])[0]
    dst = bytearray(size)
    s, d = 16, 0
    while d < size:
        code = src[s]; s += 1
        for bit in range(8):
            if d >= size:
                break
            if code & (0x80 >> bit):
                dst[d] = src[s]; d += 1; s += 1
            else:
                b1, b2 = src[s], src[s + 1]; s += 2
                dist = ((b1 & 0x0F) << 8 | b2) + 1
                n = b1 >> 4
                if n == 0:
                    n = src[s] + 0x12; s += 1
                else:
                    n += 2
                for _ in range(n):
                    dst[d] = dst[d - dist]; d += 1
    return bytes(dst)


def list_rarc(data: bytes) -> list[str]:
    assert data[:4] == b"RARC", "not a RARC archive"
    data_off = struct.unpack(">I", data[0x0C:0x10])[0] + 0x20
    info = 0x20
    node_count, node_off, _, entry_off, _, str_off = struct.unpack(">IIIIII", data[info:info + 24])
    node_off += info; entry_off += info; str_off += info

    def name_at(o: int) -> str:
        end = data.index(b"\0", str_off + o)
        return data[str_off + o:end].decode("shift_jis", "replace")

    out: list[str] = []

    def walk(node: int, prefix: str) -> None:
        base = node_off + node * 16
        _, name_o, _, n_entries, first = struct.unpack(">IIHHI", data[base:base + 16])
        for i in range(n_entries):
            e = entry_off + (first + i) * 20
            _, _, flags_type, name_o2, d_off, d_size = struct.unpack(">HHHHII", data[e:e + 16])
            nm = name_at(name_o2)
            if nm in (".", ".."):
                continue
            if (flags_type >> 8) & 0x02:
                walk(d_off, f"{prefix}{nm}/")
            else:
                out.append(f"{prefix}{nm}  ({d_size} bytes)")

    walk(0, "")
    return out


def main() -> None:
    for rel in sys.argv[1:]:
        path = Path(rel)
        if not path.is_absolute():
            path = GAME_FILES / rel
        print(f"== {rel}")
        for line in list_rarc(yaz0_decompress(path.read_bytes())):
            print("  " + line)


if __name__ == "__main__":
    main()
