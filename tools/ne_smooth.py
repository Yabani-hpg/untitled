"""Replace straight, surveyed-looking province borders with natural-looking ones. Used by import_natural_earth.py.

Modern borders drawn with a ruler (Egypt-Libya, the US states, the Sahara, the Arabian desert) look wrong
on an ancient map. This finds border stretches that are locally straight and redraws them:

1. Straightness: for every border pixel, the pixels of the same border (same province pair) within
   STRAIGHT_WINDOW_PX are fitted with a line (covariance of their positions). If the spread across that line is below STRAIGHT_MAX_SPREAD,
   the pixel lies on a straight stretch. Borders along rivers and ridges are never that straight.
2. Redraw: inside a band of BAND_PX around the straight stretches, the province map is re-read through
   a smooth random displacement field (domain warping) of up to WARP_PX, tapering to nothing at the band
   edge. A straight line becomes a meandering one around the same course. The field is smooth enough
   that it rarely folds; any piece a fold cuts off joins its neighbour. Everything outside the band stays
   as it was, land never takes water's place, and a province the meander would reshape by more than
   MAX_RESHAPE of its area is restored, so small provinces keep their shape.
"""
import numpy as np
from scipy.ndimage import binary_dilation, distance_transform_edt, uniform_filter, zoom
from skimage.measure import label

STRAIGHT_WINDOW_PX = 11      # half-size of the fitting window (~75 km)
STRAIGHT_MIN_PIXELS = 14     # border pixels needed in the window to judge it
STRAIGHT_MAX_SPREAD = 0.3    # px^2: variance across the fitted line (rasterized straight lines: ~0.05-0.25
                             # depending on angle; natural borders: well above 1)
BAND_PX = 14                 # half-width of the redrawn band (~100 km each side)
WARP_PX = 8.0                # largest displacement of the border (~55 km)
WARP_CELLS = ((80, 0.5), (30, 0.3), (12, 0.2))   # noise octaves (cell size px, weight): broad bends
                                                  # plus finer wiggles; folds are cleaned up afterwards
MAX_RESHAPE = 0.3            # a province that would lose, or gain, more than this share of its area keeps
                             # its original shape; big provinces barely notice a meander
NOISE_SEED = 1177


def _border_pairs(regions, land):
    """(y, x, a, b) for every land pixel whose right or lower neighbour is a different land region."""
    h, w = regions.shape
    out = []
    for dy, dx in ((0, 1), (1, 0)):
        nb = np.roll(regions, -dx, axis=1) if dx else np.vstack([regions[1:], regions[-1:]])
        nb_land = np.roll(land, -dx, axis=1) if dx else np.vstack([land[1:], land[-1:]])
        ys, xs = np.nonzero(land & nb_land & (regions != nb))
        out.append((ys, xs, regions[ys, xs], nb[ys, xs]))
    ys, xs, a, b = (np.concatenate(v) for v in zip(*out))
    return ys, xs, np.minimum(a, b), np.maximum(a, b)


def straight_mask(regions, land):
    """Border pixels on a locally straight stretch of their own border. Each province pair is fitted on
    its own, so junctions with other borders don't spoil the fit."""
    ys, xs, a, b = _border_pairs(regions, land)
    key = a.astype(np.int64) * (int(regions.max()) + 1) + b
    order = np.argsort(key, kind="stable")
    ys, xs, key = ys[order], xs[order], key[order]
    starts = np.flatnonzero(np.r_[True, key[1:] != key[:-1]])
    ends = np.r_[starts[1:], len(key)]

    size = 2 * STRAIGHT_WINDOW_PX + 1
    pad = STRAIGHT_WINDOW_PX + 1
    straight = np.zeros(regions.shape, dtype=bool)
    for s0, s1 in zip(starts, ends):
        if s1 - s0 < STRAIGHT_MIN_PIXELS:
            continue
        py, px = ys[s0:s1], xs[s0:s1]
        if px.max() - px.min() > regions.shape[1] // 2:
            continue                      # a border across the date line; rare, leave it
        y0, x0 = py.min() - pad, px.min() - pad
        hh, ww = py.max() - y0 + pad + 1, px.max() - x0 + pad + 1
        m = np.zeros((hh, ww), dtype=np.float64)
        ly, lx = py - y0, px - x0
        m[ly, lx] = 1.0
        yy, xx = np.mgrid[0:hh, 0:ww].astype(np.float64)

        def local(v):
            return uniform_filter(v, size=size, mode="constant") * (size * size)

        n = local(m)
        sx, sy = local(m * xx), local(m * yy)
        sxx, syy, sxy = local(m * xx * xx), local(m * yy * yy), local(m * xx * yy)
        n_p = n[ly, lx]
        mx, my = sx[ly, lx] / n_p, sy[ly, lx] / n_p
        cxx = sxx[ly, lx] / n_p - mx * mx
        cyy = syy[ly, lx] / n_p - my * my
        cxy = sxy[ly, lx] / n_p - mx * my
        spread = 0.5 * (cxx + cyy) - np.sqrt(0.25 * (cxx - cyy) ** 2 + cxy * cxy)   # smaller eigenvalue
        hit = (n_p >= STRAIGHT_MIN_PIXELS) & (spread < STRAIGHT_MAX_SPREAD)
        straight[py[hit], px[hit]] = True
    return straight


def smooth_noise(shape, seed):
    """Smooth noise in -1..1, cubic-interpolated from random grids of WARP_CELLS sizes."""
    rng = np.random.default_rng(seed)
    h, w = shape
    total = np.zeros(shape, dtype=np.float32)
    for cell, weight in WARP_CELLS:
        coarse = rng.standard_normal((h // cell + 3, w // cell + 3)).astype(np.float32)
        total += weight * zoom(coarse, cell, order=3)[:h, :w]
    return np.clip(total / (1.2 * total.std() + 1e-6), -1.0, 1.0)


def smooth_straight_borders(regions, land, seed=NOISE_SEED):
    """Returns (new regions, straight border pixels found, pixels reassigned)."""
    straight = straight_mask(regions, land)
    if not straight.any():
        return regions, 0, 0
    r = BAND_PX
    yy, xx = np.mgrid[-r:r + 1, -r:r + 1]
    band = binary_dilation(straight, yy ** 2 + xx ** 2 <= r * r) & land

    # displacement tapers from full strength in the band's core to zero at its edge
    depth = distance_transform_edt(band)
    t = np.clip(depth / (0.6 * r), 0.0, 1.0)
    taper = (t * t * (3.0 - 2.0 * t)).astype(np.float32)
    dx = WARP_PX * taper * smooth_noise(regions.shape, seed)
    dy = WARP_PX * taper * smooth_noise(regions.shape, seed + 1)

    ys, xs = np.nonzero(band)
    h, w = regions.shape
    sx = np.rint(xs + dx[ys, xs]).astype(np.int64) % w
    sy = np.clip(np.rint(ys + dy[ys, xs]).astype(np.int64), 0, h - 1)
    sampled = regions[sy, sx]
    ok = land[sy, sx]              # never pull water into land
    out = regions.copy()
    out[ys[ok], xs[ok]] = sampled[ok]

    # small provinces can't absorb a meander without being distorted: put them back as they were
    n = int(regions.max()) + 1
    moved = out != regions
    area = MAX_RESHAPE * np.bincount(regions.ravel(), minlength=n)
    reshaped = ((np.bincount(regions[moved], minlength=n) > area)
                | (np.bincount(out[moved], minlength=n) > area))
    if reshaped.any():
        undo = moved & (reshaped[regions] | reshaped[out])
        out[undo] = regions[undo]
    out = _absorb_fragments(out, band)
    return out, int(straight.sum()), int((out != regions).sum())


def _absorb_fragments(regions, band):
    """Pieces of a province that the warp cut off (lying wholly inside the band and not its largest
    piece) join the neighbouring province they share the longest border with."""
    for _ in range(3):
        comps = label(regions, background=-1, connectivity=1)
        n = comps.max() + 1
        size = np.bincount(comps.ravel(), minlength=n)
        outside = np.bincount(comps[~band].ravel(), minlength=n)
        region_of = np.zeros(n, dtype=regions.dtype)
        region_of[comps.ravel()] = regions.ravel()
        # largest piece per province
        order = np.lexsort((-size, region_of))
        largest = np.zeros(n, dtype=bool)
        first = np.ones(len(order), dtype=bool)
        first[1:] = region_of[order[1:]] != region_of[order[:-1]]
        largest[order[first]] = True
        stray = (outside == 0) & ~largest & (size > 0)
        stray[0] = False
        if not stray.any():
            return regions
        # longest shared border with a different province, per stray piece
        a = np.concatenate([comps[:, :-1].ravel(), comps[:-1].ravel()])
        b = np.concatenate([comps[:, 1:].ravel(), comps[1:].ravel()])
        a, b = np.concatenate([a, b]), np.concatenate([b, a])
        keep = stray[a] & (region_of[a] != region_of[b])
        pairs, counts = np.unique(np.column_stack([a[keep], region_of[b[keep]]]), axis=0, return_counts=True)
        best = {}
        for (piece, neighbour), c in zip(pairs, counts):
            if c > best.get(piece, (0, 0))[0]:
                best[piece] = (c, neighbour)
        target = region_of.copy()
        for piece, (_, neighbour) in best.items():
            target[piece] = neighbour
        regions = target[comps]
    return regions
