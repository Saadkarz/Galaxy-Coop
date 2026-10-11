"""Build the Galaxy Co-op SMG2 module and Syati loader, and deploy them to Dolphin.

    python tools/build_smg2.py                 # build + deploy to the default Dolphin user dir
    python tools/build_smg2.py --no-deploy
    python tools/build_smg2.py --deploy-to "D:/DolphinUsers/p2"   # extra user dirs (repeatable)
    python tools/build_smg2.py --debug          # defines GCOOP_DEBUG (more OSReport output)

Output goes to build/smg2/. The Riivolution patch is GalaxyCoop.xml plus the
GalaxyCoop/CustomCode folder, which Dolphin loads from <user>/Load/Riivolution/.

The free CodeWarrior Special Edition is not licensed for '-proc gekko', so when
the compiler refuses Gekko this script compiles with '-proc generic' against a
copy of the Syati headers in which the paired-single inline assembly is
rewritten as plain C (see HEADER_PATCHES). If a Gekko-capable mwcceppc.exe is
dropped into third_party/Syati/deps/CodeWarrior the original headers are used.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SYATI = ROOT / "third_party" / "Syati"
CW_DIR = SYATI / "deps" / "CodeWarrior"
MWCC = CW_DIR / "mwcceppc.exe"
KAMEK = SYATI / "deps" / "Kamek" / "Kamek.exe"
MODULES = ROOT / "game" / "smg2" / "modules"
SHARED = ROOT / "shared"
BUILD = ROOT / "build" / "smg2"
REGION = "SB4E"
LOADER_ADDR = "0x80001800"

BASE_FLAGS = [
    "-c", "-Cpp_exceptions", "off", "-nodefaults", "-fp", "hard", "-lang=c++",
    "-O4,s", "-inline", "on", "-rtti", "off", "-sdata", "0", "-sdata2", "0",
    "-align", "powerpc", "-func_align", "4", "-enum", "int", "-str", "pool",
    "-DGEKKO", "-DSB4", f"-D{REGION}", "-DUSA",
]


# (relative header path, original text, replacement). Each original must match
# exactly once, otherwise the build stops so a Syati update cannot silently
# leave paired-single code in place.
HEADER_PATCHES: list[tuple[str, str, str]] = [
    ("JSystem/JMath.h",
     """    #ifdef __MWERKS__
    __attribute__((always_inline))
    inline f32 PSVECSquareDistance(const register Vec* a, const register Vec* b)
    {
        
        register f32 dyz, dxy, sqdist;
        register f32 v0xy, v1yz, v0yz, v1xy;
        
        asm
        {
            psq_l    v0yz, 4(a), 0, 0
            psq_l    v1yz, 4(b), 0, 0
            ps_sub   dyz, v0yz, v1yz
            
            psq_l    v0xy, 0(a), 0, 0
            psq_l    v1xy, 0(b), 0, 0
            ps_mul   dyz, dyz, dyz
            ps_sub   dxy, v0xy, v1xy
            
            ps_madd  sqdist, dxy, dxy, dyz
            ps_sum0  sqdist, sqdist, dyz, dyz
        }
        
        return sqdist;
    }
    
    __attribute__((always_inline))
    inline void PSVECAdd2(register const Vec* vec1, register const Vec* vec2, register Vec* dst) {
        register f32 v2z, d1z, v1z, v2xy, v1xy, d1xy;
        
        __asm {            
            psq_l     v1xy, 0(vec1), 0, 0
            psq_l     v2xy, 0(vec2), 0, 0
            ps_add    d1xy, v1xy, v2xy
            psq_l     v1z,   8(vec1), 1, 0
            psq_l     v2z,   8(vec2), 1, 0
            ps_add    d1z, v1z, v2z
            psq_st    d1xy, 0(dst), 0, 0
            psq_st    d1z,  8(dst), 1, 0
        }
    }
    #else
    f32 PSVECSquareDistance(const register Vec* a, const register Vec* b);
    void PSVECAdd2(register const Vec* vec1, register const Vec* vec2, register Vec* dst);
    #endif""",
     """    inline f32 PSVECSquareDistance(const Vec* a, const Vec* b) {
        f32 dx = a->x - b->x, dy = a->y - b->y, dz = a->z - b->z;
        return dx * dx + dy * dy + dz * dz;
    }

    inline void PSVECAdd2(const Vec* vec1, const Vec* vec2, Vec* dst) {
        dst->x = vec1->x + vec2->x;
        dst->y = vec1->y + vec2->y;
        dst->z = vec1->z + vec2->z;
    }"""),
    ("JSystem/JGeometry/TVec.h",
     """            #ifdef __MWERKS__
            register const JGeometry::TVec3<f32>* this_vec = this;
            register f32 _xy, _z;

            __asm {
                psq_l _xy, 0(this_vec), 0, 0
                lfs _z, 8(this_vec)
                ps_mul _xy, _xy, _xy
                ps_madd _z, _z, _z, _xy
                ps_sum0 _z, _z, _xy, _xy
            };
            return _z;
            #endif""",
     """            return this->x * this->x + this->y * this->y + this->z * this->z;"""),
    ("JSystem/JGeometry/TMatrix.h",
     """#ifdef __MWERKS__
            register const MtxPtr pSrc = rSrc;
            register SMatrix34C< T >* pDest = this;
            register f32 rzztz;
            register f32 rxzyz;
            register f32 rzyty;
            register f32 rxyyy;
            register f32 rzxtx;
            register f32 rxxyx;

            __asm {
                psq_l     rxxyx, 0(pSrc), 0, 0
                psq_l     rzxtx, 8(pSrc), 0, 0
                psq_l     rxyyy, 0x10(pSrc), 0, 0
                psq_l     rzyty, 0x18(pSrc), 0, 0
                psq_l     rxzyz, 0x20(pSrc), 0, 0
                psq_l     rzztz, 0x28(pSrc), 0, 0
                psq_st    rxxyx, 0(pDest), 0, 0
                psq_st    rzxtx, 8(pDest), 0, 0
                psq_st    rxyyy, 0x10(pDest), 0, 0
                psq_st    rzyty, 0x18(pDest), 0, 0
                psq_st    rxzyz, 0x20(pDest), 0, 0
                psq_st    rzztz, 0x28(pDest), 0, 0
            }
            ;
#endif""",
     """            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 4; c++)
                    this->mMtx[r][c] = rSrc[r][c];"""),
    ("revolution/os/OSFastCast.h",
     """#define OSf32tou16(in,out)  asm volatile ("psq_st   %1, 0(%0), 1, 3 " : : "b" (out) , "f" (*(in)) : "memory")
#define OSu16tof32(in,out)  asm volatile ("psq_l   %0, 0(%1), 1, 3  " : "=f" (*(out)) : "b" (in) )""",
     """#define OSf32tou16(in,out)  (*(out) = (u16)(*(in)))
#define OSu16tof32(in,out)  (*(out) = (f32)(*(in)))"""),
    ("revolution/os/OSFastCast.h",
     """static inline void OSInitFastCast(void)
{
    #ifdef __MWERKS__""",
     """static inline void OSInitFastCast(void)
{
    #if 0"""),
    ("nw4r/math/arithmetic.h",
     """            asm { fsel   ret, cond, ifPos, ifNeg }""",
     """            ret = (cond >= 0.0f) ? ifPos : ifNeg;"""),
    ("Game/Util/MathUtil.h",
     """            __asm { frsqrte recip, x }""",
     """            { union { f32 f; u32 i; } est; est.f = x; est.i = 0x5F3759DF - (est.i >> 1); recip = est.f; recip = recip * (1.5f - 0.5f * x * recip * recip); }"""),
]


def log(msg: str) -> None:
    print(f"[build_smg2] {msg}", flush=True)


def run(cmd: list[str], cwd: Path | None = None) -> None:
    result = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, errors="replace")
    out = (result.stdout + result.stderr).strip()
    if result.returncode != 0:
        print(out)
        raise SystemExit(f"command failed ({result.returncode}): {Path(cmd[0]).name}")
    if out:
        print(out)


def compiler_supports_gekko() -> bool:
    probe_dir = BUILD / "probe"
    probe_dir.mkdir(parents=True, exist_ok=True)
    src = probe_dir / "probe.cpp"
    src.write_text("int gcoop_probe(int x) { return x + 1; }\n")
    result = subprocess.run([str(MWCC), "-c", "-proc", "gekko", str(src), "-o", str(probe_dir / "probe.o")],
                            capture_output=True, text=True, errors="replace")
    return result.returncode == 0


def make_generic_headers() -> Path:
    src_root = SYATI / "include"
    dst_root = BUILD / "syati_include_generic"
    if dst_root.exists():
        shutil.rmtree(dst_root)
    shutil.copytree(src_root, dst_root)
    for rel, old, new in HEADER_PATCHES:
        path = dst_root / rel
        text = path.read_text(encoding="utf-8").replace("\r\n", "\n")
        count = text.count(old)
        if count != 1:
            raise SystemExit(f"header patch for {rel} matched {count} times; Syati changed, update HEADER_PATCHES")
        path.write_text(text.replace(old, new), encoding="utf-8", newline="\n")
    return dst_root


def compile_one(src: Path, obj: Path, proc: str, includes: list[Path], extra: list[str]) -> None:
    obj.parent.mkdir(parents=True, exist_ok=True)
    cmd = [str(MWCC), *BASE_FLAGS, "-proc", proc, *extra, "-I-"]
    for inc in includes:
        cmd += ["-i", str(inc)]
    cmd += [str(src), "-o", str(obj)]
    log(f"compile {src.relative_to(ROOT)}")
    run(cmd)


def build_loader(proc: str, include_root: Path) -> Path:
    out = BUILD / "loader"
    out.mkdir(parents=True, exist_ok=True)
    obj = out / "loader.o"
    compile_one(SYATI / "loader" / "loader.cpp", obj, proc, [include_root, SYATI / "loader"], [])
    riiv = out / f"riivo_{REGION}.xml"
    run([str(KAMEK), str(obj), f"-static={LOADER_ADDR}", f"-externals={SYATI / 'symbols' / f'{REGION}.txt'}",
         f"-output-riiv={riiv}", f"-output-code={out / f'Loader{REGION}.bin'}",
         f"-output-dolphin={out / f'Dolphin{REGION}.ini'}"])
    return riiv


def build_modules(proc: str, include_root: Path, debug: bool) -> Path:
    objs: list[Path] = []
    extra = ["-DGCOOP_DEBUG"] if debug else []
    symbol_files = [SYATI / "symbols" / f"{REGION}.txt"]
    for module in sorted(p for p in MODULES.iterdir() if p.is_dir()):
        if not (module / "ModuleInfo.json").exists():
            continue
        includes = [include_root, module / "include", SHARED]
        for src in sorted((module / "source").rglob("*.cpp")):
            obj = BUILD / "obj" / module.name / src.relative_to(module / "source").with_suffix(".o")
            compile_one(src, obj, proc, includes, extra)
            objs.append(obj)
        sym = module / "symbols" / f"{REGION}.txt"
        if sym.exists():
            symbol_files.append(sym)
    if not objs:
        raise SystemExit("no module sources found")
    out = BUILD / "CustomCode"
    out.mkdir(parents=True, exist_ok=True)
    binary = out / f"CustomCode_{REGION}.bin"
    cmd = [str(KAMEK), *map(str, objs)]
    cmd += [f"-externals={s}" for s in symbol_files]
    cmd += [f"-output-kamek={binary}", f"-output-map={BUILD / f'CustomCode_{REGION}.map'}"]
    log("link CustomCode")
    run(cmd)
    log(f"CustomCode size: {binary.stat().st_size} bytes")
    return binary


def write_riivolution_xml(loader_riiv: Path) -> Path:
    loader_patches = loader_riiv.read_text(encoding="utf-8").strip()
    xml = f"""<wiidisc version="1">
\t<id game="{REGION}" />
\t<options>
\t\t<section name="Galaxy Co-op">
\t\t\t<option name="Galaxy Co-op" default="1">
\t\t\t\t<choice name="Enabled">
\t\t\t\t\t<patch id="gcoop" />
\t\t\t\t</choice>
\t\t\t</option>
\t\t</section>
\t</options>
\t<patch id="gcoop" root="/GalaxyCoop">
\t\t<folder external="CustomCode" disc="/CustomCode" create="true" />
\t\t{loader_patches}
\t</patch>
</wiidisc>
"""
    path = BUILD / "GalaxyCoop.xml"
    path.write_text(xml, encoding="utf-8")
    return path


def default_dolphin_user_dir() -> Path | None:
    exe_dir = Path(os.environ.get("GCOOP_DOLPHIN_DIR", r"D:\Games\Dolphin-x64"))
    if (exe_dir / "portable.txt").exists():
        return exe_dir / "User"
    appdata = os.environ.get("APPDATA")
    if appdata and (Path(appdata) / "Dolphin Emulator").exists():
        return Path(appdata) / "Dolphin Emulator"
    return None


def deploy(user_dir: Path, xml: Path, binary: Path) -> None:
    riiv_root = user_dir / "Load" / "Riivolution"
    (riiv_root / "riivolution").mkdir(parents=True, exist_ok=True)
    code_dir = riiv_root / "GalaxyCoop" / "CustomCode"
    code_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(xml, riiv_root / "riivolution" / "GalaxyCoop.xml")
    shutil.copy2(binary, code_dir / binary.name)
    log(f"deployed to {riiv_root}")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-deploy", action="store_true")
    ap.add_argument("--deploy-to", action="append", default=[], help="additional Dolphin user directories")
    ap.add_argument("--debug", action="store_true")
    ap.add_argument("--deploy-only", action="store_true", help="install the last build without compiling")
    args = ap.parse_args()

    if args.deploy_only:
        xml = BUILD / "GalaxyCoop.xml"
        binary = BUILD / "CustomCode" / "CustomCode_SB4E.bin"
        if not xml.exists() or not binary.exists():
            raise SystemExit("no previous build in build/smg2; build once first")
    else:
        for tool in (MWCC, KAMEK):
            if not tool.exists():
                raise SystemExit(f"missing {tool}; run tools/setup.ps1 first")

        BUILD.mkdir(parents=True, exist_ok=True)
        if compiler_supports_gekko():
            proc, include_root = "gekko", SYATI / "include"
            log("compiler supports Gekko; using original Syati headers")
        else:
            proc, include_root = "generic", make_generic_headers()
            log("compiler is not licensed for Gekko; using -proc generic with patched headers")

        loader_riiv = build_loader(proc, include_root)
        binary = build_modules(proc, include_root, args.debug)
        xml = write_riivolution_xml(loader_riiv)
        log(f"built {xml.relative_to(ROOT)} and {binary.relative_to(ROOT)}")

    if args.no_deploy:
        return
    targets: list[Path] = []
    default = default_dolphin_user_dir()
    for t in ([default] if default else []) + [Path(p) for p in args.deploy_to]:
        if t.resolve() not in [x.resolve() for x in targets]:
            targets.append(t)
    if not targets:
        log("no Dolphin user directory found; pass --deploy-to")
    for t in targets:
        deploy(t, xml, binary)


if __name__ == "__main__":
    main()
