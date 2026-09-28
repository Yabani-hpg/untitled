#!/usr/bin/env python3
"""Append a row to data/provinces.csv for every color in map/provinces.png that has none.

New provinces get the next free ids, a placeholder name and terrain, and no owner; edit
those afterwards. Existing rows are never changed or renumbered, because ids are used by
game logic and save files. Colors are processed in the order they first appear (top-left
to bottom-right), so running the tool twice on the same map gives the same ids.

Usage: python3 tools/sync_provinces.py [--dry-run]     (needs: pip install pillow numpy)
"""
import argparse
import pathlib
import sys

import numpy as np
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
MAP_PATH = ROOT / "map" / "provinces.png"
CSV_PATH = ROOT / "data" / "provinces.csv"


def read_known(csv_path):
    """Return (set of known 0xRRGGBB ints, highest id) from the definition file."""
    known, max_id, header_seen = set(), 0, False
    for line in csv_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if not header_seen:
            header_seen = True
            continue
        cols = line.split(";")
        max_id = max(max_id, int(cols[0]))
        known.add(int(cols[1].strip().lstrip("#"), 16))
    return known, max_id


def colors_in_order(map_path):
    """Unique 0xRRGGBB colors of the map, ordered by first occurrence."""
    rgb = np.asarray(Image.open(map_path).convert("RGB"), dtype=np.uint32)
    packed = ((rgb[..., 0] << 16) | (rgb[..., 1] << 8) | rgb[..., 2]).ravel()
    colors, first_index = np.unique(packed, return_index=True)
    return [int(c) for c in colors[np.argsort(first_index)]]


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--dry-run", action="store_true", help="print new rows without writing")
    args = parser.parse_args()

    known, max_id = read_known(CSV_PATH)
    missing = [c for c in colors_in_order(MAP_PATH) if c not in known]
    if not missing:
        print("provinces.csv already lists every map color.")
        return 0

    rows = []
    for offset, color in enumerate(missing, start=1):
        pid = max_id + offset
        rows.append(f"{pid};#{color:06x};Province {pid};unset;")

    if args.dry_run:
        print("\n".join(rows))
    else:
        text = CSV_PATH.read_text(encoding="utf-8")
        if not text.endswith("\n"):
            text += "\n"
        CSV_PATH.write_text(text + "\n".join(rows) + "\n", encoding="utf-8")
    print(f"{len(rows)} new province(s), ids {max_id + 1}-{max_id + len(rows)}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
