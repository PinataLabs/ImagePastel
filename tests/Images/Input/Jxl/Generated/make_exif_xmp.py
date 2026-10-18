# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes exif_xmp_source.png into the current directory: a 16 x 16 8-bit RGB PNG that carries the Exif
# (eXIf chunk) and XMP (iTXt chunk) of libjxl's jpeg_reconstruction/1x1_exif_xmp.jpg, whose path is the
# first argument. cjxl copies both into Exif and xml boxes.

import struct
import sys
import zlib

SIZE = 16


def app1_payloads(jpeg):
    """Yields the payload of every APP1 segment before the first scan."""
    pos = 2
    while pos + 4 <= len(jpeg):
        marker = jpeg[pos + 1]
        length = struct.unpack(">H", jpeg[pos + 2:pos + 4])[0]
        if marker == 0xDA:
            return
        if marker == 0xE1:
            yield jpeg[pos + 4:pos + 2 + length]
        pos += 2 + length


EXIF_TAG = b"Exif\x00\x00"
XMP_TAG = b"http://ns.adobe.com/xap/1.0/\x00"

with open(sys.argv[1], "rb") as f:
    jpeg = f.read()

exif = xmp = None
for payload in app1_payloads(jpeg):
    if payload.startswith(EXIF_TAG):
        exif = payload[len(EXIF_TAG):]
    elif payload.startswith(XMP_TAG):
        xmp = payload[len(XMP_TAG):]
assert exif is not None and xmp is not None


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


rows = b"".join(b"\x00" + b"".join(bytes(((x * 16) & 0xFF, (y * 16) & 0xFF, ((x + y) * 8) & 0xFF)) for x in range(SIZE)) for y in range(SIZE))
header = struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0)
itxt = b"XML:com.adobe.xmp\x00" + b"\x00\x00" + b"\x00" + b"\x00" + xmp
with open("exif_xmp_source.png", "wb") as f:
    f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"eXIf", exif) + chunk(b"iTXt", itxt)
            + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))
