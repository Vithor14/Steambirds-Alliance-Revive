"""
Builder for Steambirds Alliance Revive dungeon maps (format "steambirds-map" v1).

Rooms are drawn on the 3-unit wall sub-cell grid (si along u, sj along v) used by the server's map
reader (Maps.cs). The main path runs towards +v (up-left on screen), like the walker tutorial.

Every open rectangle becomes '.', every other sub-cell of a floor tile becomes '#', and the tiles around
the open area (one tile ring) are filled with '#', which gives thick walls like the tutorial map.

Positions inside rooms are given as fractions (fx across u, fy along v) of the room's open rectangle.
"""

import json
import math

SUB = 3.0          # units per wall sub-cell
TILE_SUBS = 4      # sub-cells per tile (12 units)

# World rotation (degrees, 0 = +y, clockwise) for each grid direction.
FACE_PLUS_V = 315   # towards the end of the dungeon
FACE_MINUS_V = 135  # towards the entrance
FACE_PLUS_U = 45
FACE_MINUS_U = 225


def r1(x):
    return round(x * 10) / 10.0


class Room:
    def __init__(self, name, si0, sj0, w, h, kind="room"):
        self.name = name
        self.si0, self.sj0, self.w, self.h = si0, sj0, w, h
        self.kind = kind

    @property
    def si1(self):
        return self.si0 + self.w - 1

    @property
    def sj1(self):
        return self.sj0 + self.h - 1

    @property
    def cx_sub(self):
        return self.si0 + self.w / 2.0

    def uv(self, fx, fy):
        # fx, fy in [0,1] inside the open rectangle (kept 1.2 units away from the walls)
        u0, u1 = self.si0 * SUB + 1.2, (self.si1 + 1) * SUB - 1.2
        v0, v1 = self.sj0 * SUB + 1.2, (self.sj1 + 1) * SUB - 1.2
        return r1(u0 + (u1 - u0) * fx), r1(v0 + (v1 - v0) * fy)

    def area(self, pad=0.6):
        return {
            "minU": r1(self.si0 * SUB - pad), "maxU": r1((self.si1 + 1) * SUB + pad),
            "minV": r1(self.sj0 * SUB - pad), "maxV": r1((self.sj1 + 1) * SUB + pad),
        }

    def entry_v(self):
        return r1(self.sj0 * SUB)


# ------------------------------------------------------------------ position patterns

def pat(name, n=1, **k):
    """Returns a list of (fx, fy, rot) for a pattern."""
    out = []
    if name == "center":
        out = [(0.5, k.get("fy", 0.55), k.get("rot", FACE_MINUS_V))]
    elif name == "line":
        fy = k.get("fy", 0.7)
        for i in range(n):
            fx = 0.5 if n == 1 else 0.12 + 0.76 * i / (n - 1)
            out.append((fx, fy, k.get("rot", FACE_MINUS_V)))
    elif name == "column":
        fx = k.get("fx", 0.5)
        y0, y1 = k.get("y0", 0.2), k.get("y1", 0.85)
        for i in range(n):
            fy = (y0 + y1) / 2 if n == 1 else y0 + (y1 - y0) * i / (n - 1)
            out.append((fx, fy, k.get("rot", FACE_MINUS_V)))
    elif name == "leftwall":
        y0, y1 = k.get("y0", 0.15), k.get("y1", 0.85)
        for i in range(n):
            fy = (y0 + y1) / 2 if n == 1 else y0 + (y1 - y0) * i / (n - 1)
            out.append((0.03, fy, FACE_PLUS_U))
    elif name == "rightwall":
        y0, y1 = k.get("y0", 0.15), k.get("y1", 0.85)
        for i in range(n):
            fy = (y0 + y1) / 2 if n == 1 else y0 + (y1 - y0) * i / (n - 1)
            out.append((0.97, fy, FACE_MINUS_U))
    elif name == "bothwalls":
        half = n // 2
        out = pat("leftwall", half, **k) + pat("rightwall", n - half, **k)
    elif name == "topwall":
        for i in range(n):
            fx = 0.5 if n == 1 else 0.15 + 0.7 * i / (n - 1)
            out.append((fx, 0.97, FACE_MINUS_V))
    elif name == "corners":
        m = k.get("m", 0.12)
        pts = [(m, m), (1 - m, m), (m, 1 - m), (1 - m, 1 - m)]
        out = [(x, y, FACE_MINUS_V) for x, y in pts[:n]]
    elif name == "ring":
        r = k.get("r", 0.35)
        cy = k.get("cy", 0.5)
        a0 = k.get("a0", 0)
        for i in range(n):
            a = math.radians(a0 + 360.0 * i / n)
            fx, fy = 0.5 + r * math.cos(a), cy + r * math.sin(a)
            # face the center
            du, dv = 0.5 - fx, cy - fy
            out.append((fx, fy, face(du, dv)))
    elif name == "grid":
        rows, cols = k.get("rows", 2), k.get("cols", 3)
        y0, y1 = k.get("y0", 0.3), k.get("y1", 0.8)
        for r_ in range(rows):
            for c in range(cols):
                fx = 0.5 if cols == 1 else 0.15 + 0.7 * c / (cols - 1)
                fy = (y0 + y1) / 2 if rows == 1 else y0 + (y1 - y0) * r_ / (rows - 1)
                out.append((fx, fy, k.get("rot", FACE_MINUS_V)))
    elif name == "scatter":
        seed = k.get("seed", 7)
        y0, y1 = k.get("y0", 0.3), k.get("y1", 0.9)
        x = seed
        for i in range(n):
            x = (x * 1103515245 + 12345) & 0x7FFFFFFF
            fx = 0.12 + 0.76 * ((x >> 8) % 1000) / 999.0
            x = (x * 1103515245 + 12345) & 0x7FFFFFFF
            fy = y0 + (y1 - y0) * ((x >> 8) % 1000) / 999.0
            out.append((r1(fx * 100) / 100, r1(fy * 100) / 100, FACE_MINUS_V))
    elif name == "at":
        out = [(p[0], p[1], p[2] if len(p) > 2 else FACE_MINUS_V) for p in k["pts"]]
    else:
        raise ValueError(name)
    return out


def face(du, dv):
    """World rotation for a direction given in grid (u, v) units."""
    x = (du - dv) / math.sqrt(2)
    y = (du + dv) / math.sqrt(2)
    ang = math.degrees(math.atan2(x, y))  # 0 = +y, clockwise
    return int(round(ang)) % 360


# ------------------------------------------------------------------ the map builder

class DungeonMap:
    def __init__(self, map_id, name, terrain, territory, pieces, note=""):
        self.id = map_id
        self.name = name
        self.terrain = terrain
        self.territory = territory
        self.pieces = pieces
        self.note = note
        self.open = set()
        self.rooms = {}
        self.order = []
        self.points = {}
        self.groups = {}
        self.lines = {}
        self.values = {}
        self.checkpoints = []
        self.gates = []
        self.side_gates = []
        self.signs = []
        self.types = {}
        self.encounters = []
        self.meta = {}
        self.spawn = None
        self.route = {}
        self.decor = []

    # -------------------------------------------------------------- geometry
    def carve(self, si0, sj0, w, h):
        for si in range(si0, si0 + w):
            for sj in range(sj0, sj0 + h):
                self.open.add((si, sj))

    def add_room(self, name, si0, sj0, w, h, kind="room"):
        room = Room(name, si0, sj0, w, h, kind)
        self.carve(si0, sj0, w, h)
        self.rooms[name] = room
        self.order.append(name)
        return room

    def corridor(self, si0, sj0, w, h):
        self.carve(si0, sj0, w, h)

    def chain(self, specs, axis=2, start_sj=0, corridor_w=4):
        """
        Lays the main path out: each spec is a dict with name, w, h, dx (offset of the room center from the
        axis, in sub-cells), gap (corridor length before the room, sub-cells), cw (corridor width).
        Returns the rooms in order. The corridor before each room is remembered as room.corr (si0, sj0, w, h).
        """
        rooms = []
        sj = start_sj
        prev = None
        for s in specs:
            w, h = s["w"], s["h"]
            cx = axis + s.get("dx", 0)
            si0 = int(round(cx - w / 2.0))
            gap = s.get("gap", 0 if prev is None else 6)
            cw = s.get("cw", corridor_w)
            if prev is not None:
                lo = max(prev.si0, si0)
                hi = min(prev.si1, si0 + w - 1)
                want = int(round((prev.cx_sub + (si0 + w / 2.0)) / 2.0 - cw / 2.0))
                csi0 = min(max(want, lo), hi - cw + 1)
                if csi0 < lo:
                    raise ValueError("rooms do not overlap: " + s["name"])
                self.corridor(csi0, sj, cw, gap)
                corr = (csi0, sj, cw, gap)
                sj += gap
            else:
                corr = None
            room = self.add_room(s["name"], si0, sj, w, h, s.get("kind", "room"))
            room.corr = corr
            rooms.append(room)
            sj += h
            prev = room
        return rooms

    def side_room(self, parent, name, side, w, h, gap=4, cw=4, fy=0.5, kind="side"):
        """A room to the left (-u) or right (+u) of a parent room, joined by a horizontal corridor."""
        pc = parent.sj0 + int(round((parent.h - cw) * fy))
        sj0 = pc - (h - cw) // 2
        if side == "L":
            csi0 = parent.si0 - gap
            si0 = csi0 - w
        else:
            csi0 = parent.si1 + 1
            si0 = csi0 + gap
        self.corridor(csi0, pc, gap, cw)
        room = self.add_room(name, si0, sj0, w, h, kind)
        room.corr = (csi0, pc, gap, cw)
        room.side = side
        return room

    # -------------------------------------------------------------- content
    def put(self, room, unit_type, pattern, **extra):
        """Adds enemies of one type to a room. pattern is the list returned by pat()."""
        pos = []
        for fx, fy, rot in pattern:
            u, v = room.uv(fx, fy)
            pos.append({"u": u, "v": v, "rot": rot})
        enc = self.encounter(room.name)
        entry = {"type": unit_type, "pos": pos}
        entry.update(extra)
        enc["enemies"].append(entry)
        return pos

    def encounter(self, room_name, **kw):
        for e in self.encounters:
            if e["room"] == room_name:
                e.update(kw)
                return e
        room = self.rooms[room_name]
        e = {"room": room_name, "area": room_name, "wakes": "onEnter", "line": room_name}
        e.update(kw)
        e["enemies"] = []
        self.encounters.append(e)
        return e

    def gate_before(self, room, name, unit_type, damageable=False, row=None, opens_after=None):
        """A row of gate units across the corridor that leads into the room (or across a side corridor)."""
        if room.corr is None:
            raise ValueError("room without corridor: " + room.name)
        if getattr(room, "side", None):
            # horizontal corridor (csi0, csj0, length along u, width along v): the gate is a column,
            # stored with "si" instead of "sj"
            csi0, csj0, length, cw = room.corr
            si = csi0 + (length // 2 if row is None else row)
            g = {"name": name, "type": unit_type, "si": si, "sjFrom": csj0, "sjTo": csj0 + cw - 1, "damageable": damageable}
        else:
            csi0, csj0, cw, gap = room.corr
            sj = csj0 + (gap // 2 if row is None else row)
            g = {"name": name, "type": unit_type, "sj": sj, "siFrom": csi0, "siTo": csi0 + cw - 1, "damageable": damageable}
        if opens_after:
            g["opensWhen"] = opens_after
        g["room"] = room.name
        # side gates go in their own list: the current reader (Maps.cs) only knows sj rows
        (self.side_gates if "si" in g else self.gates).append(g)
        return g

    def point(self, name, room, fx, fy, rot=FACE_MINUS_V, note=None):
        u, v = room.uv(fx, fy)
        p = {"u": u, "v": v, "rot": rot}
        if note:
            p["note"] = note
        self.points[name] = p
        return p

    def sign(self, key, room, fx=0.5, fy=0.15, radius=14):
        u, v = room.uv(fx, fy)
        self.signs.append({"key": key, "u": u, "v": v, "radius": radius, "rot": 0})

    def checkpoint(self, room, name=None):
        u, v = room.uv(0.5, 0.08)
        self.checkpoints.append({"name": name or room.name, "activeAtV": room.entry_v(), "u": u, "v": v, "rot": FACE_PLUS_V})

    # -------------------------------------------------------------- output
    def drawing(self):
        tiles = set()
        for si, sj in self.open:
            tiles.add((si // TILE_SUBS, sj // TILE_SUBS))
        ring = set()
        for a, b in tiles:
            for da in (-1, 0, 1):
                for db in (-1, 0, 1):
                    ring.add((a + da, b + db))
        tiles |= ring
        min_si = min(a for a, _ in tiles) * TILE_SUBS
        max_si = (max(a for a, _ in tiles) + 1) * TILE_SUBS - 1
        min_sj = min(b for _, b in tiles) * TILE_SUBS
        max_sj = (max(b for _, b in tiles) + 1) * TILE_SUBS - 1
        lines = []
        for sj in range(max_sj, min_sj - 1, -1):
            row = []
            for si in range(min_si, max_si + 1):
                if (si, sj) in self.open:
                    row.append(".")
                elif (si // TILE_SUBS, sj // TILE_SUBS) in tiles:
                    row.append("#")
                else:
                    row.append("-")
            lines.append("".join(row))
        return min_si, min_sj, lines

    def to_json(self):
        si0, sj0, lines = self.drawing()
        areas = {}
        for name in self.order:
            areas[name] = self.rooms[name].area()
        for name in self.order:
            self.lines.setdefault(name, self.rooms[name].entry_v())
        out = {
            "format": "steambirds-map",
            "version": 1,
            "id": self.id,
            "name": self.name,
            "terrain": self.terrain,
            "note": self.note,
        }
        out.update(self.meta)
        out.update({
            "territory": self.territory,
            "floor": {
                "wallsOnEdges": True,
                "voidMargin": 4,
                "voidTerritory": 74,
                "drawingPieces": self.pieces,
                "drawing": {
                    "siStart": si0,
                    "sjStart": sj0,
                    "legend": ". free floor, # wall, - no floor. First line = last sj row (the far end).",
                    "lines": lines,
                },
                "pieces": [],
            },
            "decor": self.decor,
            "buildings": [],
            "spawn": self.spawn,
            "points": self.points,
            "groups": self.groups,
            "areas": areas,
            "lines": self.lines,
            "values": self.values,
            "checkpoints": self.checkpoints,
            "gates": self.gates,
            "sideGates": self.side_gates,
            "signs": self.signs,
        })
        if self.route:
            out["routes"] = self.route
        out["types"] = self.types
        out["encounters"] = self.encounters
        return center_on_grid(out)


def center_on_grid(out):
    """Moves the map along v so it is centered on the origin of the client grid (the client keeps 54 tiles on each side
    of the origin; long dungeons drawn from v = 0 got too close to the edge). The move is a whole number of tiles."""
    d = out["floor"]["drawing"]
    sj_min = d["sjStart"]
    sj_max = sj_min + len(d["lines"]) - 1
    shift_tiles = -int(round((sj_min + sj_max + 1) / 2.0 / TILE_SUBS))
    if shift_tiles == 0:
        return out
    dsj = shift_tiles * TILE_SUBS
    dv = dsj * SUB

    def walk(node):
        if isinstance(node, dict):
            for k, val in list(node.items()):
                if k in ("v", "minV", "maxV", "activeAtV") and isinstance(val, (int, float)):
                    node[k] = r1(val + dv)
                elif k in ("sj", "sjFrom", "sjTo", "sjStart") and isinstance(val, int):
                    node[k] = val + dsj
                elif k == "lines" and isinstance(val, dict):
                    node[k] = {n: r1(x + dv) for n, x in val.items()}
                else:
                    walk(val)
        elif isinstance(node, list):
            for x in node:
                walk(x)

    walk(out)
    return out


def dump(obj):
    """JSON with short lists kept on one line, so the files stay readable."""
    text = json.dumps(obj, ensure_ascii=False, indent=2)
    # collapse {"u": .., "v": .., "rot": ..} objects and small dicts onto one line
    import re
    def collapse(m):
        inner = re.sub(r"\s+", " ", m.group(0))
        return inner.replace("{ ", "{").replace(" }", "}").replace("[ ", "[").replace(" ]", "]")
    text = re.sub(r"\{[^{}\[\]]{0,220}\}", collapse, text)
    text = re.sub(r"\[[^\[\]{}]{0,200}\]", collapse, text)
    return text + "\n"
