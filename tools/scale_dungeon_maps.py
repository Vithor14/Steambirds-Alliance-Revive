#!/usr/bin/env python3
"""Shrinks dungeon map files (Server/maps/dungeon_*.json) by a factor on BOTH axes (default 0.5 = a quarter of the area).

Usage:  python tools/scale_dungeon_maps.py [--factor 0.5] [--maps Server/maps] [files...]

What it does (the map editor and the server read the result like any other map):
  - every u/v of the file is multiplied by the factor (points, groups, areas, lines, checkpoints, signs, spawn,
    unit positions of the encounters and waves, routes, decor, buildings);
  - the floor drawing is resampled: each 2x2 block of sub-cells becomes one sub-cell ('#' if 3 or more of the 4
    are walls, '.' if there is any free floor, '-' otherwise). Gates (sj, siFrom..siTo) follow the same rule;
  - the result is moved by a few sub-cells so the drawing starts on a tile border (a tile is 4x4 sub-cells);
  - the file gets "scaled": factor, and the script refuses to scale a file twice.
Unit positions that end up on a wall are moved to the nearest free sub-cell.
Only factors 1/n (0.5, 0.25...) are supported for the drawing.
"""
import argparse
import glob
import json
import math
import os
import sys


def scale_drawing(d, n):
    dr = d['floor']['drawing']
    lines = dr['lines']
    h = len(lines)
    cells = {}
    for k, line in enumerate(lines):
        sj = dr['sjStart'] + (h - 1 - k)
        for c, ch in enumerate(line):
            cells[(dr['siStart'] + c, sj)] = ch
    blocks = {}
    for (si, sj), ch in cells.items():
        blocks.setdefault((si // n, sj // n), []).append(ch)

    def pick(chars):
        walls = chars.count('#')
        free = chars.count('.')
        if free and walls < 3 * len(chars) / 4:
            return '.'
        if walls:
            return '#'
        return '-'

    new = {k: pick(v) for k, v in blocks.items()}
    si0 = min(k[0] for k in new)
    si1 = max(k[0] for k in new)
    sj0 = min(k[1] for k in new)
    sj1 = max(k[1] for k in new)
    # Move the drawing so that it starts on a tile border (4 sub-cells).
    shift_si = (-si0) % 4
    shift_sj = (-sj0) % 4
    width = si1 - si0 + 1
    height = sj1 - sj0 + 1
    width_p = width + (-width) % 4
    height_p = height + (-height) % 4
    out = []
    for sj in range(sj0 + height_p - 1, sj0 - 1, -1):
        out.append(''.join(new.get((si, sj), '-') for si in range(si0, si0 + width_p)))
    dr['siStart'] = si0 + shift_si
    dr['sjStart'] = sj0 + shift_sj
    dr['lines'] = out
    return shift_si, shift_sj


class Grid:
    def __init__(self, d):
        dr = d['floor']['drawing']
        self.cells = {}
        h = len(dr['lines'])
        for k, line in enumerate(dr['lines']):
            sj = dr['sjStart'] + (h - 1 - k)
            for c, ch in enumerate(line):
                self.cells[(dr['siStart'] + c, sj)] = ch

    def free(self, si, sj):
        return self.cells.get((si, sj)) == '.'

    def nearest_free(self, si, sj):
        for r in range(0, 12):
            best = None
            for a in range(-r, r + 1):
                for b in range(-r, r + 1):
                    if max(abs(a), abs(b)) != r:
                        continue
                    if self.free(si + a, sj + b):
                        dist = a * a + b * b
                        if best is None or dist < best[0]:
                            best = (dist, si + a, sj + b)
            if best:
                return best[1], best[2]
        return si, sj


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--factor', type=float, default=0.5)
    ap.add_argument('--maps', default=os.path.join('Server', 'maps'))
    ap.add_argument('files', nargs='*')
    args = ap.parse_args()
    n = round(1.0 / args.factor)
    if abs(1.0 / n - args.factor) > 1e-6 or n < 2:
        sys.exit('only factors 1/n (0.5, 0.25...) are supported')
    files = args.files or sorted(glob.glob(os.path.join(args.maps, 'dungeon_*.json')))
    for path in files:
        with open(path, encoding='utf-8') as f:
            d = json.loads(f.read())
        if d.get('scaled'):
            print(os.path.basename(path), 'already scaled by', d['scaled'], '- skipped')
            continue
        shift_si, shift_sj = scale_drawing(d, n)
        du, dv = 3.0 * shift_si, 3.0 * shift_sj
        k = args.factor

        def su(u):
            return round(u * k + du, 2)

        def sv(v):
            return round(v * k + dv, 2)

        def point(p):
            if 'u' in p:
                p['u'] = su(p['u'])
            if 'v' in p:
                p['v'] = sv(p['v'])

        if d.get('spawn'):
            point(d['spawn'])
        for p in d.get('points', {}).values():
            point(p)
        for lst in d.get('groups', {}).values():
            for p in lst:
                point(p)
        for a in d.get('areas', {}).values():
            a['minU'], a['maxU'] = su(a['minU']), su(a['maxU'])
            a['minV'], a['maxV'] = sv(a['minV']), sv(a['maxV'])
        d['lines'] = {name: sv(v) for name, v in d.get('lines', {}).items()}
        for c in d.get('checkpoints', []):
            point(c)
            c['activeAtV'] = sv(c['activeAtV'])
        for s in d.get('signs', []):
            point(s)
            s['radius'] = max(7, round(s.get('radius', 14) * k))
        for e in d.get('decor', []) + d.get('buildings', []):
            point(e)
        for r in d.get('routes', {}).values():
            for p in r:
                point(p)
        for g in d.get('gates', []):
            g['sj'] = g['sj'] // n + shift_sj
            g['siFrom'] = g['siFrom'] // n + shift_si
            g['siTo'] = g['siTo'] // n + shift_si
        for g in d.get('sideGates', []):
            g['si'] = g['si'] // n + shift_si
            g['sjFrom'] = g['sjFrom'] // n + shift_sj
            g['sjTo'] = g['sjTo'] // n + shift_sj

        grid = Grid(d)
        moved = 0

        def ensure_free(p):
            nonlocal moved
            si, sj = math.floor(p['u'] / 3), math.floor(p['v'] / 3)
            if not grid.free(si, sj):
                ni, nj = grid.nearest_free(si, sj)
                p['u'] = round(ni * 3 + 1.5, 2)
                p['v'] = round(nj * 3 + 1.5, 2)
                moved += 1

        for enc in d.get('encounters', []):
            for grp in enc.get('enemies', []):
                for p in grp.get('pos', []):
                    point(p)
                    ensure_free(p)
            for w in enc.get('waves', []):
                for grp in w.get('groups', []):
                    for p in grp.get('pos', []):
                        point(p)
                        ensure_free(p)
        for p in [d.get('spawn')] + list(d.get('points', {}).values()) + d.get('checkpoints', []):
            if p:
                ensure_free(p)

        d['scaled'] = args.factor
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            json.dump(d, f, ensure_ascii=False, indent=2)
            f.write('\n')
        print(os.path.basename(path), 'scaled by', args.factor, '- shift', (shift_si, shift_sj), '-', moved, 'position(s) moved to free floor')


if __name__ == '__main__':
    main()
