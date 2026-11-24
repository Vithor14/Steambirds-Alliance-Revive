#!/usr/bin/env python3
"""Generates the three catalog maps for people who make maps (Server/maps):

  map_maker_floors.json   every floor piece of the game (center, cliff_*, runway): one tile each, tiles side by side.
  map_maker_decor.json    every other ground asset (props, walls, statues, forests...), in a grid.
  map_maker_enemies.json  every enemy unit of units_config.txt in a grid, the bosses in rows after them. Each one waits asleep
                          and only wakes up when it takes damage (script spawn, "asleep": true, "wakeRadius": 0, no group, so
                          only the one that was hit wakes up).

Every item has a floor text in front of it (signs: the client shows the key itself when the lore table has no such key) with
the name of the floor piece / decoration / unit. Each map is a compact grid (about square, centered on the origin) with the
plane starting in front of the first row, and checkpoints every few rows: /room N jumps to them.

Reads the game's own data from Server/data (ground_assets.txt, units_config.txt): those files are gitignored, so the maps are
made on the PC that has them. Run from the repository root:  python tools/make_map_maker_helper.py
"""
import json
import math
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "Server", "data")
MAPS = os.path.join(ROOT, "Server", "maps")

FLOOR_KINDS = ("center", "cliff_inside", "cliff_outside", "cliff_side")
KIND = re.compile(r"([^_]*?)_([^_]*?|[^_]*?_[^_]*?)(?:_([0-9]+)|)$")   # same as Server/World/WorldData.cs
TERRITORY = 67           # territory of the maps (as Iron Lair)
TERRAIN = "DungeonSneezy"
LIMIT = 636.0            # the client keeps 54 tiles (648 u) on each side of the origin


def read_assets():
    assets = {}
    for line in open(os.path.join(DATA, "ground_assets.txt"), encoding="utf8"):
        m = re.match(r"ID=(-?\d+) name=(\S+)", line)
        if m and 0 <= int(m[1]) <= 65535:
            assets.setdefault(int(m[1]), m[2])
    floors, decor = [], []
    for asset_id, name in assets.items():
        m = KIND.match(name)
        kind = m.group(2) if m else ""
        if kind in FLOOR_KINDS or "_Runway_" in name:
            floors.append((name, asset_id))
        else:
            decor.append((name, asset_id))
    floors.sort(key=lambda x: x[0].lower())
    decor.sort(key=lambda x: x[0].lower())
    return floors, decor


def read_enemies():
    rows = [l.rstrip("\n").split("\t") for l in open(os.path.join(DATA, "units_config.txt"), encoding="utf8") if l.startswith("unit\t")]
    normal, bosses, seen = [], [], set()
    for r in rows:
        name, art = r[1], r[19]
        if not art.lower().startswith("enemy") or "Death" in art or art.lower().startswith("enemyquest") or "EnemyConveyor" in art:
            continue                      # invisible controllers, walls, gates, chests, death wrecks, quest dummies
        if r[13] == "1" or r[14] == "1":  # wall / player
            continue
        if "spawner" in name.lower() or "master" in name.lower():
            continue                      # controllers of the original server
        if name in seen:
            continue
        seen.add(name)
        (bosses if r[15] == "1" else normal).append(name)
    normal.sort(key=str.lower)
    bosses.sort(key=str.lower)
    return normal, bosses


def jd(obj):
    return json.dumps(obj, ensure_ascii=False, separators=(", ", ": "))


class Grid:
    """Items in rows, centered on u = 0. Row r is at v = v0 + r * row_pitch; the plane starts in front of row 0."""

    def __init__(self, count, pitch, row_pitch, v_first=None):
        self.pitch = pitch
        self.row_pitch = row_pitch
        self.count = count
        # About square: width = cols * pitch, height = rows * row_pitch.
        self.cols = max(1, min(int((2 * LIMIT) // pitch), math.ceil(math.sqrt(count * row_pitch / pitch))))
        self.rows = max(1, math.ceil(count / self.cols))
        self.height = self.rows * row_pitch
        self.v0 = (v_first if v_first is not None else -self.height / 2 + row_pitch)

    def place(self, i):
        col, row = i % self.cols, i // self.cols
        in_row = min(self.cols, self.count - row * self.cols)
        u = (col - (in_row - 1) / 2.0) * self.pitch      # every row is centered
        return round(u, 1), round(self.v0 + row * self.row_pitch, 1), row


def checkpoints_for(grid, every, label):
    """A checkpoint at the start and one every "every" rows, in front of the row (jump with /room N)."""
    result = [("start", grid.v0 - 3 * grid.row_pitch - 4, grid.v0 - 3 * grid.row_pitch)]
    for row in range(0, grid.rows, every):
        ahead = grid.v0 + row * grid.row_pitch - grid.row_pitch
        result.append((label + " row " + str(row + 1), ahead - 4, ahead))
    return result


def write_map(file_id, name, note, grid_for_spawn, pieces, decor, signs, points, spawns, types, checkpoints, max_enemies):
    first = checkpoints[0][2]
    L = []
    L.append("{")
    L.append('  "format": "steambirds-map",')
    L.append('  "version": 1,')
    L.append('  "id": "%s",' % file_id)
    L.append('  "name": %s,' % json.dumps(name))
    L.append('  "terrain": "%s",' % TERRAIN)
    L.append('  "territory": %d,' % TERRITORY)
    L.append('  "note": %s,' % json.dumps(note))
    L.append('  "floor": {"wallsOnEdges": false, "voidMargin": 0, "voidTerritory": 74, "pieces": [')
    L.append(",\n".join("    " + jd(p) for p in pieces))
    L.append("  ]},")
    L.append('  "decor": [')
    L.append(",\n".join("    " + jd(d) for d in decor))
    L.append("  ],")
    L.append('  "buildings": [],')
    L.append('  "spawn": ' + jd({"u": 0.0, "v": round(first, 1), "rot": 315}) + ",")
    L.append('  "points": {')
    L.append(",\n".join("    " + json.dumps(k) + ": " + jd(p) for k, p in points.items()))
    L.append("  },")
    L.append('  "groups": {},')
    L.append('  "areas": {"all": ' + jd({"minU": -645.0, "maxU": 645.0, "minV": -645.0, "maxV": 645.0}) + "},")
    L.append('  "lines": {},')
    L.append('  "values": {"tier": 1' + (', "maxEnemies": %d' % max_enemies if max_enemies else "") + "},")
    L.append('  "checkpoints": [')
    L.append(",\n".join("    " + jd({"name": n, "activeAtV": round(av, 1), "u": 0.0, "v": round(cv, 1), "rot": 315}) for n, av, cv in checkpoints))
    L.append("  ],")
    L.append('  "gates": [],')
    L.append('  "sideGates": [],')
    L.append('  "signs": [')
    L.append(",\n".join("    " + jd(s) for s in signs))
    L.append("  ],")
    L.append('  "types": {')
    L.append(",\n".join("    " + json.dumps(k) + ": " + jd(t) for k, t in types.items()))
    L.append("  },")
    L.append('  "encounters": []' + ("," if spawns else ""))
    if spawns:
        L.append('  "script": {')
        L.append('    "note": "Every enemy is a spawn of its own, asleep, no wake radius and no group: it only wakes up when it takes damage (and nothing else wakes with it).",')
        L.append('    "spawns": [')
        L.append(",\n".join("      " + jd(s) for s in spawns))
        L.append("    ],")
        L.append('    "rules": []')
        L.append("  }")
    L.append("}")
    path = os.path.join(MAPS, file_id + ".json")
    with open(path, "w", encoding="utf8", newline="\n") as f:
        f.write("\n".join(L) + "\n")
    return path


def title_sign(text, grid):
    # At the start, in front of the first row; big radius so it shows from the spawn point.
    return {"key": text, "u": 0.0, "v": round(grid.v0 - 2 * grid.row_pitch, 1), "radius": 40, "rot": 0}


def check_limits(file_id, grid):
    top = grid.v0 + grid.rows * grid.row_pitch
    half_width = grid.cols * grid.pitch / 2
    if grid.v0 - 4 * grid.row_pitch < -LIMIT or top > LIMIT or half_width > LIMIT:
        sys.exit("%s does not fit the game's grid (v %.0f..%.0f, half width %.0f)" % (file_id, grid.v0, top, half_width))


def make_floors(floors):
    # One tile per piece (the four slots of the tile), tiles side by side; every second tile row is free for the names.
    pitch, row_pitch = 12.0, 24.0
    grid = Grid(len(floors), pitch, row_pitch)
    check_limits("map_maker_floors", grid)
    pieces, signs = [], [title_sign("FLOORS: %d floor pieces (one tile each)" % len(floors), grid)]
    for i, (name, asset_id) in enumerate(floors):
        u, v, row = grid.place(i)
        a = int(math.floor(u / 12.0))                    # tile of the piece (u is its center, the tile is 12 u wide)
        b = int(round(v / 12.0))
        for k in (0, 1):
            for l in (0, 1):
                pieces.append({"p": 2 * a + k, "q": 2 * b + l, "id": asset_id, "name": name, "rot": 0})
        signs.append({"key": name, "u": 12 * a + 6, "v": 12 * b - 6, "radius": 7, "rot": 0})
    cps = checkpoints_for(grid, 4, "floors")
    path = write_map("map_maker_floors", "Map Maker: Floors",
                     "Catalog of every floor piece of the game (generated by tools/make_map_maker_helper.py from the game data). Fly +v: the pieces are in rows, one tile each, with the name written on the floor in front of it. /room N jumps to a group of rows. Nothing ends this map: leave with /leave.",
                     grid, pieces, [], signs, {}, [], {}, cps, 0)
    return path, len(pieces) // 4, grid


def make_decor(decor):
    pitch, row_pitch = 17.0, 18.0
    grid = Grid(len(decor), pitch, row_pitch)
    check_limits("map_maker_decor", grid)
    out, signs = [], [title_sign("DECORATION: %d decorations" % len(decor), grid)]
    for i, (name, asset_id) in enumerate(decor):
        u, v, row = grid.place(i)
        out.append({"id": asset_id, "name": name, "u": u, "v": v, "z": 0.5, "rot": 0})
        signs.append({"key": name, "u": u, "v": round(v - 5, 1), "radius": 9, "rot": 0})
    cps = checkpoints_for(grid, 4, "decor")
    path = write_map("map_maker_decor", "Map Maker: Decoration",
                     "Catalog of every decoration of the game (generated by tools/make_map_maker_helper.py from the game data). Fly +v: the decorations are in rows, with the name written on the floor in front of each. /room N jumps to a group of rows. Nothing ends this map: leave with /leave.",
                     grid, [], out, signs, {}, [], {}, cps, 0)
    return path, len(out), grid


def make_enemies(normal, bosses):
    points, spawns, types, signs = {}, [], {}, []
    main = Grid(len(normal), 14.0, 14.0)
    # The bosses come after the normal units, a little further apart.
    boss_grid = Grid(len(bosses), 18.0, 18.0, v_first=main.v0 + main.rows * main.row_pitch + 30)
    check_limits("map_maker_enemies", main)
    if boss_grid.v0 + boss_grid.rows * boss_grid.row_pitch > LIMIT:
        sys.exit("map_maker_enemies: the boss rows do not fit the game's grid")
    signs.append(title_sign("ENEMIES: %d units, asleep until they are hit" % len(normal), main))
    signs.append({"key": "BOSSES: %d boss units, asleep until they are hit" % len(bosses), "u": 0.0,
                  "v": round(boss_grid.v0 - 12, 1), "radius": 40, "rot": 0})

    def put(grid, names):
        for i, name in enumerate(names):
            u, v, row = grid.place(i)
            pid = "e%04d" % len(points)
            points[pid] = {"u": u, "v": v, "rot": 135}
            types[name] = {"name": name, "template": name, "behavior": "chase"}
            spawns.append({"name": pid, "type": name, "point": pid, "area": "all", "asleep": True, "wakeRadius": 0})
            signs.append({"key": name, "u": u, "v": round(v - 6, 1), "radius": grid.pitch * 0.55, "rot": 0})

    put(main, normal)
    put(boss_grid, bosses)
    cps = checkpoints_for(main, 7, "enemies")
    cps.append(("bosses", boss_grid.v0 - boss_grid.row_pitch - 4, boss_grid.v0 - boss_grid.row_pitch))
    path = write_map("map_maker_enemies", "Map Maker: Enemies",
                     "Catalog of every enemy unit of the game (generated by tools/make_map_maker_helper.py from the game data). Fly +v: the units are in rows with the name written on the floor in front of each, the bosses in the last rows. They wait asleep and only wake up when they take damage (script spawns, no group; values.maxEnemies lifts the usual limit of 140 living enemies). /room N jumps to a group of rows. Nothing ends this map: leave with /leave.",
                     main, [], [], signs, points, spawns, types, cps, len(normal) + len(bosses) + 20)
    return path, len(spawns), main, boss_grid


def main():
    floors, decor = read_assets()
    normal, bosses = read_enemies()
    path, count, g = make_floors(floors)
    print("floors  %d pieces, %d x %d tiles -> %s" % (count, g.cols, g.rows, path))
    path, count, g = make_decor(decor)
    print("decor   %d decorations, %d x %d -> %s" % (count, g.cols, g.rows, path))
    path, count, g, bg = make_enemies(normal, bosses)
    print("enemies %d units (%d bosses), %d x %d + %d x %d -> %s" % (count, len(bosses), g.cols, g.rows, bg.cols, bg.rows, path))


if __name__ == "__main__":
    main()
