"""Draws a preview sheet of the dungeon maps (grid view, +v up). Usage: python preview_maps.py <maps folder> <out.png>"""

import json
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

S = 3  # pixels per sub-cell


def draw_map(m):
    d = m["floor"]["drawing"]
    lines = d["lines"]
    si0, sj0 = d["siStart"], d["sjStart"]
    h, w = len(lines), len(lines[0])
    img = Image.new("RGB", (w * S, h * S + 14), (10, 10, 16))
    dr = ImageDraw.Draw(img)
    for row, text in enumerate(lines):
        for col, ch in enumerate(text):
            if ch == ".":
                c = (170, 160, 140)
            elif ch == "#":
                c = (60, 55, 70)
            else:
                continue
            dr.rectangle([col * S, row * S + 14, col * S + S - 1, row * S + 14 + S - 1], fill=c)

    def px(u, v):
        x = (u / 3.0 - si0) * S
        y = (sj0 + h - v / 3.0) * S + 14
        return x, y

    unit_types = m["types"]
    for e in m["encounters"]:
        entries = [(i, False) for i in e.get("enemies", [])]
        for wv in e.get("waves", []):
            entries += [(g, True) for g in wv["groups"]]
        for i, wave in entries:
            t = unit_types[i["type"]]
            if t.get("boss"):
                col, r = (230, 40, 40), 4
            elif t.get("miniboss"):
                col, r = (250, 140, 20), 3
            elif t.get("opensAfterHitSeconds"):
                col, r = (250, 220, 40), 3
            elif wave:
                col, r = (180, 80, 220), 1.5
            elif t.get("behavior") in ("turret", "idle"):
                col, r = (90, 160, 230), 1.5
            else:
                col, r = (40, 120, 255), 1.5
            for p in i["pos"]:
                x, y = px(p["u"], p["v"])
                dr.ellipse([x - r, y - r, x + r, y + r], fill=col)
    for g in m["gates"] + m.get("sideGates", []):
        if "sj" in g:
            x0, y0 = px(g["siFrom"] * 3, g["sj"] * 3 + 3)
            x1, y1 = px(g["siTo"] * 3 + 3, g["sj"] * 3)
        else:
            x0, y0 = px(g["si"] * 3, g["sjTo"] * 3 + 3)
            x1, y1 = px(g["si"] * 3 + 3, g["sjFrom"] * 3)
        dr.rectangle([x0, y0, x1, y1], fill=(255, 255, 255) if not g.get("damageable") else (150, 150, 150))
    x, y = px(m["spawn"]["u"], m["spawn"]["v"])
    dr.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(40, 220, 80))
    if "portal" in m["points"]:
        p = m["points"]["portal"]
        x, y = px(p["u"], p["v"])
        dr.rectangle([x - 3, y - 3, x + 3, y + 3], outline=(40, 230, 230))
    for s in m["signs"]:
        x, y = px(s["u"], s["v"])
        dr.rectangle([x - 1, y - 1, x + 1, y + 1], fill=(255, 255, 255))
    dr.text((2, 1), m["id"], fill=(255, 255, 255))
    return img


def main(folder, out):
    imgs = []
    for fn in sorted(os.listdir(folder)):
        if fn.endswith(".json"):
            imgs.append(draw_map(json.load(open(os.path.join(folder, fn), encoding="utf-8"))))
    pad = 8
    width = sum(i.width for i in imgs) + pad * (len(imgs) + 1)
    height = max(i.height for i in imgs) + 2 * pad
    sheet = Image.new("RGB", (width, height), (0, 0, 0))
    x = pad
    for i in imgs:
        sheet.paste(i, (x, height - pad - i.height))
        x += i.width + pad
    sheet.save(out)
    print(out, sheet.size)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
