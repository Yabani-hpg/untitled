"""Terrain of every land province: its biome (vegetation) and its relief (plains, hills, mountains, impassable).

Biomes come from tools/data/vegetation_source.png, a world map of natural vegetation zones in the
Robinson projection with an 18-colour legend. It is lined up with the game map once (a Robinson fit, then
local shifts tile by tile so its coastlines meet ours) and the result cached in
tools/data/vegetation_warp.npz; --register redoes it. The 18 zones are merged into the biomes of
data/terrain.json; montane forest and grassland take the province's next most common zone.

Relief comes from map/heightmapps.png. Its steps are not linear in metres (about 17 m a step in the
lowlands, 40 m on the Tibetan plateau): metres ~ 0.348 x steps^2. A province's ruggedness is the median
change of height over ~14 km; high plateaus count as mountains, and the highest, most rugged land of
the Himalaya as impassable.

Used by generate_setup.py (which writes the results into data/provinces.csv and data/province_setup.json).
Run on its own to redo the registration and print a preview: python3 tools/terrain.py [--register]
"""
import json
import sys

import numpy as np
from PIL import Image
from scipy.interpolate import griddata
from scipy.ndimage import distance_transform_edt, gaussian_filter, map_coordinates

from map_common import HEIGHT, MAP_BOTTOM, MAP_TOP, ROOT, SEA_LEVEL, WIDTH, load_heights

SOURCE_PNG = ROOT / "tools" / "data" / "vegetation_source.png"
WARP_NPZ = ROOT / "tools" / "data" / "vegetation_warp.npz"
REGIONS_DIR = ROOT / "tools" / "data" / "regions"

# the source map's legend, top to bottom, and the biome each zone becomes (None: decided per province)
LEGEND = [
    ((178, 178, 178), "ice sheet and polar desert", "ice"),
    ((140, 204, 189), "tundra", "tundra"),
    ((0, 87, 78), "taiga", "taiga"),
    ((146, 216, 71), "temperate broadleaf forest", "temperate_forest"),
    ((245, 231, 89), "temperate steppe and savanna", "steppe"),
    ((6, 104, 6), "subtropical evergreen forest", "subtropical_forest"),
    ((124, 96, 134), "Mediterranean vegetation", "mediterranean"),
    ((89, 129, 89), "monsoon forests and mosaic", "subtropical_forest"),
    ((129, 66, 41), "arid desert", "desert"),
    ((170, 95, 61), "xeric shrubland", "dry_steppe"),
    ((136, 111, 51), "dry steppe and thorn forest", "dry_steppe"),
    ((214, 169, 114), "semiarid desert", "desert"),
    ((193, 189, 62), "grass savanna", "savanna"),
    ((155, 149, 14), "tree savanna", "savanna"),
    ((96, 122, 34), "dry forest and woodland savanna", "savanna"),
    ((0, 70, 0), "tropical rainforest", "rainforest"),
    ((149, 174, 210), "alpine tundra", "tundra"),
    ((41, 131, 132), "montane forests and grasslands", None),
]
MONTANE = len(LEGEND) - 1
LEGEND_BOX = (288, 548, 0, 240)          # rows, columns of the legend in the source image: not land
MAX_COLOR_DISTANCE = 14                  # the zones are flat colours; anything further is a border line, text or blur

# Robinson projection table: latitude, parallel length, distance from the equator
ROBINSON = np.array([[0, 1.0, 0], [5, 0.9986, 0.062], [10, 0.9954, 0.124], [15, 0.99, 0.186], [20, 0.9822, 0.248],
                     [25, 0.973, 0.31], [30, 0.96, 0.372], [35, 0.9427, 0.434], [40, 0.9216, 0.4958],
                     [45, 0.8962, 0.5571], [50, 0.8679, 0.6176], [55, 0.835, 0.6769], [60, 0.7986, 0.7346],
                     [65, 0.7597, 0.7903], [70, 0.7186, 0.8435], [75, 0.6732, 0.8936], [80, 0.6213, 0.9394],
                     [85, 0.5722, 0.9761], [90, 0.5322, 1.0]])

# relief thresholds: ruggedness in metres of height change per ~14 km, elevations in metres
METRES_PER_STEP2 = 0.348
HILLS_RUGGED, MOUNTAINS_RUGGED = 55, 170
HIGH_PLATEAU = 3000                      # this high, even gentle land is mountains
ALPINE = 3500                            # montane land this high (median, m) is alpine tundra
IMPASSABLE = [(400, 3300, 0), (0, 4400, 4800)]   # (ruggedness, median, 90th percentile elevation): any one


def load_source():
    img = np.asarray(Image.open(SOURCE_PNG).convert("RGB")).astype(np.int32)
    land = ~(img > 235).all(axis=2)
    r0, r1, c0, c1 = LEGEND_BOX
    ignore = np.zeros(land.shape, dtype=bool)
    ignore[r0:r1, c0:c1] = True
    return img, land & ~ignore, ignore


def robinson(lon, lat, params):
    x0, sx, y0, sy = params
    a = np.abs(lat)
    return (x0 + sx * lon * np.interp(a, ROBINSON[:, 0], ROBINSON[:, 1]),
            y0 - sy * np.interp(a, ROBINSON[:, 0], ROBINSON[:, 2]) * np.sign(lat) * 90)


def register():
    """Lines the source map up with ours: a Robinson fit, then a smooth field of local shifts."""
    from scipy.optimize import minimize
    from scipy.spatial import cKDTree
    img, land_img, ignore = load_source()
    h, w = land_img.shape
    ours = load_heights() > SEA_LEVEL
    step = 0.25
    lon, lat = np.meshgrid(np.arange(-180, 180, step) + step / 2, np.arange(84, -60, -step) - step / 2)
    ox = ((lon + 180) * WIDTH / 360).astype(int).clip(0, WIDTH - 1)
    oy = ((MAP_TOP - lat) * HEIGHT / (MAP_TOP - MAP_BOTTOM)).astype(int).clip(0, HEIGHT - 1)
    our = ours[oy, ox]

    def agreement(ix, iy, o):
        ok = (ix >= 0) & (ix < w) & (iy >= 0) & (iy < h)
        ix, iy = ix.clip(0, w - 1), iy.clip(0, h - 1)
        g = ok & ~ignore[iy, ix]
        return ((land_img[iy, ix] == o) & g).sum() / max(g.sum(), 1), g.mean()

    def cost(p):
        ix, iy = robinson(lon[::2, ::2], lat[::2, ::2], p)
        return -agreement(np.round(ix).astype(int), np.round(iy).astype(int), our[::2, ::2])[0]

    params = minimize(cost, [w * 0.46, w / 334, h * 0.585, h / 153], method="Nelder-Mead",
                      options={"maxiter": 3000, "xatol": 1e-3, "fatol": 1e-6}).x
    bx, by = robinson(lon, lat, params)
    tile = 40
    shifts = [(dx, dy) for dx in range(-14, 15) for dy in range(-14, 15)]
    pts, dxs, dys = [], [], []
    for ty in range(0, lon.shape[0], tile // 2):
        for tx in range(0, lon.shape[1], tile // 2):
            sl = (slice(max(0, ty - tile // 2), ty + tile), slice(max(0, tx - tile // 2), tx + tile))
            o = our[sl]
            if not 0.08 <= o.mean() <= 0.92:
                continue                  # all sea or all land: no coastline to match
            scores = []
            for dx, dy in shifts:
                s, covered = agreement(np.round(bx[sl] + dx).astype(int), np.round(by[sl] + dy).astype(int), o)
                scores.append(s if covered >= 0.7 else -1)
            i = int(np.argmax(scores))
            if scores[i] >= 0.8:
                pts.append((lon[0, min(tx + tile // 4, lon.shape[1] - 1)], lat[min(ty + tile // 4, lat.shape[0] - 1), 0]))
                dxs.append(shifts[i][0])
                dys.append(shifts[i][1])
    pts, dxs, dys = np.array(pts), np.array(dxs, float), np.array(dys, float)
    tree = cKDTree(pts)
    keep = np.array([abs(dxs[i] - np.median(dxs[nb])) <= 3 and abs(dys[i] - np.median(dys[nb])) <= 3
                     for i, nb in enumerate(tree.query_ball_point(pts, 12))])
    pts, dxs, dys = pts[keep], dxs[keep], dys[keep]
    glon, glat = np.meshgrid(np.arange(-180, 181, 1.0), np.arange(90, -91, -1.0))

    def field(v):
        f = griddata(pts, v, (glon, glat), method="linear")
        f = np.where(np.isnan(f), griddata(pts, v, (glon, glat), method="nearest"), f)
        return gaussian_filter(f, 2.0, mode="nearest")

    np.savez_compressed(WARP_NPZ, params=params, fx=field(dxs).astype(np.float32), fy=field(dys).astype(np.float32))
    print(f"vegetation map registered: Robinson {np.round(params, 3)}, {len(pts)} local shifts")


def source_xy(lon, lat):
    """Where a longitude/latitude of our map falls on the source image."""
    if not WARP_NPZ.exists():
        register()
    warp = np.load(WARP_NPZ)
    bx, by = robinson(lon, lat, warp["params"])
    gi, gj = 90 - lat, lon + 180
    return (bx + map_coordinates(warp["fx"], [gi, gj], order=1, mode="nearest"),
            by + map_coordinates(warp["fy"], [gi, gj], order=1, mode="nearest"))


def zone_map():
    """The vegetation zone (index into LEGEND) of every pixel of our map; -1 at sea."""
    img, land_img, _ = load_source()
    h, w = land_img.shape
    colors = np.array([c for c, _, _ in LEGEND])
    dist = ((img[:, :, None, :] - colors[None, None]) ** 2).sum(axis=3)
    zone = np.argmin(dist, axis=2)
    known = land_img & (np.sqrt(dist.min(axis=2)) <= MAX_COLOR_DISTANCE)
    # border lines and text inside the land take the nearest zone
    _, (iy, ix) = distance_transform_edt(~known, return_indices=True)
    zone = np.where(land_img, zone[iy, ix], -1)

    ys, xs = np.mgrid[0:HEIGHT, 0:WIDTH]
    lon = (xs + 0.5) * 360.0 / WIDTH - 180.0
    lat = MAP_TOP - (ys + 0.5) * (MAP_TOP - MAP_BOTTOM) / HEIGHT
    sx, sy = source_xy(lon, lat)
    ours = zone[np.round(sy).astype(int).clip(0, h - 1), np.round(sx).astype(int).clip(0, w - 1)]
    land = load_heights() > SEA_LEVEL
    # our land the source shows as sea (coasts, small islands, lines up a pixel off): nearest zone
    known = land & (ours >= 0)
    _, (iy, ix) = distance_transform_edt(~known, return_indices=True)
    return np.where(land, ours[iy, ix], -1)


def biome_of(zone_counts, lat, elevation=0):
    """
    A province's biome from how many of its pixels lie in each zone: the most common, montane aside.
    A province mostly montane is alpine tundra up high, else like the lowland around it; if there is
    none, highland forest in the temperate zone and highland grassland in the tropics.
    """
    counts = np.asarray(zone_counts, dtype=np.float64).copy()
    total = counts.sum()
    if total <= 0:
        return None
    montane = counts[MONTANE]
    counts[MONTANE] = 0
    by_biome = {}
    for i, (_, _, biome) in enumerate(LEGEND):
        if biome:
            by_biome[biome] = by_biome.get(biome, 0) + counts[i]
    best = max(by_biome, key=by_biome.get)
    if montane < 0.5 * total and by_biome[best] >= 0.15 * total or montane == 0:
        return best
    if elevation >= ALPINE:
        return "tundra"
    if by_biome[best] >= 0.05 * total:
        return best                      # the lowland around the mountains sets their character
    return "temperate_forest" if abs(lat) >= 23 else "savanna"


def relief_fields():
    """Per pixel: ruggedness (m per ~14 km) and elevation (m)."""
    h = load_heights().astype(np.float64)
    metres = METRES_PER_STEP2 * np.maximum(h - SEA_LEVEL, 0) ** 2
    m = gaussian_filter(metres, 2.0, mode="wrap")
    rugged = np.hypot(np.roll(m, -1, 1) - np.roll(m, 1, 1), np.roll(m, -1, 0) - np.roll(m, 1, 0))
    return rugged, metres, h > SEA_LEVEL


def relief_of(rugged, metres):
    """plains, hills, mountains or impassable from a province's land pixels' ruggedness and elevation."""
    if len(rugged) == 0:
        return "plains"
    r, e, e90 = np.median(rugged), np.median(metres), np.percentile(metres, 90)
    if any(r >= rr and e >= ee and e90 >= ee90 for rr, ee, ee90 in IMPASSABLE):
        return "impassable"
    if r >= MOUNTAINS_RUGGED or (e >= HIGH_PLATEAU and r >= HILLS_RUGGED):
        return "mountains"
    if r >= HILLS_RUGGED:
        return "hills"
    return "plains"


def region_biomes():
    """Biomes set by hand in tools/data/regions/*.json ("zone_biome": river valleys, oases), by region name."""
    out = {}
    for path in sorted(REGIONS_DIR.glob("*.json")):
        spec = json.loads(path.read_text(encoding="utf-8"))
        zones = spec.get("zone_biome", {})
        for region in spec.get("regions", []):
            biome = region.get("biome") or zones.get(region.get("zone"))
            if biome:
                out[region["name"]] = biome
    return out


if __name__ == "__main__":
    if "--register" in sys.argv:
        register()
    z = zone_map()
    palette = np.array([c for c, _, _ in LEGEND] + [(40, 70, 110)], dtype=np.uint8)
    Image.fromarray(palette[z[::2, ::2]]).save(ROOT / "tools" / "data" / "vegetation_preview.png")
    print("wrote tools/data/vegetation_preview.png")
