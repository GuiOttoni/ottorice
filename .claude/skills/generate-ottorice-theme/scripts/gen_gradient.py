#!/usr/bin/env python3
"""Generate a simple gradient wallpaper + preview PNG for an OttoRice theme.

Fallback art generator used when no real photo/artwork is supplied for the
theme. Produces a smooth diagonal gradient between 2-4 palette colors with an
optional soft "glow" blob, matching the style used by the blackturq/voidhaze/
catppuccin example themes.

Usage:
    python gen_gradient.py --colors "#1e1e2e,#cba6f7,#89b4fa" \
        --wallpaper out/wallpaper.png --preview out/preview.png \
        --wallpaper-size 3840x2160 --preview-size 1040x585

Requires Pillow (pip install pillow).
"""
import argparse
import re
from PIL import Image, ImageDraw, ImageFilter


def hex_to_rgb(h: str) -> tuple[int, int, int]:
    h = h.strip().lstrip("#")
    if len(h) == 3:
        h = "".join(c * 2 for c in h)
    if not re.fullmatch(r"[0-9a-fA-F]{6}", h):
        raise ValueError(f"Invalid hex color: {h}")
    return tuple(int(h[i : i + 2], 16) for i in (0, 2, 4))


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def make_gradient(size, colors, glow=True):
    w, h = size
    stops = [hex_to_rgb(c) for c in colors]
    img = Image.new("RGB", (w, h))
    px = img.load()
    n = len(stops) - 1
    for y in range(h):
        for x in range(0, w, 2):  # stride 2, upscale-safe, faster
            t = ((x / w) + (y / h)) / 2  # diagonal
            t = min(max(t, 0.0), 1.0)
            seg = min(int(t * n), n - 1)
            local_t = (t * n) - seg
            color = lerp(stops[seg], stops[seg + 1], local_t)
            px[x, y] = color
            if x + 1 < w:
                px[x + 1, y] = color

    if glow:
        glow_layer = Image.new("RGB", (w, h), stops[-1])
        mask = Image.new("L", (w, h), 0)
        mdraw = ImageDraw.Draw(mask)
        cx, cy, r = int(w * 0.7), int(h * 0.3), int(min(w, h) * 0.45)
        mdraw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=90)
        mask = mask.filter(ImageFilter.GaussianBlur(r // 2))
        img = Image.composite(glow_layer, img, mask)

    return img


def parse_size(s: str) -> tuple[int, int]:
    w, h = s.lower().split("x")
    return int(w), int(h)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--colors", required=True, help="Comma-separated hex colors, e.g. '#1e1e2e,#cba6f7,#89b4fa'")
    ap.add_argument("--wallpaper", required=True)
    ap.add_argument("--preview", required=True)
    ap.add_argument("--wallpaper-size", default="3840x2160")
    ap.add_argument("--preview-size", default="1040x585")
    ap.add_argument("--no-glow", action="store_true")
    args = ap.parse_args()

    colors = [c for c in args.colors.split(",") if c.strip()]
    if len(colors) < 2:
        raise SystemExit("Need at least 2 colors")

    wp = make_gradient(parse_size(args.wallpaper_size), colors, glow=not args.no_glow)
    wp.save(args.wallpaper)

    pv = make_gradient(parse_size(args.preview_size), colors, glow=not args.no_glow)
    pv.save(args.preview)

    print(f"Wrote {args.wallpaper} and {args.preview}")


if __name__ == "__main__":
    main()
