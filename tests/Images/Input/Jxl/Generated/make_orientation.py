# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Makes the orientation test files, in two steps.
#
# With no argument, writes orientation_source.png into the current directory: a 37 x 23 8-bit RGBA PNG
# whose eXIf chunk sets the Exif orientation to 6. cjxl copies that orientation into the codestream.
#
# With the path of a bare codestream that cjxl made from it, rewrites the codestream's orientation field
# to each of the eight orientations and writes orientation_1.jxl to orientation_8.jxl. Only the three
# orientation bits change; the pixels are coded as they were.

import struct
import sys
import zlib

WIDTH = 37
HEIGHT = 23
SOURCE_ORIENTATION = 6


def rgba8(x, y):
    # Every pixel differs from the others, so a wrong flip or transpose cannot go unseen.
    return (x * 255) // (WIDTH - 1), (y * 255) // (HEIGHT - 1), (x * 7 + y * 13) & 0xFF, 255 - ((x + y * WIDTH) % 200)


def exif_with_orientation(orientation):
    # A little-endian TIFF header, then IFD0 with one entry: tag 274 (Orientation), SHORT, count 1.
    ifd = struct.pack("<H", 1) + struct.pack("<HHIHH", 274, 3, 1, orientation, 0) + struct.pack("<I", 0)
    return b"II*\x00" + struct.pack("<I", 8) + ifd


def write_source():
    rows = b"".join(b"\x00" + b"".join(struct.pack(">4B", *rgba8(x, y)) for x in range(WIDTH)) for y in range(HEIGHT))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    header = struct.pack(">IIBBBBB", WIDTH, HEIGHT, 8, 6, 0, 0, 0)
    with open("orientation_source.png", "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"eXIf", exif_with_orientation(SOURCE_ORIENTATION))
                + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


class BitReader:
    """Reads bits least significant first, as JPEG XL codestreams store them."""

    def __init__(self, data, pos):
        self.data = data
        self.pos = pos

    def bits(self, n):
        value = 0
        for i in range(n):
            value |= ((self.data[self.pos >> 3] >> (self.pos & 7)) & 1) << i
            self.pos += 1
        return value

    def u32(self, distribution):
        offset, nbits = distribution[self.bits(2)]
        return offset + self.bits(nbits)


def orientation_bit(codestream):
    """Returns the position of the first orientation bit: SizeHeader, then ImageMetadata's all_default
    and extra_fields bits."""
    assert codestream[:2] == b"\xff\x0a", "not a bare codestream"
    reader = BitReader(codestream, 16)
    size_distribution = [(1, 9), (1, 13), (1, 18), (1, 30)]
    small = reader.bits(1)
    if small:
        reader.bits(5)
    else:
        reader.u32(size_distribution)
    if reader.bits(3) == 0:
        if small:
            reader.bits(5)
        else:
            reader.u32(size_distribution)
    assert reader.bits(1) == 0, "all_default is set: there is no orientation field to rewrite"
    assert reader.bits(1) == 1, "extra_fields is not set: there is no orientation field to rewrite"
    return reader.pos


def write_orientations(base_path):
    with open(base_path, "rb") as f:
        codestream = bytearray(f.read())
    pos = orientation_bit(codestream)
    for orientation in range(1, 9):
        data = bytearray(codestream)
        value = orientation - 1
        for i in range(3):
            p = pos + i
            data[p >> 3] = (data[p >> 3] & ~(1 << (p & 7))) | (((value >> i) & 1) << (p & 7))
        with open(f"orientation_{orientation}.jxl", "wb") as f:
            f.write(data)


if len(sys.argv) > 1:
    write_orientations(sys.argv[1])
else:
    write_source()
