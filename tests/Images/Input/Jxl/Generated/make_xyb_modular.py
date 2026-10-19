# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes the sources of the lossy Modular (XYB) test files into the current directory:
#
#   xyb_modular_source.ppm, 283 x 67 8-bit RGB, and xyb_modular_grey_source.pgm, the same size in grey.
#   The pixels look a little like a photo: smooth gradients, a sine texture, hard-edged blocks and some
#   noise, so the lossy encoder has something to do. 283 is wider than one 256-pixel group.
#
#   Given the path of an ICC profile, also xyb_modular_icc_animation_source.png: a 24 x 16 8-bit RGBA
#   APNG with that profile in an iCCP chunk and two frames, the second a 10 x 8 rectangle at (5, 4)
#   blended over the first.

import math
import struct
import sys
import zlib

WIDTH = 283
HEIGHT = 67


def photo(x, y):
    n = ((x * 73856093) ^ (y * 19349663)) & 0xFF
    r = 128 + 100 * math.sin(x / 17.0) * math.cos(y / 23.0)
    g = 255 * x / (WIDTH - 1)
    b = 255 * y / (HEIGHT - 1)
    if (x // 37 + y // 29) % 3 == 0:
        r, g, b = 240, 30, 40
    r += (n - 128) * 0.15
    g += (n - 128) * 0.1
    b += (n - 128) * 0.2
    return tuple(max(0, min(255, int(round(v)))) for v in (r, g, b))


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


def write_icc_animation(icc_path):
    width, height = 24, 16
    with open(icc_path, "rb") as f:
        icc = f.read()

    # (x0, y0, width, height, blend over), each shown for 100 ms.
    frames = [(0, 0, width, height, False), (5, 4, 10, 8, True)]
    out = [b"\x89PNG\r\n\x1a\n", chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))]
    out.append(chunk(b"iCCP", b"custom\x00\x00" + zlib.compress(icc)))
    out.append(chunk(b"acTL", struct.pack(">II", len(frames), 0)))
    sequence = 0
    for index, (x0, y0, w, h, blend) in enumerate(frames):
        out.append(chunk(b"fcTL", struct.pack(">IIIIIHHBB", sequence, w, h, x0, y0, 100, 1000, 0, 1 if blend else 0)))
        sequence += 1
        alpha = 255 if index == 0 else 128
        rows = b"".join(b"\x00" + b"".join(struct.pack(">4B", (x * 10 + index * 90) & 0xFF, (y * 15) & 0xFF, 100, alpha) for x in range(w)) for y in range(h))
        data = zlib.compress(rows, 9)
        if index == 0:
            out.append(chunk(b"IDAT", data))
        else:
            out.append(chunk(b"fdAT", struct.pack(">I", sequence) + data))
            sequence += 1

    out.append(chunk(b"IEND", b""))
    with open("xyb_modular_icc_animation_source.png", "wb") as f:
        f.write(b"".join(out))


pixels = [photo(x, y) for y in range(HEIGHT) for x in range(WIDTH)]

with open("xyb_modular_source.ppm", "wb") as f:
    f.write(b"P6\n%d %d\n255\n" % (WIDTH, HEIGHT))
    f.write(bytes(v for p in pixels for v in p))

with open("xyb_modular_grey_source.pgm", "wb") as f:
    f.write(b"P5\n%d %d\n255\n" % (WIDTH, HEIGHT))
    f.write(bytes(p[0] for p in pixels))

if len(sys.argv) > 1:
    write_icc_animation(sys.argv[1])
