"""Lakes and rivers from Natural Earth, rasterized onto the game map. Used by import_natural_earth.py."""
from dataclasses import dataclass

import numpy as np
import shapefile
from PIL import Image, ImageDraw

from map_common import (HEIGHT, LAND_RGB, RIVER_CLASS_RGB, RIVERS_PNG, WATER_RGB, WIDTH, clean,
                        load_ocean_mask, rasterize_polygons, to_pixels)

# Reservoirs are modern dams; leave them out of a historical map.
SKIP_LAKE_CLASSES = {"Reservoir"}

# Natural Earth scalerank: 0 = most important. Width class 3 is the widest.
def river_class(scalerank):
    if scalerank <= 2:
        return 3
    if scalerank <= 4:
        return 2
    if scalerank <= 6:
        return 1
    return 0


# Rivers small ships can sail. Rank 0-5 covers the Nile, Danube, Rhine, Volga, Elbe, Vistula, Loire...;
# the list adds historically navigable rivers Natural Earth ranks lower. Edit freely.
NAVIGABLE_MAX_RANK = 5
NAVIGABLE_EXTRA = {"Don", "Po", "Thames", "Rhône", "Dniester", "Garonne", "Guadalquivir", "Weser", "Meuse"}
NON_NAVIGABLE_CLASSES = {"River (Intermittent)"}


@dataclass
class River:
    name: str
    width_class: int
    navigable: bool


@dataclass
class Water:
    ocean: np.ndarray        # bool, fixed coastline source
    lake_index: np.ndarray   # int32, lake record index + 1 per pixel, 0 = not a lake
    lake_names: list         # indexed by lake record index
    water: np.ndarray        # bool, ocean or any lake (what the player sees as water)
    river_index: np.ndarray  # int32, river record index + 1 per river pixel off the ocean (kept inside
                             # lakes so navigable chains run through them), 0 elsewhere
    rivers: list             # River per record index
    river_lines: list        # (width class, Nx2 float pixel coords) per drawn polyline part


def build(lakes_path, rivers_path):
    ocean = load_ocean_mask()

    lakes = shapefile.Reader(lakes_path, encoding="utf-8")
    lake_records = lakes.records()
    keep = [r["featurecla"] not in SKIP_LAKE_CLASSES for r in lake_records]
    lake_index = rasterize_polygons(lakes.iterShapes(), [i + 1 if k else 0 for i, k in enumerate(keep)])
    lake_names = [clean(r["name_en"]) or clean(r["name"]) for r in lake_records]
    water = ocean | (lake_index > 0)

    reader = shapefile.Reader(rivers_path, encoding="utf-8")
    rivers = []
    for r in reader.records():
        name = clean(r["name_en"]) or clean(r["name"])
        navigable = (r["featurecla"] not in NON_NAVIGABLE_CLASSES
                     and (r["scalerank"] <= NAVIGABLE_MAX_RANK or name in NAVIGABLE_EXTRA))
        rivers.append(River(name, river_class(r["scalerank"]), navigable))

    canvas = Image.new("I", (WIDTH, HEIGHT), 0)
    classes = Image.new("L", (WIDTH, HEIGHT), 255)
    draw_i, draw_c = ImageDraw.Draw(canvas), ImageDraw.Draw(classes)
    shapes = reader.shapes()
    river_lines = []
    # narrow first so wider rivers win where they meet
    for i in sorted(range(len(rivers)), key=lambda i: rivers[i].width_class):
        shape = shapes[i]
        parts = list(shape.parts) + [len(shape.points)]
        for a, b in zip(parts[:-1], parts[1:]):
            if b - a < 2:
                continue
            line = to_pixels(shape.points[a:b])
            river_lines.append((rivers[i].width_class, line))
            pts = [tuple(p) for p in line]
            draw_i.line(pts, fill=i + 1, width=1)
            draw_c.line(pts, fill=rivers[i].width_class, width=1)
    river_index = np.asarray(canvas, dtype=np.int32).copy()
    river_class_px = np.asarray(classes, dtype=np.uint8)
    river_index[ocean] = 0

    rgb = np.empty((HEIGHT, WIDTH, 3), dtype=np.uint8)
    rgb[:] = LAND_RGB
    rgb[water] = WATER_RGB
    for cls, color in RIVER_CLASS_RGB.items():
        rgb[(river_index > 0) & ~water & (river_class_px == cls)] = color   # drawn on land only
    Image.fromarray(rgb, "RGB").save(RIVERS_PNG, optimize=True)

    return Water(ocean, lake_index, lake_names, water, river_index, rivers, river_lines)
