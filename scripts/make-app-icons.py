#!/usr/bin/env python3
"""Generate the four Netie app icons (netie-pdf / netie-image / netie-video / netie-md .ico).

Standard library only (zlib, struct). Deterministic: running it twice yields identical bytes.
Each .ico holds 16, 24, 32, 48 and 256 px frames, every frame a PNG (32-bit RGBA).
Original Netie marks: a folded page (PDF), a ridge with a sun (Image), a play wedge (Video), an M with an arrow (MD),
white on a rounded square in a per-app accent.

Usage: python3 scripts/make-app-icons.py [out_dir]
"""
from __future__ import annotations

import struct
import sys
import zlib
from pathlib import Path

SIZES = (16, 24, 32, 48, 256)
SS = 4  # supersample grid per axis

# name -> (accent top, accent bottom)
ACCENTS = {
    "pdf": ((236, 112, 98), (196, 70, 66)),
    "image": ((94, 196, 184), (52, 150, 148)),
    "video": ((150, 134, 236), (104, 92, 200)),
    "md": ((244, 178, 84), (222, 134, 52)),
}
WHITE = (255, 255, 255)


def in_round_rect(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def in_poly(x, y, pts):
    inside = False
    n = len(pts)
    j = n - 1
    for i in range(n):
        xi, yi = pts[i]
        xj, yj = pts[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i
    return inside


PAGE = [(0.31, 0.19), (0.55, 0.19), (0.69, 0.33), (0.69, 0.81), (0.31, 0.81)]


def mark_pdf(x, y):
    """Returns 0 none, 1 white, 2 accent (cut-out detail inside the white page)."""
    if not in_poly(x, y, PAGE):
        return 0
    for ly in (0.52, 0.62, 0.72):
        if 0.38 <= x <= 0.62 and ly <= y <= ly + 0.04:
            return 2
    return 1


def mark_image(x, y):
    if (x - 0.67) ** 2 + (y - 0.30) ** 2 <= 0.075 ** 2:
        return 1
    ridge = [(0.20, 0.76), (0.42, 0.42), (0.54, 0.60), (0.64, 0.47), (0.80, 0.76)]
    return 1 if in_poly(x, y, ridge) else 0


def mark_video(x, y):
    return 1 if in_poly(x, y, [(0.37, 0.27), (0.37, 0.73), (0.73, 0.50)]) else 0


def mark_md(x, y):
    """A letter M with a down arrow under it: the Markdown mark, white on the accent."""
    letter = [(0.22, 0.60), (0.22, 0.26), (0.32, 0.26), (0.50, 0.46), (0.68, 0.26),
              (0.78, 0.26), (0.78, 0.60), (0.68, 0.60), (0.68, 0.42), (0.50, 0.62),
              (0.32, 0.42), (0.32, 0.60)]
    arrow = [(0.46, 0.66), (0.54, 0.66), (0.54, 0.72), (0.62, 0.72), (0.50, 0.84),
             (0.38, 0.72), (0.46, 0.72)]
    return 1 if in_poly(x, y, letter) or in_poly(x, y, arrow) else 0


MARKS = {"pdf": mark_pdf, "image": mark_image, "video": mark_video, "md": mark_md}


def render(app: str, size: int) -> bytes:
    top, bot = ACCENTS[app]
    mark = MARKS[app]
    rows = []
    for py in range(size):
        row = bytearray([0])  # PNG filter 0
        for px in range(size):
            sums = [0.0, 0.0, 0.0]
            covered = 0
            for sy in range(SS):
                for sx in range(SS):
                    x = (px + (sx + 0.5) / SS) / size
                    y = (py + (sy + 0.5) / SS) / size
                    if not in_round_rect(x, y, 0.04, 0.04, 0.96, 0.96, 0.22):
                        continue
                    base = tuple(top[i] + (bot[i] - top[i]) * y for i in range(3))
                    col = WHITE if mark(x, y) == 1 else base
                    sums[0] += col[0]
                    sums[1] += col[1]
                    sums[2] += col[2]
                    covered += 1
            if covered == 0:
                row += bytes((0, 0, 0, 0))
            else:
                row += bytes((
                    int(round(sums[0] / covered)),
                    int(round(sums[1] / covered)),
                    int(round(sums[2] / covered)),
                    int(round(255 * covered / (SS * SS))),
                ))
        rows.append(bytes(row))
    return png(size, size, b"".join(rows))


def chunk(tag: bytes, data: bytes) -> bytes:
    body = tag + data
    return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)


def png(w: int, h: int, raw: bytes) -> bytes:
    ihdr = struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def ico(frames: list) -> bytes:
    out = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
    offset = 6 + 16 * len(frames)
    for size, data in frames:
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in frames:
        out += data
    return bytes(out)


def build(app: str) -> bytes:
    return ico([(s, render(app, s)) for s in SIZES])


def main(argv: list) -> int:
    default = Path(__file__).resolve().parents[1] / "src" / "NetieSpace" / "Assets" / "apps"
    out = Path(argv[1]) if len(argv) > 1 else default
    out.mkdir(parents=True, exist_ok=True)
    for app in ACCENTS:
        path = out / f"netie-{app}.ico"
        path.write_bytes(build(app))
        print(f"wrote {path} ({path.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
