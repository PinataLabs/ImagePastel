# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes custom_icc_source.png into the current directory: a 32 x 32 8-bit RGB PNG with an iCCP chunk.
# The ICC profile (also written as custom_icc.icc) is a v2 matrix/TRC display profile with made-up
# primaries and a tabulated tone curve, so no JPEG XL colour encoding describes it and cjxl has to embed
# it in the codestream.

import struct
import zlib

SIZE = 32


def s15f16(v):
    return struct.pack(">i", round(v * 65536))


def xyz_tag(x, y, z):
    return b"XYZ " + b"\x00" * 4 + s15f16(x) + s15f16(y) + s15f16(z)


def text_desc(text):
    ascii_text = text.encode("ascii") + b"\x00"
    # textDescriptionType: ASCII, then empty Unicode and ScriptCode parts.
    return b"desc" + b"\x00" * 4 + struct.pack(">I", len(ascii_text)) + ascii_text + b"\x00" * 8 + b"\x00" * 3 + b"\x00" * 67


def text_tag(text):
    return b"text" + b"\x00" * 4 + text.encode("ascii") + b"\x00"


def curve_tag(points):
    return b"curv" + b"\x00" * 4 + struct.pack(">I", len(points)) + b"".join(struct.pack(">H", p) for p in points)


def pad4(data):
    return data + b"\x00" * (-len(data) % 4)


# A tone curve with a kink: no gamma or parametric curve matches it.
curve = [0, 3000, 9000, 20000, 30000, 38000, 47000, 56000, 65535]
tags = [
    (b"desc", text_desc("ImageSharp JXL test: custom primaries")),
    (b"cprt", text_tag("Copyright (c) PinataLabs. Apache License 2.0.")),
    (b"wtpt", xyz_tag(0.9642, 1.0, 0.8249)),
    (b"rXYZ", xyz_tag(0.5, 0.25, 0.02)),
    (b"gXYZ", xyz_tag(0.3, 0.65, 0.12)),
    (b"bXYZ", xyz_tag(0.1642, 0.1, 0.6849)),
    (b"rTRC", curve_tag(curve)),
    (b"gTRC", curve_tag(curve)),
    (b"bTRC", curve_tag(curve)),
]

table_size = 4 + 12 * len(tags)
offset = 128 + table_size
table = struct.pack(">I", len(tags))
data = b""
for sig, body in tags:
    body = pad4(body)
    table += sig + struct.pack(">II", offset + len(data), len(body))
    data += body

size = 128 + len(table) + len(data)
header = struct.pack(">I", size) + b"none" + bytes([2, 0x10, 0, 0]) + b"mntrRGB XYZ "
header += struct.pack(">HHHHHH", 2026, 1, 1, 0, 0, 0) + b"acsp" + b"\x00" * 4 + struct.pack(">I", 0)
header += b"\x00" * 4 + b"\x00" * 4 + b"\x00" * 8 + struct.pack(">I", 0)
header += s15f16(0.9642) + s15f16(1.0) + s15f16(0.8249) + b"\x00" * 4
header += b"\x00" * (128 - len(header))
icc = header + table + data
assert len(icc) == size

with open("custom_icc.icc", "wb") as f:
    f.write(icc)


def chunk(kind, body):
    return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))


rows = b"".join(b"\x00" + b"".join(bytes(((x * 8) & 0xFF, (y * 8) & 0xFF, ((x * y) * 4) & 0xFF)) for x in range(SIZE)) for y in range(SIZE))
ihdr = struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0)
iccp = b"custom\x00\x00" + zlib.compress(icc, 9)
with open("custom_icc_source.png", "wb") as f:
    f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"iCCP", iccp)
            + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))
