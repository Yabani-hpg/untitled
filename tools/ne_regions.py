"""Hand-authored region overrides (tools/data/regions/*.json), applied on top of the admin-1 provinces.

A file replaces every province pixel inside its "area" with its own regions:
  - oasis regions claim the pixels within radius_deg of their seed,
  - valley regions split the "valley" (land within radius_px of the named rivers, plus boxes and
    circles) by nearest seed,
  - desert regions split the rest of the area by nearest seed.
A region may list several seeds to follow a bend in the river. "owner": "auto" takes the country
owning most of the region's pixels before the override. Terrain comes from the region's "terrain",
else the file's "zone_terrain" for its zone, else it is derived from the heightmap like any province.
Used by import_natural_earth.py.
"""
import json
from collections import Counter

import numpy as np
from scipy.ndimage import binary_dilation
from scipy.spatial import cKDTree

from map_common import HEIGHT, MAP_BOTTOM, MAP_TOP, ROOT, WIDTH, clean

REGIONS_DIR = ROOT / "tools" / "data" / "regions"
PX_PER_DEG_X = WIDTH / 360.0
PX_PER_DEG_Y = HEIGHT / (MAP_TOP - MAP_BOTTOM)


def load():
    return [(path.name, json.loads(path.read_text(encoding="utf-8"))) for path in sorted(REGIONS_DIR.glob("*.json"))]


def _pixel(lat, lon):
    return ((lon + 180.0) * PX_PER_DEG_X, (MAP_TOP - lat) * PX_PER_DEG_Y)


def _lat_lon_grids():
    lat = MAP_TOP - (np.arange(HEIGHT) + 0.5) / PX_PER_DEG_Y
    lon = (np.arange(WIDTH) + 0.5) / PX_PER_DEG_X - 180.0
    return lat[:, None], lon[None, :]


def _nearest_seed(mask, regions_spec):
    """Index into regions_spec of the nearest seed for every True pixel in mask."""
    seeds, owner = [], []
    for i, spec in enumerate(regions_spec):
        for lat, lon in spec["seeds"]:
            seeds.append(_pixel(lat, lon))
            owner.append(i)
    ys, xs = np.nonzero(mask)
    _, nearest = cKDTree(np.array(seeds)).query(np.column_stack([xs + 0.5, ys + 0.5]))
    return ys, xs, np.array(owner)[nearest]


def apply(spec, regions, tags, land, water, first_index, admin_names):
    """Returns (regions, metas). New regions get indices first_index, first_index + 1, ...; metas are
    record-like dicts for them, in the same order."""
    lat, lon = _lat_lon_grids()
    tag_px = tags[regions]

    area = np.zeros(regions.shape, dtype=bool)
    for a in spec["area"]:
        part = land & (tag_px == a["country"])
        if "min_lat" in a:
            part &= lat >= a["min_lat"]
        if "max_lat" in a:
            part &= lat <= a["max_lat"]
        area |= part

    valley_spec = spec.get("valley", {})
    river_ids = [i + 1 for i, r in enumerate(water.rivers) if r.name in set(valley_spec.get("rivers", []))]
    valley = np.isin(water.river_index, river_ids)
    r = int(valley_spec.get("radius_px", 0))
    if r > 0:
        yy, xx = np.mgrid[-r:r + 1, -r:r + 1]
        valley = binary_dilation(valley, yy ** 2 + xx ** 2 <= r * r)
    for box in valley_spec.get("boxes", []):
        valley |= ((lat >= box["min_lat"]) & (lat <= box["max_lat"])
                   & (lon >= box["min_lon"]) & (lon <= box["max_lon"]))
    for c in valley_spec.get("circles", []):
        valley |= (lat - c["lat"]) ** 2 + (lon - c["lon"]) ** 2 <= c["radius_deg"] ** 2
    valley &= area

    specs = spec["regions"]
    assigned = np.full(regions.shape, -1, dtype=np.int32)
    for i, s in enumerate(specs):
        if s["zone"] == "oasis":
            (slat, slon), rad = s["seeds"][0], s["radius_deg"]
            assigned[area & ((lat - slat) ** 2 + (lon - slon) ** 2 <= rad ** 2)] = i
    for zone, zone_area in (("valley", valley), ("desert", area)):
        mask = zone_area & (assigned < 0)   # after the previous zone has claimed its pixels
        zone_specs = [i for i, s in enumerate(specs) if s["zone"] == zone]
        if mask.any() and zone_specs:
            ys, xs, which = _nearest_seed(mask, [specs[i] for i in zone_specs])
            assigned[ys, xs] = np.array(zone_specs)[which]

    metas, empty = [], []
    out = regions.copy()
    for i, s in enumerate(specs):
        mask = assigned == i
        if not mask.any():
            empty.append(s["name"])
            continue
        owner = s["owner"]
        if owner == "auto":
            owner = Counter(tag_px[mask].tolist()).most_common(1)[0][0]
        index = first_index + len(metas)
        out[mask] = index
        name = clean(s["name"])
        terrain = s.get("terrain") or spec.get("zone_terrain", {}).get(s["zone"])
        metas.append({"adm0_a3": owner, "name": name, "name_en": name, "terrain": terrain,
                      "admin": admin_names.get(owner, owner), "adm1_code": f"region:{name}"})
    if empty:
        print(f"  regions with no pixels (dropped): {', '.join(empty)}")
    return out, metas
