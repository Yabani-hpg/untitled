#!/usr/bin/env python3
"""Generate the starting populations, resources, features and buildings of every land province.

Reads the map (map/provinces.png, landmap.png, heightmapps.png, rivers.png) and data/provinces.csv,
data/adjacencies.csv; writes data/province_setup.json, which the game loads on top of the provinces.
import_natural_earth.py runs this at the end, because province ids change on every import. Hand
edits to province_setup.json are overwritten; tune the tables below instead.

Each land province gets:
  features   forest, river, coast, desert, steppe, tundra, mountains, hills (from terrain, vegetation
             colour of landmap.png, rivers and adjacencies)
  pops       population groups of 1000 people ({culture, religion, occupation, units}): culture by
             continent, peasants or tribesmen by how farmable the land is
  resources  {non_renewable, food}: one deposit (or none) and one food resource
  buildings  a few production buildings the population has already put up

Usage: python3 tools/generate_setup.py
"""
import hashlib
import json
import sys

import numpy as np
from PIL import Image

from map_common import (HEIGHT, HEIGHTMAP_PNG, MAP_BOTTOM, MAP_TOP, PROVINCES_PNG, RIVER_CLASS_RGB, RIVERS_PNG,
                        ROOT, SEA_LEVEL, WIDTH)

LANDMAP_PNG = ROOT / "map" / "landmap.png"
PROVINCES_CSV = ROOT / "data" / "provinces.csv"
ADJACENCIES_CSV = ROOT / "data" / "adjacencies.csv"
BUILDINGS_JSON = ROOT / "data" / "buildings.json"
SETUP_JSON = ROOT / "data" / "province_setup.json"

WORLD_POPULATION = 50_000_000       # roughly the world around 1000 BC
UNIT = 1000                         # people per population unit
RELIGION = "pagan"

# --- culture by continent -------------------------------------------------------------------------------
EUROPE = set("""ALB ALD AND AUT BEL BGR BIH BLR CHE CYN CYP CZE DEU DNK ESP EST FIN FRA FRO GBR GRC HRV HUN IMN
IRL ISL ITA KOS LIE LTU LUX LVA MCO MDA MKD MLT MNE NLD NOR POL PRT ROU SMR SRB SVK SVN SWE UKR VAT""".split())
AFRICA = set("""AGO BDI BEN BFA BWA CAF CIV CMR COD COG COM CPV DJI DZA EGY ERI ETH GAB GHA GIN GMB GNB GNQ KEN
LBR LBY LSO MAR MDG MLI MOZ MRT MUS MWI NAM NER NGA RWA SAH SDN SDS SEN SLE SOL SOM STP SWZ SYC TCD TGO TUN TZA
UGA ZAF ZMB ZWE""".split())
CENTRAL_AMERICA = set("BLZ CRI GTM HND MEX NIC PAN SLV CUB JAM HTI DOM BHS PRI TCA".split())
UNINHABITED = set("ATA ATF HMD SGS IOT".split())
OCEANIA = set("AUS NZL PNG FJI NCL SLB VUT WSM TON PYF FSM PLW GUM".split())   # Asian culture, thinly settled

# --- population density (relative) ---------------------------------------------------------------------
DENSITY = {"farmland": 1.0, "forest": 0.35, "steppe": 0.3, "desert": 0.03, "tundra": 0.02}
DENSITY_TERRAIN = {"mountains": 0.45, "hills": 0.8}
RIVER_BONUS, COAST_BONUS = 2.2, 1.3
CONTINENT_DENSITY = {"european": 1.0, "asian": 1.2, "african": 0.8, "north_american": 0.35, "south_american": 0.45}

# share of tribesmen (the rest are peasants) by vegetation; rivers settle people
TRIBAL_SHARE = {"farmland": 0.1, "forest": 0.45, "steppe": 0.85, "desert": 0.9, "tundra": 1.0}
TRIBAL_CONTINENT_BONUS = {"african": 0.25, "north_american": 0.35, "south_american": 0.2}

# crude oil basins (lon0, lat0, lon1, lat1), where half the provinces with no other deposit have oil seeps
OIL_BASINS = [(44, 24, 57, 33), (46, 38, 56, 43), (-104, 26, -92, 36), (-73, 5, -61, 11), (65, 55, 85, 66),
              (4, 3, 9, 7), (13, 26, 25, 31), (106, 35, 126, 48), (-120, 34, -116, 37)]


def h01(*parts):
    """Deterministic pseudo-random number in 0..1 from the parts."""
    digest = hashlib.blake2b("|".join(map(str, parts)).encode(), digest_size=8).digest()
    return int.from_bytes(digest, "little") / 2 ** 64


def pick(options, *seed):
    """Weighted choice from [(value, weight)], deterministic in seed."""
    total = sum(w for _, w in options)
    r = h01(*seed) * total
    for value, w in options:
        r -= w
        if r < 0:
            return value
    return options[-1][0]


def read_csv(path):
    rows, header = [], None
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        cols = line.split(";")
        if header is None:
            header = cols
            continue
        rows.append(dict(zip(header, cols)))
    return rows


def culture_of(tag, lon, lat):
    if tag in EUROPE and lon > -30 and lat > 30:     # not the overseas parts (French Guiana, the Canaries)
        return "european"
    if tag in EUROPE:
        return "african" if lon > -30 else "north_american" if lat > 12.5 else "south_american"
    if tag == "RUS":
        return "european" if lon < 60 else "asian"
    if tag == "TUR":
        return "european" if lon < 29.5 else "asian"
    if tag in AFRICA:
        return "african"
    if tag in CENTRAL_AMERICA or tag in ("USA", "CAN", "GRL"):
        return "north_american"
    if lon < -25:
        return "north_american" if lat > 12.5 else "south_american"
    return "asian"


# grasslands that modern farmland hides on the satellite colours: (lon0, lat0, lon1, lat1)
STEPPE_BOXES = [(28, 44, 60, 52.5), (-104, 33, -96, 50)]   # the Pontic-Caspian steppe, the Great Plains
# river deltas and flood plains that were the most crowded farmland of the ancient world, whatever their
# colour from space: the Nile Delta and Faiyum, Lower Mesopotamia (lon0, lat0, lon1, lat1)
FLOODPLAIN_BOXES = [(29.8, 29.0, 32.4, 31.7), (44.0, 30.5, 48.5, 33.6)]


def vegetation(rgb, lon, lat):
    """farmland, forest, steppe, desert or tundra from the average landmap colour."""
    r, g, b = rgb
    lum = (r + g + b) / 3
    sat = max(rgb) - min(rgb)
    if abs(lat) >= 66 or (lum >= 195 and sat < 20):
        return "tundra"
    if lum >= 132 and r - b >= 40 and abs(lat) < 50:
        return "desert"
    if lum < 33 or (lum < 42 and abs(lat) >= 40) or (lat >= 50 and sat < 22 and lum < 190):
        return "forest"          # rainforest, the dark temperate forests, grey-green boreal forest
    if any(x0 <= lon <= x1 and y0 <= lat <= y1 for x0, y0, x1, y1 in STEPPE_BOXES):
        return "steppe"
    if -10 <= lon <= 30 and 30 <= lat <= 46:
        return "farmland"        # the dry Mediterranean scrub looks like steppe from space, but it was farmed
    if r - b >= 36 and lum >= 70 and (abs(lat) >= 28 or 10 <= lat < 20):
        return "steppe"          # dry grassland: temperate steppes, prairies, pampas, the Sahel
    return "farmland"


def food_for(p):
    f, veg, cul, lat, pid = p["features"], p["veg"], p["culture"], p["lat"], p["id"]
    tropical = abs(lat) < 23.5
    if veg == "tundra":
        return "fish" if "coast" in f else "sheep"
    if veg == "desert":
        if cul.endswith("american") and "river" in f:
            return "corn"
        if "river" in f and lat > 22:
            return "wheat"           # the Nile, the Euphrates
        if "river" in f or cul in ("african", "asian") and 15 < lat < 35 and h01(pid, "oasis") < 0.35:
            return "dates"
        return pick([("sheep", 2), ("horses", 1)] if cul == "asian" else [("cattle", 1), ("sheep", 1)], pid, "fd")
    if veg == "steppe" and "river" in f and abs(lat) < 38 and cul in ("asian", "african"):
        return "wheat"           # irrigated river valleys: Mesopotamia, the Indus
    if veg == "steppe":
        if cul == "asian" or cul == "european" and lat > 44:
            return pick([("horses", 3), ("sheep", 2), ("cattle", 1)], pid, "fs")
        return pick([("cattle", 3), ("sheep", 1)], pid, "fs")
    if "coast" in f and h01(pid, "fish") < 0.12:
        return "fish"
    if cul == "south_american":
        return "potatoes" if "mountains" in f or "hills" in f or lat < -30 else "corn"
    if cul == "north_american":
        return "corn"
    if cul == "african":
        if lat > 28 or lat < -30 or lat > 17 and "river" in f:     # the Mediterranean coast, the Nile, the Cape
            return "wheat"
        return pick([("millet", 3), ("cattle", 1)], pid, "fa")
    if cul == "asian" and p["lon"] > 100 and 32 <= lat < 43:
        return pick([("millet", 3), ("wheat", 1)], pid, "fc")     # the Yellow River plain
    if cul == "asian" and (p["lon"] > 75 and lat < 32 or p["lon"] > 95 and tropical):
        return "rice"
    if veg == "forest":
        return pick([("cattle", 2), ("sheep", 1), ("wheat", 1)], pid, "ff")
    return pick([("wheat", 5), ("cattle", 1), ("sheep", 1)], pid, "fw")


def deposit_for(p):
    f, pid = p["features"], p["id"]
    if "mountains" in f:
        options = [("iron", 3), ("copper", 3), ("tin", 1), ("gold", 1), ("silver", 1), ("stone", 2)]
    elif "hills" in f:
        options = [("stone", 4), ("iron", 3), ("copper", 2), ("salt", 1), (None, 3)]
    elif "desert" in f:
        options = [("salt", 3), ("stone", 1), ("copper", 1), (None, 5)]
    else:
        options = [("stone", 2), ("iron", 1), ("salt", 1), (None, 8)]
    value = pick(options, pid, "nr")
    if value is None or value == "stone":
        for lon0, lat0, lon1, lat1 in OIL_BASINS:
            if lon0 <= p["lon"] <= lon1 and lat0 <= p["lat"] <= lat1 and h01(pid, "oil") < 0.5:
                return "crude_oil"
    return value


def pops_for(p, units):
    floodplain = p["valley"] > 0.5      # settled farmers of a river valley, not desert nomads
    tribal = TRIBAL_SHARE["farmland" if floodplain else p["veg"]] + TRIBAL_CONTINENT_BONUS.get(p["culture"], 0.0)
    if p["oceania"]:
        tribal += 0.6
    if "river" in p["features"]:
        tribal *= 0.35
    if p["food"] == "horses":
        tribal = max(tribal, 0.9)
    tribal = min(max(tribal + (h01(p["id"], "tr") - 0.5) * 0.2, 0.0), 1.0)
    tribesmen = int(round(units * tribal))
    pops = []
    for occupation, n in (("peasants", units - tribesmen), ("tribesmen", tribesmen)):
        if n > 0:
            pops.append({"culture": p["culture"], "religion": RELIGION, "occupation": occupation, "units": n})
    return pops


def buildings_for(p, defs):
    """Buildings the population would have put up by itself (the same rule the game applies monthly)."""
    have = {pop["occupation"]: pop["units"] for pop in p["pops"]}
    built = []
    for b in defs:
        rule = b.get("population_builds")
        if rule is None or "population" not in b["builders"]:
            continue
        if have.get(rule["occupation"], 0) < rule["min_units"]:
            continue
        req = b.get("requires", {})
        if "features" in req and not set(req["features"]) & set(p["features"]):
            continue
        if "food" in req and p["food"] not in req["food"]:
            continue
        if "non_renewable" in req and p["deposit"] not in req["non_renewable"]:
            continue
        built.append({"type": b["id"], "level": 1})
    return built


def main():
    provinces = read_csv(PROVINCES_CSV)
    land = {int(r["id"]): r for r in provinces if r["terrain"] not in ("sea", "lake")}
    by_color = {int(r["color"][1:], 16): int(r["id"]) for r in provinces}

    rgb = np.asarray(Image.open(PROVINCES_PNG).convert("RGB")).astype(np.int32)
    key = (rgb[..., 0] << 16) | (rgb[..., 1] << 8) | rgb[..., 2]
    lut_keys = np.array(sorted(by_color))
    ids = np.array([by_color[k] for k in lut_keys])[np.searchsorted(lut_keys, key).clip(0, len(lut_keys) - 1)]
    ids = ids.ravel()
    n = max(by_color.values()) + 1

    ys, xs = np.divmod(np.arange(WIDTH * HEIGHT), WIDTH)
    lat_px = MAP_TOP - (ys + 0.5) * (MAP_TOP - MAP_BOTTOM) / HEIGHT
    km2 = np.cos(np.radians(lat_px)).astype(np.float32)          # relative area of a pixel
    count = np.bincount(ids, minlength=n).astype(np.float64)
    area = np.bincount(ids, weights=km2, minlength=n)
    safe = np.maximum(count, 1)
    # longitude as a circular mean, so provinces across the date line get a sensible centre
    ang = np.radians(xs * 360.0 / WIDTH - 180.0)
    lon = np.degrees(np.arctan2(np.bincount(ids, np.sin(ang), n), np.bincount(ids, np.cos(ang), n)))
    lat = np.bincount(ids, lat_px, n) / safe
    land_rgb = np.asarray(Image.open(LANDMAP_PNG).convert("RGB")).reshape(-1, 3).astype(np.float64)
    color = np.stack([np.bincount(ids, land_rgb[:, c], n) / safe for c in range(3)], axis=1)
    height = np.asarray(Image.open(HEIGHTMAP_PNG).convert("RGB"))[..., 0].ravel().astype(np.float64)
    elev = np.bincount(ids, np.maximum(height - SEA_LEVEL, 0), n) / safe

    rivers = np.asarray(Image.open(RIVERS_PNG).convert("RGB")).reshape(-1, 3)
    river_px = np.zeros(len(ids), dtype=bool)
    for cls, c in RIVER_CLASS_RGB.items():
        if cls >= 1:                     # the thinnest class is creeks; only real rivers count
            river_px |= (rivers == c).all(axis=1)
    river_count = np.bincount(ids[river_px], minlength=n)

    coast, river_link = set(), set()
    for r in read_csv(ADJACENCIES_CSV):
        a, b = int(r["a"]), int(r["b"])
        if a == 1 or b == 1:
            coast.add(b if a == 1 else a)
        if r["crossing"] or r["navigable"]:
            river_link.update((a, b))

    buildings = json.loads(BUILDINGS_JSON.read_text(encoding="utf-8"))["buildings"]
    setup = []
    for pid, row in sorted(land.items()):
        tag = row["owner"]
        p = {"id": pid, "lon": float(lon[pid]), "lat": float(lat[pid])}
        p["veg"] = vegetation(tuple(color[pid]), p["lon"], p["lat"])
        floodplain = any(x0 <= p["lon"] <= x1 and y0 <= p["lat"] <= y1 for x0, y0, x1, y1 in FLOODPLAIN_BOXES)
        if floodplain:
            p["veg"] = "farmland"
        p["floodplain"] = floodplain
        features = []
        if p["veg"] != "farmland":
            features.append(p["veg"])
        if row["terrain"] in ("mountains", "hills"):
            features.append(row["terrain"])
        if pid in river_link or river_count[pid] >= 3 or floodplain:
            features.append("river")
        if pid in coast:
            features.append("coast")
        p["features"] = features
        p["culture"] = culture_of(tag, p["lon"], p["lat"])
        p["food"] = food_for(p)
        p["deposit"] = deposit_for(p)

        density = DENSITY[p["veg"]]
        # a flood plain in the desert (the Nile, the Euphrates) packs its people along the river: the bonus
        # scales with how much of the province is river valley, so a vast desert that merely touches the
        # Nile stays empty
        valley = min(1.0, 12.0 * river_count[pid] / max(count[pid], 1))
        if p["floodplain"]:
            density = DENSITY["farmland"] * 5.0
        elif p["veg"] == "desert" and "river" in features:
            density = DENSITY["desert"] + DENSITY["farmland"] * 5.0 * valley
        p["valley"] = 1.0 if p["floodplain"] else valley
        weight = density * DENSITY_TERRAIN.get(row["terrain"], 1.0) * CONTINENT_DENSITY[p["culture"]]
        if "river" in features:
            weight *= RIVER_BONUS
        if "coast" in features:
            weight *= COAST_BONUS
        weight *= 0.75 + 0.5 * h01(pid, "dens")
        if abs(p["lat"]) > 47:           # short growing seasons
            weight *= min(max((62 - abs(p["lat"])) / 15, 0.08), 1.0)
        p["oceania"] = tag in OCEANIA
        if p["oceania"]:
            weight *= 0.12
        if tag in UNINHABITED or elev[pid] > 120:
            weight = 0.0
        p["weight"] = weight * area[pid]
        setup.append(p)

    scale = WORLD_POPULATION / UNIT / sum(p["weight"] for p in setup)
    out = []
    for p in setup:
        units = int(round(p["weight"] * scale))
        if p["weight"] > 0:
            units = max(units, 2 if p["veg"] in ("farmland", "forest", "steppe") else 1)
        p["pops"] = pops_for(p, units) if units > 0 else []
        entry = {
            "id": p["id"],
            "features": p["features"],
            "resources": {"non_renewable": p["deposit"], "food": p["food"]},
            "pops": p["pops"],
            "buildings": buildings_for(p, buildings),
        }
        out.append(entry)

    with SETUP_JSON.open("w", encoding="utf-8", newline="\n") as f:
        f.write('{\n\t"_comment": "Generated by tools/generate_setup.py; hand edits are overwritten. pops: units of '
                f'{UNIT} people.",\n\t"provinces": [\n')
        f.write(",\n".join("\t\t" + json.dumps(e, separators=(", ", ": ")) for e in out))
        f.write("\n\t]\n}\n")

    total = sum(pop["units"] for e in out for pop in e["pops"])
    tally = lambda k: {v: sum(1 for p in setup if p[k] == v) for v in sorted({p[k] for p in setup}, key=str)}
    occ = {}
    for e in out:
        for pop in e["pops"]:
            occ[pop["occupation"]] = occ.get(pop["occupation"], 0) + pop["units"]
    print(f"{len(out)} land provinces, {total} population units ({total * UNIT / 1e6:.1f}M people): {occ}")
    print("vegetation:", tally("veg"))
    print("culture:", tally("culture"))
    print("food:", tally("food"))
    print("deposits:", tally("deposit"))
    print("buildings:", sum(len(e["buildings"]) for e in out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
