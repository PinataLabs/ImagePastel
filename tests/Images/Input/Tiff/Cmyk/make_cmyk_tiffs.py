# Copyright (c) PinataLabs.
# Licensed under the Apache License, Version 2.0.
#
# Writes the Separated (CMYK) TIFF test files into the current directory. See README.md for the list.
# Every file is 48 x 32 pixels in strips of 8 rows, so chunky files have 4 strips and planar files 16.
# The samples cover the whole range of each ink: cyan grows to the right, magenta grows downwards,
# yellow is a hashed pattern and black steps in 8 x 8 blocks.

import struct
import zlib

WIDTH = 48
HEIGHT = 32
ROWS_PER_STRIP = 8

TAG_IMAGE_WIDTH = 256
TAG_IMAGE_LENGTH = 257
TAG_BITS_PER_SAMPLE = 258
TAG_COMPRESSION = 259
TAG_PHOTOMETRIC = 262
TAG_STRIP_OFFSETS = 273
TAG_SAMPLES_PER_PIXEL = 277
TAG_ROWS_PER_STRIP = 278
TAG_STRIP_BYTE_COUNTS = 279
TAG_PLANAR_CONFIGURATION = 284
TAG_PREDICTOR = 317
TAG_INK_SET = 332
TAG_NUMBER_OF_INKS = 334
TAG_ICC_PROFILE = 34675

SHORT = 3
LONG = 4
UNDEFINED = 7

COMPRESSION_NONE = 1
COMPRESSION_JPEG = 7
COMPRESSION_DEFLATE = 8
PHOTOMETRIC_SEPARATED = 5
INK_SET_CMYK = 1
INK_SET_NOT_CMYK = 2


def cmyk8(x, y):
    c = (x * 255) // (WIDTH - 1)
    m = (y * 255) // (HEIGHT - 1)
    ye = (x * 13 + y * 29) % 256
    k = ((x // 8 + y // 8) * 37) % 256
    return c, m, ye, k


def cmyk16(x, y):
    c = (x * 65535) // (WIDTH - 1)
    m = (y * 65535) // (HEIGHT - 1)
    ye = (x * 3001 + y * 7919) % 65536
    k = ((x // 8 + y // 8) * 9473) % 65536
    return c, m, ye, k


def pack_samples(samples, bits, endian):
    return bytes(samples) if bits == 8 else struct.pack(endian + '%dH' % len(samples), *samples)


def apply_predictor(row, channels):
    # TIFF 6.0 section 14: each sample is stored as the difference to the same sample of the pixel on its left.
    out = list(row)
    for i in range(len(row) - 1, channels - 1, -1):
        out[i] = row[i] - row[i - channels]

    return out


def make_strips(bits, endian, planar, compression, predictor):
    sample = cmyk8 if bits == 8 else cmyk16
    mask = (1 << bits) - 1
    planes = range(4) if planar else [None]
    strips = []
    for plane in planes:
        for top in range(0, HEIGHT, ROWS_PER_STRIP):
            data = b''
            for y in range(top, top + ROWS_PER_STRIP):
                row = []
                for x in range(WIDTH):
                    pixel = sample(x, y)
                    row.extend(pixel if plane is None else [pixel[plane]])

                if predictor:
                    row = [v & mask for v in apply_predictor(row, 1 if planar else 4)]

                data += pack_samples(row, bits, endian)

            if compression == COMPRESSION_DEFLATE:
                data = zlib.compress(data, 9)
            elif compression == COMPRESSION_JPEG:
                # Not a JPEG stream: the decoder must refuse the file before it reads any strip.
                data = bytes(len(data))

            strips.append(data)

    return strips


def write_tiff(path, bits, endian='<', planar=False, compression=COMPRESSION_NONE, predictor=False, ink_set=None, number_of_inks=None, icc=None):
    strips = make_strips(bits, endian, planar, compression, predictor)
    entries = [
        (TAG_IMAGE_WIDTH, LONG, [WIDTH]),
        (TAG_IMAGE_LENGTH, LONG, [HEIGHT]),
        (TAG_BITS_PER_SAMPLE, SHORT, [bits] * 4),
        (TAG_COMPRESSION, SHORT, [compression]),
        (TAG_PHOTOMETRIC, SHORT, [PHOTOMETRIC_SEPARATED]),
        (TAG_STRIP_OFFSETS, LONG, [0] * len(strips)),
        (TAG_SAMPLES_PER_PIXEL, SHORT, [4]),
        (TAG_ROWS_PER_STRIP, LONG, [ROWS_PER_STRIP]),
        (TAG_STRIP_BYTE_COUNTS, LONG, [len(s) for s in strips]),
        (TAG_PLANAR_CONFIGURATION, SHORT, [2 if planar else 1]),
    ]
    if predictor:
        entries.append((TAG_PREDICTOR, SHORT, [2]))
    if ink_set is not None:
        entries.append((TAG_INK_SET, SHORT, [ink_set]))
    if number_of_inks is not None:
        entries.append((TAG_NUMBER_OF_INKS, SHORT, [number_of_inks]))
    if icc is not None:
        entries.append((TAG_ICC_PROFILE, UNDEFINED, icc))

    entries.sort(key=lambda e: e[0])

    def value_bytes(field_type, values):
        if field_type == UNDEFINED:
            return bytes(values)
        return struct.pack(endian + ('H' if field_type == SHORT else 'I') * len(values), *values)

    ifd_offset = 8
    ifd_size = 2 + len(entries) * 12 + 4
    extra_offset = ifd_offset + ifd_size
    extra_size = 0
    for _, field_type, values in entries:
        size = len(value_bytes(field_type, values))
        if size > 4:
            extra_size += size + (size & 1)

    data_offset = extra_offset + extra_size
    offsets = []
    position = data_offset
    for strip in strips:
        offsets.append(position)
        position += len(strip)

    entries = [(tag, t, offsets if tag == TAG_STRIP_OFFSETS else v) for tag, t, v in entries]

    header = (b'II' if endian == '<' else b'MM') + struct.pack(endian + 'HI', 42, ifd_offset)
    ifd = struct.pack(endian + 'H', len(entries))
    extra = b''
    for tag, field_type, values in entries:
        raw = value_bytes(field_type, values)
        if len(raw) > 4:
            ifd += struct.pack(endian + 'HHII', tag, field_type, len(values), extra_offset + len(extra))
            extra += raw + (b'\0' if len(raw) & 1 else b'')
        else:
            ifd += struct.pack(endian + 'HHI', tag, field_type, len(values)) + raw.ljust(4, b'\0')

    ifd += struct.pack(endian + 'I', 0)
    with open(path, 'wb') as f:
        f.write(header + ifd + extra + b''.join(strips))


def s15f16(v):
    return struct.pack('>i', round(v * 65536))


def srgb_to_linear(v):
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def lab_from_inks(c, m, y, k):
    # Ink coverage with dot gain, so the profile differs from the plain CMYK-to-RGB formula,
    # then the formula to sRGB, the D50-adapted sRGB colorants to XYZ, and XYZ to CIELAB.
    c, m, y, k = (1 - (1 - v) ** 1.6 for v in (c, m, y, k))
    r, g, b = (srgb_to_linear((1 - v) * (1 - k)) for v in (c, m, y))
    xyz = (
        0.4360747 * r + 0.3850649 * g + 0.1430804 * b,
        0.2225045 * r + 0.7168786 * g + 0.0606169 * b,
        0.0139322 * r + 0.0971045 * g + 0.7141733 * b,
    )
    white = (0.9642, 1.0, 0.8249)

    def f(t):
        return t ** (1 / 3) if t > (6 / 29) ** 3 else t / (3 * (6 / 29) ** 2) + 4 / 29

    fx, fy, fz = (f(v / w) for v, w in zip(xyz, white))
    return 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)


def tag_text(text):
    return b'text' + bytes(4) + text.encode('ascii') + b'\0'


def tag_desc(text):
    ascii_text = text.encode('ascii') + b'\0'
    return b'desc' + bytes(4) + struct.pack('>I', len(ascii_text)) + ascii_text + struct.pack('>IIHB', 0, 0, 0, 0) + bytes(67)


def tag_xyz(x, y, z):
    return b'XYZ ' + bytes(4) + s15f16(x) + s15f16(y) + s15f16(z)


def tag_lut16_cmyk_to_lab(grid):
    data = b'mft2' + bytes(4) + struct.pack('>BBBB', 4, 3, grid, 0)
    for row in range(3):
        for col in range(3):
            data += s15f16(1.0 if row == col else 0.0)

    data += struct.pack('>HH', 2, 2)
    data += struct.pack('>8H', *([0, 65535] * 4))

    # The first input channel varies least rapidly.
    steps = [i / (grid - 1) for i in range(grid)]
    for c in steps:
        for m in steps:
            for y in steps:
                for k in steps:
                    lab_l, lab_a, lab_b = lab_from_inks(c, m, y, k)

                    # ICC v2 16-bit Lab: L* 0..100 to 0..0xFF00, a* and b* -128..127.996 to 0..0xFFFF.
                    values = (lab_l * 65280 / 100, (lab_a + 128) * 256, (lab_b + 128) * 256)
                    data += struct.pack('>3H', *(min(65535, max(0, round(v))) for v in values))

    data += struct.pack('>6H', *([0, 65535] * 3))
    return data


def make_cmyk_profile():
    tags = [
        (b'desc', tag_desc('ImageSharp test CMYK')),
        (b'cprt', tag_text('Copyright (c) PinataLabs. Licensed under the Apache License, Version 2.0.')),
        (b'wtpt', tag_xyz(0.9642, 1.0, 0.8249)),
        (b'A2B0', tag_lut16_cmyk_to_lab(5)),
    ]

    offset = 128 + 4 + 12 * len(tags)
    table = struct.pack('>I', len(tags))
    body = b''
    for signature, data in tags:
        table += signature + struct.pack('>II', offset + len(body), len(data))
        body += data
        body += bytes((4 - len(data) % 4) % 4)

    size = offset + len(body)
    header = struct.pack('>I', size) + bytes(4) + struct.pack('>I', 0x02100000)
    header += b'scnr' + b'CMYK' + b'Lab '
    header += struct.pack('>6H', 2026, 10, 18, 0, 0, 0)
    header += b'acsp' + bytes(4) + bytes(4) + bytes(4) + bytes(4) + bytes(8)
    header += struct.pack('>I', 0)
    header += s15f16(0.9642) + s15f16(1.0) + s15f16(0.8249)
    header += bytes(4) + bytes(16) + bytes(28)
    assert len(header) == 128
    return header + table + body


def main():
    write_tiff('cmyk-contig-08.tiff', 8, ink_set=INK_SET_CMYK, number_of_inks=4)
    write_tiff('cmyk-contig-16_lsb.tiff', 16, '<')
    write_tiff('cmyk-contig-16_msb.tiff', 16, '>')
    write_tiff('cmyk-planar-08.tiff', 8, planar=True)
    write_tiff('cmyk-planar-16_lsb.tiff', 16, '<', planar=True)
    write_tiff('cmyk-planar-16_msb.tiff', 16, '>', planar=True)
    write_tiff('cmyk-contig-08_deflate_predictor.tiff', 8, compression=COMPRESSION_DEFLATE, predictor=True)
    write_tiff('cmyk-contig-16_msb_deflate_predictor.tiff', 16, '>', compression=COMPRESSION_DEFLATE, predictor=True)

    profile = make_cmyk_profile()
    with open('cmyk-test-profile.icc', 'wb') as f:
        f.write(profile)
    write_tiff('cmyk-contig-08_icc.tiff', 8, icc=profile)

    write_tiff('inkset-not-cmyk-08.tiff', 8, ink_set=INK_SET_NOT_CMYK, number_of_inks=4)
    write_tiff('cmyk-contig-08_jpeg.tiff', 8, compression=COMPRESSION_JPEG)


if __name__ == '__main__':
    main()
