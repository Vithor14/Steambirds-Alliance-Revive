"""
Builds the per-map enemy types ("types", same fields as Server\\enemies.json) from the units of the
190828 configBinary (dungeon_units.json, made by the extractor) plus per-dungeon overrides.
"""

import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
_units = None


def units():
    global _units
    if _units is None:
        with open(os.path.join(HERE, "dungeon_units.json"), encoding="utf-8") as f:
            data = json.load(f)
        _units = {}
        for folder, items in data.items():
            for u in items:
                u = dict(u)
                u["folder"] = folder
                _units[u["template"]] = u
    return _units


_reach = None


def weapon_reach(template):
    """Longest range (world units) at which the server lets this unit fire (same rule as Enemies.GunRange),
    read from BepInEx\\units_config.txt (the plugin dump). 0 = unarmed or file not found.
    The file is looked for in $STEAMBIRDS_UNITS_CONFIG, then in ..\\..\\Server\\data next to this tool."""
    global _reach
    if _reach is None:
        _reach = {}
        for path in (os.environ.get("STEAMBIRDS_UNITS_CONFIG", ""),
                     os.path.join(HERE, "..", "..", "Server", "data", "units_config.txt")):
            if not path or not os.path.exists(path):
                continue
            with open(path, encoding="utf-8") as f:
                for line in f:
                    p = line.rstrip("\n").split("\t")
                    if p[0] != "arma" or len(p) < 16:
                        continue
                    damage, speed, duration, rng = fnum(p[11]), fnum(p[12]), fnum(p[13]), fnum(p[14])
                    if damage <= 0:
                        continue
                    flight = speed * duration
                    reach = rng if rng > 0.1 else flight
                    if flight > 0.1:
                        reach = min(reach, flight * 1.1)
                    if reach <= 0.5:
                        reach = 18.0
                    _reach[p[1]] = max(_reach.get(p[1], 0.0), reach)
            break
    return _reach.get(template, 0.0)


def fnum(x, default=0.0):
    try:
        return float(x)
    except (TypeError, ValueError):
        return default


def make_type(template, **over):
    u = units().get(template)
    if u is None:
        raise KeyError("unknown unit template " + template)
    hp = fnum(u["hp"])
    speed = fnum(u["speed"])
    role = u["role"]
    t = {"name": u["name"] or template, "template": template}
    if role == "BOSS":
        t["boss"] = True
        t["behavior"] = "turret" if speed <= 0.3 else "circle"
        t["aggroRadius"] = 60
        t["firstShotDelay"] = 2.0
    elif role == "MINIBOSS":
        t["miniboss"] = True
        t["behavior"] = "turret" if speed <= 0.3 else "circle"
        t["aggroRadius"] = 45
        t["firstShotDelay"] = 1.5
    elif role in ("wall",):
        t["behavior"] = "idle"
    elif role == "treasure":
        t["behavior"] = "idle"
        t["opensAfterHitSeconds"] = 1.5
    elif role == "wreck":
        t["behavior"] = "idle"
        t["invulnerable"] = True
    elif speed <= 0.3:
        t["behavior"] = "turret" if u["guns"] else "idle"
        if u["guns"]:
            t["aggroRadius"] = 30
    elif "leader" in template.lower():
        t["behavior"] = "circle"
        t["preferredDistance"] = 9
        t["aggroRadius"] = 32
    elif speed >= 6:
        t["behavior"] = "charge"
        t["preferredDistance"] = 6
        t["aggroRadius"] = 30
    else:
        t["behavior"] = "chase"
        t["preferredDistance"] = 7
        t["aggroRadius"] = 28
    if hp >= 1000000 and role not in ("wreck",):
        t["invulnerable"] = True
    # A unit that circles the player farther away than its weapon reaches never shoots (the Munitions Engineer
    # mines had range 6 and circled at 7): come in to about half of the weapon reach.
    reach = weapon_reach(template)
    if reach > 0 and t.get("behavior") in ("chase", "circle", "charge"):
        preferred = t.get("preferredDistance", 12)
        if preferred > reach * 0.75:
            t["preferredDistance"] = round(max(2.0, reach * 0.5), 1)
    t.update(over)
    return t


def add_types(m, templates, overrides=None):
    overrides = overrides or {}
    for tpl in templates:
        if tpl in m.types:
            continue
        m.types[tpl] = make_type(tpl, **overrides.get(tpl, {}))
