# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes the sources of the XYB VarDCT test files into the current directory:
#   vardct_xyb_64_source.ppm        64 x 64 8-bit RGB: one group, so the frame is a single section
#   vardct_xyb_300x200_source.ppm   300 x 200 8-bit RGB: two groups in one row
#   vardct_xyb_600x400_source.ppm   600 x 400 8-bit RGB: three by two groups
#   vardct_xyb_grey_source.pgm      300 x 200 8-bit grey, the red channel of the 300 x 200 image
#   vardct_xyb_13x37_source.ppm     13 x 37 8-bit RGB: neither side a multiple of 8, and narrower
#                                   than two blocks
# The pixels look a little like a photo: smooth gradients, a sine texture, hard-edged blocks and some
# noise, so every DCT frequency has something to code.

import math


def photo(x, y, width, height):
    n = ((x * 73856093) ^ (y * 19349663)) & 0xFF
    r = 128 + 100 * math.sin(x / 17.0) * math.cos(y / 23.0)
    g = 255 * x / (width - 1)
    b = 255 * y / (height - 1)
    if (x // 37 + y // 29) % 3 == 0:
        r, g, b = 240, 30, 40
    r += (n - 128) * 0.15
    g += (n - 128) * 0.1
    b += (n - 128) * 0.2
    return tuple(max(0, min(255, int(round(v)))) for v in (r, g, b))


def pixels(width, height):
    return [photo(x, y, width, height) for y in range(height) for x in range(width)]


def write_ppm(path, width, height):
    with open(path, "wb") as f:
        f.write(b"P6\n%d %d\n255\n" % (width, height))
        f.write(bytes(v for p in pixels(width, height) for v in p))


def write_pgm(path, width, height):
    with open(path, "wb") as f:
        f.write(b"P5\n%d %d\n255\n" % (width, height))
        f.write(bytes(p[0] for p in pixels(width, height)))


write_ppm("vardct_xyb_64_source.ppm", 64, 64)
write_ppm("vardct_xyb_300x200_source.ppm", 300, 200)
write_ppm("vardct_xyb_600x400_source.ppm", 600, 400)
write_pgm("vardct_xyb_grey_source.pgm", 300, 200)
write_ppm("vardct_xyb_13x37_source.ppm", 13, 37)
