#!/usr/bin/env python3
r"""
build_editor_assets.py - makes the thumbnails the map editor of the launcher shows for floor pieces, walls and
unit art (enemies, gates, chests).  Needs Python 3.8+, numpy and Pillow (pip install numpy pillow).

Input  (all produced by the project tools; nothing is downloaded):
  --assets   the AssetRipper output folder that has Mesh\ and PrefabHierarchyObject\ with the .glb files
             (default C:\\Steambirds\\Assets\\Assets)
  --ground   ground_assets.txt written by the Steambirds Quieter plugin (BepInEx folder of the game)
  --units    units_config.txt (server\\data) - its "arte" column names the prefab that draws each unit
Output (--out, default C:\\Steambirds\\Launcher\\editor-assets):
  ground\\<asset name, lower case>.png   top view of every floor piece / wall / decoration with a mesh
  units\\<art name, lower case>.png      top view of the art prefab of every unit that has one
  thumbs.json                           for every picture, "half": half of the side of the picture in world units
                                        (the editor draws the picture at that size)

The pictures are made from the geometry only (AssetRipper did not export materials, so there are no game
textures): lighting from above plus the vertex colors of the mesh.  They are meant to show the shape and size of
each block, not to look like a screenshot.  Game files are not part of the GitHub repository, so neither are the
pictures: every person who builds the launcher runs this once (Build launcher.ps1 copies the folder to dist).

Units: the picture is made in the local frame of the prefab with the nose (+y) pointing up.
Floor pieces: local x/y of the mesh, +y up in the picture.  Up in the game is -z, so the camera looks along +z.

---------------------------------------------------------------------------------------------------------------
Faz as miniaturas que o editor de mapas do launcher mostra (pecas de chao, paredes e arte das unidades).
Precisa de Python 3.8+, numpy e Pillow.  As imagens vem so da geometria (o AssetRipper nao exportou materiais).
"""
import argparse
import json
import os
import re
import struct
import sys

try:
    import numpy as np
    from PIL import Image, ImageDraw
except ImportError:
    print("This tool needs numpy and Pillow:  pip install numpy pillow")
    sys.exit(1)

LIGHT = np.array([-0.35, 0.35, -0.87])          # unit vector pointing to the light (up is -z)
LIGHT = LIGHT / np.linalg.norm(LIGHT)
SUPERSAMPLE = 2


# --------------------------------------------------------------------------------------------- glb reading

COMPONENT = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
TYPE_SIZE = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
NP_DTYPE = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}


def read_glb(path):
    with open(path, "rb") as f:
        data = f.read()
    magic, _version, _length = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF":
        raise ValueError("not a glb file")
    offset = 12
    js = None
    binary = b""
    while offset < len(data):
        chunk_len, chunk_type = struct.unpack_from("<I4s", data, offset)
        offset += 8
        chunk = data[offset:offset + chunk_len]
        offset += chunk_len
        if chunk_type == b"JSON":
            js = json.loads(chunk.decode("utf-8"))
        elif chunk_type.startswith(b"BIN"):
            binary = chunk
    if js is None:
        raise ValueError("no JSON chunk")
    return js, binary


def accessor(js, binary, index):
    acc = js["accessors"][index]
    view = js["bufferViews"][acc["bufferView"]]
    comp = acc["componentType"]
    count = acc["count"]
    width = TYPE_SIZE[acc["type"]]
    dtype = np.dtype(NP_DTYPE[comp])
    view_start = view.get("byteOffset", 0)
    acc_offset = acc.get("byteOffset", 0)
    stride = view.get("byteStride", 0)
    item = dtype.itemsize * width
    if stride and stride != item:
        # Interleaved attributes: row r starts at view_start + r * stride, the attribute at acc_offset inside it.
        length = stride * (count - 1) + acc_offset + item
        raw = np.frombuffer(binary, dtype=np.uint8, count=length, offset=view_start)
        index = np.arange(count)[:, None] * stride + acc_offset + np.arange(item)[None, :]
        arr = np.ascontiguousarray(raw[index]).view(dtype).reshape(count, width)
    else:
        arr = np.frombuffer(binary, dtype=dtype, count=count * width, offset=view_start + acc_offset).reshape(count, width)
    arr = arr.astype(np.float64)
    if acc.get("normalized"):
        arr = arr / float(np.iinfo(NP_DTYPE[comp]).max)
    return arr


def node_matrix(node):
    if "matrix" in node:
        return np.array(node["matrix"], dtype=np.float64).reshape(4, 4).T
    t = node.get("translation", [0, 0, 0])
    q = node.get("rotation", [0, 0, 0, 1])
    s = node.get("scale", [1, 1, 1])
    x, y, z, w = q
    r = np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ])
    m = np.eye(4)
    m[:3, :3] = r * np.array(s)[None, :]
    m[:3, 3] = t
    return m


def collect_triangles(js, binary):
    """Returns (positions[n,3], normals[n,3], colors[n,3] or None) for all triangles, in prefab space."""
    nodes = js.get("nodes", [])
    scenes = js.get("scenes", [])
    roots = scenes[0]["nodes"] if scenes else list(range(len(nodes)))
    out_pos, out_nrm, out_col = [], [], []
    has_color = False

    def visit(index, parent):
        nonlocal has_color
        node = nodes[index]
        matrix = parent @ node_matrix(node)
        if "mesh" in node:
            mesh = js["meshes"][node["mesh"]]
            for prim in mesh["primitives"]:
                if prim.get("mode", 4) != 4:
                    continue
                attrs = prim["attributes"]
                pos = accessor(js, binary, attrs["POSITION"])
                nrm = accessor(js, binary, attrs["NORMAL"]) if "NORMAL" in attrs else None
                col = accessor(js, binary, attrs["COLOR_0"]) if "COLOR_0" in attrs else None
                if "indices" in prim:
                    idx = accessor(js, binary, prim["indices"]).astype(np.int64).reshape(-1)
                else:
                    idx = np.arange(len(pos))
                idx = idx[: (len(idx) // 3) * 3].reshape(-1, 3)
                p = np.c_[pos, np.ones(len(pos))] @ matrix.T
                p = p[:, :3]
                if nrm is not None:
                    n = nrm @ matrix[:3, :3].T
                    length = np.linalg.norm(n, axis=1)
                    length[length == 0] = 1
                    n = n / length[:, None]
                else:
                    n = None
                out_pos.append(p[idx])
                if n is not None:
                    out_nrm.append(n[idx])
                else:
                    out_nrm.append(np.zeros((len(idx), 3, 3)))
                if col is not None:
                    out_col.append(col[idx][:, :, :3])
                    has_color = True
                else:
                    out_col.append(np.full((len(idx), 3, 3), -1.0))
        for child in node.get("children", []):
            visit(child, matrix)

    for r in roots:
        visit(r, np.eye(4))
    if not out_pos:
        return None
    pos = np.concatenate(out_pos)
    nrm = np.concatenate(out_nrm)
    col = np.concatenate(out_col) if has_color else None
    return pos, nrm, col


# ------------------------------------------------------------------------------------------------ rendering

def face_normals(pos):
    a = pos[:, 1] - pos[:, 0]
    b = pos[:, 2] - pos[:, 0]
    n = np.cross(a, b)
    length = np.linalg.norm(n, axis=1)
    length[length == 0] = 1
    return n / length[:, None]


def render(tris, half, size):
    """Top view. tris = (pos, nrm, col). The picture covers [-half, half] in x and y, +y up."""
    pos, nrm, col = tris
    # Triangles whose winding is not consistent with the normals: use the vertex normals when present.
    fn = face_normals(pos)
    vn = nrm.mean(axis=1)
    has_vn = np.linalg.norm(vn, axis=1) > 0.01
    normal = np.where(has_vn[:, None], vn, fn)
    length = np.linalg.norm(normal, axis=1)
    length[length == 0] = 1
    normal = normal / length[:, None]

    shade = 0.42 + 0.58 * np.clip(normal @ LIGHT, 0, 1)

    z = pos[:, :, 2].mean(axis=1)
    zmin, zmax = float(z.min()), float(z.max())
    span = max(zmax - zmin, 1e-6)
    height = 1.0 - (z - zmin) / span                     # 1 = highest (smallest z)
    if col is not None:
        base = col.mean(axis=1)
        white = np.all(base > 0.92, axis=1) | np.all(base < 0, axis=1)
        grey = np.stack([0.50 + 0.30 * height, 0.54 + 0.30 * height, 0.60 + 0.28 * height], axis=1)
        base = np.where(white[:, None], grey, base)
    else:
        base = np.stack([0.50 + 0.30 * height, 0.54 + 0.30 * height, 0.60 + 0.28 * height], axis=1)
    rgb = np.clip(base * shade[:, None] * 255.0, 0, 255).astype(np.uint8)

    big = size * SUPERSAMPLE
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    scale = big / (2.0 * half)
    order = np.argsort(-z)                               # farthest (largest z) first
    for i in order:
        tri = pos[i]
        pts = [((p[0] + half) * scale, (half - p[1]) * scale) for p in tri]
        draw.polygon(pts, fill=(int(rgb[i][0]), int(rgb[i][1]), int(rgb[i][2]), 255))
    return img.resize((size, size), Image.LANCZOS)


def half_extent(pos, minimum, margin=1.08):
    ex = float(max(np.abs(pos[:, :, 0]).max(), np.abs(pos[:, :, 1]).max()))
    return max(minimum, round(ex * margin, 2))


def make_picture(path, kind, size):
    js, binary = read_glb(path)
    tris = collect_triangles(js, binary)
    if tris is None:
        return None
    name = os.path.basename(path).lower()
    if kind == "ground":
        if "_center" in name:
            half = 4.0                                   # one 6 u slot plus a margin
        elif "_wall" in name and "dwall" not in name:
            half = 2.0                                   # one 3 u wall sub-cell plus a margin
        else:
            half = half_extent(tris[0], 3.0)
    else:
        half = half_extent(tris[0], 0.8)
    return render(tris, half, size), half


# --------------------------------------------------------------------------------------------------- driver

def read_ground_names(path):
    names = []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line.startswith("ID="):
                continue
            part = line.split(" name=", 1)
            if len(part) != 2:
                continue
            name = part[1].split(" fourPiece=", 1)[0].strip()
            if name:
                names.append(name)
    return names


def read_unit_arts(path):
    arts = set()
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            if not line.startswith("unidade\t"):
                continue
            cols = line.rstrip("\n").split("\t")
            if len(cols) > 19 and cols[19]:
                arts.add(cols[19])
    return sorted(arts)


def index_folder(folder):
    result = {}
    if os.path.isdir(folder):
        for entry in os.listdir(folder):
            if entry.lower().endswith(".glb"):
                result[entry[:-4].lower()] = os.path.join(folder, entry)
    return result


def main():
    parser = argparse.ArgumentParser(description="Makes the thumbnails of the launcher's map editor.")
    parser.add_argument("--assets", default=r"C:\Steambirds\Assets\Assets")
    parser.add_argument("--out", default=r"C:\Steambirds\Launcher\editor-assets")
    parser.add_argument("--ground", default=None, help="ground_assets.txt (default: <out>\\ground_assets.txt)")
    parser.add_argument("--units", default=r"C:\Steambirds\Server\data\units_config.txt")
    parser.add_argument("--all-meshes", action="store_true",
                        help="also render every .glb of Mesh\\ that is not in ground_assets.txt")
    args = parser.parse_args()

    ground_file = args.ground or os.path.join(args.out, "ground_assets.txt")
    meshes = index_folder(os.path.join(args.assets, "Mesh"))
    prefabs = index_folder(os.path.join(args.assets, "PrefabHierarchyObject"))
    print("Mesh files: %d, prefab files: %d" % (len(meshes), len(prefabs)))

    os.makedirs(os.path.join(args.out, "ground"), exist_ok=True)
    os.makedirs(os.path.join(args.out, "units"), exist_ok=True)
    index_path = os.path.join(args.out, "thumbs.json")
    index = {"ground": {}, "units": {}}
    if os.path.exists(index_path):
        try:
            with open(index_path, encoding="utf-8") as f:
                index = json.load(f)
            index.setdefault("ground", {})
            index.setdefault("units", {})
        except Exception:
            pass

    ground_names = []
    if os.path.exists(ground_file):
        ground_names = read_ground_names(ground_file)
    else:
        print("ground_assets.txt not found (%s): floor pieces are skipped." % ground_file)
    if args.all_meshes:
        ground_names += [os.path.basename(p)[:-4] for p in meshes.values()]

    done = 0
    for name in sorted(set(ground_names), key=str.lower):
        key = name.lower()
        # A few meshes are named without the underscore before the number (dungeonboss_center01).
        alias = re.sub(r"_(\d+)$", r"\1", key)
        path = meshes.get(key) or prefabs.get(key) or meshes.get(alias) or prefabs.get(alias)
        if not path:
            continue
        try:
            made = make_picture(path, "ground", 128 if "_wall" not in key else 96)
        except Exception as error:
            print("  skipped %s: %s" % (name, error))
            continue
        if made is None:
            continue
        picture, half = made
        picture.save(os.path.join(args.out, "ground", key + ".png"))
        index["ground"][key] = {"half": half}
        done += 1
    print("floor pieces and walls: %d pictures" % done)

    arts = []
    if os.path.exists(args.units):
        arts = read_unit_arts(args.units)
    else:
        print("units_config.txt not found (%s): unit art is skipped." % args.units)
    done = 0
    for art in arts:
        key = art.lower()
        path = prefabs.get(key)
        if not path:
            continue
        try:
            made = make_picture(path, "unit", 128)
        except Exception as error:
            print("  skipped %s: %s" % (art, error))
            continue
        if made is None:
            continue
        picture, half = made
        picture.save(os.path.join(args.out, "units", key + ".png"))
        index["units"][key] = {"half": half}
        done += 1
    print("unit art: %d pictures (of %d art names; the others have no exported prefab)" % (done, len(arts)))

    with open(index_path, "w", encoding="utf-8") as f:
        json.dump(index, f, indent=1, sort_keys=True)
    print("wrote", index_path)


if __name__ == "__main__":
    main()
