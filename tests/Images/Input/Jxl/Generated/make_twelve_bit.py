# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes twelve_bit_source.png into the current directory: a 45 x 33 16-bit RGBA PNG whose samples
# are 12-bit values scaled to 16 bits, with an sBIT chunk of 12 bits per channel. Each channel is a
# different mix of ramps and fixed-seed noise, and alpha includes 0 and 4095.

import random
import struct
import zlib

WIDTH = 45
HEIGHT = 33
MAX12 = 4095
MAX16 = 65535

random.seed(211)


def pixel(x, y):
    r = x * MAX12 // (WIDTH - 1)
    g = (y * MAX12 // (HEIGHT - 1) + random.randrange(64)) % (MAX12 + 1)
    b = random.randrange(MAX12 + 1)
    a = (0, MAX12, random.randrange(MAX12 + 1))[x % 3]
    return tuple(round(v * MAX16 / MAX12) for v in (r, g, b, a))


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


rows = b""
for y in range(HEIGHT):
    rows += b"\x00" + b"".join(struct.pack(">4H", *pixel(x, y)) for x in range(WIDTH))

header = struct.pack(">IIBBBBB", WIDTH, HEIGHT, 16, 6, 0, 0, 0)  # 16-bit RGBA
significant_bits = bytes((12, 12, 12, 12))
with open("twelve_bit_source.png", "wb") as f:
    f.write(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"sBIT", significant_bits)
        + chunk(b"IDAT", zlib.compress(rows, 9))
        + chunk(b"IEND", b"")
    )
