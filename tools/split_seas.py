#!/usr/bin/env python3
"""Split the single Ocean province into sea zones, Paradox style.

Every ocean pixel of map/provinces.png joins the nearest seed of tools/data/sea_zones.json (nearest on
the sphere); a zone cut in two by land keeps its largest piece and the rest joins the neighbouring zone
it touches most. Writes map/provinces.png, data/provinces.csv (the Ocean row becomes the first zone,
keeping id 1; the other zones are appended) and data/adjacencies.csv (rows touching the sea are
recomputed: border lengths between sea zones and their coasts, river mouths kept on the zone the
river's province borders most). Land provinces, their ids and colours are untouched.

Usage: python3 tools/split_seas.py
"""
import json
import zlib
from collections import Counter, defaultdict

import numpy as np
from PIL import Image
from scipy.ndimage import binary_dilation, label
from scipy.spatial import cKDTree

from map_common import HEIGHT, MAP_BOTTOM, MAP_TOP, PROVINCES_PNG, ROOT, WIDTH

PROVINCES_CSV = ROOT / "data" / "provinces.csv"
ADJACENCIES_CSV = ROOT / "data" / "adjacencies.csv"
ZONES_JSON = ROOT / "tools" / "data" / "sea_zones.json"
OCEAN_ID = 1


def xyz(lon, lat):
    lon, lat = np.radians(lon), np.radians(lat)
    return np.column_stack([np.cos(lat) * np.cos(lon), np.cos(lat) * np.sin(lon), np.sin(lat)])


def read_csv(path):
    header, rows = None, []
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue
        if header is None:
            header = line.split(";")
            continue
        rows.append(dict(zip(header, line.split(";"))))
    return header, rows


def color_for(key, used):
    salt = 0
    while True:
        value = zlib.crc32(f"sea:{key}:{salt}".encode()) & 0xFFFFFF
        rgb = (value >> 16 & 255, value >> 8 & 255, value & 255)
        if rgb not in used and rgb != (255, 255, 255):
            used.add(rgb)
            return rgb
        salt += 1


def main():
    zones = json.loads(ZONES_JSON.read_text(encoding="utf-8"))["zones"]
    header, rows = read_csv(PROVINCES_CSV)
    by_color = {int(r["color"][1:], 16): int(r["id"]) for r in rows}
    ocean_color = next(int(r["color"][1:], 16) for r in rows if int(r["id"]) == OCEAN_ID)
    rgb = np.asarray(Image.open(PROVINCES_PNG).convert("RGB")).astype(np.int32)
    key = (rgb[..., 0] << 16) | (rgb[..., 1] << 8) | rgb[..., 2]
    lut_keys = np.array(sorted(by_color))
    ids = np.array([by_color[k] for k in lut_keys])[np.searchsorted(lut_keys, key).clip(0, len(lut_keys) - 1)]
    ids[key != lut_keys[np.searchsorted(lut_keys, key).clip(0, len(lut_keys) - 1)]] = 0
    ocean = key == ocean_color
    print(f"{int(ocean.sum())} ocean pixels, {len(zones)} zones")

    # nearest seed on the sphere
    ys, xs = np.nonzero(ocean)
    lon = xs * (360.0 / WIDTH) - 180.0 + 180.0 / WIDTH
    lat = MAP_TOP - (ys + 0.5) * (MAP_TOP - MAP_BOTTOM) / HEIGHT
    tree = cKDTree(xyz(np.array([z[0] for z in zones]), np.array([z[1] for z in zones])))
    _, nearest = tree.query(xyz(lon, lat).astype(np.float32), workers=-1)
    zone = np.zeros(ids.shape, dtype=np.int32)
    zone[ys, xs] = nearest + 1

    # a zone cut by land: keep the biggest piece, give the rest to the neighbouring zone it touches most
    for _ in range(4):
        changed = 0
        for z in range(1, len(zones) + 1):
            mask = zone == z
            if not mask.any():
                continue
            yy, xx = np.nonzero(mask)
            y0, y1, x0, x1 = yy.min(), yy.max() + 1, xx.min(), xx.max() + 1
            sub = mask[y0:y1, x0:x1]
            lab, n = label(sub)
            if n <= 1:
                continue
            sizes = np.bincount(lab.ravel())
            keep = int(np.argmax(sizes[1:])) + 1
            for comp in range(1, n + 1):
                if comp == keep:
                    continue
                piece = lab == comp
                ring = binary_dilation(piece, iterations=2) & ~piece
                around = zone[y0:y1, x0:x1][ring]
                around = around[(around != 0) & (around != z)]
                if around.size == 0:
                    continue          # an enclosed sea with no other zone: stays
                target = np.bincount(around).argmax()
                zone[y0:y1, x0:x1][piece] = target
                changed += 1
        print(f"  pass: {changed} pieces reassigned")
        if changed == 0:
            break

    # zones with pixels, numbered: the first keeps the Ocean's id 1, the rest follow the last province
    counts = np.bincount(zone.ravel(), minlength=len(zones) + 1)
    used = set(tuple(int(v) for v in bytes.fromhex(r["color"][1:])) for r in rows)
    next_id = max(int(r["id"]) for r in rows) + 1
    zone_id, zone_rows = {}, []
    for z in range(1, len(zones) + 1):
        if counts[z] == 0:
            print(f"  zone '{zones[z - 1][2]}' has no sea pixels (dropped)")
            continue
        if not zone_id:
            pid, color = OCEAN_ID, tuple(int(v) for v in bytes.fromhex(next(r["color"][1:] for r in rows if int(r["id"]) == OCEAN_ID)))
        else:
            pid, color = next_id, color_for(zones[z - 1][2], used)
            next_id += 1
        zone_id[z] = pid
        zone_rows.append((pid, color, zones[z - 1][2]))
    lut = np.zeros(len(zones) + 1, dtype=np.int32)
    for z, pid in zone_id.items():
        lut[z] = pid
    new_ids = np.where(zone > 0, lut[zone], ids)

    # the image: ocean pixels in their zone's colour
    palette = {int(r["id"]): tuple(int(v) for v in bytes.fromhex(r["color"][1:])) for r in rows}
    for pid, color, _ in zone_rows:
        palette[pid] = color
    pal = np.zeros((next_id, 3), dtype=np.uint8)
    for pid, color in palette.items():
        pal[pid] = color
    out = rgb.astype(np.uint8)
    out[zone > 0] = pal[new_ids[zone > 0]]
    Image.fromarray(out, "RGB").save(PROVINCES_PNG, optimize=True)

    # the csv: Ocean row replaced, zones appended
    lines = PROVINCES_CSV.read_text(encoding="utf-8").splitlines()
    result = []
    for line in lines:
        if line.startswith(f"{OCEAN_ID};"):
            pid, color, name = zone_rows[0]
            line = f"{pid};#{'%02x%02x%02x' % color};{name};sea;"
        result.append(line)
    for pid, color, name in zone_rows[1:]:
        result.append(f"{pid};#{'%02x%02x%02x' % color};{name};sea;")
    PROVINCES_CSV.write_text("\n".join(result) + "\n", encoding="utf-8", newline="\n")

    # adjacency: keep land rows; recompute every pair that touches the sea
    adj_lines = ADJACENCIES_CSV.read_text(encoding="utf-8").splitlines()
    kept, head, mouths = [], [], {}
    for line in adj_lines:
        if line.startswith("#") or line.startswith("a;b;"):
            head.append(line)
            continue
        a, b, border, crossing, nav = line.split(";")
        if int(a) == OCEAN_ID or int(b) == OCEAN_ID:
            other = int(b) if int(a) == OCEAN_ID else int(a)
            if nav:
                mouths[other] = nav
            continue
        kept.append(line)
    is_sea = np.zeros(next_id, dtype=bool)
    for pid, _, _ in zone_rows:
        is_sea[pid] = True
    border = Counter()
    pairs = [(new_ids, np.roll(new_ids, -1, axis=1)), (new_ids[:-1], new_ids[1:])]
    for pa, pb in pairs:
        a, b = pa.ravel(), pb.ravel()
        keep = (a != b) & (is_sea[a] | is_sea[b])
        lo, hi = np.minimum(a[keep], b[keep]), np.maximum(a[keep], b[keep])
        keys, n = np.unique(lo.astype(np.int64) * next_id + hi, return_counts=True)
        border.update(dict(zip(keys.tolist(), n.tolist())))
    coasts = defaultdict(list)
    for k, n in border.items():
        lo, hi = divmod(k, next_id)
        for land, sea in ((lo, hi), (hi, lo)):
            if is_sea[sea] and not is_sea[land]:
                coasts[land].append((n, sea))
    new_rows = []
    for k, n in border.items():
        lo, hi = divmod(k, next_id)
        land = lo if is_sea[hi] and not is_sea[lo] else hi if is_sea[lo] and not is_sea[hi] else None
        nav = ""
        if land is not None and land in mouths and max(coasts[land])[1] == (hi if land == lo else lo):
            nav = mouths[land]
        new_rows.append((lo, hi, n, "", nav))
    # river mouths whose province no longer touches the sea by border: link to the nearest zone at 0 border
    for land, nav in mouths.items():
        if land not in coasts:
            yy, xx = np.nonzero(new_ids == land)
            if yy.size == 0:
                continue
            sea_y, sea_x = np.nonzero(zone[max(yy.min() - 8, 0):yy.max() + 9, max(xx.min() - 8, 0):xx.max() + 9])
            if sea_y.size == 0:
                continue
            cy, cx = max(yy.min() - 8, 0) + sea_y, max(xx.min() - 8, 0) + sea_x
            d = (cy[:, None] - yy[None, ::max(1, yy.size // 200)]) ** 2 + (cx[:, None] - xx[None, ::max(1, xx.size // 200)]) ** 2
            sea = int(new_ids[cy[d.min(axis=1).argmin()], cx[d.min(axis=1).argmin()]])
            new_rows.append((min(land, sea), max(land, sea), 0, "", nav))
    # straits: channels too narrow for the map, linked as if the zones shared a border
    zone_of_name = {name: pid for pid, _, name in zone_rows}
    have = {(min(a, b), max(a, b)) for a, b, *_ in new_rows}
    for x, y in json.loads(ZONES_JSON.read_text(encoding="utf-8")).get("straits", []):
        if x not in zone_of_name or y not in zone_of_name:
            print(f"  strait {x} - {y}: unknown zone")
            continue
        a, b = sorted((zone_of_name[x], zone_of_name[y]))
        if (a, b) not in have:
            new_rows.append((a, b, 1, "", ""))
            have.add((a, b))
    all_rows = [tuple(l.split(";")) for l in kept] + [tuple(str(v) for v in r) for r in new_rows]
    all_rows.sort(key=lambda r: (int(r[0]), int(r[1])))
    ADJACENCIES_CSV.write_text("\n".join(head + [";".join(r) for r in all_rows]) + "\n", encoding="utf-8", newline="\n")
    print(f"{len(zone_rows)} sea zones written; {len(new_rows)} sea adjacencies, {len(kept)} land rows kept, {len(mouths)} river mouths")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
