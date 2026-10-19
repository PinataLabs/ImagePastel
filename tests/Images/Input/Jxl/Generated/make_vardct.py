# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes the PNGs that the VarDCT test files are made from, into the current directory:
#   vardct_small.png    64 x 48 8-bit RGB: one group, so the frame is a single section
#   vardct_groups.png   280 x 72 8-bit RGB: two groups, wide enough for a custom coefficient order
#   vardct_alpha.png    72 x 56 8-bit RGBA: the alpha is an extra channel in the Modular stream
# The patterns mix smooth gradients, hard edges and fine texture, so every DCT frequency has
# something to code.

import struct
import zlib


def rgb8(x, y, width, height):
    r = (x * 255) // (width - 1)
    g = (y * 255) // (height - 1)
    b = 255 if (x // 12 + y // 12) % 2 else ((x * 7) ^ (y * 5)) & 0xFF
    return r, g, b


def rgba8(x, y, width, height):
    r, g, b = rgb8(x, y, width, height)
    a = 255 if (x - 36) ** 2 + (y - 28) ** 2 < 20 ** 2 else (x * 3 + y) & 0xFF
    return r, g, b, a


def write_png(path, colour_type, fmt, pixel, width, height):
    rows = b""
    for y in range(height):
        rows += b"\x00" + b"".join(struct.pack(fmt, *pixel(x, y, width, height)) for x in range(width))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    header = struct.pack(">IIBBBBB", width, height, 8, colour_type, 0, 0, 0)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


write_png("vardct_small.png", 2, ">3B", rgb8, 64, 48)
write_png("vardct_groups.png", 2, ">3B", rgb8, 280, 72)
write_png("vardct_alpha.png", 6, ">4B", rgba8, 72, 56)
