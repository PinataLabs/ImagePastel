# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes the sources of the XYB VarDCT files with every block size into the current directory. Each is
# 320 x 288 8-bit RGB: wider than one 256-pixel group, so the frame has two by two groups.
#   vardct_xyb_sizes_noisy_source.ppm    smooth colour with soft noise: many small blocks (DCT8, 2x2, 4x8, 8x4)
#   vardct_xyb_sizes_fine_source.ppm     smooth colour with a fine, fading texture: blocks of 8 to 64 pixels
#   vardct_xyb_sizes_smooth_source.ppm   smooth colour only: 32x64 and 64x64 blocks
# None has a hard edge or a flat noise-free area of hard detail, where cjxl -e 7 picks the Identity and
# AFV blocks that come later (D9f).

import math

WIDTH = 320
HEIGHT = 288


def smooth(x, y, w, h):
    r = 128 + 90 * math.sin(x / 70.0 + 0.5) * math.cos(y / 90.0)
    g = 60 + 140 * x / (w - 1) + 20 * math.sin((x + y) / 50.0)
    b = 200 - 150 * y / (h - 1) + 25 * math.cos(x / 40.0)
    return r, g, b


def noisy(x, y, w, h):
    r, g, b = smooth(x, y, w, h)
    n = (((x * 73856093) ^ (y * 19349663)) & 0xFF) - 128
    a = 0.3 * (0.5 + 0.5 * math.sin(x / 45.0) * math.cos(y / 35.0))
    return r + n * a, g + n * a * 0.8, b + n * a * 0.6


def fine(x, y, w, h):
    r, g, b = smooth(x, y, w, h)
    t = 35 * math.sin(x * 1.6) * math.sin(y * 1.6) + 20 * math.sin(x * 0.7 + y * 2.1)
    m = 0.5 + 0.5 * math.sin(x / 25.0 + y / 31.0)
    return r + t * m, g + t * m, b + t * m


def write_ppm(path, fn):
    with open(path, "wb") as f:
        f.write(b"P6\n%d %d\n255\n" % (WIDTH, HEIGHT))
        data = bytearray()
        for y in range(HEIGHT):
            for x in range(WIDTH):
                data.extend(max(0, min(255, int(round(v)))) for v in fn(x, y, WIDTH, HEIGHT))
        f.write(bytes(data))


write_ppm("vardct_xyb_sizes_noisy_source.ppm", noisy)
write_ppm("vardct_xyb_sizes_fine_source.ppm", fine)
write_ppm("vardct_xyb_sizes_smooth_source.ppm", smooth)
