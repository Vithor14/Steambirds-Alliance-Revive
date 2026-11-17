"""Checks the generated dungeon maps. Usage: python check_maps.py <maps folder> <units_config.txt> <cfg190828.json>"""

import json
import math
import os
import re
import sys


def load_open(m):
    d = m["floor"]["drawing"]
    si0, sj0, lines = d["siStart"], d["sjStart"], d["lines"]
    n = len(lines)
    cells = {}
    for row, text in enumerate(lines):
        sj = sj0 + (n - 1 - row)
        for col, ch in enumerate(text):
            cells[(si0 + col, sj)] = ch
    return cells


def cell_of(u, v):
    return int(math.floor(u / 3.0)), int(math.floor(v / 3.0))


def main(folder, units_file, cfg_file):
    units_txt = open(units_file, encoding="utf-8", errors="replace").read()
    known = set(re.findall(r"^unidade\t([^\t]+)\t", units_txt, re.M))
    gun_count = {}
    for name in re.findall(r"^arma\t([^\t]+)\t", units_txt, re.M):
        gun_count[name] = gun_count.get(name, 0) + 1
    cfg = json.load(open(cfg_file, encoding="utf-8"))
    lore = cfg["Texts/en-US/lore"]
    bark = cfg["Texts/en-US/bark"]
    problems = 0
    for fn in sorted(os.listdir(folder)):
        if not fn.endswith(".json"):
            continue
        m = json.load(open(os.path.join(folder, fn), encoding="utf-8"))
        cells = load_open(m)
        errs = []

        def on_floor(p, what):
            c = cell_of(p["u"], p["v"])
            if cells.get(c) != ".":
                errs.append("%s is outside the free floor: %s -> cell %s = %r" % (what, (p["u"], p["v"]), c, cells.get(c)))

        on_floor(m["spawn"], "spawn")
        for k, p in m["points"].items():
            on_floor(p, "ponto " + k)
        for cp in m["checkpoints"]:
            on_floor(cp, "checkpoint " + cp["name"])
        for s in m["signs"]:
            on_floor(s, "placa " + s["key"])
            if s["key"] not in lore:
                errs.append("sign without text in the lore table: " + s["key"])
        unit_types = m["types"]
        for tid, t in unit_types.items():
            if t["template"] not in known:
                errs.append("template does not exist in client 201219: " + t["template"])
            for k in ("onDeath",):
                if k in t and t[k] not in unit_types:
                    errs.append("type %s: %s=%s has no type" % (tid, k, t[k]))
            for f in t.get("phases", []):
                if "summon" in f and f["summon"] not in unit_types:
                    errs.append("type %s summons %s which has no type" % (tid, f["summon"]))
            for s in t.get("sequence", []):
                if s not in unit_types:
                    errs.append("sequence without type: " + s)
            inv = t.get("periodicSummon")
            if inv and inv.get("type") not in unit_types:
                errs.append("type %s: periodicSummon of %s has no type" % (tid, inv.get("type")))
            chest = t.get("dpsChest")
            if chest:
                for k in ("reward", "fail"):
                    if chest.get(k) and chest[k] not in unit_types:
                        errs.append("type %s: dpsChest.%s=%s has no type" % (tid, k, chest[k]))
                if t.get("opensAfterHitSeconds"):
                    errs.append("type %s: dpsChest with opensAfterHitSeconds (the chest would open by itself)" % tid)
            guns = gun_count.get(t["template"], 0)
            entry_lists = [t.get("guns", [])] + [f.get("guns", []) for f in t.get("phases", [])]
            for entries in entry_lists:
                for g in entries or []:
                    if not (0 <= g < guns):
                        errs.append("type %s: gun %d does not exist (the unit has %d)" % (tid, g, guns))
            for k, key in t.get("barks", {}).items():
                if ".." in key or key.endswith("_"):
                    continue
                if key not in bark:
                    errs.append("bark without text in the bark table: %s (%s.%s)" % (key, tid, k))
        areas = m["areas"]
        for e in m["encounters"]:
            area = areas[e["area"]]
            entries = list(e.get("enemies", []))
            for w in e.get("waves", []):
                entries += w["groups"]
            for i in entries:
                if i["type"] not in unit_types:
                    errs.append("encounter %s: type %s is not defined" % (e["room"], i["type"]))
                for p in i["pos"]:
                    on_floor(p, "%s/%s" % (e["room"], i["type"]))
                    if not (area["minU"] <= p["u"] <= area["maxU"] and area["minV"] <= p["v"] <= area["maxV"]):
                        errs.append("%s/%s outside the room area" % (e["room"], i["type"]))
        # rooms must not touch each other except through their corridors
        names = list(areas)
        for i in range(len(names)):
            for j in range(i + 1, len(names)):
                a, b = areas[names[i]], areas[names[j]]
                gap = 6.0  # one wall tile at least... approx: rooms closer than 6 u share a wall
                if (a["minU"] - gap < b["maxU"] and b["minU"] - gap < a["maxU"] and
                        a["minV"] - gap < b["maxV"] and b["minV"] - gap < a["maxV"]):
                    # allowed if they are connected in the chain (corridor of 0 length is not used)
                    errs.append("rooms too close or overlapping: %s and %s" % (names[i], names[j]))
        rooms = {e["room"] for e in m["encounters"]} | set(areas)

        def check_condition(cond, what):
            for part in (cond or "").split("+"):
                part = part.strip()
                if part.startswith("cleared:") and part[6:] not in rooms:
                    errs.append("%s: room %s does not exist" % (what, part[6:]))
                elif part.startswith("death:") and part[6:] not in unit_types:
                    errs.append("%s: type %s does not exist" % (what, part[6:]))

        for e in m["encounters"]:
            if str(e.get("wakes", "")).startswith("death:"):
                check_condition(e["wakes"], "encontro %s acorda" % e["room"])
            for i in e.get("enemies", []):
                if i.get("wakes"):
                    check_condition(i["wakes"], "encontro %s/%s acorda" % (e["room"], i["type"]))
        if m.get("exit"):
            check_condition(m["exit"].get("when"), "exit")
            if m["exit"].get("point") and m["exit"]["point"] not in m["points"]:
                errs.append("exit: point %s does not exist" % m["exit"]["point"])
        # the client keeps 54 tiles on each side of the grid origin
        d0 = m["floor"]["drawing"]
        sj_lo, sj_hi = d0["sjStart"], d0["sjStart"] + len(d0["lines"]) - 1
        si_lo, si_hi = d0["siStart"], d0["siStart"] + len(d0["lines"][0]) - 1
        if min(si_lo, sj_lo) < -54 * 4 or max(si_hi, sj_hi) >= 54 * 4:
            errs.append("floor goes past the client grid (54 tiles on each side): si %d..%d sj %d..%d" %
                        (si_lo, si_hi, sj_lo, sj_hi))
        for g in m["gates"] + m.get("sideGates", []):
            check_condition(g.get("opensWhen"), "gate %s" % g["name"])
            if g.get("room") and g["room"] not in rooms:
                errs.append("gate %s: room %s does not exist" % (g["name"], g["room"]))
            if "sj" in g:
                row = [(si, g["sj"]) for si in range(g["siFrom"], g["siTo"] + 1)]
                left, right = (g["siFrom"] - 1, g["sj"]), (g["siTo"] + 1, g["sj"])
            else:
                row = [(g["si"], sj) for sj in range(g["sjFrom"], g["sjTo"] + 1)]
                left, right = (g["si"], g["sjFrom"] - 1), (g["si"], g["sjTo"] + 1)
            if any(cells.get(c) != "." for c in row):
                errs.append("gate %s goes through a wall" % g["name"])
            if cells.get(left) == "." or cells.get(right) == ".":
                errs.append("gate %s does not close the whole passage" % g["name"])
            if g["type"] not in unit_types:
                errs.append("gate %s: type %s is not defined" % (g["name"], g["type"]))
        n_enemies = sum(len(i["pos"]) for e in m["encounters"] for i in e.get("enemies", []))
        n_wave = sum(len(g["pos"]) for e in m["encounters"] for w in e.get("waves", []) for g in w["groups"])
        d = m["floor"]["drawing"]
        print("%-22s %3d lines x %3d  rooms=%2d types=%3d enemies=%3d (+%d in waves) gates=%d  errors=%d" % (
            fn, len(d["lines"]), len(d["lines"][0]), len(areas), len(unit_types), n_enemies, n_wave, len(m["gates"]) + len(m.get("sideGates", [])), len(errs)))
        for e in errs:
            print("    - " + e)
        problems += len(errs)
    print("total problems:", problems)


if __name__ == "__main__":
    main(*sys.argv[1:4])
