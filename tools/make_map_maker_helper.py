#!/usr/bin/env python3
"""Generates the three catalog maps for people who make maps (Server/maps):

  map_maker_floors.json   every floor piece of the game (center, cliff_*, runway): one tile each, tiles side by side.
  map_maker_decor.json    every other ground asset (props, walls, statues, forests...), in a grid.
  map_maker_enemies.json  every enemy unit of units_config.txt in a grid, the bosses in a second grid above it. Each one waits
                          asleep and only wakes up when it takes damage (script spawn, "asleep": true, "wakeRadius": 0, no
                          group, so only the one that was hit wakes up).

Every item has a floor text in front of it (signs: the client shows the key itself when the lore table has no such key) with
the name of the floor piece / decoration / unit. Each map is a compact, about square grid on a floor of its own with walls all
around it. The plane starts in the middle: floors and decor have an empty strip across the middle of the grid; the enemies map
starts between the normal units (below) and the bosses (above). /room N jumps to a group of rows.

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
DRAWING_PIECES = [58814, 28963, 64648]   # the floor of Iron Lair (territory 67): the plain floor under the catalog items
LIMIT = 600.0            # the client keeps 54 tiles (648 u) on each side of the origin; the abyss around needs a few tiles too
MARGIN = 36.0            # free floor between the items and the walls
NEVER = 99999.0          # a checkpoint that is only reached with /room N (flying never makes it the current one)


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
    """Items in rows, every row centered on u = 0, about square. Row r is at v = v_start + r * row_pitch (+ gap from gap_row on)."""

    def __init__(self, count, pitch, row_pitch, gap_row=None, gap=0.0):
        self.pitch = pitch
        self.row_pitch = row_pitch
        self.count = count
        self.cols = max(1, min(int((2 * LIMIT) // pitch), math.ceil(math.sqrt(count * row_pitch / pitch))))
        self.rows = max(1, math.ceil(count / self.cols))
        self.gap_row = gap_row
        self.gap = gap if gap_row is not None else 0.0
        self.height = self.rows * row_pitch + self.gap
        self.v_start = 0.0

    def row_v(self, row):
        return self.v_start + row * self.row_pitch + (self.gap if self.gap_row is not None and row >= self.gap_row else 0.0)

    def place(self, i):
        col, row = i % self.cols, i // self.cols
        in_row = min(self.cols, self.count - row * self.cols)
        u = (col - (in_row - 1) / 2.0) * self.pitch      # every row is centered
        return round(u, 1), round(self.row_v(row), 1), row

    def bottom(self):
        return self.v_start - self.row_pitch / 2

    def top(self):
        return self.row_v(self.rows - 1) + self.row_pitch / 2

    def plaza_v(self):
        """Middle of the free strip (only with a gap)."""
        return self.v_start + (self.gap_row - 0.5) * self.row_pitch + self.gap / 2


def row_checkpoints(grid, every, label):
    """A checkpoint in front of every "every"-th row (jump with /room N)."""
    result = []
    for row in range(0, grid.rows, every):
        v = grid.row_v(row) - grid.row_pitch * 0.9
        result.append((label + " row " + str(row + 1), NEVER, v))
    return result


def snap_rect(r):
    return (math.floor(r[0] / 12.0) * 12, math.ceil(r[1] / 12.0) * 12, math.floor(r[2] / 12.0) * 12, math.ceil(r[3] / 12.0) * 12)


def margin_rect(box):
    return (box[0] - MARGIN, box[1] + MARGIN, box[2] - MARGIN, box[3] + MARGIN)


def drawing(shape):
    """The plain floor under the whole map: the union of rectangles (each a box of whole tiles), as the file's ASCII drawing.
    One rectangle = a plain box; several = a shape that follows the content (the enemies map narrows where the bosses are)."""
    rects = [snap_rect(r) for r in shape]
    umin, umax = min(r[0] for r in rects), max(r[1] for r in rects)
    vmin, vmax = min(r[2] for r in rects), max(r[3] for r in rects)
    if min(umin, vmin) < -LIMIT or max(umax, vmax) > LIMIT:
        sys.exit("the map does not fit the game's grid (u %d..%d, v %d..%d)" % (umin, umax, vmin, vmax))
    width, height = (umax - umin) // 3, (vmax - vmin) // 3
    lines = []
    for row in range(height):                       # first line = the highest sj
        v = vmax - 3 * row - 1.5
        chars = []
        for col in range(width):
            u = umin + 3 * col + 1.5
            chars.append("." if any(r[0] < u < r[1] and r[2] < v < r[3] for r in rects) else "-")
        lines.append("".join(chars))
    return {"siStart": umin // 3, "sjStart": vmin // 3,
            "legend": ". free floor, - no floor. First line = last sj row (the back). The walls are the edge walls of the floor (wallsOnEdges).",
            "lines": lines}, (umin, umax, vmin, vmax)


def write_map(file_id, name, note, shape, pieces, decor, signs, points, spawn, spawns, types, checkpoints, max_enemies):
    draw, (umin, umax, vmin, vmax) = drawing(shape)
    L = []
    L.append("{")
    L.append('  "format": "steambirds-map",')
    L.append('  "version": 1,')
    L.append('  "id": "%s",' % file_id)
    L.append('  "name": %s,' % json.dumps(name))
    L.append('  "terrain": "%s",' % TERRAIN)
    L.append('  "territory": %d,' % TERRITORY)
    L.append('  "note": %s,' % json.dumps(note))
    L.append('  "floor": {"wallsOnEdges": true, "voidMargin": 4, "voidTerritory": 74, "drawingPieces": %s,' % jd(DRAWING_PIECES))
    L.append('    "drawing": {"siStart": %d, "sjStart": %d, "legend": %s, "lines": [' % (draw["siStart"], draw["sjStart"], json.dumps(draw["legend"])))
    L.append(",\n".join('      "%s"' % row for row in draw["lines"]))
    L.append("    ]},")
    L.append('    "pieces": [')
    L.append(",\n".join("      " + jd(p) for p in pieces))
    L.append("  ]},")
    L.append('  "decor": [')
    L.append(",\n".join("    " + jd(d) for d in decor))
    L.append("  ],")
    L.append('  "buildings": [],')
    L.append('  "spawn": ' + jd({"u": 0.0, "v": round(spawn, 1), "rot": 315}) + ",")
    L.append('  "points": {')
    L.append(",\n".join("    " + json.dumps(k) + ": " + jd(p) for k, p in points.items()))
    L.append("  },")
    L.append('  "groups": {},')
    L.append('  "areas": {"all": ' + jd({"minU": umin + 6, "maxU": umax - 6, "minV": vmin + 6, "maxV": vmax - 6}) + "},")
    L.append('  "lines": {},')
    L.append('  "values": {"tier": 1' + (', "maxEnemies": %d' % max_enemies if max_enemies else "") + "},")
    L.append('  "checkpoints": [')
    L.append(",\n".join("    " + jd({"name": n, "activeAtV": av, "u": 0.0, "v": round(cv, 1), "rot": 315}) for n, av, cv in checkpoints))
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


def bounding_box(*groups):
    us, vs = [], []
    for g in groups:
        for u, v in g:
            us.append(u)
            vs.append(v)
    return min(us), max(us), min(vs), max(vs)


def make_floors(floors):
    # One tile per piece (the four slots of the tile), tiles side by side; every second tile row is free for the names.
    # The middle of the grid has a free strip of 4 tile rows: the plane starts there.
    pitch, row_pitch = 12.0, 24.0
    grid = Grid(len(floors), pitch, row_pitch)
    grid = Grid(len(floors), pitch, row_pitch, gap_row=grid.rows // 2, gap=48.0)
    grid.v_start = round((-grid.height / 2 + row_pitch / 2) / 12.0) * 12.0
    spawn_v = grid.plaza_v()
    pieces, signs, spots = [], [{"key": "FLOORS: %d floor pieces (one tile each)" % len(floors), "u": 0.0,
                                 "v": round(spawn_v + 10, 1), "radius": 40, "rot": 0}], []
    for i, (name, asset_id) in enumerate(floors):
        u, v, row = grid.place(i)
        a = int(math.floor(u / 12.0))                    # tile of the piece (u is its center, the tile is 12 u wide)
        b = int(round(v / 12.0))
        for k in (0, 1):
            for l in (0, 1):
                pieces.append({"p": 2 * a + k, "q": 2 * b + l, "id": asset_id, "name": name, "rot": 0})
        signs.append({"key": name, "u": 12 * a + 6, "v": 12 * b - 6, "radius": 7, "rot": 0})
        spots += [(12 * a, 12 * b - 8), (12 * a + 12, 12 * b + 12)]
    cps = [("start", -NEVER, spawn_v)] + row_checkpoints(grid, 4, "floors")
    box = bounding_box(spots, [(0, spawn_v)])
    path = write_map("map_maker_floors", "Map Maker: Floors",
                     "Catalog of every floor piece of the game (generated by tools/make_map_maker_helper.py from the game data). The plane starts in the middle; the pieces are in rows, one tile each, with the name written on the floor in front of it, on a plain floor with walls all around. /room N jumps to a group of rows. Nothing ends this map: leave with /leave.",
                     [margin_rect(box)], pieces, [], signs, {}, spawn_v, [], {}, cps, 0)
    return path, len(pieces) // 4, grid


def make_decor(decor):
    pitch, row_pitch = 17.0, 18.0
    grid = Grid(len(decor), pitch, row_pitch)
    grid = Grid(len(decor), pitch, row_pitch, gap_row=grid.rows // 2, gap=44.0)
    grid.v_start = -grid.height / 2 + row_pitch / 2
    spawn_v = grid.plaza_v()
    out, spots = [], []
    signs = [{"key": "DECORATION: %d decorations" % len(decor), "u": 0.0, "v": round(spawn_v + 10, 1), "radius": 40, "rot": 0}]
    for i, (name, asset_id) in enumerate(decor):
        u, v, row = grid.place(i)
        out.append({"id": asset_id, "name": name, "u": u, "v": v, "z": 0.5, "rot": 0})
        signs.append({"key": name, "u": u, "v": round(v - 5, 1), "radius": 9, "rot": 0})
        spots += [(u - pitch / 2, v - 8), (u + pitch / 2, v + 8)]
    cps = [("start", -NEVER, spawn_v)] + row_checkpoints(grid, 4, "decor")
    box = bounding_box(spots, [(0, spawn_v)])
    path = write_map("map_maker_decor", "Map Maker: Decoration",
                     "Catalog of every decoration of the game (generated by tools/make_map_maker_helper.py from the game data). The plane starts in the middle; the decorations are in rows, with the name written on the floor in front of each, on a plain floor with walls all around. /room N jumps to a group of rows. Nothing ends this map: leave with /leave.",
                     [margin_rect(box)], [], out, signs, {}, spawn_v, [], {}, cps, 0)
    return path, len(out), grid


def make_enemies(normal, bosses):
    points, spawns, types, signs, spots = {}, [], {}, [], []
    plaza = 60.0
    main = Grid(len(normal), 10.0, 10.0)
    boss_grid = Grid(len(bosses), 16.0, 16.0)
    total = main.height + plaza + boss_grid.height
    main.v_start = -total / 2 + main.row_pitch / 2
    spawn_v = main.top() + plaza / 2
    boss_grid.v_start = main.top() + plaza + boss_grid.row_pitch / 2

    signs.append({"key": "ENEMIES: %d units (below), asleep until they are hit" % len(normal), "u": -45.0, "v": round(spawn_v, 1), "radius": 45, "rot": 0})
    signs.append({"key": "BOSSES: %d units (above), asleep until they are hit" % len(bosses), "u": 45.0, "v": round(spawn_v, 1), "radius": 45, "rot": 0})

    def put(grid, names):
        for i, name in enumerate(names):
            u, v, row = grid.place(i)
            pid = "e%04d" % len(points)
            points[pid] = {"u": u, "v": v, "rot": 135}
            types[name] = {"name": name, "template": name, "behavior": "chase"}
            spawns.append({"name": pid, "type": name, "point": pid, "area": "all", "asleep": True, "wakeRadius": 0})
            signs.append({"key": name, "u": u, "v": round(v - 6, 1), "radius": grid.pitch * 0.55, "rot": 0})
            spots.append((u - grid.pitch / 2, v - 8))
            spots.append((u + grid.pitch / 2, v + 8))

    put(main, normal)
    put(boss_grid, bosses)
    cps = [("start", -NEVER, spawn_v)] + row_checkpoints(main, 7, "enemies") + row_checkpoints(boss_grid, 3, "bosses")
    # The floor follows the content and narrows where the bosses are, so nobody gets lost: a wide room for the normal units, a
    # neck with the start in it (just wide enough for the two titles) and a narrow room for the bosses.
    main_w = main.cols * main.pitch / 2 + MARGIN
    boss_w = boss_grid.cols * boss_grid.pitch / 2 + MARGIN
    neck_w = max(boss_w, 80.0)
    shape = [
        (-main_w, main_w, main.bottom() - MARGIN, main.top() + 12),                     # normal units
        (-neck_w, neck_w, main.top() - 12, boss_grid.bottom() + 12),                     # the neck (the start)
        (-boss_w, boss_w, boss_grid.bottom() - 12, boss_grid.top() + MARGIN),            # bosses
    ]
    path = write_map("map_maker_enemies", "Map Maker: Enemies",
                     "Catalog of every enemy unit of the game (generated by tools/make_map_maker_helper.py from the game data). The plane starts between the normal units (below) and the bosses (above); the name of each unit is written on the floor in front of it, on a plain floor with walls all around that narrows where the bosses are. They wait asleep and only wake up when they take damage (script spawns, no group; values.maxEnemies lifts the usual limit of 140 living enemies). /room N jumps to a group of rows. Nothing ends this map: leave with /leave.",
                     shape, [], [], signs, points, spawn_v, spawns, types, cps, len(normal) + len(bosses) + 20)
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
