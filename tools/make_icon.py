"""Draws every NudgeNest icon from the logo shapes in assets/logo.svg and assets/favicon.svg.

  assets/icon.ico               the exe icon (16-256 px)
  assets/logo-32/64/256.png     built into the exe (tray icon, About screen)
  docs/favicon.ico, docs/assets/*.png   the website's favicons and app icons

Run: python tools/make_icon.py   (needs Pillow)
"""
import math
import shutil
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
GRAY, MINT, INK, NIGHT = (0x9A, 0xA9, 0x9F), (0x86, 0xE3, 0xCE), (0x10, 0x28, 0x21), (0x0B, 0x0F, 0x0B)

# same coordinates as the SVGs (256 x 256 grid)
LOGO = [  # drawn inside rotate(-12 128 128)
    (GRAY, [(28, 38), (182, 38), (182, 66), (56, 66), (56, 204), (28, 204)]),
    (MINT, [(76, 90), (230, 90), (230, 214), (76, 214)]),
    (INK, [(153, 110), (194, 152), (170, 152), (170, 190), (136, 190), (136, 152), (112, 152)]),
]
FAVICON = [
    (MINT, [(24, 40), (232, 40), (232, 216), (24, 216)]),
    (INK, [(128, 66), (188, 128), (150, 128), (150, 192), (106, 192), (106, 128), (68, 128)]),
]


def rotate(points, degrees, cx=128, cy=128):
    a = math.radians(degrees)
    return [(cx + (x - cx) * math.cos(a) - (y - cy) * math.sin(a),
             cy + (x - cx) * math.sin(a) + (y - cy) * math.cos(a)) for x, y in points]


def draw(shapes, size, angle=0.0, background=None, pad=0.0):
    """Render the shapes at size x size (4x supersampled). pad = empty margin as a fraction of the size."""
    big = size * 4
    img = Image.new("RGBA", (big, big), background + (255,) if background else (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    scale = big * (1 - 2 * pad) / 256
    offset = big * pad
    for color, points in shapes:
        pts = rotate(points, angle) if angle else points
        d.polygon([(offset + x * scale, offset + y * scale) for x, y in pts], fill=color + (255,))
    return img.resize((size, size), Image.LANCZOS)


def logo(size, **kw):
    return draw(LOGO, size, angle=-12, **kw)


def favicon(size, **kw):
    return draw(FAVICON, size, **kw)


def icon_set(size):
    """Small sizes use the simpler favicon mark so they stay crisp."""
    return favicon(size) if size <= 32 else logo(size)


def main():
    assets = ROOT / "assets"
    assets.mkdir(exist_ok=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [icon_set(s) for s in sizes]
    frames[-1].save(assets / "icon.ico", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    favicon(32).save(assets / "logo-32.png")
    logo(64).save(assets / "logo-64.png")
    logo(256).save(assets / "logo-256.png")

    docs = ROOT / "docs"
    (docs / "assets").mkdir(parents=True, exist_ok=True)
    fav = [favicon(s) for s in (16, 32, 48)]
    fav[-1].save(docs / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)], append_images=fav[:-1])
    favicon(32).save(docs / "assets" / "favicon-32.png")
    logo(180, background=NIGHT, pad=0.12).save(docs / "apple-touch-icon.png")
    logo(192, pad=0.04).save(docs / "assets" / "icon-192.png")
    logo(512, pad=0.04).save(docs / "assets" / "icon-512.png")
    for name in ("logo.svg", "favicon.svg"):
        shutil.copyfile(assets / name, docs / "assets" / name)
    print("icons written to", assets, "and", docs)


if __name__ == "__main__":
    main()
