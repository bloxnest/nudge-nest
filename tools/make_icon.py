"""Draws every NudgeNest icon: a mint tile that says AFK (BloxNest family style: tilted, with a grey frame).

"AFK" is set in Bahnschrift Bold (comes with Windows 10/11), smooth at every size:
  - big sizes and the vector logos use the font's own outlines (assets/logo.svg, the app's XAML logo)
  - 16-32 px are drawn by FreeType at that exact size (Condensed, so the letters can be taller), hinted and
    sharp but not pixel art
  - 24 and 32 px (the taskbar) are a straight version of the logo, so its edges sit on whole pixels

  assets/icon.ico               the exe and taskbar icon (16-256 px)
  assets/tray-*.ico             the hidden-icons icon in each state colour (16-32 px)
  assets/logo-*.png             README / store-style images
  docs/favicon.ico, docs/assets/*   the website's favicons and app icons
  src/MainWindow.xaml           the letters' path in the app's two logos

Run: python tools/make_icon.py   (needs Pillow and fontTools)
"""
import math
import re
import struct
from io import BytesIO
from pathlib import Path

from fontTools.pens.basePen import BasePen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONT_FILE = "C:/Windows/Fonts/bahnschrift.ttf"
FONT_STYLE = "Bold SemiCondensed"
SMALL_STYLE = "Bold Condensed"   # narrower, so the letters can be taller at 16-32 px
GRAY, MINT, INK, NIGHT = (0x9A, 0xA9, 0x9F), (0x86, 0xE3, 0xCE), (0x10, 0x28, 0x21), (0x0B, 0x0F, 0x0B)
TRAY = {"on": MINT, "standby": (0xE6, 0xC4, 0x7A), "off": (0x8C, 0x95, 0x8A), "stopped": (0xF0, 0x9A, 0x9A)}

# ---- the big logo, on a 256 x 256 grid (same coordinates as assets/logo.svg and the app's XAML) ----
FRAME = [(28, 38), (182, 38), (182, 66), (56, 66), (56, 204), (28, 204)]
TILE = (76, 90, 230, 214)
TEXT_WIDTH = 132          # "AFK" ink width on the tile, in grid units

# ---- straight logos for the taskbar: frame thickness, frame bar ends (x, y), tile box, text padding ----
SMALL_LOGOS = {
    24: (2, (15, 18), (4, 6, 23, 21), (1, 3)),
    32: (3, (21, 25), (5, 8, 31, 29), (2, 4)),
}


# ---------- the font ----------

def font_outlines():
    """Bahnschrift at the Bold SemiCondensed setting, as a static font."""
    font = TTFont(FONT_FILE)
    names = font["name"]
    for inst in font["fvar"].instances:
        if names.getDebugName(inst.subfamilyNameID) == FONT_STYLE:
            return instancer.instantiateVariableFont(font, dict(inst.coordinates))
    raise SystemExit("Bahnschrift " + FONT_STYLE + " not found")


def text_glyphs(font, text="AFK"):
    """[(glyph, x offset)] in font units, with the font's own spacing."""
    cmap, hmtx = font.getBestCmap(), font["hmtx"]
    glyphs, x = [], 0
    for ch in text:
        name = cmap[ord(ch)]
        glyphs.append((name, x))
        x += hmtx[name][0]
    return glyphs


def text_transform(font, box, width):
    """Font units -> grid: 'AFK' is `width` wide, centred in box (x0, y0, x1, y1)."""
    gs = font.getGlyphSet()
    bounds = BoundsPen(gs)
    for name, dx in text_glyphs(font):
        gs[name].draw(TransformPen(bounds, (1, 0, 0, 1, dx, 0)))
    xmin, ymin, xmax, ymax = bounds.bounds
    s = width / (xmax - xmin)
    x0, y0, x1, y1 = box
    tx = (x0 + x1) / 2 - (xmin + xmax) / 2 * s
    ty = (y0 + y1) / 2 + (ymin + ymax) / 2 * s
    return s, tx, ty


def text_path(font, box, width, digits=1):
    """SVG / XAML path data for 'AFK' laid into box."""
    s, tx, ty = text_transform(font, box, width)
    gs = font.getGlyphSet()
    parts = []
    for name, dx in text_glyphs(font):
        pen = SVGPathPen(gs, ntos=lambda v: f"{v:.{digits}f}".rstrip("0").rstrip("."))
        gs[name].draw(TransformPen(pen, (s, 0, 0, -s, tx + dx * s, ty)))
        parts.append(pen.getCommands())
    return " ".join(parts)


class FlattenPen(BasePen):
    """Turns glyph outlines into polygons (for drawing with Pillow)."""

    def __init__(self, glyphset, steps=12):
        super().__init__(glyphset)
        self.contours, self.current, self.steps = [], [], steps

    def _moveTo(self, p):
        self.current = [p]

    def _lineTo(self, p):
        self.current.append(p)

    def _curveToOne(self, p1, p2, p3):
        p0 = self._getCurrentPoint()
        for i in range(1, self.steps + 1):
            t = i / self.steps
            u = 1 - t
            self.current.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                                 u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))

    def _qCurveToOne(self, p1, p2):
        p0 = self._getCurrentPoint()
        for i in range(1, self.steps + 1):
            t = i / self.steps
            u = 1 - t
            self.current.append((u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0],
                                 u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1]))

    def _closePath(self):
        if len(self.current) > 2:
            self.contours.append(self.current)
        self.current = []

    _endPath = _closePath


def text_polygons(font, box, width):
    s, tx, ty = text_transform(font, box, width)
    gs = font.getGlyphSet()
    pen = FlattenPen(gs)
    for name, dx in text_glyphs(font):
        gs[name].draw(TransformPen(pen, (s, 0, 0, -s, tx + dx * s, ty)))
    return pen.contours


# ---------- drawing ----------

def area(points):
    return sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(points, points[1:] + points[:1])) / 2


def rotate(points, degrees, cx=128, cy=128):
    a = math.radians(degrees)
    return [(cx + (x - cx) * math.cos(a) - (y - cy) * math.sin(a),
             cy + (x - cx) * math.sin(a) + (y - cy) * math.cos(a)) for x, y in points]


def logo(font, size, background=None, pad=0.0):
    """The full logo (frame + tilted tile + AFK), 4x supersampled."""
    big = size * 4
    img = Image.new("RGBA", (big, big), background + (255,) if background else (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    scale = big * (1 - 2 * pad) / 256
    off = big * pad

    def place(points):
        return [(off + x * scale, off + y * scale) for x, y in rotate(points, -12)]

    x0, y0, x1, y1 = TILE
    d.polygon(place(FRAME), fill=GRAY + (255,))
    d.polygon(place([(x0, y0), (x1, y0), (x1, y1), (x0, y1)]), fill=MINT + (255,))
    # letters: the font's strokes overlap, so fill every outline, then cut out any reversed one (a hole)
    contours = text_polygons(font, TILE, TEXT_WIDTH)
    outer = max(contours, key=lambda c: abs(area(c)))
    ink = Image.new("1", (big, big), 0)
    holes = Image.new("1", (big, big), 0)
    for contour in contours:
        target = ink if (area(contour) > 0) == (area(outer) > 0) else holes
        ImageDraw.Draw(target).polygon(place(contour), fill=1)
    ink = ImageChops.logical_and(ink, ImageChops.invert(holes))
    img.paste(INK + (255,), (0, 0), ink.convert("L"))
    return img.resize((size, size), Image.LANCZOS)


def small_font(px):
    f = ImageFont.truetype(FONT_FILE, px)
    f.set_variation_by_name(SMALL_STYLE)
    return f


def small_text(d, box, ink=INK):
    """'AFK' as big as fits in box, drawn by FreeType at the real pixel size (hinted, smooth)."""
    x0, y0, x1, y1 = box
    best = None
    for px in range(4, 200):
        f = small_font(px)
        left, top, right, bottom = d.textbbox((0, 0), "AFK", font=f)
        if right - left > x1 - x0 or bottom - top > y1 - y0:
            break
        best = (f, left, top, right, bottom)
    f, left, top, right, bottom = best
    x = x0 + ((x1 - x0) - (right - left)) / 2 - left
    y = y0 + ((y1 - y0) - (bottom - top)) / 2 - top
    d.text((round(x), round(y)), "AFK", font=f, fill=ink + (255,))


def small_logo(size):
    """The taskbar logo (24 and 32 px): straight, with every edge on a whole pixel."""
    t, (bar_end_x, bar_end_y), tile, (px, py) = SMALL_LOGOS[size]
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    top = 2
    d.rectangle([0, top, bar_end_x, top + t - 1], fill=GRAY + (255,))   # frame: top bar
    d.rectangle([0, top, t - 1, bar_end_y], fill=GRAY + (255,))         # frame: left bar
    d.rectangle(tile, fill=MINT + (255,))
    x0, y0, x1, y1 = tile
    small_text(d, (x0 + px, y0 + py, x1 + 1 - px, y1 + 1 - py))
    return img


def badge(size, fill=MINT):
    """A flat rounded tile that says AFK: the hidden-icons icon and other tiny sizes."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=max(2, size // 6), fill=fill + (255,))
    pad = max(1, size // 12)
    small_text(d, (pad, size * 0.25, size - pad, size * 0.75))
    return img


def icon_frame(font, size):
    """16-20 px: the flat badge. 24-32 px: the straight logo (taskbar). Bigger: the tilted logo."""
    if size in SMALL_LOGOS:
        return small_logo(size)
    return badge(size) if size < 24 else logo(font, size)


def save_ico(path, frames):
    """Writes an .ico: classic 32-bit BMP pictures up to 64 px (every Windows API and .NET can read those),
    PNG for 128 and 256 px to keep the exe small."""
    frames = sorted((f.convert("RGBA") for f in frames), key=lambda f: f.width)
    pictures = []
    for f in frames:
        w, h = f.size
        if w >= 128:
            buf = BytesIO()
            f.save(buf, "PNG")
            pictures.append(buf.getvalue())
        else:
            header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
            pixels = f.tobytes("raw", "BGRA", 0, -1)             # bottom row first
            mask = bytes(((w + 31) // 32) * 4 * h)               # unused: the alpha channel decides
            pictures.append(header + pixels + mask)
    data = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    for f, picture in zip(frames, pictures):
        w, h = f.size
        data += struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32, len(picture), offset)
        offset += len(picture)
    Path(path).write_bytes(data + b"".join(pictures))


# ---------- vector logos ----------

def write_svgs(font, assets):
    letters = text_path(font, TILE, TEXT_WIDTH)
    logo_svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" role="img" aria-label="NudgeNest logo">
  <g transform="rotate(-12 128 128)">
    <path fill="#9AA99F" d="M28 38H182V66H56V204H28Z"/>
    <path fill="#86E3CE" d="M76 90H230V214H76Z"/>
    <path fill="#102821" d="{letters}"/>
  </g>
</svg>
'''
    (assets / "logo.svg").write_text(logo_svg, encoding="utf-8", newline="\n")
    badge_letters = text_path(font, (1.2, 4.5, 14.8, 11.5), 13.6, digits=2)
    favicon_svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" role="img" aria-label="NudgeNest">
  <rect width="16" height="16" rx="2.5" fill="#86E3CE"/>
  <path fill="#102821" d="{badge_letters}"/>
</svg>
'''
    (assets / "favicon.svg").write_text(favicon_svg, encoding="utf-8", newline="\n")
    return letters


def write_xaml(letters):
    """Puts the letters into both logos in src/MainWindow.xaml (header and About page)."""
    xaml = ROOT / "src" / "MainWindow.xaml"
    text = xaml.read_text(encoding="utf-8")
    text, count = re.subn(r'(<Path x:Name="[A-Za-z]*LogoLetters" Fill="#102821" Data=")[^"]*("/>)',
                          lambda m: m.group(1) + "F1 " + letters + m.group(2), text)   # F1: fill overlaps (nonzero)
    if count != 2:
        raise SystemExit(f"expected 2 logo letter paths in MainWindow.xaml, found {count}")
    xaml.write_text(text, encoding="utf-8", newline="\n")


def main():
    font = font_outlines()
    assets = ROOT / "assets"
    docs = ROOT / "docs"
    (docs / "assets").mkdir(parents=True, exist_ok=True)

    save_ico(assets / "icon.ico", [icon_frame(font, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    for state, fill in TRAY.items():
        save_ico(assets / f"tray-{state}.ico", [badge(s, fill) for s in (16, 20, 24, 32)])
    logo(font, 256).save(assets / "logo-256.png")
    small_logo(32).save(assets / "logo-32.png")
    logo(font, 64).save(assets / "logo-64.png")

    save_ico(docs / "favicon.ico", [badge(16), small_logo(32), logo(font, 48)])
    small_logo(32).save(docs / "assets" / "favicon-32.png")
    logo(font, 180, background=NIGHT, pad=0.1).save(docs / "apple-touch-icon.png")
    logo(font, 192, pad=0.04).save(docs / "assets" / "icon-192.png")
    logo(font, 512, pad=0.04).save(docs / "assets" / "icon-512.png")

    letters = write_svgs(font, assets)
    for name in ("logo.svg", "favicon.svg"):
        (docs / "assets" / name).write_bytes((assets / name).read_bytes())
    write_xaml(letters)
    print("icons written to", assets, "and", docs)


if __name__ == "__main__":
    main()
