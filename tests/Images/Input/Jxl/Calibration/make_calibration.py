# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
"""Makes the cjxl calibration table for the lossy JPEG XL quality checks.

    py -3 make_calibration.py images     # writes gradient.png, text.png and line_art.png
    py -3 make_calibration.py table      # encodes every image with cjxl and writes calibration.json

The table step needs the libjxl v0.12.0 tools (cjxl, djxl and ssimulacra2) in the folder that
JXL_TOOLS_DIR names. For every image, effort and distance it runs

    cjxl <image> <out.jxl> -d <distance> -e <effort>
    djxl <out.jxl> <out.png> --bits_per_sample=16
    ssimulacra2 <image> <out.png>

and records the size of out.jxl and the score. The score is of djxl's 16-bit PNG, whose samples
are clamped to 0 to 1 as an Image<TPixel> decode's are. ssimulacra2 given out.jxl itself scores
the unclamped float samples, which differs by up to 9 points on line art. The arguments are
written into the table ("cjxl_arguments", "djxl_arguments"), and the jxl-oracle test
JxlCalibrationOracleTests runs the same commands and checks that they reproduce the table.

The image step needs only Python. Its PNGs have no colour chunks, so every tool reads them as sRGB.
"""

import json
import os
import struct
import subprocess
import sys
import tempfile
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
INPUT_ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))

# Name, path under tests/Images/Input, kind. Small images keep cjxl to one frame (no streaming
# above 8 groups) and keep the managed SSIMULACRA2 fast.
IMAGES = [
    ("bike", "Png/Bike.png", "photo"),
    ("calliphora", "Png/CalliphoraPartial.png", "photo"),
    ("bike_alpha", "Png/bike-small.png", "photo with alpha"),
    ("gradient", "Jxl/Calibration/gradient.png", "synthetic gradient"),
    ("text", "Jxl/Calibration/text.png", "text"),
    ("line_art", "Jxl/Calibration/line_art.png", "line art"),
]
EFFORTS = [3, 5, 7]
DISTANCES = [1, 2, 4]
CJXL_ARGUMENTS = ["-d", "{distance}", "-e", "{effort}"]
DJXL_ARGUMENTS = ["--bits_per_sample=16"]


def write_png(path, width, height, rows):
    """Writes 8-bit RGB rows (lists of (r, g, b)) as a PNG with no ancillary chunks."""

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    raw = bytearray()
    for row in rows:
        raw.append(0)
        for r, g, b in row:
            raw += bytes((r, g, b))
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def clamp8(v):
    return max(0, min(255, int(round(v))))


def make_gradient():
    """256 x 256: a hue ramp across, fading to grey down the top half, and a dark-to-light ramp
    with a slow colour shift in the bottom half. Smooth areas show banding and blocking."""
    width = height = 256
    rows = []
    for y in range(height):
        row = []
        for x in range(width):
            if y < 128:
                t = x / (width - 1)
                # Piecewise-linear hue ramp: red, yellow, green, cyan, blue, magenta.
                h = t * 6
                i = int(h) % 6
                f = h - int(h)
                rgb = [(1, f, 0), (1 - f, 1, 0), (0, 1, f), (0, 1 - f, 1), (f, 0, 1), (1, 0, 1 - f)][i]
                s = 1 - y / 127
                row.append(tuple(clamp8(255 * (c * s + 0.5 * (1 - s))) for c in rgb))
            else:
                v = x / (width - 1)
                w = (y - 128) / 127
                row.append((clamp8(255 * v), clamp8(255 * v * (0.8 + 0.2 * w)), clamp8(255 * (v * (1 - w) + 0.25 * w))))
        rows.append(row)
    write_png(os.path.join(HERE, "gradient.png"), width, height, rows)


# A 5 x 7 bitmap font: each glyph is 7 rows of 5 bits, most significant bit on the left.
FONT = {
    " ": [0, 0, 0, 0, 0, 0, 0],
    "0": [14, 17, 19, 21, 25, 17, 14], "1": [4, 12, 4, 4, 4, 4, 14], "2": [14, 17, 1, 2, 4, 8, 31],
    "3": [31, 2, 4, 2, 1, 17, 14], "4": [2, 6, 10, 18, 31, 2, 2], "5": [31, 16, 30, 1, 1, 17, 14],
    "6": [6, 8, 16, 30, 17, 17, 14], "7": [31, 1, 2, 4, 8, 8, 8], "8": [14, 17, 17, 14, 17, 17, 14],
    "9": [14, 17, 17, 15, 1, 2, 12],
    "A": [14, 17, 17, 31, 17, 17, 17], "B": [30, 17, 17, 30, 17, 17, 30], "C": [14, 17, 16, 16, 16, 17, 14],
    "D": [28, 18, 17, 17, 17, 18, 28], "E": [31, 16, 16, 30, 16, 16, 31], "F": [31, 16, 16, 30, 16, 16, 16],
    "G": [14, 17, 16, 23, 17, 17, 15], "H": [17, 17, 17, 31, 17, 17, 17], "I": [14, 4, 4, 4, 4, 4, 14],
    "J": [7, 2, 2, 2, 2, 18, 12], "K": [17, 18, 20, 24, 20, 18, 17], "L": [16, 16, 16, 16, 16, 16, 31],
    "M": [17, 27, 21, 21, 17, 17, 17], "N": [17, 17, 25, 21, 19, 17, 17], "O": [14, 17, 17, 17, 17, 17, 14],
    "P": [30, 17, 17, 30, 16, 16, 16], "Q": [14, 17, 17, 17, 21, 18, 13], "R": [30, 17, 17, 30, 20, 18, 17],
    "S": [15, 16, 16, 14, 1, 1, 30], "T": [31, 4, 4, 4, 4, 4, 4], "U": [17, 17, 17, 17, 17, 17, 14],
    "V": [17, 17, 17, 17, 17, 10, 4], "W": [17, 17, 17, 21, 21, 21, 10], "X": [17, 17, 10, 4, 10, 17, 17],
    "Y": [17, 17, 17, 10, 4, 4, 4], "Z": [31, 1, 2, 4, 8, 16, 31],
    ".": [0, 0, 0, 0, 0, 12, 12], ",": [0, 0, 0, 0, 12, 4, 8], "-": [0, 0, 0, 31, 0, 0, 0],
}


def canvas(width, height, colour):
    return [[colour] * width for _ in range(height)]


def draw_text(pixels, x0, y0, text, scale, colour):
    for i, ch in enumerate(text):
        glyph = FONT[ch]
        for gy, bits in enumerate(glyph):
            for gx in range(5):
                if bits & (16 >> gx):
                    for sy in range(scale):
                        for sx in range(scale):
                            x = x0 + (i * 6 + gx) * scale + sx
                            y = y0 + gy * scale + sy
                            if 0 <= y < len(pixels) and 0 <= x < len(pixels[0]):
                                pixels[y][x] = colour


def make_text():
    """320 x 200: black, red and blue text at three sizes on white, and white text on dark blue."""
    width, height = 320, 200
    px = canvas(width, height, (255, 255, 255))
    lines = [
        ("THE QUICK BROWN FOX JUMPS", 1, (0, 0, 0)),
        ("OVER THE LAZY DOG. 0123456789", 1, (0, 0, 0)),
        ("JPEG XL CALIBRATION", 2, (0, 0, 0)),
        ("DISTANCE 1, 2, 4", 2, (200, 0, 0)),
        ("EFFORT 3 5 7", 3, (0, 0, 180)),
    ]
    y = 6
    for text, scale, colour in lines:
        draw_text(px, 6, y, text, scale, colour)
        y += 8 * scale + 6
    for yy in range(y, height):
        for xx in range(width):
            px[yy][xx] = (20, 30, 90)
    draw_text(px, 8, y + 8, "WHITE ON BLUE - SSIMULACRA2", 1, (255, 255, 255))
    draw_text(px, 8, y + 22, "LINE ART AND TEXT", 2, (255, 230, 0))
    write_png(os.path.join(HERE, "text.png"), width, height, px)


def plot(px, x, y, colour):
    if 0 <= y < len(px) and 0 <= x < len(px[0]):
        px[y][x] = colour


def line(px, x0, y0, x1, y1, colour):
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
    err = dx + dy
    while True:
        plot(px, x0, y0, colour)
        if x0 == x1 and y0 == y1:
            return
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy


def circle(px, cx, cy, r, colour):
    x, y, err = r, 0, 1 - r
    while x >= y:
        for a, b in ((x, y), (y, x), (-y, x), (-x, y), (-x, -y), (-y, -x), (y, -x), (x, -y)):
            plot(px, cx + a, cy + b, colour)
        y += 1
        if err < 0:
            err += 2 * y + 1
        else:
            x -= 1
            err += 2 * (y - x) + 1


def make_line_art():
    """256 x 256: one-pixel lines, a grid, hatching, circles and a fan of rays, aliased, on white."""
    width = height = 256
    px = canvas(width, height, (255, 255, 255))
    black = (0, 0, 0)
    for i in range(0, 128, 8):
        line(px, i, 0, i, 127, (90, 90, 90))
        line(px, 0, i, 127, i, (90, 90, 90))
    for i in range(0, 256, 6):
        line(px, 128 + i // 2, 0, 255, 127 - i // 2, (0, 120, 0))
    for r in range(6, 60, 7):
        circle(px, 64, 192, r, (180, 0, 0) if r % 2 else black)
    for k in range(0, 32):
        line(px, 192, 192, 192 + (k - 16) * 4, 130, black)
        line(px, 192, 192, 255, 192 + (k - 16) * 4, (0, 0, 200))
    line(px, 0, 0, 255, 255, black)
    line(px, 0, 255, 255, 0, black)
    write_png(os.path.join(HERE, "line_art.png"), width, height, px)


def tool(name):
    directory = os.environ.get("JXL_TOOLS_DIR")
    if not directory:
        sys.exit("JXL_TOOLS_DIR is not set.")
    path = os.path.join(directory, name + (".exe" if os.name == "nt" else ""))
    if not os.path.isfile(path):
        sys.exit(f"{path} does not exist.")
    return path


def make_table():
    cjxl, djxl, ssimulacra2 = tool("cjxl"), tool("djxl"), tool("ssimulacra2")
    version = subprocess.run([cjxl, "--version"], capture_output=True, text=True, check=True).stdout.splitlines()[0]
    rows = []
    with tempfile.TemporaryDirectory() as temp:
        for name, path, _ in IMAGES:
            source = os.path.join(INPUT_ROOT, *path.split("/"))
            for effort in EFFORTS:
                for distance in DISTANCES:
                    out = os.path.join(temp, f"{name}_e{effort}_d{distance}.jxl")
                    arguments = [a.format(distance=distance, effort=effort) for a in CJXL_ARGUMENTS]
                    decoded = out[:-len(".jxl")] + ".png"
                    subprocess.run([cjxl, source, out, *arguments], capture_output=True, check=True)
                    subprocess.run([djxl, out, decoded, *DJXL_ARGUMENTS], capture_output=True, check=True)
                    score = subprocess.run([ssimulacra2, source, decoded], capture_output=True, text=True, check=True).stdout.strip()
                    rows.append({
                        "image": name,
                        "effort": effort,
                        "distance": distance,
                        "size": os.path.getsize(out),
                        "ssimulacra2": float(score),
                    })
                    print(name, effort, distance, rows[-1]["size"], score)
    table = {
        "tools": version,
        "cjxl_arguments": CJXL_ARGUMENTS,
        "djxl_arguments": DJXL_ARGUMENTS,
        "images": [{"name": n, "path": p, "kind": k} for n, p, k in IMAGES],
        "rows": rows,
    }
    with open(os.path.join(HERE, "calibration.json"), "w", newline="\n") as f:
        json.dump(table, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    step = sys.argv[1] if len(sys.argv) > 1 else ""
    if step == "images":
        make_gradient()
        make_text()
        make_line_art()
    elif step == "table":
        make_table()
    else:
        sys.exit(__doc__)
