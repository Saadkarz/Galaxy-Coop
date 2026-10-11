"""Print `<cab key>\t<install path>` for every file in an MSI's File table.

Used by tools/setup.ps1 to pull the CodeWarrior command line tools out of the
Special Edition installer without running its InstallScript-based setup.
"""

import msilib
import sys

db = msilib.OpenDatabase(sys.argv[1], msilib.MSIDBOPEN_READONLY)


def rows(query, n):
    view = db.OpenView(query)
    view.Execute(None)
    out = []
    while True:
        rec = view.Fetch()
        if rec is None:
            break
        out.append([rec.GetString(i + 1) for i in range(n)])
    view.Close()
    return out


def long_name(s):
    return s.split(":")[-1].split("|")[-1]


dirs = {d: (p, n) for d, p, n in rows("SELECT Directory, Directory_Parent, DefaultDir FROM Directory", 3)}
comps = {c: d for c, d in rows("SELECT Component, Directory_ FROM Component", 2)}


def dir_path(d):
    parts = []
    while d and d in dirs:
        parent, name = dirs[d]
        parts.append(long_name(name))
        d = parent
    return "/".join(reversed([x for x in parts if x not in (".", "SourceDir")]))


for key, comp, name in rows("SELECT File, Component_, FileName FROM File", 3):
    print(f"{key}\t{dir_path(comps.get(comp, ''))}/{long_name(name)}")
