"""Crops the window renders from `tests\\run-tests.bat shots` into the website's screenshots.

The renders are the whole WPF window at 2x, including its 16 px see-through shadow margin.
The website draws its own border and shadow, so only the window itself is kept (still at 2x, for sharp
screens), and docs/index.html shows them at half size.
"""
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
RENDERS = ROOT / "tests" / "bin" / "website"
ASSETS = ROOT / "docs" / "assets"
MARGIN = 16 * 2  # the window's shadow margin, at 2x

for render, name in (("main.png", "app-main.png"), ("options.png", "app-options.png"), ("about.png", "app-about.png")):
    image = Image.open(RENDERS / render).convert("RGBA")
    width, height = image.size
    window = image.crop((MARGIN, MARGIN, width - MARGIN, height - MARGIN))
    window.save(ASSETS / name, optimize=True)
    print(f"{name}: {window.size[0]} x {window.size[1]} (shown at {window.size[0] // 2} x {window.size[1] // 2})")
