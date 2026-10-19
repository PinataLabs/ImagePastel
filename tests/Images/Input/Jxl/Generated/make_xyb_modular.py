# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes the sources of the lossy Modular (XYB) test files into the current directory:
# xyb_modular_source.ppm, 283 x 67 8-bit RGB, and xyb_modular_grey_source.pgm, the same size in grey.
# The pixels look a little like a photo: smooth gradients, a sine texture, hard-edged blocks and some
# noise, so the lossy encoder has something to do. 283 is wider than one 256-pixel group.

import math

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


pixels = [photo(x, y) for y in range(HEIGHT) for x in range(WIDTH)]

with open("xyb_modular_source.ppm", "wb") as f:
    f.write(b"P6\n%d %d\n255\n" % (WIDTH, HEIGHT))
    f.write(bytes(v for p in pixels for v in p))

with open("xyb_modular_grey_source.pgm", "wb") as f:
    f.write(b"P5\n%d %d\n255\n" % (WIDTH, HEIGHT))
    f.write(bytes(p[0] for p in pixels))
