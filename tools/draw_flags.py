#!/usr/bin/env python3
"""Draw country flags as PNG files under gfx/flags.

Countries with a file in data/countries/ list their own flags there, each with a condition (dynamic
flags); their designs are drawn by the functions in SPECIAL below. Every other country gets one generated
banner in its map colour, with a motif of its main culture: gfx/flags/<TAG>.png.

Outputs (overwritten): gfx/flags/*.png
Usage: python3 tools/draw_flags.py        (needs: pip install cairosvg)
"""
import hashlib
import json
import math
import sys

import cairosvg

from map_common import ROOT

OUT = ROOT / "gfx" / "flags"
COUNTRIES_JSON = ROOT / "data" / "countries.json"
COUNTRY_DIR = ROOT / "data" / "countries"
PROVINCES_CSV = ROOT / "data" / "provinces.csv"
SETUP_JSON = ROOT / "data" / "province_setup.json"
W, H = 300, 200

WHITE, GOLD, BLACK, RED, BLUE, GREEN = "#f1ead8", "#d9a93a", "#231c16", "#9a2622", "#1f3f78", "#2f6b3a"


def svg(body, defs=""):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">'
            f'<defs>{defs}</defs>{body}'
            f'<rect x="1" y="1" width="{W - 2}" height="{H - 2}" fill="none" stroke="#000" stroke-opacity="0.35" stroke-width="2"/></svg>')


def render(text, name):
    OUT.mkdir(parents=True, exist_ok=True)
    cairosvg.svg2png(bytestring=text.encode(), write_to=str(OUT / name), output_width=W, output_height=H)


# ---------------------------------------------------------------------------------------------- Egypt

def deshret(front=True):
    """The red crown of Lower Egypt, in a 100x100 box facing right: the tall back plate, the cap and the coil."""
    back = '<path d="M18 86 L18 10 L32 10 L32 56 Z" fill="#b8261e" stroke="#3a0d09" stroke-width="3" stroke-linejoin="round"/>'
    cap = '<path d="M18 86 L18 56 L32 56 C48 58 66 58 80 62 L84 86 Z" fill="#c62d24" stroke="#3a0d09" stroke-width="3" stroke-linejoin="round"/>'
    coil = ('<path d="M44 60 C46 44 60 36 70 42 C78 47 74 58 66 56 C61 55 61 49 65 48" fill="none" stroke="#e3b94a" '
            'stroke-width="4" stroke-linecap="round"/>')
    return (back, cap + (coil if front else ""))


def hedjet():
    """The white crown of Upper Egypt: a tall cone ending in a bulb."""
    return ('<path d="M30 74 C33 54 42 42 44 26 C45 10 50 2 57 2 C64 2 69 10 68 26 C67 42 76 54 80 74 Z" '
            'fill="#f7f3e8" stroke="#3a2a1c" stroke-width="3" stroke-linejoin="round"/>')


def uraeus():
    return ('<path d="M82 74 C88 70 92 62 90 56 C88 51 83 52 83 57" fill="none" stroke="#e3b94a" stroke-width="4.5" '
            'stroke-linecap="round"/><ellipse cx="84" cy="56" rx="3.5" ry="4.5" fill="#e3b94a" stroke="#6b4f1d"/>')


def pschent(x, y, size):
    """The double crown: the white crown rising out of the red one."""
    back, front = deshret()
    s = size / 100
    return f'<g transform="translate({x} {y}) scale({s})">{back}{hedjet()}{front}{uraeus()}</g>'


def single_crown(x, y, size, which):
    s = size / 100
    if which == "white":
        body = hedjet() + '<path d="M34 72 L76 72 L78 86 L32 86 Z" fill="#f7f3e8" stroke="#3a2a1c" stroke-width="3"/>' + uraeus()
    else:
        back, front = deshret()
        body = back + front + uraeus()
    return f'<g transform="translate({x} {y}) scale({s})">{body}</g>'


def roundel(cx, cy, r, fill, ring):
    return (f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="{fill}" stroke="{ring}" stroke-width="6"/>'
            f'<circle cx="{cx}" cy="{cy}" r="{r - 7}" fill="none" stroke="{GOLD}" stroke-width="2"/>')


def egypt_default():
    """White for Upper Egypt, red for Lower Egypt, the double crown of the united Two Lands on a golden sun."""
    return svg(f'<rect width="150" height="200" fill="{WHITE}"/><rect x="150" width="150" height="200" fill="#b8261e"/>'
               + roundel(150, 100, 76, GOLD, "#1f3b7a") + pschent(90, 38, 124))


def egypt_upper():
    """Only Upper Egypt: white, with the white crown and the green sedge of the south."""
    sedge = "".join(f'<path d="M{x} 200 C{x} 170 {x + (i - 2) * 6} 150 {x + (i - 2) * 12} 138" fill="none" stroke="{GREEN}" stroke-width="5"/>'
                    for i, x in enumerate(range(30, 300, 60)))
    return svg(f'<rect width="300" height="200" fill="{WHITE}"/>{sedge}'
               + roundel(150, 96, 72, "#2e5fa3", "#1b1410") + single_crown(92, 36, 116, "white"))


def egypt_lower():
    """Only Lower Egypt: red, with the red crown and the papyrus of the north."""
    papyrus = "".join(f'<path d="M{x} 200 L{x} 160" stroke="{GREEN}" stroke-width="5"/><path d="M{x - 14} 146 L{x} 162 L{x + 14} 146 Z" fill="{GREEN}"/>'
                      for x in range(30, 300, 60))
    return svg(f'<rect width="300" height="200" fill="#b8261e"/>{papyrus}'
               + roundel(150, 96, 72, "#2f7a4a", "#1b1410") + single_crown(92, 36, 116, "red"))


def egypt_empire():
    """The empire from Nubia to Syria: the double crown on a winged sun, inside a border of the Nine Bows."""
    wings = ""
    for side in (-1, 1):
        for i in range(4):
            y = 84 + i * 9
            wings += (f'<path d="M150 {y} C{150 + side * 60} {y - 18 + i * 4} {150 + side * 110} {y - 22 + i * 6} {150 + side * (128 - i * 12)} {y - 8 + i * 3} '
                      f'C{150 + side * 100} {y + 2} {150 + side * 60} {y + 6} 150 {y + 8} Z" fill="{["#1f3b7a", GOLD, "#2e8a8a", GOLD][i]}" stroke="#1b1410" stroke-width="1.5"/>')
    bows = "".join(f'<path d="M{16 + i * 31} 14 q12 -10 24 0" fill="none" stroke="{BLACK}" stroke-width="3"/>' for i in range(9))
    return svg(f'<rect width="150" height="200" fill="{WHITE}"/><rect x="150" width="150" height="200" fill="#b8261e"/>'
               f'<rect x="4" y="4" width="292" height="192" fill="none" stroke="{GOLD}" stroke-width="8"/>'
               f'<rect x="10" y="10" width="280" height="180" fill="none" stroke="#1f3b7a" stroke-width="3"/>{bows}'
               + wings + roundel(150, 100, 50, GOLD, "#1f3b7a") + pschent(110, 60, 80))


def egypt_islamic():
    """Misr under Islam: green, with a white crescent and three stars."""
    stars = "".join(star(x, y, 10, WHITE) for x, y in ((208, 70), (226, 100), (208, 130)))
    return svg(f'<rect width="300" height="200" fill="#1f6b3a"/>'
               f'<circle cx="150" cy="100" r="58" fill="{WHITE}"/><circle cx="170" cy="100" r="50" fill="#1f6b3a"/>{stars}')


def kerma():
    """Kerma: the red field and black rim of its black-topped pottery, with the horns of the sacred bull."""
    horns = ('<path d="M96 132 C92 104 104 84 126 82 C118 94 116 108 122 120 L150 132 L178 120 C184 108 182 94 174 82 '
             'C196 84 208 104 204 132 C196 152 172 158 150 150 C128 158 104 152 96 132 Z" fill="#f1ead8" stroke="#231c16" stroke-width="3"/>'
             '<circle cx="150" cy="118" r="14" fill="#d9a93a" stroke="#231c16" stroke-width="3"/>')
    return svg(f'<rect width="300" height="200" fill="#8f2a24"/><rect width="300" height="46" fill="#231c16"/>'
               f'<rect y="46" width="300" height="6" fill="#d9a93a"/>{horns}')


SPECIAL = {
    "KER": {"KER.png": kerma},
    "EGY": {
        "EGY.png": egypt_default,
        "EGY_upper.png": egypt_upper,
        "EGY_lower.png": egypt_lower,
        "EGY_empire.png": egypt_empire,
        "EGY_islamic.png": egypt_islamic,
    },
}


# -------------------------------------------------------------------------------------------- generic

def star(cx, cy, r, color, points=5, inner=0.45):
    pts = []
    for i in range(points * 2):
        a = -math.pi / 2 + i * math.pi / points
        rr = r if i % 2 == 0 else r * inner
        pts.append(f"{cx + rr * math.cos(a):.1f},{cy + rr * math.sin(a):.1f}")
    return f'<polygon points="{" ".join(pts)}" fill="{color}"/>'


def motif(kind, cx, cy, r, color, dark):
    """A culture's emblem, centred at (cx, cy) with radius r."""
    if kind == "labrys":      # the double axe
        return (f'<rect x="{cx - r * 0.07}" y="{cy - r}" width="{r * 0.14}" height="{r * 2}" fill="{color}"/>'
                f'<path d="M{cx} {cy - r * 0.35} C{cx - r * 0.5} {cy - r * 0.9} {cx - r} {cy - r * 0.6} {cx - r} {cy - r * 0.1} '
                f'C{cx - r} {cy + r * 0.3} {cx - r * 0.5} {cy + r * 0.5} {cx} {cy} Z" fill="{color}"/>'
                f'<path d="M{cx} {cy - r * 0.35} C{cx + r * 0.5} {cy - r * 0.9} {cx + r} {cy - r * 0.6} {cx + r} {cy - r * 0.1} '
                f'C{cx + r} {cy + r * 0.3} {cx + r * 0.5} {cy + r * 0.5} {cx} {cy} Z" fill="{color}"/>')
    if kind == "wheel":       # the sun wheel
        spokes = "".join(f'<line x1="{cx}" y1="{cy}" x2="{cx + r * math.cos(a):.1f}" y2="{cy + r * math.sin(a):.1f}" stroke="{color}" stroke-width="{r * 0.12:.1f}"/>'
                         for a in [i * math.pi / 4 for i in range(8)])
        return f'<circle cx="{cx}" cy="{cy}" r="{r * 0.9}" fill="none" stroke="{color}" stroke-width="{r * 0.16:.1f}"/>{spokes}'
    if kind == "sun":         # a rayed sun
        rays = "".join(f'<polygon points="{cx + r * 0.55 * math.cos(a - 0.12):.1f},{cy + r * 0.55 * math.sin(a - 0.12):.1f} '
                       f'{cx + r * math.cos(a):.1f},{cy + r * math.sin(a):.1f} {cx + r * 0.55 * math.cos(a + 0.12):.1f},{cy + r * 0.55 * math.sin(a + 0.12):.1f}" fill="{color}"/>'
                       for a in [i * math.pi / 6 for i in range(12)])
        return f'{rays}<circle cx="{cx}" cy="{cy}" r="{r * 0.5}" fill="{color}"/>'
    if kind == "star":
        return star(cx, cy, r, color, points=8, inner=0.5)
    if kind == "lozenge":
        return (f'<polygon points="{cx},{cy - r} {cx + r * 0.7},{cy} {cx},{cy + r} {cx - r * 0.7},{cy}" fill="{color}"/>'
                f'<polygon points="{cx},{cy - r * 0.45} {cx + r * 0.3},{cy} {cx},{cy + r * 0.45} {cx - r * 0.3},{cy}" fill="{dark}"/>')
    if kind == "shield":      # a hide shield on crossed spears
        return (f'<line x1="{cx - r}" y1="{cy + r}" x2="{cx + r}" y2="{cy - r}" stroke="{color}" stroke-width="{r * 0.1:.1f}"/>'
                f'<line x1="{cx + r}" y1="{cy + r}" x2="{cx - r}" y2="{cy - r}" stroke="{color}" stroke-width="{r * 0.1:.1f}"/>'
                f'<ellipse cx="{cx}" cy="{cy}" rx="{r * 0.45}" ry="{r * 0.8}" fill="{color}" stroke="{dark}" stroke-width="3"/>'
                f'<line x1="{cx}" y1="{cy - r * 0.8}" x2="{cx}" y2="{cy + r * 0.8}" stroke="{dark}" stroke-width="4"/>')
    if kind == "directions":  # the four directions
        return (f'<circle cx="{cx}" cy="{cy}" r="{r * 0.95}" fill="none" stroke="{color}" stroke-width="{r * 0.12:.1f}"/>'
                f'<rect x="{cx - r * 0.12}" y="{cy - r * 0.8}" width="{r * 0.24}" height="{r * 1.6}" fill="{color}"/>'
                f'<rect x="{cx - r * 0.8}" y="{cy - r * 0.12}" width="{r * 1.6}" height="{r * 0.24}" fill="{color}"/>')
    if kind == "zigzag":      # lightning, the thunderbird's arrows
        return (f'<polyline points="{cx - r},{cy - r * 0.5} {cx - r * 0.5},{cy + r * 0.3} {cx},{cy - r * 0.3} {cx + r * 0.5},{cy + r * 0.5} {cx + r},{cy - r * 0.3}" '
                f'fill="none" stroke="{color}" stroke-width="{r * 0.22:.1f}" stroke-linejoin="round"/>')
    if kind == "chakana":     # the stepped cross
        s = r / 3
        pts = [(-1, -3), (1, -3), (1, -1), (3, -1), (3, 1), (1, 1), (1, 3), (-1, 3), (-1, 1), (-3, 1), (-3, -1), (-1, -1)]
        return (f'<polygon points="{" ".join(f"{cx + x * s:.1f},{cy + y * s:.1f}" for x, y in pts)}" fill="{color}"/>'
                f'<circle cx="{cx}" cy="{cy}" r="{s * 0.8:.1f}" fill="{dark}"/>')
    return f'<circle cx="{cx}" cy="{cy}" r="{r * 0.7}" fill="{color}"/>'


MOTIFS = {
    "european": ["labrys", "wheel", "star", "sun"],
    "asian": ["sun", "star", "lozenge", "wheel"],
    "african": ["shield", "lozenge", "sun", "star"],
    "north_american": ["directions", "zigzag", "sun", "star"],
    "south_american": ["chakana", "sun", "zigzag", "lozenge"],
}


def luminance(hex_color):
    r, g, b = (int(hex_color[i:i + 2], 16) / 255 for i in (1, 3, 5))
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def deepen(color):
    """Country map colours (RGB triples) are pale; flags want deeper dyes."""
    r, g, b = color if isinstance(color, list) else (int(color[i:i + 2], 16) for i in (1, 3, 5))
    return "#%02x%02x%02x" % (int(r * 0.72), int(g * 0.72), int(b * 0.72))


def generic_flag(tag, color, culture):
    h = int(hashlib.blake2b(tag.encode(), digest_size=8).hexdigest(), 16)
    main = deepen(color)
    accents = [c for c in (WHITE, GOLD, BLACK, RED, BLUE, GREEN) if abs(luminance(c) - luminance(main)) > 0.25] or [WHITE]
    second = accents[h % len(accents)]
    emblem_color = GOLD if abs(luminance(GOLD) - luminance(main)) > 0.2 else WHITE
    kind = MOTIFS.get(culture, MOTIFS["european"])[(h >> 8) % 4]
    layout = (h >> 16) % 6
    if layout == 0:     # field with a border
        field = f'<rect width="300" height="200" fill="{main}"/><rect x="10" y="10" width="280" height="180" fill="none" stroke="{second}" stroke-width="12"/>'
        emblem = motif(kind, 150, 100, 56, emblem_color, main)
    elif layout == 1:   # horizontal halves
        field = f'<rect width="300" height="100" fill="{main}"/><rect y="100" width="300" height="100" fill="{second}"/>'
        emblem = f'<circle cx="150" cy="100" r="62" fill="{main}" stroke="{second}" stroke-width="5"/>' + motif(kind, 150, 100, 44, emblem_color, main)
    elif layout == 2:   # vertical tricolour
        field = (f'<rect width="300" height="200" fill="{main}"/><rect x="100" width="100" height="200" fill="{second}"/>')
        emblem = motif(kind, 150, 100, 44, main, second)
    elif layout == 3:   # hoist band
        field = f'<rect width="300" height="200" fill="{main}"/><rect width="80" height="200" fill="{second}"/>'
        emblem = motif(kind, 190, 100, 56, emblem_color, main)
    elif layout == 4:   # diagonal
        field = f'<rect width="300" height="200" fill="{main}"/><polygon points="0,200 300,0 300,200" fill="{second}"/>'
        emblem = f'<circle cx="150" cy="100" r="58" fill="{main}" stroke="{second}" stroke-width="5"/>' + motif(kind, 150, 100, 42, emblem_color, main)
    else:               # swallowtail-cut war banner look: bands
        field = (f'<rect width="300" height="200" fill="{main}"/><rect y="24" width="300" height="14" fill="{second}"/>'
                 f'<rect y="162" width="300" height="14" fill="{second}"/>')
        emblem = motif(kind, 150, 100, 52, emblem_color, main)
    return svg(field + emblem)


def main_cultures():
    owner = {}
    for line in PROVINCES_CSV.read_text(encoding="utf-8").splitlines():
        if line and line[0].isdigit():
            cols = line.split(";")
            owner[int(cols[0])] = cols[4]
    units = {}
    for p in json.loads(SETUP_JSON.read_text(encoding="utf-8"))["provinces"]:
        tag = owner.get(p["id"])
        for pop in p["pops"]:
            units.setdefault(tag, {}).setdefault(pop["culture"], 0)
            units[tag][pop["culture"]] += pop["units"]
    return {tag: max(c, key=c.get) for tag, c in units.items() if c}


def main():
    countries = json.loads(COUNTRIES_JSON.read_text(encoding="utf-8"))
    cultures = main_cultures()
    drawn = 0
    for tag, designs in SPECIAL.items():
        for name, draw in designs.items():
            render(draw(), name)
            drawn += 1
    for c in countries:
        if c["tag"] in SPECIAL:
            continue
        render(generic_flag(c["tag"], c["color"], cultures.get(c["tag"], "european")), f"{c['tag']}.png")
        drawn += 1
    print(f"drew {drawn} flags")
    return 0


if __name__ == "__main__":
    sys.exit(main())
