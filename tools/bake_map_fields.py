#!/usr/bin/env python3
"""Bake coastline and river distance fields into the G and B channels of map/heightmapps.png.

The terrain shader reads one bilinear-filtered texel of these fields and thresholds it,
which gives smooth, resolution-independent coasts and rivers at the cost of a single
texture fetch (instead of dilating the river mask per pixel at runtime).

  R: height (untouched; 128 = sea level)
  G: coast field  = 0.5 + signed_distance_to_coast_px / (2 * RANGE)          (land > 0.5)
  B: river field  = 0.5 - (distance_to_river_px - half_width) / (2 * RIVER_RANGE)
                    (inside the river > 0.5; half_width depends on the river's width class)

Land/sea comes from map/rivers.png (white land, grey sea). River distances are measured to the
source polylines when tools/import_natural_earth.py passes them in, so river edges follow the real
curves; run standalone (after hand-editing rivers.png) they are measured to its coloured pixels.
Standalone: python3 tools/bake_map_fields.py   (needs pillow numpy scipy)
"""
import pathlib
import sys

import numpy as np
from PIL import Image
from scipy.ndimage import binary_dilation, distance_transform_edt
from scipy.spatial import cKDTree

from map_common import RIVER_CLASS_RGB

ROOT = pathlib.Path(__file__).resolve().parent.parent
HEIGHTMAP = ROOT / "map" / "heightmapps.png"
RIVERS = ROOT / "map" / "rivers.png"
RANGE = 8.0        # coast: pixels encoded on each side; = FIELD_RANGE_TEXELS in shaders/main.gdshader
RIVER_RANGE = 4.0  # rivers: = RIVER_RANGE_TEXELS in shaders/main.gdshader
# River half-width in pixels per width class, narrowest .. widest
RIVER_HALF_WIDTH = {0: 0.58, 1: 0.64, 2: 0.76, 3: 0.95}  # >= ~0.55 keeps thin rivers continuous under bilinear sampling
LINE_SAMPLE_STEP = 0.1  # px between samples along a river line (distance error < 0.005 px)
PAD = int(RANGE) + 2


def distance_to(mask):
    """Euclidean distance (px) from each pixel to the nearest True pixel; the map wraps east-west."""
    padded = np.pad(mask, ((PAD, PAD), (PAD, PAD)), mode="wrap")
    padded[:PAD, :] = np.repeat(padded[PAD:PAD + 1, :], PAD, axis=0)  # no north-south wrap
    padded[-PAD:, :] = np.repeat(padded[-PAD - 1:-PAD, :], PAD, axis=0)
    if not padded.any():
        return np.full(mask.shape, np.inf, dtype=np.float32)
    dist = distance_transform_edt(~padded)
    return dist[PAD:-PAD, PAD:-PAD].astype(np.float32)


def river_edge_from_pixels(rivers, river):
    """Distance (px) to the edge of the nearest river, measured to the coloured pixels of rivers.png."""
    edge = np.full(river.shape, RIVER_RANGE, dtype=np.float32)
    for cls, half_width in RIVER_HALF_WIDTH.items():
        mask = river & (np.abs(rivers - np.array(RIVER_CLASS_RGB[cls])).max(axis=-1) <= 20)
        if mask.any():
            edge = np.minimum(edge, distance_to(mask) - half_width)
    return edge


def _densify(lines):
    """Points every LINE_SAMPLE_STEP px along each polyline."""
    out = []
    for line in lines:
        a, b = line[:-1], line[1:]
        seg = np.hypot(*(b - a).T)
        steps = np.maximum(np.ceil(seg / LINE_SAMPLE_STEP).astype(int), 1)
        t = np.concatenate([np.arange(n) / n for n in steps])
        idx = np.repeat(np.arange(len(a)), steps)
        out.append(a[idx] + (b[idx] - a[idx]) * t[:, None])
        out.append(line[-1:])
    return np.concatenate(out)


def river_edge_from_lines(lines, shape):
    """Distance (px) from each texel centre to the edge of the nearest river, measured to the source
    polylines (pixel coordinates, x in 0..width). Only texels within reach of a river are computed."""
    h, w = shape
    edge = np.full(shape, RIVER_RANGE, dtype=np.float32)
    for cls, half_width in RIVER_HALF_WIDTH.items():
        cls_lines = [line for c, line in lines if c == cls and len(line) >= 2]
        if not cls_lines:
            continue
        pts = _densify(cls_lines)
        reach = RIVER_RANGE + half_width
        # the map wraps east-west: mirror samples near either edge
        pts = np.concatenate([pts, pts[pts[:, 0] < reach + 1] + (w, 0), pts[pts[:, 0] > w - reach - 1] - (w, 0)])
        tree = cKDTree(pts)

        near = np.zeros(shape, dtype=bool)
        ix = np.clip(np.floor(pts[:, 0]).astype(int), 0, w - 1)
        iy = np.clip(np.floor(pts[:, 1]).astype(int), 0, h - 1)
        near[iy, ix] = True
        r = int(np.ceil(reach)) + 1
        yy, xx = np.mgrid[-r:r + 1, -r:r + 1]
        near = binary_dilation(near, yy ** 2 + xx ** 2 <= r * r)
        ys, xs = np.nonzero(near)

        d, _ = tree.query(np.column_stack([xs + 0.5, ys + 0.5]), distance_upper_bound=reach + 1)
        edge[ys, xs] = np.minimum(edge[ys, xs], np.minimum(d, RIVER_RANGE + half_width) - half_width)
    return edge


def to_byte(field):
    return np.clip(np.rint(field * 255.0), 0, 255).astype(np.uint8)


def main(river_lines=None):
    Image.MAX_IMAGE_PIXELS = None
    height = np.asarray(Image.open(HEIGHTMAP).convert("RGB"))[..., 0]
    rivers = np.asarray(Image.open(RIVERS).convert("RGB")).astype(np.int16)
    if rivers.shape[:2] != height.shape:
        sys.exit(f"{RIVERS.name} is {rivers.shape[1]}x{rivers.shape[0]}, expected {height.shape[1]}x{height.shape[0]}")

    spread = rivers.max(axis=-1) - rivers.min(axis=-1)
    river = spread > 60                               # saturated palette colors
    sea = (~river) & (rivers.max(axis=-1) < 190)      # grey background; land is white

    # Signed distance measured between pixel centers, so the 0.5 contour sits on the pixel edge.
    land_side = distance_to(sea) - 0.5                # > 0 on land
    sea_side = distance_to(~sea) - 0.5                # > 0 at sea
    coast = np.where(sea, -sea_side, land_side)
    coast_field = 0.5 + np.clip(coast, -RANGE, RANGE) / (2.0 * RANGE)

    # distance to the edge of the nearest river; negative inside a river
    if river_lines is not None:
        edge = river_edge_from_lines(river_lines, height.shape)
    else:
        edge = river_edge_from_pixels(rivers, river)
    river_field = 0.5 - np.clip(edge, -RIVER_RANGE, RIVER_RANGE) / (2.0 * RIVER_RANGE)

    out = np.dstack([height, to_byte(coast_field), to_byte(river_field)])
    Image.fromarray(out, "RGB").save(HEIGHTMAP, optimize=True)
    print(f"{HEIGHTMAP.name}: {int((~sea).sum())} land px, {int(river.sum())} river px baked")


if __name__ == "__main__":
    main()
