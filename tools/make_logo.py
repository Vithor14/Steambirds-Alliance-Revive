#!/usr/bin/env python3
"""
Steambirds Alliance Revive - logo generator.

Draws the logo once as a list of polygons and writes it twice, from the same geometry:
  * logo.svg / icon.svg - vector (paths, no fonts needed: the lettering is drawn here, glyph by glyph)
  * logo.png / icon.png / icon.ico - raster, supersampled with Pillow (transparent background)

The style follows the original game logo (black plate, golden yellow, slanted blocky lettering,
extruded "ALLIANCE", head / wing / tail emblem). Nothing is copied from the original image:
every shape below is drawn from scratch. Only Pillow is needed:

    python tools\make_logo.py                  # writes Branding\
    python tools\make_logo.py --out some\dir --preview previews\
"""
import argparse
import math
import os

from PIL import Image, ImageChops, ImageDraw

W, H = 2000, 1480          # logo canvas (units == pixels of the main PNG)
SS = 3                     # supersampling factor for the PNG

BLACK = "#151019"
YELLOW = "#F8BA24"
EMBER = "#F2622A"          # "REVIVE" accent (not in the original: marks the community revival)

# ----------------------------------------------------------------------------------------------
# Lettering: blocky condensed capitals on a 100-unit cap height (y = 0 top, y = 100 baseline).
# Each glyph is (advance width, [contours]); several contours are filled with the even-odd rule,
# so a second contour inside the first one is a hole.
# ----------------------------------------------------------------------------------------------
GLYPHS = {
    "L": (60, [[(0, 0), (27, 0), (27, 76), (60, 76), (60, 100), (0, 100)]]),
    "I": (27, [[(0, 0), (27, 0), (27, 100), (0, 100)]]),
    "T": (68, [[(0, 0), (68, 0), (68, 23), (47.5, 23), (47.5, 100), (20.5, 100), (20.5, 23), (0, 23)]]),
    "E": (58, [[(0, 0), (58, 0), (58, 22), (27, 22), (27, 39), (52, 39), (52, 61), (27, 61),
                (27, 78), (58, 78), (58, 100), (0, 100)]]),
    "A": (86, [[(28, 0), (58, 0), (86, 100), (59, 100), (53.4, 80), (32.6, 80), (27, 100), (0, 100)],
               [(43, 43), (48.4, 62), (37.6, 62)]]),
    "M": (90, [[(0, 0), (26, 0), (45, 44), (64, 0), (90, 0), (90, 100), (65, 100), (65, 50),
                (53, 78), (37, 78), (25, 50), (25, 100), (0, 100)]]),
    "N": (76, [[(0, 0), (24, 0), (52, 54), (52, 0), (76, 0), (76, 100), (52, 100), (24, 46),
                (24, 100), (0, 100)]]),
    "C": (72, [[(14, 0), (58, 0), (72, 14), (72, 34), (46, 34), (46, 28), (26, 28), (26, 72),
                (46, 72), (46, 66), (72, 66), (72, 86), (58, 100), (14, 100), (0, 86), (0, 14)]]),
    "S": (66, [[(0, 14), (14, 0), (66, 0), (66, 22), (26, 22), (26, 39), (66, 39), (66, 86),
                (52, 100), (0, 100), (0, 78), (40, 78), (40, 61), (0, 61)]]),
    "B": (68, [[(0, 0), (52, 0), (68, 14), (68, 40), (60, 50), (68, 60), (68, 86), (52, 100), (0, 100)],
               [(26, 22), (42, 22), (44, 24), (44, 37), (42, 39), (26, 39)],
               [(26, 61), (42, 61), (44, 63), (44, 76), (42, 78), (26, 78)]]),
    "R": (72, [[(0, 0), (52, 0), (68, 14), (68, 44), (58, 55), (72, 100), (46, 100), (35, 61),
                (26, 61), (26, 100), (0, 100)],
               [(26, 22), (42, 22), (44, 24), (44, 37), (42, 39), (26, 39)]]),
    "D": (70, [[(0, 0), (46, 0), (70, 24), (70, 76), (46, 100), (0, 100)],
               [(26, 22), (40, 22), (44, 26), (44, 74), (40, 78), (26, 78)]]),
    "V": (84, [[(0, 0), (27, 0), (42, 66), (57, 0), (84, 0), (56, 100), (28, 100)]]),
}
SKEW = 0.22                # italic lean (top of every letter shifted right by 0.22 * height)


def layout_text(text, scale, cx, baseline, tracking):
    """Return [(contours, quad)] per letter, in canvas coordinates. quad = slanted bounding box."""
    widths = [GLYPHS[ch][0] for ch in text]
    total = sum(widths) + tracking * (len(text) - 1) + 100 * SKEW
    x = cx - total * scale / 2

    def tf(px, py):
        return (x + (px + (100 - py) * SKEW) * scale, baseline - (100 - py) * scale)

    out = []
    for ch in text:
        w, contours = GLYPHS[ch]
        pts = [[tf(px, py) for px, py in c] for c in contours]
        quad = [tf(0, 0), tf(w, 0), tf(w, 100), tf(0, 100)]
        out.append((pts, quad))
        x += (w + tracking) * scale
    return out


# ----------------------------------------------------------------------------------------------
# Tiny drawing engine: every call draws on the PNG and appends the same shape to the SVG.
# ----------------------------------------------------------------------------------------------
def rgb(hexstr):
    h = hexstr.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def path_d(contours):
    parts = []
    for c in contours:
        parts.append("M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in c) + " Z")
    return " ".join(parts)


class Canvas:
    def __init__(self, w, h, background=None):
        self.w, self.h = w, h
        self.img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
        self.defs, self.body = [], []
        self._n = 0
        self._stack = []
        self.background = background
        self.s, self.ox, self.oy = 1.0, 0.0, 0.0     # current transform (scale, offset)

    def set_transform(self, scale=1.0, ox=0.0, oy=0.0):
        self.s, self.ox, self.oy = scale, ox, oy

    def T(self, contours):
        return [[(self.ox + x * self.s, self.oy + y * self.s) for x, y in c] for c in contours]

    def _id(self, prefix):
        self._n += 1
        return f"{prefix}{self._n}"

    # -- raster helpers -----------------------------------------------------------------------
    def _composite(self, color, mask, x0, y0):
        layer = Image.new("RGBA", mask.size, rgb(color) + (255,))
        layer.putalpha(mask)
        self.img.alpha_composite(layer, (int(x0 * SS), int(y0 * SS)))

    def shape(self, contours, fill, outline=0, outline_color=BLACK, extrude=0, extrude_color=None):
        """Even-odd filled polygon(s) with an optional round outline and downward extrusion."""
        extrude_color = extrude_color or outline_color
        contours = self.T(contours)
        outline *= self.s
        extrude = int(round(extrude * self.s))
        # ---- svg
        pid = self._id("p")
        self.defs.append(f'<path id="{pid}" fill-rule="evenodd" d="{path_d(contours)}"/>')
        g = []
        if extrude:
            for dy in range(extrude, 0, -2):
                g.append(f'<use href="#{pid}" transform="translate(0 {dy})" fill="{extrude_color}" '
                         f'stroke="{extrude_color}" stroke-width="{2 * outline}" stroke-linejoin="round"/>')
        if outline:
            g.append(f'<use href="#{pid}" fill="{outline_color}" stroke="{outline_color}" '
                     f'stroke-width="{2 * outline}" stroke-linejoin="round"/>')
        g.append(f'<use href="#{pid}" fill="{fill}"/>')
        self.body.append("\n".join(g))
        # ---- png
        xs = [p[0] for c in contours for p in c]
        ys = [p[1] for c in contours for p in c]
        m = outline + 3
        x0, y0 = math.floor(min(xs) - m), math.floor(min(ys) - m)
        x1, y1 = math.ceil(max(xs) + m), math.ceil(max(ys) + m + extrude)
        size = ((x1 - x0) * SS, (y1 - y0) * SS)

        def pts(c):
            return [((x - x0) * SS, (y - y0) * SS) for x, y in c]

        fillmask = Image.new("L", size, 0)
        for c in contours:
            t = Image.new("L", size, 0)
            ImageDraw.Draw(t).polygon(pts(c), fill=255)
            fillmask = ImageChops.difference(fillmask, t)
        black = fillmask.copy()
        if outline:
            sm = Image.new("L", size, 0)
            d = ImageDraw.Draw(sm)
            for c in contours:
                p = pts(c)
                d.line(p + [p[0], p[1]], fill=255, width=int(round(2 * outline * SS)), joint="curve")
            black = ImageChops.lighter(black, sm)
        if extrude:
            ex = black.copy()
            for dy in list(range(2, extrude, 2)) + [extrude]:
                ex = ImageChops.lighter(ex, ImageChops.offset(black, 0, dy * SS))
            self._composite(extrude_color, ex, x0, y0)
        if outline:
            self._composite(outline_color, black, x0, y0)
        self._composite(fill, fillmask, x0, y0)

    # -- groups whose pieces are cut by "erase" shapes (holes and slits through black plates) --
    def begin_group(self):
        self._stack.append((self.img, len(self.body)))
        self.img = Image.new("RGBA", self.img.size, (0, 0, 0, 0))

    def end_group(self, erase_contours):
        erase_contours = self.T(erase_contours)
        parent, start = self._stack.pop()
        if erase_contours:
            mask = Image.new("L", self.img.size, 0)
            d = ImageDraw.Draw(mask)
            for c in erase_contours:
                d.polygon([(x * SS, y * SS) for x, y in c], fill=255)
            alpha = ImageChops.subtract(self.img.getchannel("A"), mask)
            self.img.putalpha(alpha)
        parent.alpha_composite(self.img)
        self.img = parent
        inner = self.body[start:]
        del self.body[start:]
        mid = self._id("m")
        cuts = path_d(erase_contours)
        self.defs.append(f'<mask id="{mid}" maskUnits="userSpaceOnUse" x="0" y="0" width="{self.w}" '
                         f'height="{self.h}"><rect width="{self.w}" height="{self.h}" fill="#fff"/>'
                         f'<path d="{cuts}" fill="#000"/></mask>')
        self.body.append(f'<g mask="url(#{mid})">\n' + "\n".join(inner) + "\n</g>")

    # -- output -------------------------------------------------------------------------------
    def svg_text(self, title):
        bg = f'<rect width="{self.w}" height="{self.h}" fill="{self.background}"/>\n' if self.background else ""
        return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {self.w} {self.h}" '
                f'width="{self.w}" height="{self.h}" role="img" aria-label="{title}">\n'
                f'<title>{title}</title>\n<defs>\n' + "\n".join(self.defs) + "\n</defs>\n" + bg +
                "\n".join(self.body) + "\n</svg>\n")

    def png(self, width=None):
        out = self.img.resize((self.w, self.h), Image.LANCZOS)
        if width and width != self.w:
            out = out.resize((width, round(self.h * width / self.w)), Image.LANCZOS)
        return out


# ----------------------------------------------------------------------------------------------
# Geometry helpers
# ----------------------------------------------------------------------------------------------
CX = W / 2


def mirror(pts):
    return [(W - x, y) for x, y in pts]


def rotated_rect(c, length, thick, angle):
    """Rectangle centred on c, long side along `angle` degrees (clockwise on screen)."""
    a = math.radians(angle)
    u, v = (math.cos(a), math.sin(a)), (-math.sin(a), math.cos(a))
    return [(c[0] + su * u[0] * length / 2 + sv * v[0] * thick / 2,
             c[1] + su * u[1] * length / 2 + sv * v[1] * thick / 2)
            for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1))]


def local(c, angle, du, dv):
    a = math.radians(angle)
    return (c[0] + du * math.cos(a) - dv * math.sin(a), c[1] + du * math.sin(a) + dv * math.cos(a))


def hexagon(c, r, angle):
    return [(c[0] + r * math.cos(math.radians(angle + 60 * k)),
             c[1] + r * math.sin(math.radians(angle + 60 * k))) for k in range(6)]


ANG = 24
POD_C = (205, 165)


def draw_emblem(cv, ember_cells=True):
    """Head, wings and tail. Everything is black plates and yellow plates, like the original emblem."""
    pod = rotated_rect(POD_C, 330, 200, ANG)
    barrel_c = local(POD_C, ANG, 165 + 16 + 150, 0)
    barrel = rotated_rect(barrel_c, 300, 225, ANG)
    bar2 = [(60, 300), (800, 478), (800, 560), (445, 520), (85, 432)]
    bar3 = [(205, 520), (520, 552), (500, 660), (238, 640)]
    root = [(660, 236), (884, 222), (884, 540), (590, 500)]

    cells = []                                   # hexagon cells of the left rocket pod
    for row, dv in enumerate((-42, 38)):
        for k in range(3):
            du = (-105 if row == 0 else -72) + 67 * k
            cells.append(hexagon(local(POD_C, ANG, du, dv), 27, ANG))
    right_cells = [[local(POD_C, ANG, -40, -50), local(POD_C, ANG, -40, 50), local(POD_C, ANG, 50, 0)],
                   hexagon(local(POD_C, ANG, 118, -36), 14, ANG)]
    slits = [[(150, 296), (174, 302), (150, 470), (126, 464)],
             [(212, 312), (226, 316), (206, 470), (192, 466)]]

    # wings: black slabs, slits cut all the way through
    cv.begin_group()
    for piece in (pod, barrel, bar2, bar3, root):
        cv.shape([piece], BLACK)
        cv.shape([mirror(piece)], BLACK)
    cv.end_group(slits + [mirror(s) for s in slits])
    # the cells are not cut through: they glow (ember), the one spot of colour in the wings
    cell_color = EMBER if ember_cells else BLACK
    for c in cells:
        cv.shape([c], cell_color)
    for c in right_cells:
        cv.shape([mirror(c)], cell_color)

    # tail (three plates)
    tail_mid = [(880, 1000), (1120, 1000), (1132, 1215), (1000, 1385), (868, 1215)]
    tail_l = [(700, 1000), (858, 1000), (850, 1195), (712, 1085)]
    for plate in (tail_l, mirror(tail_l), tail_mid):
        cv.shape([plate], YELLOW, outline=13)
    for x in (940, 1060):
        cv.shape([hexagon((x, 1190), 11, 0)], BLACK)

    # head plate with its visor slot and two rivets
    head = [(800, 70), (1200, 70), (1262, 150), (1112, 205), (1160, 470), (840, 470), (888, 205), (738, 150)]
    cv.shape([head], YELLOW, outline=14)
    cv.shape([[(974, 96), (1046, 96), (1034, 124), (986, 124)]], EMBER if ember_cells else BLACK)
    for x in (872, 1128):
        cv.shape([hexagon((x, 436), 11, 0)], BLACK)


def build_logo():
    cv = Canvas(W, H)
    draw_emblem(cv)

    top = layout_text("STEAMBIRDS", 1.5, CX, 640, 5)
    cv.shape([[(445, 625), (1560, 625), (1556, 695), (385, 695)]], BLACK)   # joins the two rows
    for _, quad in top:
        cv.shape([quad], BLACK, outline=8)
    for contours, _ in top:
        cv.shape(contours, YELLOW, outline=11)

    main = layout_text("ALLIANCE", 2.6, CX, 930, 5)
    for _, quad in main:
        cv.shape([quad], BLACK, outline=10)
    for contours, _ in main:
        cv.shape(contours, YELLOW, outline=14, extrude=26)

    # REVIVE banner: black plate across the tail, ember lettering, yellow stripes on both sides
    banner = [(580, 985), (1480, 985), (1440, 1160), (540, 1160)]
    cv.shape([banner], BLACK, outline=6)
    word = layout_text("REVIVE", 1.25, CX + 10, 1124, 26)
    for contours, _ in word:
        cv.shape(contours, EMBER)
    for side in (-1, 1):
        for k in range(3):
            x = CX + 10 - 352 - 28 * k if side < 0 else CX + 10 + 356 + 28 * k
            bar = [(x + 14, 1030), (x + 30, 1030), (x + 14 - 22, 1124), (x - 2 - 22, 1124)]
            cv.shape([bar], YELLOW)
    return cv



def build_icon(letter="R", letter_color=YELLOW):
    """Square emblem for avatars / launcher icon: the emblem with one big letter on the black plate."""
    cv = Canvas(1024, 1024)
    cv.set_transform(0.5, 12, 160)
    draw_emblem(cv)
    cv.shape([[(480, 490), (1520, 490), (1470, 740), (1370, 1005), (630, 1005), (530, 740)]], BLACK)
    glyph = layout_text(letter, 4.8, CX, 985, 0)
    for _, quad in glyph:
        cv.shape([quad], BLACK, outline=10)
    for contours, _ in glyph:
        cv.shape(contours, letter_color, outline=14, extrude=26)
    return cv



def save_previews(cv, folder, prefix):
    os.makedirs(folder, exist_ok=True)
    base_png = cv.png()
    for name, bg in (("light", (236, 232, 222, 255)), ("dark", (38, 44, 56, 255))):
        base = Image.new("RGBA", base_png.size, bg)
        base.alpha_composite(base_png)
        half = (base_png.width // 2, base_png.height // 2)
        base.convert("RGB").resize(half, Image.LANCZOS).save(os.path.join(folder, f"{prefix}_{name}.png"))


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="Generate the Steambirds Alliance Revive logo files.")
    ap.add_argument("--out", default=os.path.join(here, "..", "Branding"), help="output folder (default: Branding)")
    ap.add_argument("--preview", help="also write preview PNGs on light and dark backgrounds to this folder")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    logo = build_logo()
    with open(os.path.join(args.out, "logo.svg"), "w", encoding="utf-8") as f:
        f.write(logo.svg_text("Steambirds Alliance Revive"))
    logo.png().save(os.path.join(args.out, "logo.png"))

    icon = build_icon()
    with open(os.path.join(args.out, "icon.svg"), "w", encoding="utf-8") as f:
        f.write(icon.svg_text("Steambirds Alliance Revive - emblem"))
    icon_png = icon.png()
    icon_png.save(os.path.join(args.out, "icon.png"))
    icon_png.save(os.path.join(args.out, "icon.ico"),
                  sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

    if args.preview:
        save_previews(logo, args.preview, "logo")
        save_previews(icon, args.preview, "icon")
    print("written to", os.path.abspath(args.out))
