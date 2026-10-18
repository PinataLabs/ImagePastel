# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes two 64 x 64 16-bit RGBA PNGs into the current directory:
#   premultiplied_alpha_source.png    colours already multiplied by alpha: the cjxl input
#   premultiplied_alpha_straight.png  the same pixels with straight alpha: what a decoder must output
# Alpha steps through 0, 1/4, 1/2, 3/4 and 1 every 8 columns. Where alpha is 0 the colour is 0.

import struct
import zlib

SIZE = 64
MAX = 65535


def straight_pixel(x, y):
    a = round(((x // 8) % 5) / 4 * MAX)
    if a == 0:
        return 0, 0, 0, 0
    r = round(x / (SIZE - 1) * MAX)
    g = round(y / (SIZE - 1) * MAX)
    b = round(((x * y) % SIZE) / (SIZE - 1) * MAX)
    return r, g, b, a


def premultiplied_pixel(x, y):
    r, g, b, a = straight_pixel(x, y)
    return round(r * a / MAX), round(g * a / MAX), round(b * a / MAX), a


def write_png(path, pixel):
    rows = b""
    for y in range(SIZE):
        rows += b"\x00" + b"".join(struct.pack(">4H", *pixel(x, y)) for x in range(SIZE))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    header = struct.pack(">IIBBBBB", SIZE, SIZE, 16, 6, 0, 0, 0)  # 16-bit RGBA
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


write_png("premultiplied_alpha_source.png", premultiplied_pixel)
write_png("premultiplied_alpha_straight.png", straight_pixel)
