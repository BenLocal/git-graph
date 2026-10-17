#!/usr/bin/env python3
"""Render the app icon to PNG and ICO files with Pillow.

Draws the same picture as src/GitGraph.App/Assets/gitgraph.svg (two transit lanes
and the HEAD station with its signal halo) on the SVG's 64-unit grid, supersampled
for smooth edges. Writes gitgraph-<size>.png for each size and gitgraph.ico into
the output folder. Run it again after changing the SVG.

Usage: render_icon.py [--out src/GitGraph.App/Assets/Icons]
"""

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw

SIZES = [16, 24, 32, 48, 64, 128, 256, 512]
SUPERSAMPLE = 4

CANVAS = "#171C26"
VERMILION = "#E4572E"
TEAL = "#2EA6A0"
SIGNAL = "#F2B33D"


def bezier(p0, p1, p2, p3, steps=48):
    pts = []
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        pts.append((
            u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
            u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1],
        ))
    return pts


def stroke(draw, points, color, width, k):
    """A round-capped stroke, drawn as dots along the path; Pillow's wide polylines break at joints."""
    r = width * k / 2
    step = max(r / 4, 0.5)
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        x0, y0, x1, y1 = x0 * k, y0 * k, x1 * k, y1 * k
        n = max(1, int(((x1 - x0) ** 2 + (y1 - y0) ** 2) ** 0.5 / step))
        for i in range(n + 1):
            x, y = x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n
            draw.ellipse((x - r, y - r, x + r, y + r), fill=color)


def circle(draw, cx, cy, r, k, fill=None, outline=None, width=0):
    draw.ellipse(((cx - r) * k, (cy - r) * k, (cx + r) * k, (cy + r) * k),
                 fill=fill, outline=outline, width=round(width * k))


def render(size):
    k = size * SUPERSAMPLE / 64
    img = Image.new("RGBA", (size * SUPERSAMPLE,) * 2, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, 64 * k - 1, 64 * k - 1), radius=14 * k, fill=CANVAS)

    stroke(d, [(22, 12), (22, 52)], VERMILION, 5, k)
    # The teal branch: M22 44 c0-10 20-8 20-18 v-8
    stroke(d, bezier((22, 44), (22, 34), (42, 36), (42, 26)) + [(42, 18)], TEAL, 5, k)

    circle(d, 22, 44, 5.5 + 2, k, fill=VERMILION)
    circle(d, 22, 44, 5.5 - 2, k, fill=CANVAS)
    circle(d, 42, 18, 9 + 1.5, k, fill=SIGNAL)
    circle(d, 42, 18, 9 - 1.5, k, fill=CANVAS)
    circle(d, 42, 18, 5, k, fill=TEAL)
    return img.resize((size, size), Image.LANCZOS)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", default="src/GitGraph.App/Assets/Icons")
    args = parser.parse_args()

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    images = {s: render(s) for s in SIZES}
    for s, img in images.items():
        img.save(out / ("gitgraph-%d.png" % s))
    images[256].save(out / "gitgraph.ico", sizes=[(s, s) for s in SIZES if s <= 256])
    print("icons written to %s" % out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
