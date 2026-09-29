"""Draws the top bar's resource icons into gfx/icons/ (64x64 PNG): gold, population, the three kinds of
research (military, admin, science) and the army. Run from the project root: python3 tools/draw_icons.py"""
import os
import cairosvg

OUT = "gfx/icons"
DARK = "#2a1c0e"

ICONS = {
    # a gold coin with a raised rim and a sun stamp
    "gold": f"""
        <circle cx="32" cy="32" r="26" fill="#c9962e" stroke="{DARK}" stroke-width="3"/>
        <circle cx="32" cy="32" r="19" fill="#f0c75a" stroke="#a87a22" stroke-width="2"/>
        <circle cx="32" cy="32" r="6" fill="#c9962e"/>
        <g stroke="#a87a22" stroke-width="3" stroke-linecap="round">
          <line x1="32" y1="17" x2="32" y2="22"/><line x1="32" y1="42" x2="32" y2="47"/>
          <line x1="17" y1="32" x2="22" y2="32"/><line x1="42" y1="32" x2="47" y2="32"/>
        </g>""",
    # a man and a woman
    "population": f"""
        <circle cx="23" cy="18" r="8" fill="#e2c9a0" stroke="{DARK}" stroke-width="2.5"/>
        <path d="M10 56 L12 36 Q13 28 23 28 Q33 28 34 36 L36 56 Z" fill="#8a6a3e" stroke="{DARK}" stroke-width="2.5"/>
        <circle cx="42" cy="20" r="7.5" fill="#e2c9a0" stroke="{DARK}" stroke-width="2.5"/>
        <path d="M29 56 L37 33 Q42 29 47 33 L55 56 Z" fill="#b8563a" stroke="{DARK}" stroke-width="2.5"/>""",
    # crossed swords
    "military": f"""
        <g stroke="{DARK}" stroke-width="2.5" stroke-linejoin="round">
          <path d="M12 8 L16 8 L46 40 L42 44 L10 14 Z" fill="#d8d8d0"/>
          <path d="M52 8 L48 8 L18 40 L22 44 L54 14 Z" fill="#d8d8d0"/>
          <path d="M38 42 L46 34 L50 38 L42 46 Z" fill="#c9962e"/>
          <path d="M26 42 L18 34 L14 38 L22 46 Z" fill="#c9962e"/>
          <path d="M44 44 L52 52 L55 57 L50 55 L42 47 Z" fill="#7a3a22"/>
          <path d="M20 44 L12 52 L9 57 L14 55 L22 47 Z" fill="#7a3a22"/>
        </g>""",
    # a crown: rule and government
    "admin": f"""
        <path d="M8 46 L10 18 L22 32 L32 12 L42 32 L54 18 L56 46 Z" fill="#e0b848" stroke="{DARK}" stroke-width="3" stroke-linejoin="round"/>
        <rect x="8" y="46" width="48" height="9" rx="2" fill="#c9962e" stroke="{DARK}" stroke-width="3"/>
        <circle cx="32" cy="38" r="4" fill="#3a6ab8" stroke="{DARK}" stroke-width="1.5"/>
        <circle cx="19" cy="40" r="3" fill="#b83a3a" stroke="{DARK}" stroke-width="1.5"/>
        <circle cx="45" cy="40" r="3" fill="#b83a3a" stroke="{DARK}" stroke-width="1.5"/>""",
    # an open scroll with writing: learning and invention
    "science": f"""
        <path d="M14 12 L48 12 Q54 12 54 18 L54 50 Q54 56 48 56 L20 56 Q14 56 14 50 Z" fill="#efe2bf" stroke="{DARK}" stroke-width="3" stroke-linejoin="round"/>
        <path d="M10 12 Q10 6 16 6 Q22 6 22 12 L22 18 L14 18 Q10 18 10 12 Z" fill="#d9c796" stroke="{DARK}" stroke-width="2.5"/>
        <g stroke="#3a7a8a" stroke-width="3" stroke-linecap="round">
          <line x1="26" y1="24" x2="46" y2="24"/><line x1="26" y1="32" x2="44" y2="32"/>
          <line x1="26" y1="40" x2="46" y2="40"/><line x1="26" y1="48" x2="38" y2="48"/>
        </g>""",
    # a shield with a spear: the army
    "army": f"""
        <path d="M32 6 L54 14 L52 34 Q48 50 32 58 Q16 50 12 34 L10 14 Z" fill="#9a3a2a" stroke="{DARK}" stroke-width="3" stroke-linejoin="round"/>
        <path d="M32 12 L48 18 L46 34 Q43 45 32 51 Z" fill="#c24e36"/>
        <circle cx="32" cy="30" r="7" fill="#e0b848" stroke="{DARK}" stroke-width="2"/>""",
}

os.makedirs(OUT, exist_ok=True)
for name, body in ICONS.items():
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">{body}</svg>'
    cairosvg.svg2png(bytestring=svg.encode(), write_to=f"{OUT}/{name}.png", output_width=64, output_height=64)
    print(f"{OUT}/{name}.png")
