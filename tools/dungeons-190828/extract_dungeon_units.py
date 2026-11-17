"""
Reads a Steambirds configBinary (DarkConfig binary, LZMA) and writes dungeon_units.json: every unit of the
dungeon folders (Units/60D..999D, DungeonTraps, Tutorial) with its display name, health, speed, guns and role.

Usage: python extract_dungeon_units.py <configBinary> [dungeon_units.json] [config_dump.json]
The optional third argument also dumps the whole configBinary as JSON (useful to search texts, quests, barks).
"""

import io
import json
import lzma
import struct
import sys
import zlib

MAGIC = 860704227


def parse_config_binary(raw):
    if struct.unpack_from("<i", raw, 0)[0] != MAGIC:
        raise ValueError("not a configBinary")
    compression = raw[5]
    if compression == 3:
        body = lzma.decompress(raw[7:], format=lzma.FORMAT_ALONE)
    elif compression == 1:
        body = zlib.decompress(raw[7:], -15)
    elif compression == 0:
        body = raw[7:]
    else:
        raise ValueError("unknown compression %d" % compression)
    r = io.BytesIO(body)

    def cint():
        value, shift = 0, 0
        while True:
            b = r.read(1)[0]
            value |= (b & 0x7F) << shift
            shift += 7
            if not b & 0x80:
                return value

    def rstr():
        return r.read(cint()).decode("utf-8")

    strings = {}
    for _ in range(struct.unpack("<i", r.read(4))[0]):
        i = cint()
        strings[i] = rstr()

    def text(kind):
        if kind == 0:
            return None
        if kind == 240:
            return rstr()
        if kind == 255:
            return strings.get(cint())
        raise ValueError("text marker %d" % kind)

    def node():
        t = r.read(1)[0]
        if t == 1:
            d = {}
            for _ in range(cint()):
                k = text(r.read(1)[0])
                d[k] = node()
            return d
        if t == 2:
            return [node() for _ in range(cint())]
        if t == 3:
            return text(r.read(1)[0])
        return None

    files = {}
    for _ in range(struct.unpack("<H", r.read(2))[0]):
        name = rstr()
        r.read(16)  # checksum, size, modified
        files[name] = node()
    return files


def role(file_name, v):
    loot = v.get("loot") or {}
    tables = " ".join(str(x.get("lootTableId", "")) for x in loot.values() if isinstance(x, dict))
    if file_name.endswith("DEATH"):
        return "wreck"
    if v.get("isWall") == "true":
        return "wall"
    if "treasure" in file_name.lower() or v.get("art") == "CollectableTreasureCachePrefab":
        return "treasure"
    if v.get("showPostFightDetails") == "true" or "boss_" in tables:
        return "BOSS"
    if "miniboss" in tables:
        return "MINIBOSS"
    if float(v.get("maxSpeed", "0") or 0) == 0:
        return "static_gun" if v.get("gunMounts") else "static"
    return "enemy"


def main(cfg_path, out_path="dungeon_units.json", dump_path=None):
    files = parse_config_binary(open(cfg_path, "rb").read())
    names = files.get("Texts/en-US/units", {})
    out = {}
    for k in sorted(files):
        parts = k.split("/")
        if not k.startswith("Units/") or len(parts) < 3:
            continue
        folder = parts[1]
        if not ("D " in folder or "D-" in folder or folder in ("DungeonTraps", "00 - Tutorial Dungeon")):
            continue
        v = files[k]
        t = v.get("templateName")
        out.setdefault(folder, []).append({
            "file": parts[-1], "template": t, "name": names.get(t, ""), "role": role(parts[-1], v),
            "hp": v.get("maxHitPoints"), "tier": v.get("tier"), "speed": v.get("maxSpeed"),
            "guns": len(v.get("gunMounts") or []), "art": v.get("art"), "xp": v.get("xpMultiplier")})
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, ensure_ascii=False)
    print(out_path, sum(len(x) for x in out.values()), "units in", len(out), "folders")
    if dump_path:
        with open(dump_path, "w", encoding="utf-8") as f:
            json.dump(files, f, ensure_ascii=False)
        print(dump_path, len(files), "config files")


if __name__ == "__main__":
    main(*sys.argv[1:4])
