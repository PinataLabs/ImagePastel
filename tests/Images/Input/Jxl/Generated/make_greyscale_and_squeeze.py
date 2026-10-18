# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes the PNGs that the greyscale and squeeze test files are made from, into the current directory:
#   greyscale_source.png         64 x 64 8-bit grey
#   greyscale_alpha_source.png   64 x 64 16-bit grey with alpha
#   rgba_source.png              321 x 283 8-bit RGBA: odd sizes, and larger than one 256 x 256 group
# The patterns mix smooth gradients with hard edges, so squeeze has both kinds of detail to code.

import struct
import zlib

SIZE = 64
RGBA_WIDTH = 321
RGBA_HEIGHT = 283


def grey8(x, y):
    return ((x * 4) ^ (y * 3)) & 0xFF if (x // 16 + y // 16) % 2 else (x * 255) // (SIZE - 1)


def grey_alpha16(x, y):
    g = (x * 1031 + y * 977) % 65536
    a = round(y / (SIZE - 1) * 65535)
    return g, a


def rgba8(x, y):
    r = (x * 255) // (RGBA_WIDTH - 1)
    g = (y * 255) // (RGBA_HEIGHT - 1)
    b = 255 if (x // 40 + y // 40) % 2 else (x + y) & 0xFF
    a = 255 if (x - 160) ** 2 + (y - 140) ** 2 < 100 ** 2 else (x * 4) & 0xFF
    return r, g, b, a


def write_png(path, bit_depth, colour_type, fmt, pixel, width=SIZE, height=SIZE):
    rows = b""
    for y in range(height):
        rows += b"\x00" + b"".join(struct.pack(fmt, *pixel(x, y)) for x in range(width))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    header = struct.pack(">IIBBBBB", width, height, bit_depth, colour_type, 0, 0, 0)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


write_png("greyscale_source.png", 8, 0, ">B", lambda x, y: (grey8(x, y),))
write_png("greyscale_alpha_source.png", 16, 4, ">2H", grey_alpha16)
write_png("rgba_source.png", 8, 6, ">4B", rgba8, RGBA_WIDTH, RGBA_HEIGHT)
