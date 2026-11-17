"""
Converts map files (and enemies.json / dungeons.json) from the old Portuguese format to the current English format.

Usage:
    python migrate_legacy_map.py <file or folder> [...] [--out <folder>] [--table <legacy-names.json>]

Without --out the files are rewritten in place. Comments (//) are not kept: the server writes no comments in maps.
Text written by people (notes, names) is not translated.
"""

import argparse
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from legacy_names import DEFAULT_TABLE, LegacyNames, load_json  # noqa: E402


def dump(obj):
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "dungeons-190828"))
    try:
        from dungeon_builder import dump as compact_dump
        return compact_dump(obj)
    except ImportError:
        return json.dumps(obj, ensure_ascii=False, indent=2) + "\n"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--out")
    ap.add_argument("--table", default=DEFAULT_TABLE)
    a = ap.parse_args()
    legacy = LegacyNames(a.table)
    files = []
    for p in a.paths:
        files += sorted(glob.glob(os.path.join(p, "*.json"))) if os.path.isdir(p) else [p]
    for f in files:
        data = legacy.translate(load_json(f))
        target = os.path.join(a.out, os.path.basename(f)) if a.out else f
        if a.out:
            os.makedirs(a.out, exist_ok=True)
        open(target, "w", encoding="utf-8", newline="\n").write(dump(data))
        print("converted", f, "->", target)


if __name__ == "__main__":
    main()
