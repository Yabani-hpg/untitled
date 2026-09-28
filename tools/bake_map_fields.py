#!/usr/bin/env python3
"""Bake coastline and river distance fields into the G and B channels of map/heightmapps.png.

The terrain shader reads one bilinear-filtered texel of these fields and thresholds it,
which gives smooth, resolution-independent coasts and rivers at the cost of a single
texture fetch (instead of dilating the river mask per pixel at runtime).

  R: height (untouched; 128 = sea level)
  G: coast field  = 0.5 + signed_distance_to_coast_px / (2 * RANGE)   (land > 0.5)
  B: river field  = 1 - distance_to_river_px / RANGE                   (1 on the river)

Land/sea comes from map/rivers.png (white land, grey sea); rivers are its colored pixels.
Rerun after editing rivers.png:  python3 tools/bake_map_fields.py   (needs pillow numpy scipy)
"""
import pathlib
import sys

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt

ROOT = pathlib.Path(__file__).resolve().parent.parent
HEIGHTMAP = ROOT / "map" / "heightmapps.png"
RIVERS = ROOT / "map" / "rivers.png"
RANGE = 8.0  # pixels encoded on each side; keep in sync with FIELD_RANGE_TEXELS in shaders/main.gdshader
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


def to_byte(field):
    return np.clip(np.rint(field * 255.0), 0, 255).astype(np.uint8)


def main():
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

    river_field = 1.0 - np.clip(distance_to(river), 0.0, RANGE) / RANGE

    out = np.dstack([height, to_byte(coast_field), to_byte(river_field)])
    Image.fromarray(out, "RGB").save(HEIGHTMAP, optimize=True)
    print(f"{HEIGHTMAP.name}: {int((~sea).sum())} land px, {int(river.sum())} river px baked")


if __name__ == "__main__":
    main()
