"""Shared map geometry and raster helpers for the tools in this folder."""
import pathlib

import numpy as np
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
OCEAN_MASK_PNG = ROOT / "tools" / "data" / "ocean_mask.png"   # white = sea; the fixed coastline source
RIVERS_PNG = ROOT / "map" / "rivers.png"
HEIGHTMAP_PNG = ROOT / "map" / "heightmapps.png"
PROVINCES_PNG = ROOT / "map" / "provinces.png"

# Equirectangular: longitude -180..180 across the width, latitude MAP_TOP..MAP_BOTTOM down the height.
# Fitted against the coastline of the game's land mask.
WIDTH, HEIGHT = 5632, 2316
MAP_TOP, MAP_BOTTOM = 86.6, -61.6
SEA_LEVEL = 128  # heightmap value of sea level

# rivers.png palette (also hard-coded in shaders/main.gdshader and tools/bake_map_fields.py)
LAND_RGB = (255, 255, 255)
WATER_RGB = (122, 122, 122)
RIVER_CLASS_RGB = {           # narrowest .. widest
    0: (0, 225, 255),
    1: (0, 200, 255),
    2: (0, 100, 255),
    3: (0, 0, 200),
}

Image.MAX_IMAGE_PIXELS = None


def to_pixels(points):
    """(lon, lat) array -> (x, y) float pixel coordinates."""
    points = np.asarray(points, dtype=np.float64)
    x = (points[:, 0] + 180.0) * (WIDTH / 360.0)
    y = (MAP_TOP - points[:, 1]) * (HEIGHT / (MAP_TOP - MAP_BOTTOM))
    return np.column_stack([x, y])


def _rings(shape):
    parts = list(shape.parts) + [len(shape.points)]
    for a, b in zip(parts[:-1], parts[1:]):
        if b - a >= 3:
            yield np.asarray(shape.points[a:b], dtype=np.float64)


def _is_hole(ring):
    # Shapefile outer rings are clockwise, holes counter-clockwise (positive shoelace area with y up).
    x, y = ring[:, 0], ring[:, 1]
    return (np.dot(x, np.roll(y, -1)) - np.dot(np.roll(x, -1), y)) > 0


def rasterize_polygons(shapes, values, canvas=None):
    """Paint polygons (with holes) into an int32 array; later shapes overwrite earlier ones.

    Each shape is drawn into a mask the size of its own bounding box, so a hole only
    cuts its own polygon and never erases an enclave drawn before it.
    """
    if canvas is None:
        canvas = np.zeros((HEIGHT, WIDTH), dtype=np.int32)
    for shape, value in zip(shapes, values):
        if not value or not shape.points:
            continue
        rings = [(to_pixels(r), _is_hole(r)) for r in _rings(shape)]
        if not rings:
            continue
        allpts = np.concatenate([r for r, _ in rings])
        x0, y0 = np.floor(allpts.min(axis=0)).astype(int) - 1
        x1, y1 = np.ceil(allpts.max(axis=0)).astype(int) + 1
        cx0, cy0 = max(x0, 0), max(y0, 0)
        cx1, cy1 = min(x1, WIDTH), min(y1, HEIGHT)
        if cx0 >= cx1 or cy0 >= cy1:
            continue
        mask = Image.new("1", (cx1 - cx0, cy1 - cy0), 0)
        draw = ImageDraw.Draw(mask)
        for ring, hole in sorted(rings, key=lambda r: r[1]):   # outer rings first, then holes
            draw.polygon([tuple(p) for p in ring - (cx0, cy0)], fill=0 if hole else 1)
        m = np.asarray(mask, dtype=bool)
        canvas[cy0:cy1, cx0:cx1][m] = value
    return canvas


def load_ocean_mask():
    return np.asarray(Image.open(OCEAN_MASK_PNG).convert("L")) > 127


def load_heights():
    return np.asarray(Image.open(HEIGHTMAP_PNG).convert("RGB"))[..., 0]


def clean(text):
    """Single-line text safe for the ;-separated data files."""
    return " ".join(str(text or "").replace(";", ",").split())
