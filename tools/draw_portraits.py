#!/usr/bin/env python3
"""Draw ruler portraits as PNG files under gfx/portraits.

Every portrait is a painted bust in profile, facing right, the way rulers appear on ancient coins and
wall paintings. Named rulers get their own drawing (Seti II of Egypt); every culture also gets a set of
generic portraits, which rulers without a drawing of their own are given (see data/portraits.json).

Outputs (overwritten):
  gfx/portraits/EGY_seti_ii.png, gfx/portraits/EGY_amenemhat_iii.png
  gfx/portraits/generic/<culture>_<n>.png
  data/portraits.json               the generic portraits of each culture

Usage: python3 tools/draw_portraits.py        (needs: pip install cairosvg)
"""
import json
import math
import random
import sys

import cairosvg

from map_common import ROOT

OUT = ROOT / "gfx" / "portraits"
PORTRAITS_JSON = ROOT / "data" / "portraits.json"
W, H = 320, 400
GENERIC_PER_CULTURE = 6

# ---------------------------------------------------------------------------------------------- the bust

# head and neck in profile, facing right: skull, forehead, brow, nose, lips, chin, jaw, throat
HEAD = ("M150 96 C190 86 212 110 213 138 C213 148 216 154 215 160 "
        "C{nose_x0} 171 {nose_x1} 184 {nose_tip} 193 C{nose_tip1} 198 227 200 220 200 "
        "C221 204 223 207 220 210 C217 212 215 213 214 214 C217 216 219 219 216 223 "
        "C{chin_x} 229 {chin_x} 236 208 242 C200 250 188 252 178 252 "
        "L181 284 C184 296 192 306 204 318 L106 330 "
        "C112 300 114 282 120 258 C102 242 96 206 101 164 C106 124 124 100 150 96 Z")

BODY = "M34 400 C44 336 96 304 170 302 C246 302 292 336 304 400 Z"

EAR = "M146 176 C138 172 132 180 134 192 C136 204 142 210 150 206 C156 202 154 180 146 176 Z"

# the eye as Egyptian painters drew it: frontal almond, heavy outline, kohl line swept back
EYE = """
<path d="M188 166 C194 160 204 160 210 166 C204 171 194 172 188 166 Z" fill="#f4efe4"/>
<circle cx="201" cy="166" r="3.3" fill="#1b1410"/>
<path d="M186 166 C194 158 205 158 211 166 C205 172 194 173 186 166 Z" fill="none" stroke="#120d0a" stroke-width="2.4"/>
<path d="M188 166 C182 167 176 169 170 172" fill="none" stroke="#120d0a" stroke-width="2.6" stroke-linecap="round"/>
<path d="M186 155 C194 150 206 150 213 155" fill="none" stroke="#120d0a" stroke-width="3" stroke-linecap="round"/>
"""

NATURAL_EYE = """
<path d="M192 166 C197 162 205 162 209 166 C205 169 197 170 192 166 Z" fill="#f1ece2"/>
<circle cx="203" cy="166" r="2.8" fill="{iris}"/>
<path d="M191 166 C197 161 205 161 210 166" fill="none" stroke="#1b1410" stroke-width="1.8"/>
<path d="M188 156 C195 151 205 151 212 156" fill="none" stroke="{brow}" stroke-width="3.2" stroke-linecap="round"/>
"""

MOUTH = '<path d="M213 214 C209 214 206 215 203 216" fill="none" stroke="#4a2418" stroke-width="1.6" stroke-linecap="round"/>'


def head(skin, shade, nose=0, chin=0):
    nose_tip = 232 + nose
    d = HEAD.format(nose_x0=221 + nose * 0.3, nose_x1=229 + nose * 0.8, nose_tip=nose_tip,
                    nose_tip1=nose_tip + 1, chin_x=213 + chin)
    return f"""
<path d="{d}" fill="url(#skin)" stroke="{shade}" stroke-width="1.5"/>
<path d="{EAR}" fill="{skin}" stroke="{shade}" stroke-width="1.5"/>
<path d="M142 186 C140 190 142 196 146 198" fill="none" stroke="{shade}" stroke-width="1.5"/>
"""


def skin_gradient(skin, shade):
    return f"""
<linearGradient id="skin" x1="0" y1="0" x2="1" y2="0.3">
  <stop offset="0" stop-color="{shade}"/><stop offset="0.55" stop-color="{skin}"/><stop offset="1" stop-color="{lighten(skin, 0.12)}"/>
</linearGradient>"""


def frame(inner, background, rim="#c9a24a", rim_dark="#6b4f1d", defs=""):
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">
<defs>{defs}
  <clipPath id="card"><rect x="8" y="8" width="{W - 16}" height="{H - 16}" rx="14"/></clipPath>
</defs>
<rect x="0" y="0" width="{W}" height="{H}" rx="18" fill="{rim_dark}"/>
<rect x="3" y="3" width="{W - 6}" height="{H - 6}" rx="16" fill="{rim}"/>
<g clip-path="url(#card)">
{background}
{inner}
</g>
<rect x="8" y="8" width="{W - 16}" height="{H - 16}" rx="14" fill="none" stroke="{rim_dark}" stroke-width="2"/>
</svg>"""


# -------------------------------------------------------------------------------------------- Seti II

def seti_ii():
    """Seti II (Userkheperure Setepenre), pharaoh of Egypt c. 1203-1197 BC, in the blue khepresh war crown."""
    skin, shade = "#a55a32", "#6e3519"
    defs = skin_gradient(skin, shade) + """
<linearGradient id="wall" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#e8cf98"/><stop offset="1" stop-color="#c9a468"/>
</linearGradient>
<linearGradient id="crown" x1="0" y1="0" x2="1" y2="1">
  <stop offset="0" stop-color="#3f63b8"/><stop offset="1" stop-color="#1b2f6e"/>
</linearGradient>
<clipPath id="crownclip">
  <path d="M212 150 C222 108 210 66 182 48 C150 30 108 44 98 88 C90 122 96 156 112 178 L150 170 C168 154 190 146 212 150 Z"/>
</clipPath>
<clipPath id="bodyclip"><path d="M34 400 C44 336 96 304 170 302 C246 302 292 336 304 400 Z"/></clipPath>"""

    background = """
<rect x="0" y="0" width="320" height="400" fill="url(#wall)"/>
<rect x="0" y="0" width="320" height="30" fill="#1f3b7a"/>
<g fill="#e3b94a">""" + "".join(f'<path d="M{x} 30 L{x + 10} 14 L{x + 20} 30 Z"/>' for x in range(4, 320, 22)) + """</g>
<rect x="0" y="30" width="320" height="5" fill="#b23a24"/>
<!-- cartouche: Userkheperure, the throne name -->
<g transform="translate(18 60)">
  <rect x="0" y="0" width="46" height="150" rx="23" fill="#f2e2b8" stroke="#1b1410" stroke-width="3"/>
  <line x1="-4" y1="156" x2="50" y2="156" stroke="#1b1410" stroke-width="4"/>
  <circle cx="23" cy="24" r="10" fill="#c8402a" stroke="#1b1410" stroke-width="1.5"/>
  <path d="M11 58 C11 44 35 44 35 58 L35 64 L11 64 Z" fill="#2e6d9e" stroke="#1b1410" stroke-width="1.5"/>
  <ellipse cx="23" cy="80" rx="6" ry="8" fill="none" stroke="#1b1410" stroke-width="3"/><path d="M23 88 L23 108 M13 90 L33 90" stroke="#1b1410" stroke-width="3"/>
  <ellipse cx="23" cy="122" rx="11" ry="7" fill="#2f7a4a" stroke="#1b1410" stroke-width="1.5"/>
  <path d="M14 136 L32 136" stroke="#1b1410" stroke-width="3"/>
</g>
<g fill="#1b1410" opacity="0.55">
  <path d="M276 70 l10 0 l0 26 l-10 0 z"/><circle cx="281" cy="112" r="6"/><path d="M272 130 q9 -12 18 0 z"/>
  <path d="M276 150 l10 0 l0 20 l-10 0 z"/><path d="M270 186 l22 0 l-4 8 l-14 0 z"/>
</g>"""

    body = f"""
<g clip-path="url(#bodyclip)">
  <path d="M34 400 C44 336 96 304 170 302 C246 302 292 336 304 400 Z" fill="url(#skin)"/>
  <!-- wesekh: the broad collar, bands of faience beads -->
  <g fill="none" stroke-width="11">
    <ellipse cx="168" cy="300" rx="84" ry="44" stroke="#1f5fa8"/>
    <ellipse cx="168" cy="300" rx="97" ry="55" stroke="#d9a93a"/>
    <ellipse cx="168" cy="300" rx="110" ry="66" stroke="#b23a24"/>
    <ellipse cx="168" cy="300" rx="123" ry="77" stroke="#2f8a5a"/>
    <ellipse cx="168" cy="300" rx="136" ry="88" stroke="#d9a93a"/>
  </g>
  <g fill="#d9a93a" stroke="#6b4f1d" stroke-width="1">""" + "".join(
        f'<path d="M{168 + 146 * c:.1f} {300 + 96 * s:.1f} l{7 * c:.1f} {12 * s + 4:.1f} l{-7 * s:.1f} {3:.1f} z"/>'
        for c, s in [(math.cos(a / 10), math.sin(a / 10)) for a in range(2, 30)]) + """</g>
  <g fill="none" stroke="#1b1410" stroke-width="1.2" opacity="0.5">
    <ellipse cx="168" cy="300" rx="78" ry="38"/><ellipse cx="168" cy="300" rx="142" ry="94"/>
  </g>
</g>"""

    face = head(skin, shade, nose=0, chin=0) + EYE + MOUTH + f"""
<!-- ceremonial false beard, braided, held by a strap along the jaw -->
<path d="M204 238 L214 238 L219 288 C216 292 210 292 207 288 Z" fill="#2a1d14" stroke="#120d0a" stroke-width="1.5"/>
<g stroke="#5a4330" stroke-width="1.4">""" + "".join(f'<line x1="{205 + i * 0.3}" y1="{246 + i * 7}" x2="{217 - i * 0.1}" y2="{244 + i * 7}"/>' for i in range(6)) + f"""</g>
<path d="M204 238 C190 234 170 222 152 206" fill="none" stroke="#2a1d14" stroke-width="3"/>"""

    crown = """
<!-- khepresh: the blue war crown, studded with gold discs, with a golden uraeus -->
<path d="M212 150 C222 108 210 66 182 48 C150 30 108 44 98 88 C90 122 96 156 112 178 L150 170 C168 154 190 146 212 150 Z"
      fill="url(#crown)" stroke="#0f1a3d" stroke-width="3"/>
<g clip-path="url(#crownclip)" fill="#e7c04d" stroke="#8a6a1c" stroke-width="0.8">""" + "".join(
        f'<circle cx="{x + (7 if (y // 14) % 2 else 0)}" cy="{y}" r="3.2"/>'
        for y in range(44, 180, 14) for x in range(90, 230, 14)) + """</g>
<path d="M112 178 C130 170 150 164 170 157 C186 152 200 150 212 150" fill="none" stroke="#e7c04d" stroke-width="5"/>
<path d="M112 178 C130 170 150 164 170 157 C186 152 200 150 212 150" fill="none" stroke="#8a6a1c" stroke-width="1"/>
<path d="M100 150 C92 170 88 196 96 214 C100 200 104 184 112 172 Z" fill="#1b2f6e" stroke="#0f1a3d" stroke-width="2"/>
<!-- uraeus: the rearing cobra of kingship -->
<path d="M210 146 C216 136 226 132 228 122 C229 114 222 112 218 118 C216 122 220 126 222 124"
      fill="none" stroke="#e7c04d" stroke-width="5" stroke-linecap="round"/>
<path d="M210 146 C216 136 226 132 228 122 C229 114 222 112 218 118" fill="none" stroke="#8a6a1c" stroke-width="1.2"/>
<ellipse cx="227" cy="119" rx="4" ry="6" fill="#e7c04d" stroke="#8a6a1c" stroke-width="1"/>
<!-- the crown's streamers -->
<path d="M110 176 C96 210 90 240 96 270 L104 268 C100 240 106 210 118 180 Z" fill="#b23a24" stroke="#5e1a0f" stroke-width="1.5"/>
"""
    skin_body = f'<path d="{BODY}" fill="url(#skin)" stroke="{shade}" stroke-width="1.5"/>'
    return frame(skin_body + face + body + crown, background, defs=defs)


def amenemhat_iii():
    """Amenemhat III (Nimaatre), pharaoh of Egypt c. 1831-1786 BC, in the striped nemes headcloth of his statues."""
    skin, shade = "#9a5230", "#64301a"
    defs = skin_gradient(skin, shade) + """
<linearGradient id="wall" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#e8cf98"/><stop offset="1" stop-color="#c9a468"/>
</linearGradient>
<linearGradient id="crown" x1="0" y1="0" x2="1" y2="1">
  <stop offset="0" stop-color="#3f63b8"/><stop offset="1" stop-color="#1b2f6e"/>
</linearGradient>
<clipPath id="crownclip">
  <path d="M212 150 C222 108 210 66 182 48 C150 30 108 44 98 88 C90 122 96 156 112 178 L150 170 C168 154 190 146 212 150 Z"/>
</clipPath>
<clipPath id="bodyclip"><path d="M34 400 C44 336 96 304 170 302 C246 302 292 336 304 400 Z"/></clipPath>"""

    background = """
<rect x="0" y="0" width="320" height="400" fill="url(#wall)"/>
<rect x="0" y="0" width="320" height="30" fill="#1f3b7a"/>
<g fill="#e3b94a">""" + "".join(f'<path d="M{x} 30 L{x + 10} 14 L{x + 20} 30 Z"/>' for x in range(4, 320, 22)) + """</g>
<rect x="0" y="30" width="320" height="5" fill="#b23a24"/>
<!-- cartouche: Nimaatre, the throne name -->
<g transform="translate(18 60)">
  <rect x="0" y="0" width="46" height="150" rx="23" fill="#f2e2b8" stroke="#1b1410" stroke-width="3"/>
  <line x1="-4" y1="156" x2="50" y2="156" stroke="#1b1410" stroke-width="4"/>
  <circle cx="23" cy="24" r="10" fill="#c8402a" stroke="#1b1410" stroke-width="1.5"/>
  <path d="M11 58 C11 44 35 44 35 58 L35 64 L11 64 Z" fill="#2e6d9e" stroke="#1b1410" stroke-width="1.5"/>
  <ellipse cx="23" cy="80" rx="6" ry="8" fill="none" stroke="#1b1410" stroke-width="3"/><path d="M23 88 L23 108 M13 90 L33 90" stroke="#1b1410" stroke-width="3"/>
  <ellipse cx="23" cy="122" rx="11" ry="7" fill="#2f7a4a" stroke="#1b1410" stroke-width="1.5"/>
  <path d="M14 136 L32 136" stroke="#1b1410" stroke-width="3"/>
</g>
<g fill="#1b1410" opacity="0.55">
  <path d="M276 70 l10 0 l0 26 l-10 0 z"/><circle cx="281" cy="112" r="6"/><path d="M272 130 q9 -12 18 0 z"/>
  <path d="M276 150 l10 0 l0 20 l-10 0 z"/><path d="M270 186 l22 0 l-4 8 l-14 0 z"/>
</g>"""

    body = f"""
<g clip-path="url(#bodyclip)">
  <path d="M34 400 C44 336 96 304 170 302 C246 302 292 336 304 400 Z" fill="url(#skin)"/>
  <!-- wesekh: the broad collar, bands of faience beads -->
  <g fill="none" stroke-width="11">
    <ellipse cx="168" cy="300" rx="84" ry="44" stroke="#1f5fa8"/>
    <ellipse cx="168" cy="300" rx="97" ry="55" stroke="#d9a93a"/>
    <ellipse cx="168" cy="300" rx="110" ry="66" stroke="#b23a24"/>
    <ellipse cx="168" cy="300" rx="123" ry="77" stroke="#2f8a5a"/>
    <ellipse cx="168" cy="300" rx="136" ry="88" stroke="#d9a93a"/>
  </g>
  <g fill="#d9a93a" stroke="#6b4f1d" stroke-width="1">""" + "".join(
        f'<path d="M{168 + 146 * c:.1f} {300 + 96 * s:.1f} l{7 * c:.1f} {12 * s + 4:.1f} l{-7 * s:.1f} {3:.1f} z"/>'
        for c, s in [(math.cos(a / 10), math.sin(a / 10)) for a in range(2, 30)]) + """</g>
  <g fill="none" stroke="#1b1410" stroke-width="1.2" opacity="0.5">
    <ellipse cx="168" cy="300" rx="78" ry="38"/><ellipse cx="168" cy="300" rx="142" ry="94"/>
  </g>
</g>"""

    face = head(skin, shade, nose=0, chin=0) + EYE + MOUTH + f"""
<!-- ceremonial false beard, braided, held by a strap along the jaw -->
<path d="M204 238 L214 238 L219 288 C216 292 210 292 207 288 Z" fill="#2a1d14" stroke="#120d0a" stroke-width="1.5"/>
<g stroke="#5a4330" stroke-width="1.4">""" + "".join(f'<line x1="{205 + i * 0.3}" y1="{246 + i * 7}" x2="{217 - i * 0.1}" y2="{244 + i * 7}"/>' for i in range(6)) + f"""</g>
<path d="M204 238 C190 234 170 222 152 206" fill="none" stroke="#2a1d14" stroke-width="3"/>"""

    crown = """
<!-- nemes: the striped royal headcloth, falling in lappets to the shoulders, with a golden uraeus -->
<defs>
  <clipPath id="nemesclip">
    <path d="M212 150 C222 108 210 66 182 48 C150 30 108 44 98 88 C90 122 92 150 92 170 L84 296 L138 300 L146 196 C166 174 190 160 212 150 Z"/>
  </clipPath>
</defs>
<path d="M212 150 C222 108 210 66 182 48 C150 30 108 44 98 88 C90 122 92 150 92 170 L84 296 L138 300 L146 196 C166 174 190 160 212 150 Z"
      fill="#e7c04d" stroke="#6b4f1d" stroke-width="3"/>
<g clip-path="url(#nemesclip)" fill="#2b4a9a">""" + "".join(
        f'<path d="M60 {y} L240 {y - 28} L240 {y - 16} L60 {y + 12} Z"/>' for y in range(40, 330, 24)) + """</g>
<path d="M212 150 C222 108 210 66 182 48 C150 30 108 44 98 88 C90 122 92 150 92 170 L84 296 L138 300 L146 196 C166 174 190 160 212 150 Z"
      fill="none" stroke="#6b4f1d" stroke-width="3"/>
<!-- the headband across the brow -->
<path d="M100 160 C130 150 160 148 212 150" fill="none" stroke="#e7c04d" stroke-width="7"/>
<path d="M100 160 C130 150 160 148 212 150" fill="none" stroke="#8a6a1c" stroke-width="1.2"/>
<!-- uraeus: the rearing cobra of kingship -->
<path d="M210 146 C216 136 226 132 228 122 C229 114 222 112 218 118 C216 122 220 126 222 124"
      fill="none" stroke="#e7c04d" stroke-width="5" stroke-linecap="round"/>
<path d="M210 146 C216 136 226 132 228 122 C229 114 222 112 218 118" fill="none" stroke="#8a6a1c" stroke-width="1.2"/>
<ellipse cx="227" cy="119" rx="4" ry="6" fill="#e7c04d" stroke="#8a6a1c" stroke-width="1"/>
"""
    skin_body = f'<path d="{BODY}" fill="url(#skin)" stroke="{shade}" stroke-width="1.5"/>'
    return frame(skin_body + face + body + crown, background, defs=defs)


# --------------------------------------------------------------------------------------- generic busts

CULTURE_LOOKS = {
    # skin tones, hair colours, eye colours, clothing colours, background colours
    "european": dict(skin=["#e2b48f", "#d9a47c", "#caa07a", "#e8c1a0"], hair=["#3b2618", "#6b4424", "#a8743a", "#2a1c14", "#8a5a2a"],
                     eyes=["#3b2a1c", "#4a6a8a", "#5a6b3a"], cloth=["#7a2a24", "#2f4f7a", "#5d6b2f", "#8a6a2a", "#4a3a6a"],
                     bg=["#5c6e7c", "#6b5a44", "#4f5f4a"]),
    "asian": dict(skin=["#d8a878", "#c9956a", "#e0b98f", "#b88458"], hair=["#15110e", "#241a14", "#2e241c"],
                  eyes=["#2a1c14", "#3b2a1c"], cloth=["#8e2b22", "#26466e", "#c49a3a", "#2f5f4a", "#5e2a5a"],
                  bg=["#7a3a2a", "#3a4a5a", "#6a5a3a"]),
    "african": dict(skin=["#7a4a2c", "#5e3820", "#8e5a36", "#4a2c1a", "#6b4026"], hair=["#120d0a", "#1e1510"],
                    eyes=["#1e1510", "#2a1c14"], cloth=["#c8702a", "#2f6b3a", "#9e2a24", "#d9a93a", "#26466e"],
                    bg=["#8a5a2a", "#6b3a24", "#4a5a3a"]),
    "north_american": dict(skin=["#b77a4e", "#a86c44", "#c48a5a", "#9a603a"], hair=["#15110e", "#241a14"],
                           eyes=["#241a14", "#2e2218"], cloth=["#8a6a44", "#6b4a2c", "#a8843a", "#7a3a24"],
                           bg=["#4a5a44", "#6a5a44", "#3e4e5e"]),
    "south_american": dict(skin=["#b57446", "#a4683e", "#c08050", "#96603a"], hair=["#15110e", "#1e1510"],
                           eyes=["#241a14", "#2e2218"], cloth=["#b23a24", "#2f7a6a", "#d9a93a", "#5e2a5a", "#2a5a8e"],
                           bg=["#3a6a5a", "#7a4a2a", "#5a3a5a"]),
}


def lighten(hex_color, amount):
    r, g, b = (int(hex_color[i:i + 2], 16) for i in (1, 3, 5))
    f = lambda c: int(c + (255 - c) * amount) if amount >= 0 else int(c * (1 + amount))
    return "#%02x%02x%02x" % (f(r), f(g), f(b))


def generic(culture, index):
    rng = random.Random(f"{culture}:{index}")
    look = CULTURE_LOOKS[culture]
    skin = rng.choice(look["skin"])
    shade = lighten(skin, -0.35)
    hair = rng.choice(look["hair"])
    cloth = rng.choice(look["cloth"])
    trim = rng.choice(["#d9a93a", "#e8e0cc", "#1b1410"])
    bg = rng.choice(look["bg"])
    nose = rng.randint(-3, 4)
    chin = rng.randint(-2, 3)
    bearded = rng.random() < {"european": 0.75, "asian": 0.6, "african": 0.35, "north_american": 0.1, "south_american": 0.15}[culture]
    defs = skin_gradient(skin, shade) + f"""
<radialGradient id="bg" cx="0.6" cy="0.4" r="0.8">
  <stop offset="0" stop-color="{lighten(bg, 0.25)}"/><stop offset="1" stop-color="{lighten(bg, -0.35)}"/>
</radialGradient>"""
    background = '<rect x="0" y="0" width="320" height="400" fill="url(#bg)"/>'

    parts = []
    # hair behind the head
    long_hair = culture in ("north_american", "south_american") or rng.random() < 0.5
    if long_hair:
        parts.append(f'<path d="M150 96 C110 100 92 150 96 210 C98 250 104 290 118 320 L150 318 C136 280 132 240 136 200 Z" fill="{hair}"/>')
    clothing = []
    # clothing
    clothing.append(f'<path d="{BODY}" fill="{cloth}" stroke="{lighten(cloth, -0.4)}" stroke-width="2"/>')
    clothing.append(f'<path d="M110 312 C140 330 200 330 232 312" fill="none" stroke="{trim}" stroke-width="7"/>')
    if culture == "european":
        clothing.append(f'<circle cx="236" cy="330" r="9" fill="#c9a24a" stroke="#6b4f1d" stroke-width="2"/>')          # fibula
    elif culture == "african":
        for i in range(9):
            clothing.append(f'<circle cx="{150 + i * 9}" cy="{300 + abs(i - 4) * -2 + 16}" r="5" fill="{["#d9a93a", "#b23a24", "#2f8a5a"][i % 3]}" stroke="#1b1410"/>')
    elif culture == "north_american":
        for i in range(7):
            clothing.append(f'<rect x="{150 + i * 11}" y="{300 + 10}" width="5" height="18" rx="2" fill="#efe6d2" stroke="#6b5a44"/>')      # bone choker
    elif culture == "south_american":
        clothing.append(f'<path d="M60 400 C80 350 120 318 170 314 C220 318 262 350 282 400 Z" fill="{lighten(cloth, -0.2)}"/>')
        for i in range(10):
            clothing.append(f'<rect x="{92 + i * 16}" y="{352 - abs(i - 5) * 6}" width="10" height="10" fill="{trim}"/>')
    elif culture == "asian":
        clothing.append(f'<path d="M150 304 L200 360 L214 400" fill="none" stroke="{trim}" stroke-width="6"/>')           # robe overlap

    parts.append(head(skin, shade, nose, chin))
    parts.extend(clothing)
    parts.append(NATURAL_EYE.format(iris=rng.choice(look["eyes"]), brow=hair))
    parts.append(MOUTH)

    # hair on top, and headwear by culture
    style = rng.randrange(3)
    top = (f'<path d="M209 126 C212 102 196 86 160 86 C120 86 97 116 98 160 C98 192 104 220 118 246 '
           f'C124 226 128 204 134 186 C138 172 148 162 160 152 C176 140 192 130 209 126 Z" fill="{hair}"/>')
    parts.append(top)
    if culture == "european":
        if style == 0:
            parts.append('<path d="M104 146 C140 132 184 128 214 138 L214 128 C184 118 140 122 104 136 Z" fill="#c9a24a" stroke="#6b4f1d" stroke-width="1.5"/>')     # bronze diadem
        elif style == 1:  # crested bronze helmet
            parts.append('<path d="M214 146 C220 100 196 72 160 70 C120 70 98 104 100 150 L118 150 C140 136 180 134 214 146 Z" fill="#b8893a" stroke="#5e4318" stroke-width="2.5"/>')
            parts.append(f'<path d="M110 74 C140 40 190 44 210 80 C190 62 140 60 110 74 Z" fill="{rng.choice(["#9e2a24", "#1b1410", "#e8e0cc"])}"/>')
            parts.append('<path d="M178 146 C182 164 184 180 182 198 L168 200 C168 182 166 164 164 150 Z" fill="#b8893a" stroke="#5e4318" stroke-width="2"/>')      # cheek guard
    elif culture == "asian":
        if style == 0:   # topknot with a pin
            parts.append(f'<ellipse cx="150" cy="80" rx="18" ry="14" fill="{hair}"/><line x1="124" y1="84" x2="178" y2="72" stroke="#d9a93a" stroke-width="4"/>')
        elif style == 1:  # tall cap
            parts.append(f'<path d="M110 132 C112 90 128 60 160 56 C194 60 206 96 206 132 C176 124 140 124 110 132 Z" fill="{lighten(cloth, -0.3)}" stroke="#1b1410" stroke-width="2"/>')
            parts.append('<path d="M108 134 C140 124 180 124 208 134" fill="none" stroke="#d9a93a" stroke-width="4"/>')
        else:            # round Near Eastern hat with a band
            parts.append(f'<path d="M104 136 C104 100 130 80 160 80 C194 80 214 104 214 136 C180 126 140 126 104 136 Z" fill="{rng.choice(["#e8e0cc", "#9e2a24", "#26466e"])}" stroke="#1b1410" stroke-width="2"/>')
    elif culture == "african":
        if style == 0:   # head wrap
            wrap = rng.choice(["#c8702a", "#26466e", "#d9a93a", "#9e2a24"])
            parts.append(f'<path d="M100 150 C98 100 130 72 164 74 C200 78 218 104 214 142 C180 126 140 128 100 150 Z" fill="{wrap}" stroke="{lighten(wrap, -0.45)}" stroke-width="2"/>')
            parts.append(f'<path d="M110 120 C140 100 180 96 210 110 M104 136 C140 116 184 112 214 126" fill="none" stroke="{lighten(wrap, -0.3)}" stroke-width="3"/>')
        elif style == 1:  # gold circlet with beads
            parts.append('<path d="M104 142 C140 130 184 126 214 136" fill="none" stroke="#d9a93a" stroke-width="6"/>')
            parts.append('<circle cx="212" cy="136" r="5" fill="#b23a24" stroke="#5e1a0f"/>')
        parts.append('<circle cx="146" cy="206" r="6" fill="none" stroke="#d9a93a" stroke-width="3"/>')     # earring
    elif culture == "north_american":
        parts.append(f'<path d="M104 142 C140 130 184 126 214 136" fill="none" stroke="{rng.choice(["#9e2a24", "#26466e", "#2f6b3a"])}" stroke-width="7"/>')
        for i in range(1 + style):
            a = -30 - i * 18
            parts.append(f'<path d="M{128 - i * 8} 132 C{110 - i * 8} {100 + i * 4} {100 - i * 10} {70 + i * 6} {96 - i * 12} {48 + i * 10} C{114 - i * 8} {70 + i * 4} {126 - i * 6} {100} {134 - i * 6} 132 Z" '
                         f'fill="#efe6d2" stroke="#3a2a1c" stroke-width="1.5"/>')
            parts.append(f'<path d="M{99 - i * 12} {54 + i * 10} L{106 - i * 10} {66 + i * 8}" stroke="#1b1410" stroke-width="5"/>')
        parts.append('<path d="M118 150 C112 200 112 250 118 300" fill="none" stroke="#6b4a2c" stroke-width="5"/>')     # braid
    elif culture == "south_american":
        colors = ["#2f8a5a", "#d9a93a", "#b23a24", "#2a5a8e"]
        for i in range(7):
            c = colors[(i + style) % 4]
            parts.append(f'<path d="M{110 + i * 14} 132 C{100 + i * 16} {90 - abs(i - 3) * 6} {104 + i * 16} {60 - abs(i - 3) * 8} {112 + i * 14} {44 - abs(i - 3) * 8} '
                         f'C{124 + i * 14} {64 - abs(i - 3) * 8} {124 + i * 14} {96} {124 + i * 14} 132 Z" fill="{c}" stroke="#1b1410" stroke-width="1.2"/>')
        parts.append('<path d="M104 142 C140 128 184 124 214 134 L214 124 C184 114 140 118 104 132 Z" fill="#d9a93a" stroke="#6b4f1d" stroke-width="1.5"/>')
        parts.append('<circle cx="146" cy="192" r="12" fill="#d9a93a" stroke="#6b4f1d" stroke-width="2"/><circle cx="146" cy="192" r="5" fill="#2f8a5a"/>')   # ear spool

    if bearded:
        full = culture in ("european", "asian") and rng.random() < 0.7
        if full:
            parts.append(f'<path d="M150 204 C160 222 176 230 196 226 C204 224 210 222 214 224 C218 240 214 258 204 270 C188 284 164 280 150 262 C142 246 142 226 150 204 Z" fill="{hair}"/>')
            if culture == "asian":   # the long curled beards of Mesopotamian kings
                for i in range(4):
                    parts.append(f'<path d="M{160 + i * 11} 240 l0 34" stroke="{lighten(hair, 0.25)}" stroke-width="2"/>')
        else:
            parts.append(f'<path d="M196 224 C204 224 212 226 216 224 C218 236 212 250 202 254 C190 256 182 246 184 234 Z" fill="{hair}"/>')
        parts.append(f'<path d="M204 212 C210 206 216 206 222 210 C216 212 210 214 204 214 Z" fill="{hair}"/>')          # moustache

    return frame("\n".join(parts), background, defs=defs)


def render(svg, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    cairosvg.svg2png(bytestring=svg.encode(), write_to=str(path), output_width=W, output_height=H)


def main():
    render(seti_ii(), OUT / "EGY_seti_ii.png")
    render(amenemhat_iii(), OUT / "EGY_amenemhat_iii.png")
    table = {}
    for culture in CULTURE_LOOKS:
        paths = []
        for i in range(1, GENERIC_PER_CULTURE + 1):
            render(generic(culture, i), OUT / "generic" / f"{culture}_{i}.png")
            paths.append(f"res://gfx/portraits/generic/{culture}_{i}.png")
        table[culture] = paths
    with PORTRAITS_JSON.open("w", encoding="utf-8", newline="\n") as f:
        f.write('{\n\t"_comment": "Generic ruler portraits by culture, for rulers without a portrait of their own. Generated by tools/draw_portraits.py.",\n')
        f.write('\t"generic": {\n')
        f.write(",\n".join(f'\t\t"{c}": ' + json.dumps(p) for c, p in table.items()))
        f.write("\n\t}\n}\n")
    print(f"drew Seti II and {sum(len(p) for p in table.values())} generic portraits")
    return 0


if __name__ == "__main__":
    sys.exit(main())
