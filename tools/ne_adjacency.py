"""Province adjacency with river crossings and navigable river links. Used by import_natural_earth.py.

A border is a river crossing when a river runs along it (like the Rhine between Alsace and Baden):
at least RIVER_BORDER_MIN_PX border pixel pairs, and RIVER_BORDER_MIN_SHARE of the border, have a
river running parallel to that bit of border within a pixel of it. Direction is what matters: a
river flowing from one province into the next (like the Nile from nome to nome) crosses the border
at right angles and doesn't count, however short the border.

Two provinces are linked by a navigable river when a navigable river pixel in one touches the other
(8-connected). That covers a river flowing from one province into the next and the provinces on both
banks of a river that runs along their border. Rivers ending at a sea or lake province link to it
when their last land pixels are within MOUTH_REACH_PX of that water.
"""
from collections import Counter, defaultdict

import numpy as np
from scipy.ndimage import distance_transform_edt, maximum_filter

RIVER_BORDER_MIN_PX = 3
RIVER_BORDER_MIN_SHARE = 0.3
MOUTH_REACH_PX = 3.0


def _pairs(a, b):
    """Canonical (low, high) province pairs of two aligned arrays, ignoring equal ones."""
    keep = a != b
    a, b = a[keep], b[keep]
    return np.minimum(a, b), np.maximum(a, b), keep


def compute(ids, is_water, river_index, rivers):
    """ids: province id per pixel. is_water: bool per province id. river_index: river record + 1 per
    pixel (0 = none; not cleared inside lakes so navigable chains run through them). rivers: records
    with .name and .navigable. Returns rows (a, b, border_px, crossing_river, navigable_river)."""
    n = int(ids.max()) + 1
    # 4-connected neighbour pairs; east-west wraps, north-south doesn't
    horiz = (ids, np.roll(ids, -1, axis=1))
    vert = (ids[:-1], ids[1:])
    river = river_index > 0
    # river pixels whose river continues vertically / horizontally through them
    runs_v = river & (np.roll(river, 1, axis=0) | np.roll(river, -1, axis=0))
    runs_h = river & (np.roll(river, 1, axis=1) | np.roll(river, -1, axis=1))
    # a vertical border segment between columns x and x+1 has a river along it if a vertical run lies
    # in columns x-1..x+2 (same row); likewise for horizontal segments and rows y-1..y+2
    along_v = runs_v | np.roll(runs_v, 1, axis=1) | np.roll(runs_v, -1, axis=1) | np.roll(runs_v, -2, axis=1)
    along_h = runs_h | np.roll(runs_h, 1, axis=0) | np.roll(runs_h, -1, axis=0) | np.roll(runs_h, -2, axis=0)
    river_near = maximum_filter(river_index, size=3, mode="wrap")

    border = Counter()
    river_pairs = Counter()
    river_names = defaultdict(Counter)
    for (pa, pb), shift in ((horiz, "h"), (vert, "v")):
        lo, hi, keep = _pairs(pa.ravel(), pb.ravel())
        keys = lo.astype(np.int64) * n + hi
        border.update(dict(zip(*np.unique(keys, return_counts=True))))

        if shift == "h":
            along = along_v.ravel()
            i1, i2 = river_near.ravel(), np.roll(river_near, -1, axis=1).ravel()
        else:
            along = along_h[:-1].ravel()
            i1, i2 = river_near[:-1].ravel(), river_near[1:].ravel()
        land = ~is_water[lo] & ~is_water[hi]
        on_river = along[keep] & land
        rk = keys[on_river]
        river_pairs.update(dict(zip(*np.unique(rk, return_counts=True))))
        for k, r in zip(rk, np.maximum(i1[keep], i2[keep])[on_river]):
            if r:
                river_names[int(k)][r - 1] += 1

    navigable = np.array([False] + [r.navigable for r in rivers])
    nav_px = navigable[river_index] & (river_index > 0)
    nav_links = {}

    # a navigable river pixel touching another province (8-connected): flow-through and both banks
    h, w = ids.shape
    for dy, dx in ((0, 1), (1, 0), (1, 1), (1, -1)):
        a_ids = ids[:h - dy]
        b_ids = np.roll(ids, -dx, axis=1)[dy:]
        a_nav = nav_px[:h - dy]
        b_nav = np.roll(nav_px, -dx, axis=1)[dy:]
        river = np.where(a_nav, river_index[:h - dy], np.roll(river_index, -dx, axis=1)[dy:])
        touch = (a_nav | b_nav) & (a_ids != b_ids)
        for pa, pb, r in zip(a_ids[touch], b_ids[touch], river[touch]):
            nav_links.setdefault((min(pa, pb), max(pa, pb)), r - 1)

    # navigable river mouths into sea and lake provinces
    water_px = is_water[ids]
    dist, (iy, ix) = distance_transform_edt(~water_px, return_indices=True)
    mouth = nav_px & ~water_px & (dist <= MOUTH_REACH_PX)
    for y, x in zip(*np.nonzero(mouth)):
        pa, pb = ids[y, x], ids[iy[y, x], ix[y, x]]
        nav_links.setdefault((min(pa, pb), max(pa, pb)), river_index[y, x] - 1)

    rows = []
    for key in sorted(set(border) | {a * n + b for a, b in nav_links}):
        a, b = divmod(int(key), n)
        length = int(border.get(key, 0))
        crossing = ""
        rp = river_pairs.get(key, 0)
        if rp >= RIVER_BORDER_MIN_PX and rp >= RIVER_BORDER_MIN_SHARE * length:
            named = [(c, rivers[i].name) for i, c in river_names[key].items() if rivers[i].name]
            crossing = max(named)[1] if named else "river"
        nav = ""
        if (a, b) in nav_links:
            nav = rivers[nav_links[(a, b)]].name or "river"
        rows.append((a, b, length, crossing, nav))
    return rows
