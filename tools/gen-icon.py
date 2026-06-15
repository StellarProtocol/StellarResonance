#!/usr/bin/env python3
"""Generate the Stellar app icon — a glowing cyan 4-point sparkle.

Outputs into src/StellarLauncher.App/Assets/:
  stellar.png   (512, Avalonia window/taskbar icon + README logo)
  stellar.ico   (multi-size, Windows .exe ApplicationIcon)
  stellar.icns  (macOS .app bundle icon, PNG-based, hand-rolled)

The sparkle is scaled to leave a safe margin so its Gaussian halo never clips
against the canvas edges.
"""
from __future__ import annotations

import io
import math
import struct
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parents[1] / "src/StellarLauncher.App/Assets"
S = 1024
# Sparkle scale (fraction of half-canvas). 0.32 keeps the tips well inside so the
# ~6% blur halo has room to fade out without touching the edges.
TIP = 0.32
INNER = TIP * 0.24

def sparkle(cx, cy, outer, inner):
    pts = []
    for i in range(8):
        ang = math.radians(i * 45 - 90)          # tip at top
        r = outer if i % 2 == 0 else inner
        pts.append((cx + r * math.cos(ang), cy + r * math.sin(ang)))
    return pts

def render() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # Each layer = a SOLID colour painted through a blurred ALPHA MASK. Blurring a
    # mask (not an RGBA layer) keeps the colour pure cyan and only fades the alpha,
    # so there's no black bleeding into the halo — no dark/muddy glow.
    def layer(color, tip, inner, blur):
        mask = Image.new("L", (S, S), 0)
        ImageDraw.Draw(mask).polygon(sparkle(S / 2, S / 2, S * tip, S * inner), fill=255)
        if blur:
            mask = mask.filter(ImageFilter.GaussianBlur(S * blur))
        out = Image.new("RGBA", (S, S), color + (0,))
        out.putalpha(mask)
        return out

    # Deeper azure halo — stacked so it reads as a rich blue glow, not pale cyan.
    halo = layer((20, 110, 210), TIP, INNER, 0.055)
    for _ in range(4):
        img = Image.alpha_composite(img, halo)
    # Tighter brighter glow into the core.
    inner_glow = layer((45, 150, 235), TIP * 0.92, INNER, 0.018)
    for _ in range(2):
        img = Image.alpha_composite(img, inner_glow)
    # Crisp body + near-white core.
    img = Image.alpha_composite(img, layer((120, 200, 250), TIP * 0.90, INNER * 0.78, 0.004))
    img = Image.alpha_composite(img, layer((225, 245, 255), TIP * 0.64, INNER * 0.46, 0.0))
    return img

def write_icns(master: Image.Image, path: Path) -> None:
    types = [(b"icp4", 16), (b"icp5", 32), (b"ic07", 128),
             (b"ic08", 256), (b"ic09", 512), (b"ic10", 1024)]
    entries = b""
    for code, sz in types:
        buf = io.BytesIO()
        master.resize((sz, sz), Image.LANCZOS).save(buf, format="PNG")
        data = buf.getvalue()
        entries += code + struct.pack(">I", len(data) + 8) + data
    path.write_bytes(b"icns" + struct.pack(">I", len(entries) + 8) + entries)

def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    master = render()
    master.resize((512, 512), Image.LANCZOS).save(OUT / "stellar.png")
    master.save(OUT / "stellar.ico",
                sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    write_icns(master, OUT / "stellar.icns")
    print(f"wrote {OUT}/stellar.png (512), stellar.ico, stellar.icns")

if __name__ == "__main__":
    main()
