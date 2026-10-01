"""Draws every NudgeNest icon: a mint tile that says AFK (BloxNest family style: tilted, with a grey frame).

Big sizes use blocky vector letters (same shapes as assets/logo.svg and the app's XAML logo).
Small sizes use hand-made pixel letters so "AFK" stays sharp: a flat badge at 16-20 px (tray), and a straight
version of the logo at 24 and 32 px (taskbar).

  assets/icon.ico               the exe icon (16-256 px)
  assets/logo-256.png           README / store-style image
  docs/favicon.ico, docs/assets/*.png   the website's favicons and app icons

Run: python tools/make_icon.py   (needs Pillow)
"""
import math
import shutil
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
GRAY, MINT, INK, NIGHT = (0x9A, 0xA9, 0x9F), (0x86, 0xE3, 0xCE), (0x10, 0x28, 0x21), (0x0B, 0x0F, 0x0B)

# ---- the big logo, on a 256 x 256 grid (same coordinates as assets/logo.svg) ----
FRAME = [(28, 38), (182, 38), (182, 66), (56, 66), (56, 204), (28, 204)]
TILE = [(76, 90), (230, 90), (230, 214), (76, 214)]
A_OUTER = [(85, 122), (125, 122), (125, 182), (112, 182), (112, 162), (98, 162), (98, 182), (85, 182)]
A_HOLE = [(98, 135), (112, 135), (112, 149), (98, 149)]
F = [(133, 122), (173, 122), (173, 135), (146, 135), (146, 147), (167, 147), (167, 160), (146, 160), (146, 182), (133, 182)]
K_STEM = [(181, 122), (194, 122), (194, 182), (181, 182)]
K_UP = [(194, 146), (209, 122), (224, 122), (202, 158)]
K_DOWN = [(199, 150), (224, 182), (209, 182), (194, 162)]
LETTERS = [A_OUTER, F, K_STEM, K_UP, K_DOWN]

# ---- pixel letters for small icons ----
FONT_4x5 = {
    "A": ["0110", "1001", "1111", "1001", "1001"],
    "F": ["1111", "1000", "1110", "1000", "1000"],
    "K": ["1001", "1010", "1100", "1010", "1001"],
}
FONT_5x7 = {
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "F": ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
    "K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
}
FONT_BOLD_6x8 = {   # 2 px strokes, like the big logo's letters at 32 px
    "A": ["011110", "110011", "110011", "110011", "111111", "110011", "110011", "110011"],
    "F": ["111111", "110000", "110000", "111110", "110000", "110000", "110000", "110000"],
    "K": ["110011", "110110", "111100", "111000", "111000", "111100", "110110", "110011"],
}

# Taskbar-size logos: the same grey frame + mint tile + AFK, but straight and on whole pixels, because
# a tilted logo turns to mush at 24-32 px. Windows shows 24 px in the taskbar at 100% scaling (32 px at 125-150%).
#   size: (frame thickness, frame left/top, frame bar ends (x, y), tile box, font, letters' top-left)
SMALL_LOGOS = {
    24: (3, (0, 2), (17, 20), (5, 7, 23, 21), FONT_5x7, (6, 11)),
    32: (4, (0, 2), (23, 27), (7, 9, 31, 28), FONT_BOLD_6x8, (9, 15)),
}


def rotate(points, degrees, cx=128, cy=128):
    a = math.radians(degrees)
    return [(cx + (x - cx) * math.cos(a) - (y - cy) * math.sin(a),
             cy + (x - cx) * math.sin(a) + (y - cy) * math.cos(a)) for x, y in points]


def logo(size, background=None, pad=0.0):
    """The full logo (frame + tilted tile + AFK), 4x supersampled."""
    big = size * 4
    img = Image.new("RGBA", (big, big), background + (255,) if background else (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    scale = big * (1 - 2 * pad) / 256
    off = big * pad

    def poly(points, color):
        d.polygon([(off + x * scale, off + y * scale) for x, y in rotate(points, -12)], fill=color + (255,))

    poly(FRAME, GRAY)
    poly(TILE, MINT)
    for shape in LETTERS:
        poly(shape, INK)
    poly(A_HOLE, MINT)
    return img.resize((size, size), Image.LANCZOS)


def pixel_text(d, font, x0, y0, scale=1, ink=INK):
    glyph_w = len(font["A"][0])
    for i, ch in enumerate("AFK"):
        for row, bits in enumerate(font[ch]):
            for col, bit in enumerate(bits):
                if bit == "1":
                    x = x0 + (i * (glyph_w + 1) + col) * scale
                    y = y0 + row * scale
                    d.rectangle([x, y, x + scale - 1, y + scale - 1], fill=ink + (255,))


def small_logo(size):
    """The logo for the taskbar (24 and 32 px): every edge on a whole pixel, so nothing is blurry."""
    t, (fx, fy), (bar_end_x, bar_end_y), tile, font, (lx, ly) = SMALL_LOGOS[size]
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([fx, fy, bar_end_x, fy + t - 1], fill=GRAY + (255,))   # frame: top bar
    d.rectangle([fx, fy, fx + t - 1, bar_end_y], fill=GRAY + (255,))   # frame: left bar
    d.rectangle(tile, fill=MINT + (255,))
    pixel_text(d, font, lx, ly)
    return img


def badge(size, fill=MINT, ink=INK, top=None):
    """A flat rounded tile with pixel-art AFK: crisp at 16-32 px. Used for small icons and the tray."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    radius = max(2, size // 6)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=fill + (255,))
    font, scale = (FONT_5x7, 1) if size == 24 else (FONT_4x5, 2 if size >= 32 else 1)
    glyph_w, glyph_h = len(font["A"][0]), len(font["A"])
    width = (3 * glyph_w + 2) * scale
    height = glyph_h * scale
    x0 = (size - width) // 2
    y0 = (size - height) // 2 if top is None else top
    pixel_text(d, font, x0, y0, scale, ink)
    return img


def icon_frame(size):
    """16-20 px: the flat AFK badge (tray, small icons). 24-32 px: the straight logo (taskbar). Bigger: the tilted logo."""
    if size in SMALL_LOGOS:
        return small_logo(size)
    return badge(size) if size < 24 else logo(size)


def main():
    assets = ROOT / "assets"
    assets.mkdir(exist_ok=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [icon_frame(s) for s in sizes]
    frames[-1].save(assets / "icon.ico", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    logo(256).save(assets / "logo-256.png")
    small_logo(32).save(assets / "logo-32.png")
    logo(64).save(assets / "logo-64.png")

    docs = ROOT / "docs"
    (docs / "assets").mkdir(parents=True, exist_ok=True)
    fav = [badge(16), small_logo(32), logo(48)]
    fav[-1].save(docs / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)], append_images=fav[:-1])
    small_logo(32).save(docs / "assets" / "favicon-32.png")
    logo(180, background=NIGHT, pad=0.1).save(docs / "apple-touch-icon.png")
    logo(192, pad=0.04).save(docs / "assets" / "icon-192.png")
    logo(512, pad=0.04).save(docs / "assets" / "icon-512.png")
    for name in ("logo.svg", "favicon.svg"):
        shutil.copyfile(assets / name, docs / "assets" / name)
    print("icons written to", assets, "and", docs)


if __name__ == "__main__":
    main()
